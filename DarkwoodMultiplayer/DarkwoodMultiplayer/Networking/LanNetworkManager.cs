using System;
using System.Net;
using System.Net.Sockets;
using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    public sealed class LanNetworkManager : MonoBehaviour, INetEventListener
    {
        public const float SendInterval = 0.05f;

        public static LanNetworkManager Instance { get; private set; }

        private NetManager _net;
        private NetPeer _peer;
        private NetworkRole _role = NetworkRole.Offline;
        private RemotePlayerProxy _remoteProxy;
        private WorldSyncService _worldSync;
        private float _sendTimer;
        private float _proxyAggroTimer;
        private Vector3 _lastSentPosition;
        private bool _handshakeComplete;

        public NetworkRole Role => _role;
        public bool IsConnected => _peer != null && _peer.ConnectionState == ConnectionState.Connected;
        public string StatusText { get; private set; } = "Offline";
        public WorldSyncService WorldSync => _worldSync;
        public RemotePlayerProxy RemoteProxy => _remoteProxy;

        public static bool IsApplyingRemoteState { get; private set; }

        public event Action Connected;
        public event Action Disconnected;

        private void Awake()
        {
            Instance = this;
            _worldSync = new WorldSyncService(ModRuntime.Log);
        }

        public void StartHost(int port)
        {
            if (!ModConfig.IsLanMode)
            {
                StatusText = "LAN disabled — set PlayMode = LAN in config";
                ModRuntime.Log.LogWarning(StatusText);
                return;
            }

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
            if (!ModConfig.IsLanMode)
            {
                StatusText = "LAN disabled — set PlayMode = LAN in config";
                ModRuntime.Log.LogWarning(StatusText);
                return;
            }

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
            if (!ModConfig.IsLanMode)
                return;

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

            // Both sides: send own position to the other side at 20 Hz
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
        }

        private void ProxyAggroCheck()
        {
            if (!PlayerPositionManager.HasRemotePlayer || _remoteProxy == null)
                return;

            Transform proxyT = _remoteProxy.transform;
            Character[] all = CharacterTracker.GetAll();

            foreach (Character c in all)
            {
                if (c == null || !c.alive || c.dummy || c.sleeping)
                    continue;
                if (c.target == proxyT)
                    continue;

                float distToProxy = Vector3.Distance(c.transform.position, proxyT.position);
                float farRange = (float)c.farViewDistance * c.aniSightRangeModifier;

                if (distToProxy > farRange)
                    continue;

                float nearRange = (float)c.nearViewDistance * c.aniSightRangeModifier;

                // Close proximity: always redirect to proxy (overrides host aggro)
                if (distToProxy <= nearRange)
                {
                    c.attackCharacter(proxyT);
                    continue;
                }

                // Far range: only redirect idle enemies
                if (c.target != null)
                    continue;

                Vector3 toProxy = proxyT.position - c.transform.position;
                if (Physics.Raycast(c.transform.position, toProxy.normalized, out var hit, distToProxy, 18909185))
                {
                    if (hit.collider != null && hit.collider.GetComponent<RemotePlayerProxy>() != null)
                        c.attackCharacter(proxyT);
                }
            }
        }

        private void LateUpdate()
        {
            if (!ModConfig.IsLanMode) return;
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

                _remoteProxy.ApplyNetworkState(netState);
                return;
            }

            // Client receives host player state
            EnsureRemoteProxy();
            if (_remoteProxy == null)
                return;

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
            if (Singleton<Controller>.Instance != null)
            {
                Singleton<Controller>.Instance.workbenchLevel = msg.Level;
                ModRuntime.Log?.LogInfo("[Workbench] Level synced to " + msg.Level);
            }
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
            var msg = new PhysicsStateMessage { Doors = new[] { door } };
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        public void SendTrapState(TrapState ts)
        {
            if (!IsConnected) return;
            var msg = new PhysicsStateMessage { Traps = new[] { ts } };
            ModRuntime.Log?.LogInfo("[TrapSync] sending trap triggered at " + ts.PosX + "," + ts.PosY + "," + ts.PosZ);
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        public void SendItemSpawn(ItemSpawnMessage msg)
        {
            if (!IsConnected) return;
            ModRuntime.Log?.LogInfo("[ItemSpawn] sending " + msg.ItemType + " at " + msg.PosX + "," + msg.PosY + "," + msg.PosZ);
            Send(NetMessageType.ItemSpawn, w => msg.Serialize(w));
        }

        public void SendGeneratorState(GeneratorState gs)
        {
            if (!IsConnected) return;
            var msg = new PhysicsStateMessage { Generators = new[] { gs } };
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        public void SendLightState(LightStateMessage ls)
        {
            if (!IsConnected) return;
            Send(NetMessageType.LightState, w => ls.Serialize(w));
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
        }

        private void DestroyRemoteProxy()
        {
            if (_remoteProxy == null)
                return;

            Destroy(_remoteProxy.gameObject);
            _remoteProxy = null;
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
