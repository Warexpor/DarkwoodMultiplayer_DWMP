namespace DarkwoodMultiplayer.Networking
{
    public enum NetMessageType : byte
    {
        Handshake = 1,
        PlayerState = 2,
        WorldSession = 3,
        PhysicsState = 4
    }

    /// <summary>
    /// Sent immediately after connect so both sides agree on protocol version.
    /// </summary>
    public struct HandshakeMessage
    {
        public int ProtocolVersion;

        public void Serialize(NetWriter writer)
        {
            writer.Put(ProtocolVersion);
        }

        public static HandshakeMessage Deserialize(NetReader reader)
        {
            return new HandshakeMessage { ProtocolVersion = reader.GetInt() };
        }
    }

    /// <summary>
    /// Host publishes save/world identifiers so the client can load the matching profile.
    /// </summary>
    public struct WorldSessionMessage
    {
        public string SaveSlotName;
        public int WorldSeed;
        public int ChapterId;
        public int DayIndex;
        public string BigLocationName;

        public void Serialize(NetWriter writer)
        {
            writer.Put(SaveSlotName ?? string.Empty);
            writer.Put(WorldSeed);
            writer.Put(ChapterId);
            writer.Put(DayIndex);
            writer.Put(BigLocationName ?? string.Empty);
        }

        public static WorldSessionMessage Deserialize(NetReader reader)
        {
            return new WorldSessionMessage
            {
                SaveSlotName = reader.GetString(),
                WorldSeed = reader.GetInt(),
                ChapterId = reader.GetInt(),
                DayIndex = reader.GetInt(),
                BigLocationName = reader.GetString()
            };
        }
    }

    /// <summary>
    /// Compact player snapshot for LAN sync (position + animation).
    /// </summary>
    public struct PlayerStateMessage
    {
        public float PosX;
        public float PosY;
        public float PosZ;
        public float VelX;
        public float VelZ;
        public byte LocomotionState;
        public bool FlipX;
        public bool Running;
        public short LegFacingY;
        public bool ReverseLegs;
        public short TorsoFacingY;
        public string TorsoClip;
        public string LegsClip;

        public void Serialize(NetWriter writer)
        {
            writer.Put(PosX);
            writer.Put(PosY);
            writer.Put(PosZ);
            writer.Put(VelX);
            writer.Put(VelZ);
            writer.Put(LocomotionState);
            writer.Put(FlipX);
            writer.Put(Running);
            writer.Put(LegFacingY);
            writer.Put(ReverseLegs);
            writer.Put(TorsoFacingY);
            writer.Put(TorsoClip);
            writer.Put(LegsClip);
        }

        public static PlayerStateMessage Deserialize(NetReader reader)
        {
            return new PlayerStateMessage
            {
                PosX = reader.GetFloat(),
                PosY = reader.GetFloat(),
                PosZ = reader.GetFloat(),
                VelX = reader.GetFloat(),
                VelZ = reader.GetFloat(),
                LocomotionState = reader.GetByte(),
                FlipX = reader.GetBool(),
                Running = reader.GetBool(),
                LegFacingY = reader.GetShort(),
                ReverseLegs = reader.GetBool(),
                TorsoFacingY = reader.GetShort(),
                TorsoClip = reader.GetString(),
                LegsClip = reader.GetString()
            };
        }
    }

    /// <summary>
    /// Thin wrapper so we do not reference LiteNetLib writer types in message structs.
    /// </summary>
    public sealed class NetWriter
    {
        private readonly LiteNetLib.Utils.NetDataWriter _inner = new LiteNetLib.Utils.NetDataWriter();

        public void Put(byte value) => _inner.Put(value);
        public void Put(short value) => _inner.Put(value);
        public void Put(int value) => _inner.Put(value);
        public void Put(float value) => _inner.Put(value);
        public void Put(bool value) => _inner.Put(value);
        public void Put(string value) => _inner.Put(value ?? string.Empty);

        public byte[] CopyData() => _inner.CopyData();
    }

    public sealed class NetReader
    {
        private readonly LiteNetLib.Utils.NetDataReader _inner;

        public NetReader(byte[] data)
        {
            _inner = new LiteNetLib.Utils.NetDataReader(data);
        }

        public byte GetByte() => _inner.GetByte();
        public short GetShort() => _inner.GetShort();
        public int GetInt() => _inner.GetInt();
        public float GetFloat() => _inner.GetFloat();
        public bool GetBool() => _inner.GetBool();
        public string GetString() => _inner.GetString();
    }
}
