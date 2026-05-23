namespace DarkwoodMultiplayer.Networking
{
    public enum NetMessageType : byte
    {
        Handshake = 1,
        PlayerState = 2,
        WorldSession = 3,
        PhysicsState = 4,
        ItemSpawn = 5,
        LightState = 6,
        EntityState = 7,
        PlayerAttack = 8,
        DamagePlayer = 9,
        PlayerDied = 10,
        ContainerItem = 11,
        BarricadeEvent = 12,
        WorkbenchLevel = 13,
        JournalItem = 14,
        FriendlyFire = 15
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

        public void Reset() => _inner.Reset();
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

    public struct LightStateMessage
    {
        public float PosX, PosY, PosZ;
        public bool IsOn;
        public string ItemName;

        public void Serialize(NetWriter writer)
        {
            writer.Put(PosX); writer.Put(PosY); writer.Put(PosZ);
            writer.Put(IsOn);
            writer.Put(ItemName ?? string.Empty);
        }

        public static LightStateMessage Deserialize(NetReader reader)
        {
            return new LightStateMessage
            {
                PosX = reader.GetFloat(),
                PosY = reader.GetFloat(),
                PosZ = reader.GetFloat(),
                IsOn = reader.GetBool(),
                ItemName = reader.GetString()
            };
        }
    }

    public struct ItemSpawnMessage
    {
        public string ItemType;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotX;
        public float RotY;
        public float RotZ;

        public void Serialize(NetWriter writer)
        {
            writer.Put(ItemType ?? string.Empty);
            writer.Put(PosX);
            writer.Put(PosY);
            writer.Put(PosZ);
            writer.Put(RotX);
            writer.Put(RotY);
            writer.Put(RotZ);
        }

        public static ItemSpawnMessage Deserialize(NetReader reader)
        {
            return new ItemSpawnMessage
            {
                ItemType = reader.GetString(),
                PosX = reader.GetFloat(),
                PosY = reader.GetFloat(),
                PosZ = reader.GetFloat(),
                RotX = reader.GetFloat(),
                RotY = reader.GetFloat(),
                RotZ = reader.GetFloat()
            };
        }
    }

    public struct EntitySnapshotNet
    {
        public short Index;
        public float PosX, PosY, PosZ;
        public float RotY;
        public string Clip;
        public short ClipFrame;
        public bool Alive;
        public byte HealthPct;

        public void Serialize(NetWriter w)
        {
            w.Put(Index);
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(RotY);
            w.Put(Clip ?? "");
            w.Put(ClipFrame);
            w.Put(Alive);
            w.Put(HealthPct);
        }

        public static EntitySnapshotNet Deserialize(NetReader r) => new EntitySnapshotNet
        {
            Index = r.GetShort(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotY = r.GetFloat(),
            Clip = r.GetString(),
            ClipFrame = r.GetShort(),
            Alive = r.GetBool(),
            HealthPct = r.GetByte()
        };
    }

    public struct EntityStateMessage
    {
        public EntitySnapshotNet[] Entities;

        public void Serialize(NetWriter w)
        {
            int count = Entities != null ? Entities.Length : 0;
            w.Put(count);
            for (int i = 0; i < count; i++)
                Entities[i].Serialize(w);
        }

        public static EntityStateMessage Deserialize(NetReader r)
        {
            int count = r.GetInt();
            var arr = new EntitySnapshotNet[count];
            for (int i = 0; i < count; i++)
                arr[i] = EntitySnapshotNet.Deserialize(r);
            return new EntityStateMessage { Entities = arr };
        }
    }

    public struct PlayerAttackMessage
    {
        public short TargetNameHash;
        public int Damage;
        public float AttackerPosX, AttackerPosY, AttackerPosZ;

        public void Serialize(NetWriter w)
        {
            w.Put(TargetNameHash);
            w.Put(Damage);
            w.Put(AttackerPosX); w.Put(AttackerPosY); w.Put(AttackerPosZ);
        }

        public static PlayerAttackMessage Deserialize(NetReader r) => new PlayerAttackMessage
        {
            TargetNameHash = r.GetShort(),
            Damage = r.GetInt(),
            AttackerPosX = r.GetFloat(), AttackerPosY = r.GetFloat(), AttackerPosZ = r.GetFloat()
        };
    }

    public struct DamagePlayerMessage
    {
        public int Damage;
        public float AttackerPosX, AttackerPosY, AttackerPosZ;
        public bool CanCutInHalf;
        public bool ShowRedScreen;

        public void Serialize(NetWriter w)
        {
            w.Put(Damage);
            w.Put(AttackerPosX); w.Put(AttackerPosY); w.Put(AttackerPosZ);
            w.Put(CanCutInHalf);
            w.Put(ShowRedScreen);
        }

        public static DamagePlayerMessage Deserialize(NetReader r) => new DamagePlayerMessage
        {
            Damage = r.GetInt(),
            AttackerPosX = r.GetFloat(), AttackerPosY = r.GetFloat(), AttackerPosZ = r.GetFloat(),
            CanCutInHalf = r.GetBool(),
            ShowRedScreen = r.GetBool()
        };
    }

    public struct PlayerDiedMessage
    {
        public void Serialize(NetWriter w) { }
        public static PlayerDiedMessage Deserialize(NetReader r) => new PlayerDiedMessage();
    }

    public enum ContainerAction : byte
    {
        TakeItem = 0,
        PlaceItem = 1,
        RemoveItem = 2
    }

    public struct ContainerItemMessage
    {
        public float PosX, PosY, PosZ;
        public ContainerAction Action;
        public byte SlotIndex;
        public string ItemType;
        public int Amount;
        public float Durability;
        public int Ammo;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put((byte)Action);
            w.Put(SlotIndex);
            w.Put(ItemType ?? "");
            w.Put(Amount);
            w.Put(Durability);
            w.Put(Ammo);
        }

        public static ContainerItemMessage Deserialize(NetReader r) => new ContainerItemMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Action = (ContainerAction)r.GetByte(),
            SlotIndex = r.GetByte(),
            ItemType = r.GetString(),
            Amount = r.GetInt(),
            Durability = r.GetFloat(),
            Ammo = r.GetInt()
        };
    }

    public enum BarricadeAction : byte
    {
        Built = 0,
        Destroyed = 1,
        Damaged = 2
    }

    public struct BarricadeEventMessage
    {
        public float PosX, PosY, PosZ;
        public byte IsWindow; // 0 = door, 1 = window
        public BarricadeAction Action;
        public int Health;
        public bool PlayerBarricade;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(IsWindow);
            w.Put((byte)Action);
            w.Put(Health);
            w.Put(PlayerBarricade);
        }

        public static BarricadeEventMessage Deserialize(NetReader r) => new BarricadeEventMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            IsWindow = r.GetByte(),
            Action = (BarricadeAction)r.GetByte(),
            Health = r.GetInt(),
            PlayerBarricade = r.GetBool()
        };
    }

    public struct WorkbenchLevelMessage
    {
        public int Level;

        public void Serialize(NetWriter w) => w.Put(Level);
        public static WorkbenchLevelMessage Deserialize(NetReader r) => new WorkbenchLevelMessage { Level = r.GetInt() };
    }

    public enum JournalItemKind : byte
    {
        Note = 0,
        Key = 1,
        QuestItem = 2,
        JournalEntry = 3
    }

    public struct JournalItemMessage
    {
        public JournalItemKind Kind;
        public string Type;

        public void Serialize(NetWriter w)
        {
            w.Put((byte)Kind);
            w.Put(Type ?? "");
        }

        public static JournalItemMessage Deserialize(NetReader r) => new JournalItemMessage
        {
            Kind = (JournalItemKind)r.GetByte(),
            Type = r.GetString()
        };
    }

    public struct FriendlyFireMessage
    {
        public int Damage;
        public float AttackerPosX, AttackerPosY, AttackerPosZ;
        public bool CanCutInHalf;

        public void Serialize(NetWriter w)
        {
            w.Put(Damage);
            w.Put(AttackerPosX); w.Put(AttackerPosY); w.Put(AttackerPosZ);
            w.Put(CanCutInHalf);
        }

        public static FriendlyFireMessage Deserialize(NetReader r) => new FriendlyFireMessage
        {
            Damage = r.GetInt(),
            AttackerPosX = r.GetFloat(), AttackerPosY = r.GetFloat(), AttackerPosZ = r.GetFloat(),
            CanCutInHalf = r.GetBool()
        };
    }
}
