using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    public sealed class LanNetworkManager : MonoBehaviour, INetEventListener
    {
        public const float SendInterval = 0.033f;

        public static LanNetworkManager Instance { get; private set; }

        private NetManager _net;
        private NetPeer _peer;
        private NetworkRole _role = NetworkRole.Offline;
        private RemotePlayerProxy _remoteProxy;
        private WorldSyncService _worldSync;
        private float _sendTimer;
        private float _proxyAggroTimer;
        private float _effectSyncTimer;
        private Vector3 _lastSentPosition;
        private bool _wasDragging;
        private bool _handshakeComplete;

        public NetworkRole Role => _role;
        public bool IsConnected => _peer != null && _peer.ConnectionState == ConnectionState.Connected;
        public string StatusText { get; private set; } = "Offline";
        public WorldSyncService WorldSync => _worldSync;
        public RemotePlayerProxy RemoteProxy => _remoteProxy;
        public Transform RemoteProxyTransform => _remoteProxy != null ? _remoteProxy.transform : null;

        public static bool IsApplyingRemoteState { get; private set; }

        /// <summary>True while performing a save triggered by the remote peer.</summary>
        internal static bool _isRemoteSaveInProgress;

        public event Action Connected;
        public event Action Disconnected;

        private void Awake()
        {
            Instance = this;
            _worldSync = new WorldSyncService(ModRuntime.Log);
        }

        public void StartHost(int port)
        {
            StopNetwork();
            _role = NetworkRole.Host;
            _net = new NetManager(this) { UnconnectedMessagesEnabled = true };
            if (!_net.Start(port))
            {
                StatusText = "Failed to bind port " + port;
                _role = NetworkRole.Offline;
                return;
            }

            StatusText = "Hosting on port " + port;
            ModRuntime.Log.LogInfo(StatusText);
        }

        public void ConnectToHost(string address, int port)
        {
            StopNetwork();
            _role = NetworkRole.Client;
            _net = new NetManager(this) { UnconnectedMessagesEnabled = true };
            _net.Start();
            _peer = _net.Connect(address, port, PluginInfo.Name);
            StatusText = "Connecting to " + address + ":" + port;
            ModRuntime.Log.LogInfo(StatusText);
        }

        public void StopNetwork()
        {
            DestroyRemoteProxy();
            _handshakeComplete = false;
            _peer = null;
            _sendTimer = 0f;
            _worldSync?.Reset();
            Sync.WorldPhysicsSyncService.Reset();

            if (_net != null)
            {
                _net.Stop();
                _net = null;
            }

            if (_role != NetworkRole.Offline)
                Disconnected?.Invoke();

            _role = NetworkRole.Offline;
            StatusText = "Offline";
        }

        private void Update()
        {
            _net?.PollEvents();

            if (!IsConnected || !_handshakeComplete)
                return;

            _sendTimer += Time.deltaTime;

            // Host: broadcast entity states to clients
            if (_role == NetworkRole.Host)
            {
                EntityStateBroadcastService.Tick();

                _proxyAggroTimer += Time.deltaTime;
                if (_proxyAggroTimer >= 0.5f)
                {
                    _proxyAggroTimer = 0f;
                    ProxyAggroCheck();
                }

                _physicsSendTimer += Time.deltaTime;
                if (_physicsSendTimer >= PhysicsSendInterval)
                {
                    _physicsSendTimer = 0f;
                    SendWorldSnapshot();
                }
            }

            if (_sendTimer < SendInterval)
                return;

            Player local = Player.Instance;
            if (local == null)
                return;

            _sendTimer = 0f;
            Vector3 pos = local.transform.position;
            Vector3 vel = (pos - _lastSentPosition) / SendInterval;
            _lastSentPosition = pos;

            // Client: periodically sync skill/effect state to host
            if (_role == NetworkRole.Client)
            {
                _effectSyncTimer += Time.deltaTime;
                if (_effectSyncTimer >= 2f)
                {
                    _effectSyncTimer = 0f;
                    SendPlayerEffects();
                }
            }

            // Both sides: send own position to the other side at ~30 Hz
            var msg = new PlayerStateMessage
            {
                PosX = pos.x,
                PosY = pos.y,
                PosZ = pos.z,
                VelX = vel.x,
                VelZ = vel.z,
                LocomotionState = (byte)PlayerAnimationSnapshot.ReadLocomotion(local),
                FlipX = PlayerAnimationSnapshot.ReadFlipX(local),
                Running = local.running,
                LegFacingY = PlayerAnimationSnapshot.ReadLegFacingY(local),
                ReverseLegs = PlayerAnimationSnapshot.ReadReverseLegs(local),
                TorsoFacingY = PlayerAnimationSnapshot.ReadTorsoFacingY(local),
                TorsoClip = PlayerAnimationSnapshot.ReadTorsoClip(local),
                LegsClip = PlayerAnimationSnapshot.ReadLegsClip(local)
            };
            Send(NetMessageType.PlayerState, w => msg.Serialize(w));

            // Host: also track own position locally for AI checks
            if (_role == NetworkRole.Host)
                PlayerPositionManager.ReportHostPosition(pos);

            // Both sides: if dragging an object, sync its position at ~30 Hz
            if (local.dragging && local.itemBeingDragged != null)
            {
                Item dragged = local.itemBeingDragged;
                var dragMsg = new DragSyncMessage
                {
                    PosX = dragged.transform.position.x,
                    PosY = dragged.transform.position.y,
                    PosZ = dragged.transform.position.z,
                    RotX = dragged.transform.eulerAngles.x,
                    RotY = dragged.transform.eulerAngles.y,
                    RotZ = dragged.transform.eulerAngles.z,
                    IsDragging = true,
                    ObjectName = dragged.name,
                    ItemType = dragged.invItem != null ? dragged.invItem.type : ""
                };
                Send(NetMessageType.DragSync, w => dragMsg.Serialize(w));
                _wasDragging = true;
            }
            else if (_wasDragging)
            {
                // Drag ended — send one final message so the receiver knows
                // to clean up any locally-spawned copy.
                _wasDragging = false;
                Item prevDragged = local.itemBeingDragged;
                var dragMsg = new DragSyncMessage
                {
                    IsDragging = false,
                    ObjectName = prevDragged != null ? prevDragged.name : ""
                };
                Send(NetMessageType.DragSync, w => dragMsg.Serialize(w));
            }
        }

        private void ProxyAggroCheck()
        {
            if (_remoteProxy == null)
                return;

            Transform proxyT = _remoteProxy.transform;
            Character[] all = CharacterTracker.GetAll();

            if (all.Length == 0)
                return;

            int aggroed = 0;
            int skippedFar = 0;
            int skippedAlreadyTargeting = 0;

            bool proxyHasEotF = _remoteProxy.RemoteHasEnemyOfTheForest;

            foreach (Character c in all)
            {
                if (c == null || !c.alive || c.dummy)
                    continue;

                if (c.target == proxyT)
                {
                    skippedAlreadyTargeting++;
                    continue;
                }

                // Skip neutral entities (rabbits, pigs, chickens) — they flee, never chase
                // However, EnemyOfTheForest makes all animalAggressive entities attack
                if (c.aggressiveness == Aggressiveness.neutral)
                {
                    if (!proxyHasEotF || c.faction != Faction.animalAggressive)
                    {
                        skippedFar++;
                        continue;
                    }
                }

                // Skip entities that don't interact with the player faction at all
                if (!c.attacksFaction(Faction.player))
                {
                    bool runsFromPlayer = HarmonyLib.Traverse.Create(c)
                        .Method("runsAwayFromFaction", Faction.player)
                        .GetValue<bool>();
                    if (!runsFromPlayer)
                    {
                        if (!proxyHasEotF || c.faction != Faction.animalAggressive)
                        {
                            skippedFar++;
                            continue;
                        }
                    }
                }

                // Determine if this entity should flee from the proxy (rather than attack)
                bool runsFromProxy = c.aggressiveness == Aggressiveness.flee ||
                    c.aggressiveness == Aggressiveness.fleeAndDespawn ||
                    (c.attacksFaction(Faction.player) == false &&
                     HarmonyLib.Traverse.Create(c)
                         .Method("runsAwayFromFaction", Faction.player)
                         .GetValue<bool>());

                float distToProxy = Vector3.Distance(c.transform.position, proxyT.position);

                // Skip entities that have a Sniffer component — those are handled by
                // HostSnifferUpdatePatch which respects the full sniffTime + cooldownTime
                // timing (animation, sound, delayed attack). Without this, ProxyAggroCheck
                // would bypass the Sniffer entirely and cause instant attacks instead
                // of the sniff-then-attack sequence.
                if (c.GetComponent<Sniffer>() != null)
                {
                    skippedFar++;
                    continue;
                }

                // --- Use short proximity range, NOT visual range ---
                // HostCanSeeEnemyPatch already handles visual detection at the full farViewDistance.
                // This check is only for short-range detection that the periodic canSeeEnemy
                // coroutine might miss between cycles.
                float proxRange = Mathf.Min((float)c.nearViewDistance * c.aniSightRangeModifier, 300f);

                if (proxRange <= 0f || distToProxy > proxRange)
                {
                    skippedFar++;
                    continue;
                }

                // Wake up and redirect sleeping enemies near the proxy
                if (c.sleeping)
                {
                    c.wakeup();
                    if (runsFromProxy)
                        c.runAway(proxyT.position);
                    else
                        c.attackCharacter(proxyT);
                    aggroed++;
                    continue;
                }

                // Close proximity: redirect to proxy (overrides host aggro)
                if (runsFromProxy)
                    c.runAway(proxyT.position);
                else
                    c.attackCharacter(proxyT);
                aggroed++;
            }

            if (aggroed > 0 || (_aggroLogCounter++ % 10 == 0))
                ModRuntime.Log?.LogInfo($"[ProxyAggro] checked {all.Length} chars, aggroed={aggroed}, far={skippedFar}, alreadyTargetingOthers={skippedAlreadyTargeting}");
        }



        private static int _aggroLogCounter;

        private void LateUpdate()
        {
            if (!IsConnected || !_handshakeComplete) return;

            // Both sides: interpolate world physics objects
            Sync.WorldPhysicsSyncService.UpdateObjectInterpolation();

            // Client only: interpolate remote entity positions for smooth movement
            if (_role == NetworkRole.Client)
            {
                ClientEntityInterpolationService.TickLateUpdate();
            }
        }

        public static readonly NetWriter _sendWriter = new NetWriter();

        public void Send(NetMessageType type, Action<NetWriter> writeBody, DeliveryMethod method = DeliveryMethod.Unreliable)
        {
            if (_peer == null)
                return;

            _sendWriter.Reset();
            _sendWriter.Put((byte)type);
            writeBody(_sendWriter);
            _peer.Send(_sendWriter.CopyData(), method);
        }

        public void OnPeerConnected(NetPeer peer)
        {
            _peer = peer;
            _handshakeComplete = false;
            StatusText = "Peer connected";
            ModRuntime.Log.LogInfo(StatusText);

            Send(NetMessageType.Handshake, w =>
            {
                new HandshakeMessage { ProtocolVersion = PluginInfo.ProtocolVersion }.Serialize(w);
            });

            if (_role == NetworkRole.Host)
            {
                EntityStateBroadcastService.SetPeer(peer);
                WorldSessionMessage session = _worldSync.BuildHostSession();
                Send(NetMessageType.WorldSession, w => session.Serialize(w));
            }

            EnsureRemoteProxy();
            Connected?.Invoke();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            ModRuntime.Log.LogInfo("Peer disconnected: " + disconnectInfo.Reason);
            StopNetwork();
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            ModRuntime.Log.LogError("Network error: " + socketError);
            StatusText = "Error: " + socketError;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (!reader.TryGetByte(out byte messageType))
                return;

            var type = (NetMessageType)messageType;
            byte[] payload = reader.GetRemainingBytes();

            IsApplyingRemoteState = true;
            try
            {
                switch (type)
                {
                case NetMessageType.Handshake:
                    HandleHandshake(HandshakeMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PlayerState:
                    HandlePlayerState(PlayerStateMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.WorldSession:
                    HandleWorldSession(WorldSessionMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PhysicsState:
                    HandlePhysicsState(PhysicsStateMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.ItemSpawn:
                    HandleItemSpawn(ItemSpawnMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.LightState:
                    HandleLightState(LightStateMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.EntityState:
                    HandleEntityState(EntityStateMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PlayerAttack:
                    HandlePlayerAttack(PlayerAttackMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.DamagePlayer:
                    HandleDamagePlayer(DamagePlayerMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PlayerDied:
                    HandlePlayerDied();
                    break;
                case NetMessageType.ContainerItem:
                    HandleContainerItem(ContainerItemMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.BarricadeEvent:
                    HandleBarricadeEvent(BarricadeEventMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.WorkbenchLevel:
                    HandleWorkbenchLevel(WorkbenchLevelMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.JournalItem:
                    HandleJournalItem(JournalItemMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.FriendlyFire:
                    HandleFriendlyFire(FriendlyFireMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PlayerSound:
                    HandlePlayerSound(PlayerSoundMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PlayerScare:
                    HandlePlayerScare(PlayerScareMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.PlayerEffectSync:
                    HandlePlayerEffectSync(PlayerEffectSyncMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.DragSync:
                    HandleDragSync(DragSyncMessage.Deserialize(new NetReader(payload)));
                    break;
                case NetMessageType.SaveSync:
                    HandleSaveSync();
                    break;
                }
            }
            finally
            {
                IsApplyingRemoteState = false;
            }
        }

        private void HandleHandshake(HandshakeMessage handshake)
        {
            if (handshake.ProtocolVersion != PluginInfo.ProtocolVersion)
            {
                ModRuntime.Log.LogError(
                    "Protocol mismatch. Local="
                    + PluginInfo.ProtocolVersion
                    + " remote="
                    + handshake.ProtocolVersion
                    + " — update both mods to the same version.");
                _peer?.Disconnect();
                return;
            }

            _handshakeComplete = true;
            StatusText = _role == NetworkRole.Host ? "Client joined" : "Connected to host";
            ModRuntime.Log.LogInfo("Handshake OK");
            EnsureRemoteProxy();

            if (_role == NetworkRole.Client)
                StatusText += " — waiting for host world session";
        }

        private void HandleWorldSession(WorldSessionMessage session)
        {
            _worldSync.ApplyHostSession(session, asClient: _role == NetworkRole.Client);
            if (_role == NetworkRole.Client)
                StatusText = "Synced — load " + session.SaveSlotName + " (ch" + session.ChapterId + ")";
        }

        private void HandlePlayerState(PlayerStateMessage state)
        {
            if (_role == NetworkRole.Host)
            {
                PlayerPositionManager.UpdateRemotePlayer(
                    new Vector3(state.PosX, state.PosY, state.PosZ),
                    state.TorsoFacingY);

                EnsureRemoteProxy();
                if (_remoteProxy == null)
                    return;

                var netState = new PlayerStateNet
                {
                    Position = new Vector3(state.PosX, state.PosY, state.PosZ),
                    Locomotion = (SecondPlayerAnimController.LocomotionState)state.LocomotionState,
                    FlipX = state.FlipX,
                    LegFacingY = state.LegFacingY,
                    ReverseLegs = state.ReverseLegs,
                    TorsoFacingY = state.TorsoFacingY,
                    TorsoClip = state.TorsoClip,
                    LegsClip = state.LegsClip
                };

                _remoteProxy.RemoteRunning = state.Running;
                _remoteProxy.RemoteLocomotion = (SecondPlayerAnimController.LocomotionState)state.LocomotionState;
                _remoteProxy.ApplyNetworkState(netState);
                return;
            }

            // Client receives host player state
            EnsureRemoteProxy();
            if (_remoteProxy == null)
                return;

            _remoteProxy.RemoteRunning = state.Running;
            _remoteProxy.RemoteLocomotion = (SecondPlayerAnimController.LocomotionState)state.LocomotionState;

            var hostState = new PlayerStateNet
            {
                Position = new Vector3(state.PosX, state.PosY, state.PosZ),
                Locomotion = (SecondPlayerAnimController.LocomotionState)state.LocomotionState,
                FlipX = state.FlipX,
                LegFacingY = state.LegFacingY,
                ReverseLegs = state.ReverseLegs,
                TorsoFacingY = state.TorsoFacingY,
                TorsoClip = state.TorsoClip,
                LegsClip = state.LegsClip
            };

            _remoteProxy.ApplyNetworkState(hostState);
        }

        private void HandleEntityState(EntityStateMessage msg)
        {
            if (_role == NetworkRole.Client)
            {
                ClientEntityInterpolationService.ApplySnapshot(msg);
            }
        }

        // Tracks host-spawned copies of items that don't exist on this side
        // (e.g. remote player dragged an item in an unloaded world-grid chunk).
        private readonly HashSet<int> _spawnedDragProxyItems = new HashSet<int>();

        private void HandleDragSync(DragSyncMessage msg)
        {
            EnsureRemoteProxy();

            Vector3 targetPos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            Vector3 targetRot = new Vector3(msg.RotX, msg.RotY, msg.RotZ);

            if (!msg.IsDragging)
            {
                // Drag ended — destroy the locally-spawned proxy copy so it
                // doesn't linger as a duplicate when the world-grid chunk loads.
                CleanupSpawnedDragProxy(msg.ObjectName);
                return;
            }

            Item item = FindDraggedItemLocally(msg.ObjectName, targetPos);
            if (item == null)
            {
                // Item doesn't exist on this side — spawn it on-demand so we
                // can reflect the remote player's manipulation.
                item = SpawnDraggedItem(msg);
                if (item == null)
                {
                    ModRuntime.Log?.LogInfo("[DragSync] cannot spawn \"" + msg.ObjectName + "\" type=" + msg.ItemType);
                    return;
                }
                _spawnedDragProxyItems.Add(item.GetInstanceID());
                ModRuntime.Log?.LogInfo("[DragSync] spawned " + item.name + " for remote drag");
            }

            // Remove from interpolation — DragSyncMessage is more authoritative
            // and instant, while UpdateObjectInterpolation would smooth over
            // the jump and fight subsequent DragSync updates.
            Sync.WorldPhysicsSyncService.RemoveObjectFromInterpolation(item.gameObject);

            Rigidbody targetRb = item.GetComponent<Rigidbody>();
            if (targetRb != null)
            {
                targetRb.position = targetPos;
                targetRb.rotation = Quaternion.Euler(targetRot);
                targetRb.velocity = Vector3.zero;
                targetRb.angularVelocity = Vector3.zero;
            }

            ModRuntime.Log?.LogInfo("[DragSync] " + item.name + " → " + targetPos);
        }

        /// <summary>Spawn an item on this peer so the remote drag can be reflected.</summary>
        private Item SpawnDraggedItem(DragSyncMessage msg)
        {
            if (string.IsNullOrEmpty(msg.ItemType))
            {
                ModRuntime.Log?.LogInfo("[DragSync] no ItemType to spawn \"" + msg.ObjectName + "\"");
                return null;
            }

            if (Singleton<ItemsDatabase>.Instance == null)
            {
                ModRuntime.Log?.LogWarning("[DragSync] ItemsDatabase not available");
                return null;
            }

            if (!Singleton<ItemsDatabase>.Instance.hasItem(msg.ItemType))
            {
                ModRuntime.Log?.LogInfo("[DragSync] ItemsDatabase has no item type \"" + msg.ItemType + "\"");
                return null;
            }

            InvItem itemDef = Singleton<ItemsDatabase>.Instance.getItem(msg.ItemType, instantiate: false);
            if (itemDef == null || itemDef.item == null)
            {
                ModRuntime.Log?.LogInfo("[DragSync] no prefab for \"" + msg.ItemType + "\"");
                return null;
            }

            GameObject prefab = itemDef.item as GameObject;
            if (prefab == null)
            {
                ModRuntime.Log?.LogInfo("[DragSync] prefab is not a GameObject for \"" + msg.ItemType + "\"");
                return null;
            }

            Vector3 pos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            Quaternion rot = Quaternion.Euler(msg.RotX, msg.RotY, msg.RotZ);
            GameObject go = Core.AddPrefab(prefab, pos, rot, null);
            if (go == null)
            {
                // Fallback: direct instantiate if Core.AddPrefab fails
                go = UnityEngine.Object.Instantiate(prefab, pos, rot);
            }

            if (go == null) return null;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.position = pos;
                rb.rotation = rot;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            return go.GetComponent<Item>();
        }

        /// <summary>Destroy a proxy-spawned copy when the remote player stops dragging.</summary>
        private void CleanupSpawnedDragProxy(string objectName)
        {
            if (_spawnedDragProxyItems.Count == 0) return;

            List<int> toRemove = new List<int>();
            foreach (int id in _spawnedDragProxyItems)
            {
                // Find by name as a safety check
                GameObject go = null;
                foreach (Item candidate in UnityEngine.Object.FindObjectsOfType<Item>())
                {
                    if (candidate.GetInstanceID() == id)
                    {
                        go = candidate.gameObject;
                        break;
                    }
                }

                if (go != null)
                {
                    // Match by name if provided
                    if (!string.IsNullOrEmpty(objectName) && !go.name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ModRuntime.Log?.LogInfo("[DragSync] destroying proxy-spawned " + go.name);
                    UnityEngine.Object.Destroy(go);
                }
                toRemove.Add(id);
            }

            foreach (int id in toRemove)
                _spawnedDragProxyItems.Remove(id);
        }

        /// <summary>Find a draggable Item on this peer by name and proximity.</summary>
        private Item FindDraggedItemLocally(string name, Vector3 nearPos)
        {
            // Strategy 1: overlap sphere near the reported position (radius 6u
            // to cover the gap between the sent position and the local copy).
            Collider[] nearby = Physics.OverlapSphere(nearPos, 6f);
            Item best = null;
            float bestDist = 6f;
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null || nearby[i].isTrigger) continue;
                Rigidbody rb = nearby[i].attachedRigidbody;
                if (rb == null) continue;
                Item item = rb.GetComponent<Item>();
                if (item == null || !item.draggable) continue;
                if (item.beingDragged) continue; // skip locally-dragged items
                if (!string.IsNullOrEmpty(name) && !item.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                float d = Vector3.Distance(rb.position, nearPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = item;
                }
            }
            if (best != null)
            {
                ModRuntime.Log?.LogInfo("[DragSync] found " + best.name + " near pos (" + bestDist.ToString("F1") + " u)");
                return best;
            }

            // Strategy 2: global scan by name — catches objects on unloaded
            // world-grid chunks or far from the reported position.
            if (!string.IsNullOrEmpty(name))
            {
                foreach (Item candidate in UnityEngine.Object.FindObjectsOfType<Item>())
                {
                    if (candidate == null || !candidate.draggable) continue;
                    if (candidate.beingDragged) continue;
                    if (candidate.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        ModRuntime.Log?.LogInfo("[DragSync] found " + candidate.name + " via global scan");
                        return candidate;
                    }
                }
            }

            return null;
        }

        private void HandlePlayerAttack(PlayerAttackMessage msg)
        {
            if (_role != NetworkRole.Host) return;
            if (!PlayerPositionManager.HasRemotePlayer) return;

            Character target = CharacterTracker.FindByStableId(msg.TargetNameHash);
            if (target == null) return;

            Vector3 attackPos = new Vector3(msg.AttackerPosX, msg.AttackerPosY, msg.AttackerPosZ);
            float distSq = Vector3.SqrMagnitude(target.transform.position - attackPos);
            if (distSq > 350f * 350f) return;

            Transform proxyT = _remoteProxy != null ? _remoteProxy.transform : null;

            if (proxyT != null && target.alive && target.target != proxyT)
                target.attackCharacter(proxyT);

            target.getHit(msg.Damage, proxyT, canCutInHalf: false, byPlayer: true, canInterrupt: true);
        }

        private void HandleDamagePlayer(DamagePlayerMessage msg)
        {
            if (_role != NetworkRole.Client) return;
            Player local = Player.Instance;
            if (local == null) return;

            local.getHit(msg.Damage, local.transform, msg.CanCutInHalf, byPlayer: false, canInterrupt: true, showRedScreen: msg.ShowRedScreen);
        }

        private void HandlePlayerDied()
        {
            if (_role != NetworkRole.Host) return;
            ModRuntime.Log?.LogInfo("[Death] Client died — saving world state");
            if (Singleton<SaveManager>.Instance != null)
                Singleton<SaveManager>.Instance.Save(doJson: true);
        }

        private void HandleContainerItem(ContainerItemMessage msg)
        {
            Vector3 pos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            Inventory inv = FindInventoryByPos(pos);
            if (inv == null) return;

            if (msg.Action == ContainerAction.TakeItem || msg.Action == ContainerAction.RemoveItem)
            {
                if (msg.SlotIndex < inv.slots.Count)
                {
                    InvSlot slot = inv.slots[msg.SlotIndex];
                    if (!InvItemClass.isNull(slot.invItem))
                    {
                        if (msg.Amount >= slot.invItem.amount)
                            slot.removeItem();
                        else
                            slot.invItem.removeAmount(msg.Amount);
                    }
                }
            }
            else if (msg.Action == ContainerAction.PlaceItem)
            {
                if (msg.SlotIndex < inv.slots.Count)
                {
                    InvSlot slot = inv.slots[msg.SlotIndex];
                    if (InvItemClass.isNull(slot.invItem))
                    {
                        slot.createItem(msg.ItemType, msg.Amount, msg.Durability > 0f ? msg.Durability : 1f);
                        if (msg.Ammo > 0 && !InvItemClass.isNull(slot.invItem))
                            slot.invItem.ammo = msg.Ammo;
                    }
                    else if (slot.invItem.type == msg.ItemType)
                    {
                        slot.invItem.amount += msg.Amount;
                        slot.invItem.refresh();
                    }
                }
            }
        }

        private void HandleBarricadeEvent(BarricadeEventMessage msg)
        {
            Vector3 pos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            if (msg.IsWindow == 0)
            {
                Door door = FindDoorByPos(pos);
                if (door == null) return;

                if (msg.Action == BarricadeAction.Built)
                {
                    door.barricaded = true;
                    door.playerBarricade = msg.PlayerBarricade;
                    door.barricadeHealth = msg.Health;
                    door.setToBarricaded();
                }
                else if (msg.Action == BarricadeAction.Destroyed)
                {
                    door.destroyBarricade(silent: true);
                }
                else if (msg.Action == BarricadeAction.Damaged)
                {
                    door.barricadeHealth = msg.Health;
                    if (door.barricadeHealth <= 0)
                        door.destroyBarricade(silent: true);
                }
            }
            else
            {
                Window window = FindWindowByPos(pos);
                if (window == null) return;

                var tWin = Traverse.Create(window);
                if (msg.Action == BarricadeAction.Built)
                {
                    tWin.Field("barricaded").SetValue(true);
                    tWin.Field("playerBarricade").SetValue(msg.PlayerBarricade);
                    tWin.Field("barricadeHealth").SetValue(msg.Health);
                    string bSprite = tWin.Field("barricadedSprite").GetValue<string>();
                    if (!string.IsNullOrEmpty(bSprite))
                        window.sprite.SetSprite(bSprite);
                    GameObject frame = tWin.Field("barricadeFrame").GetValue<GameObject>();
                    if (frame != null)
                        frame.SetActive(true);
                }
                else if (msg.Action == BarricadeAction.Destroyed || msg.Action == BarricadeAction.Damaged)
                {
                    tWin.Field("barricadeHealth").SetValue(msg.Health);
                    if (msg.Health <= 0)
                        window.destroyBarricade(silent: true);
                }
            }
        }

        private void HandleWorkbenchLevel(WorkbenchLevelMessage msg)
        {
            if (Singleton<Controller>.Instance == null) return;

            int prevLevel = Singleton<Controller>.Instance.workbenchLevel;
            Singleton<Controller>.Instance.workbenchLevel = msg.Level;
            ModRuntime.Log?.LogInfo("[Workbench] Level synced from " + prevLevel + " to " + msg.Level);

            // If the workbench inventory is currently open, refresh the display
            // so the player sees the updated level and recipes immediately.
            try
            {
                if (Player.Instance != null && Player.Instance.openedItemInventory != null)
                {
                    Workbench wb = Player.Instance.openedItemInventory.GetComponent<Workbench>();
                    if (wb == null)
                        wb = Player.Instance.openedItemInventory.transform.parent?.GetComponent<Workbench>();

                    if (wb != null)
                    {
                        wb.currentLevel = msg.Level;
                        wb.refreshWorkbenchUpgrade();
                        wb.workbenchInventory.refreshRecipes();
                    }
                }
            }
            catch { }
        }

        private void HandleJournalItem(JournalItemMessage msg)
        {
            Journal journal = Singleton<UI>.Instance?.journal;
            if (journal == null) return;

            switch (msg.Kind)
            {
                case JournalItemKind.Note:
                    if (!journal.notesDict.ContainsKey(msg.Type))
                    {
                        Journal.Note note = new Journal.Note();
                        note.type = msg.Type;
                        note.timePickedUp = Singleton<Controller>.Instance != null
                            ? Singleton<Controller>.Instance.CurrentTime : 0;
                        journal.notesDict.Add(msg.Type, note);
                        journal.showJournalInfoPopup("Note", msg.Type);
                    }
                    break;
                case JournalItemKind.Key:
                    if (!journal.keysDict.ContainsKey(msg.Type))
                    {
                        Journal.Key key = new Journal.Key();
                        key.type = msg.Type;
                        journal.keysDict.Add(msg.Type, key);
                        journal.showJournalInfoPopup("Key", msg.Type);
                    }
                    break;
                case JournalItemKind.QuestItem:
                    if (!journal.itemsDict.ContainsKey(msg.Type))
                    {
                        Journal.Item item = new Journal.Item();
                        item.type = msg.Type;
                        journal.itemsDict.Add(msg.Type, item);
                        journal.showJournalInfoPopup("InvItem", msg.Type);
                    }
                    break;
                case JournalItemKind.JournalEntry:
                    journal.addJournalEntry(msg.Type, noPopup: false);
                    break;
            }
        }

        private void HandleFriendlyFire(FriendlyFireMessage msg)
        {
            if (_role != NetworkRole.Host) return;
            Player host = Player.Instance;
            if (host == null) return;

            Vector3 atkPos = new Vector3(msg.AttackerPosX, msg.AttackerPosY, msg.AttackerPosZ);
            Transform atkTransform = _remoteProxy != null ? _remoteProxy.transform : host.transform;
            host.getHit(msg.Damage, atkTransform, msg.CanCutInHalf, byPlayer: true, canInterrupt: true);
            ModRuntime.Log?.LogInfo("[FriendlyFire] Host took " + msg.Damage + " damage from client");
        }

        private static Inventory FindInventoryByPos(Vector3 pos)
        {
            Collider[] nearby = Physics.OverlapSphere(pos, 1f);
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null) continue;
                Inventory inv = nearby[i].GetComponentInParent<Inventory>();
                if (inv != null && inv.invType == Inventory.InvType.itemInv)
                    return inv;
            }
            return null;
        }

        private static Door FindDoorByPos(Vector3 pos)
        {
            return Sync.DoorTracker.FindByPosition(pos);
        }

        private static Window FindWindowByPos(Vector3 pos)
        {
            Collider[] nearby = Physics.OverlapSphere(pos, 1f);
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null) continue;
                Window w = nearby[i].GetComponentInParent<Window>();
                if (w != null) return w;
            }
            return null;
        }

        private float _physicsSendTimer;
        private int _physicsRecvLogCounter;
        private const float PhysicsSendInterval = 0.3f;

        public void SendDoorState(DoorState door)
        {
            if (!IsConnected) return;
            if (IsApplyingRemoteState) return;
            var msg = new PhysicsStateMessage { Doors = new[] { door } };
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        public void SendTrapState(TrapState ts)
        {
            if (!IsConnected) return;
            if (IsApplyingRemoteState) return;
            var msg = new PhysicsStateMessage { Traps = new[] { ts } };
            ModRuntime.Log?.LogInfo("[TrapSync] sending trap triggered at " + ts.PosX + "," + ts.PosY + "," + ts.PosZ);
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        public void SendItemSpawn(ItemSpawnMessage msg)
        {
            if (!IsConnected) return;
            if (IsApplyingRemoteState) return;
            ModRuntime.Log?.LogInfo("[ItemSpawn] sending " + msg.ItemType + " at " + msg.PosX + "," + msg.PosY + "," + msg.PosZ);
            Send(NetMessageType.ItemSpawn, w => msg.Serialize(w));
        }

        public void SendGeneratorState(GeneratorState gs)
        {
            if (!IsConnected) return;
            if (IsApplyingRemoteState) return;
            var msg = new PhysicsStateMessage { Generators = new[] { gs } };
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        public void SendLightState(LightStateMessage ls)
        {
            if (!IsConnected) return;
            if (IsApplyingRemoteState) return;
            Send(NetMessageType.LightState, w => ls.Serialize(w));
        }

        /// <summary>Sends a save-sync trigger to the remote peer.</summary>
        public void SendSaveSync()
        {
            if (!IsConnected) return;
            if (_isRemoteSaveInProgress) return;
            ModRuntime.Log?.LogInfo("[SaveSync] sending save trigger to remote");
            Send(NetMessageType.SaveSync, w => new SaveSyncMessage().Serialize(w), LiteNetLib.DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Handles a save-sync trigger from the remote peer.</summary>
        private void HandleSaveSync()
        {
            if (_isRemoteSaveInProgress) return;
            ModRuntime.Log?.LogInfo("[SaveSync] received save trigger from remote, saving locally");
            _isRemoteSaveInProgress = true;
            try
            {
                // Show saving indicator immediately so the local player sees feedback
                try
                {
                    if (Singleton<UI>.Instance != null)
                        Singleton<UI>.Instance.showSavingIndicator();
                }
                catch { }

                // Perform the actual save. The showSavingIndicator parameter defaults to true
                // so finishSaving will also call it, but we already showed it above.
                SaveManager save = Singleton<SaveManager>.Instance;
                if (save != null)
                {
                    save.Save(doJson: false, doSaveProfile: true, force: true);

                    // Ensure lastTimeSaved is updated so the "time since last save" timer
                    // on both sides shows roughly the same value.
                    // (SaveManager sets this only inside the non-early-return path.)
                    var t = HarmonyLib.Traverse.Create(save);
                    t.Field("lastTimeSaved").SetValue(System.DateTime.Now);
                }

                // Also sync the profile's timeSaved string so the save selection screen
                // shows consistent timestamps across players.
                if (Core.currentProfile != null)
                    Core.currentProfile.timeSaved = System.DateTime.Now.ToString();
            }
            finally
            {
                _isRemoteSaveInProgress = false;
            }
        }

        private void SendWorldSnapshot()
        {
            if (!IsConnected)
                return;

            if (Sync.WorldPhysicsSyncService.TryBuildWorldSnapshot(out var msg))
                Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        private void HandlePhysicsState(PhysicsStateMessage state)
        {
            string fromPeer = (_role == NetworkRole.Host) ? "client" : "host";
            int oc = state.Objects?.Length ?? 0;
            int dc = state.Doors?.Length ?? 0;
            int tc = state.Traps?.Length ?? 0;
            int gc = state.Generators?.Length ?? 0;
            if ((oc > 0 || dc > 0 || tc > 0 || gc > 0) && ++_physicsRecvLogCounter % 30 == 0)
                ModRuntime.Log?.LogInfo("[PhysicsRecv] objects=" + oc + " doors=" + dc + " traps=" + tc + " gens=" + gc + " from " + fromPeer);
            Sync.WorldPhysicsSyncService.ApplySnapshot(state, fromPeer);
        }

        private void HandleItemSpawn(ItemSpawnMessage msg)
        {
            string fromPeer = (_role == NetworkRole.Host) ? "client" : "host";
            ModRuntime.Log?.LogInfo("[ItemSpawn] received " + msg.ItemType + " at " + msg.PosX + "," + msg.PosY + "," + msg.PosZ + " from " + fromPeer);

            if (Singleton<ItemsDatabase>.Instance == null)
            {
                ModRuntime.Log?.LogWarning("[ItemSpawn] ItemsDatabase not available");
                return;
            }

            if (!Singleton<ItemsDatabase>.Instance.hasItem(msg.ItemType))
            {
                ModRuntime.Log?.LogWarning("[ItemSpawn] unknown item type: " + msg.ItemType);
                return;
            }

            InvItem itemDef = Singleton<ItemsDatabase>.Instance.getItem(msg.ItemType, instantiate: false);
            if (itemDef == null || itemDef.item == null)
            {
                ModRuntime.Log?.LogWarning("[ItemSpawn] no prefab for " + msg.ItemType);
                return;
            }

            Vector3 pos = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            Quaternion rot = Quaternion.Euler(msg.RotX, msg.RotY, msg.RotZ);
            GameObject go = Core.AddPrefab(itemDef.item, pos, rot, null);
            if (go != null)
            {
                Trigger trig = go.GetComponent<Trigger>();
                if (trig != null)
                    trig.setByPlayer = true;
            }
            else
            {
                ModRuntime.Log?.LogWarning("[ItemSpawn] Core.AddPrefab returned null for " + msg.ItemType);
            }
        }

        private void HandleLightState(LightStateMessage ls)
        {
            string fromPeer = (_role == NetworkRole.Host) ? "client" : "host";
            Sync.WorldPhysicsSyncService.ApplyLightState(ls, fromPeer);
        }

        private void EnsureRemoteProxy()
        {
            if (_remoteProxy != null)
                return;

            _remoteProxy = RemotePlayerProxy.Spawn(ModRuntime.Log);
            if (_remoteProxy != null)
                _remoteProxy.OnFootstep += HandleProxyFootstep;
        }

        private void DestroyRemoteProxy()
        {
            if (_remoteProxy == null)
                return;

            _remoteProxy.OnFootstep -= HandleProxyFootstep;
            Destroy(_remoteProxy.gameObject);
            _remoteProxy = null;
        }

        private void HandleProxyFootstep(bool running)
        {
            if (_remoteProxy == null) return;
            Transform proxyT = _remoteProxy.transform;
            float range = running ? 350f : 150f;
            Character.alertInArea(proxyT.position, range, false, 1f);
            PlayProxyFootstepSound(_remoteProxy, running);
        }

        private void SendPlayerEffects()
        {
            Player local = Player.Instance;
            if (local == null) return;

            var msg = new PlayerEffectSyncMessage
            {
                HasShadowWard = local.effects.hasEffectType(CharacterEffectType.shadowWard),
                HasForestSpiritWard = local.effects.hasEffectType(CharacterEffectType.forestSpiritWard),
                FriendOfTheForest = local.skills.FriendOfTheForest,
                EnemyOfTheForest = local.skills.EnemyOfTheForest,
                Invisible = local.invisible,
                IgnoreMe = local.ignoreMe
            };
            Send(NetMessageType.PlayerEffectSync, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }

        private void HandlePlayerEffectSync(PlayerEffectSyncMessage msg)
        {
            EnsureRemoteProxy();
            if (_remoteProxy == null) return;

            _remoteProxy.RemoteHasShadowWard = msg.HasShadowWard;
            _remoteProxy.RemoteHasForestSpiritWard = msg.HasForestSpiritWard;
            _remoteProxy.RemoteHasFriendOfTheForest = msg.FriendOfTheForest;
            _remoteProxy.RemoteHasEnemyOfTheForest = msg.EnemyOfTheForest;

            CharBase cb = _remoteProxy.GetComponent<CharBase>();
            if (cb != null)
            {
                cb.invisible = msg.Invisible;
                cb.ignoreMe = msg.IgnoreMe;
            }
        }

        private const float MaxFootstepAudioDistance = 500f;

        /// <summary>
        /// Plays a 3D-positioned footstep sound at the proxy's transform.
        /// Detects ground type via the proxy's CharBase, then plays the
        /// appropriate footstep clip using the local player's sound IDs.
        /// Silently returns if the proxy is beyond MaxFootstepAudioDistance.
        /// </summary>
        private static void PlayProxyFootstepSound(RemotePlayerProxy proxy, bool running)
        {
            Transform proxyT = proxy.transform;
            Player local = Player.Instance;
            if (local == null) return;
            if (Vector3.Distance(local.transform.position, proxyT.position) > MaxFootstepAudioDistance)
                return;

            CharacterSounds cs = local.GetComponent<CharacterSounds>();
            if (cs == null) return;

            CharBase proxyCB = proxy.GetComponent<CharBase>();
            if (proxyCB != null)
                proxyCB.checkGround();
            GroundType gt = proxyCB != null ? proxyCB.groundType : GroundType.grass;

            string soundID = null;
            switch (gt)
            {
                case GroundType.grass: soundID = cs.footstepGrass; break;
                case GroundType.wood: soundID = cs.footstepWood; break;
                case GroundType.tiles: soundID = cs.footstepTiles; break;
                case GroundType.bridge: soundID = cs.footstepBridge; break;
                case GroundType.rug: soundID = cs.footstepCarpet; break;
                case GroundType.water: soundID = cs.footstepWater; break;
                case GroundType.infection: soundID = cs.footstepInfection; break;
                default: soundID = cs.footstepGrass; break;
            }

            float volumeModifier = running ? 1.3f : 0.7f;
            float vol = cs.footstepVolume * volumeModifier;

            if (!string.IsNullOrEmpty(soundID))
                AudioController.Play(soundID, proxyT, vol);

            AudioController.Play("walk_clothes_noises", proxyT, vol);

            if (UnityEngine.Random.Range(0f, 1f) > 1f - cs.footHitGroundSoundChance)
            {
                string addSound = gt == GroundType.wood ? "footsteps_wood_add" : "footstep_branches_add";
                AudioController.Play(addSound, proxyT, 1f);
            }
        }

        private void HandlePlayerSound(PlayerSoundMessage msg)
        {
            if (_role != NetworkRole.Host) return;
            if (_remoteProxy == null) return;

            Transform proxyT = _remoteProxy.transform;
            Character.alertInArea(proxyT.position, msg.Range, msg.DangerousSound, msg.Volume, msg.Gunshot);
        }

        private void HandlePlayerScare(PlayerScareMessage msg)
        {
            if (_role != NetworkRole.Host) return;
            if (_remoteProxy == null) return;

            Transform proxyT = _remoteProxy.transform;
            Character.scareInArea(proxyT.position, msg.Range);
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (_role == NetworkRole.Host)
                request.AcceptIfKey(PluginInfo.Name);
            else
                request.Reject();
        }
    }
}
