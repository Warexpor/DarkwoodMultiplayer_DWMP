using System;
using System.Net;
using System.Net.Sockets;
using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    public sealed class LanNetworkManager : MonoBehaviour, INetEventListener
    {
        public const float SendInterval = 0.05f;

        private NetManager _net;
        private NetPeer _peer;
        private NetworkRole _role = NetworkRole.Offline;
        private RemotePlayerProxy _remoteProxy;
        private WorldSyncService _worldSync;
        private float _sendTimer;
        private Vector3 _lastSentPosition;
        private bool _handshakeComplete;

        public NetworkRole Role => _role;
        public bool IsConnected => _peer != null && _peer.ConnectionState == ConnectionState.Connected;
        public string StatusText { get; private set; } = "Offline";
        public WorldSyncService WorldSync => _worldSync;

        public event Action Connected;
        public event Action Disconnected;

        private void Awake()
        {
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
            _physicsSendTimer += Time.deltaTime;

            if (_physicsSendTimer >= PhysicsSendInterval)
            {
                _physicsSendTimer = 0f;
                SendPhysicsSnapshot();
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
        }

        private void Send(NetMessageType type, Action<NetWriter> writeBody)
        {
            if (_peer == null)
                return;

            var writer = new NetWriter();
            writer.Put((byte)type);
            writeBody(writer);
            _peer.Send(writer.CopyData(), DeliveryMethod.Unreliable);
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
        }

        private float _physicsSendTimer;
        private const float PhysicsSendInterval = 0.15f;

        public void SendDoorState(DoorState door)
        {
            if (!IsConnected) return;
            var msg = new PhysicsStateMessage { Doors = new[] { door } };
            Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        private void SendPhysicsSnapshot()
        {
            if (!IsConnected)
                return;

            if (Sync.WorldPhysicsSyncService.TryBuildSnapshot(out var msg))
                Send(NetMessageType.PhysicsState, w => msg.Serialize(w));
        }

        private void HandlePhysicsState(PhysicsStateMessage state)
        {
            string fromPeer = (_role == NetworkRole.Host) ? "client" : "host";
            Sync.WorldPhysicsSyncService.ApplySnapshot(state, fromPeer);
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
