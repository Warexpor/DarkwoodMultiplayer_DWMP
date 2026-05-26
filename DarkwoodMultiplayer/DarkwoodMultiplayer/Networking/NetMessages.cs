namespace DarkwoodMultiplayer.Networking
{
    /// <summary>
    /// Identifies the type of a network message for serialization/deserialization dispatch.
    /// </summary>
    public enum NetMessageType : byte
    {
        /// <summary>Initial handshake for protocol version agreement.</summary>
        Handshake = 1,
        /// <summary>Player position and animation state snapshot.</summary>
        PlayerState = 2,
        /// <summary>Host publishes save/world identifiers for client to match.</summary>
        WorldSession = 3,
        /// <summary>Physics state sync (deprecated/planned).</summary>
        PhysicsState = 4,
        /// <summary>Item spawn event.</summary>
        ItemSpawn = 5,
        /// <summary>Light source on/off state.</summary>
        LightState = 6,
        /// <summary>Entity snapshot for interpolation.</summary>
        EntityState = 7,
        /// <summary>Player attack event targeting a specific entity.</summary>
        PlayerAttack = 8,
        /// <summary>Damage applied to the local player by the remote.</summary>
        DamagePlayer = 9,
        /// <summary>Notification that a player has died.</summary>
        PlayerDied = 10,
        /// <summary>Container inventory operation (take/place/remove).</summary>
        ContainerItem = 11,
        /// <summary>Barricade built/destroyed/damaged event.</summary>
        BarricadeEvent = 12,
        /// <summary>Workbench upgrade level sync.</summary>
        WorkbenchLevel = 13,
        /// <summary>Journal/note/key item pickup sync.</summary>
        JournalItem = 14,
        /// <summary>Friendly fire damage event.</summary>
        FriendlyFire = 15,
        /// <summary>Player effect/status flags sync (shadow ward, invisibility, etc.).</summary>
        PlayerEffectSync = 16,
        /// <summary>Player-made sound notification for AI alert.</summary>
        PlayerSound = 17,
        /// <summary>Player weapon aim scare notification for AI.</summary>
        PlayerScare = 18,
        /// <summary>Dragged object position/rotation sync.</summary>
        DragSync = 19,
        /// <summary>Trigger a save on the remote peer.</summary>
        SaveSync = 20,
        /// <summary>Host broadcasts current game time to the client.</summary>
        TimeSync = 21,
        /// <summary>Host broadcasts an entity sound event to the client.</summary>
        EntitySound = 22,
        /// <summary>Client→Host: a world object was harvested/destroyed by clicking (e.g. mushroom).</summary>
        WorldObjectRemoved = 23,
        /// <summary>Either peer: player's active light (flashlight/torch/lantern) toggled.</summary>
        PlayerLightState = 24,
        /// <summary>Client→Host: player threw a throwable item (molotov, etc.).</summary>
        ThrowableSpawn = 25,
        /// <summary>Client→Host: a world object exploded (barrel, gas tank, etc.).</summary>
        ExplosionTrigger = 26,
        /// <summary>Either peer: play an audio clip at the proxy position.</summary>
        PlayerAudio = 27,
        /// <summary>Either peer: a gasoline trail was spawned at a world position.</summary>
        GasTrailSpawn = 28,
        /// <summary>Either peer: gasoline ignited at a world position (non-explosion ignition).</summary>
        GasIgnite = 29,
        /// <summary>Either peer: player's torso or legs animation clip changed (immediate event-based sync).</summary>
        PlayerAnimation = 30,
        /// <summary>Either peer: player switched animation library (item equip changes weapon sprites).</summary>
        PlayerAnimLibrary = 31,
        /// <summary>Host→Client: bullet impact FX (blood, wall hit, muzzle flash) at a world position.</summary>
        BulletImpact = 32,
        /// <summary>Either peer: player fired a weapon (sync muzzle flash, projectile visuals).</summary>
        PlayerFiredWeapon = 33,
        /// <summary>Either peer: an inventory item was dropped into the world.</summary>
        DroppedItemSpawn = 34,
        /// <summary>Either peer: a networked dropped item was picked up.</summary>
        DroppedItemPickup = 35,
        /// <summary>Either peer: saw fuel/inventory state changed (convert or add fuel).</summary>
        SawState = 36
    }

    /// <summary>
    /// Sent immediately after connect so both sides agree on protocol version.
    /// </summary>
    public struct HandshakeMessage
    {
        /// <summary>Protocol version to validate compatibility.</summary>
        public int ProtocolVersion;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter writer)
        {
            writer.Put(ProtocolVersion);
        }

        /// <summary>Deserializes a HandshakeMessage from the provided reader.</summary>
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
        /// <summary>Save slot name the host is using (e.g. "profile_0").</summary>
        public string SaveSlotName;
        /// <summary>Deterministic world seed derived from chapter ID.</summary>
        public int WorldSeed;
        /// <summary>Current chapter being played.</summary>
        public int ChapterId;
        /// <summary>Current in-game day index.</summary>
        public int DayIndex;
        /// <summary>Name of the player's current big location.</summary>
        public string BigLocationName;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter writer)
        {
            writer.Put(SaveSlotName ?? string.Empty);
            writer.Put(WorldSeed);
            writer.Put(ChapterId);
            writer.Put(DayIndex);
            writer.Put(BigLocationName ?? string.Empty);
        }

        /// <summary>Deserializes a WorldSessionMessage from the provided reader.</summary>
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
        /// <summary>World position X.</summary>
        public float PosX;
        /// <summary>World position Y.</summary>
        public float PosY;
        /// <summary>World position Z.</summary>
        public float PosZ;
        /// <summary>Velocity on the X axis.</summary>
        public float VelX;
        /// <summary>Velocity on the Z axis.</summary>
        public float VelZ;
        /// <summary>Current locomotion animation state (idle, walk, run, etc.).</summary>
        public byte LocomotionState;
        /// <summary>Whether the sprite is flipped horizontally.</summary>
        public bool FlipX;
        /// <summary>Whether the player is currently running.</summary>
        public bool Running;
        /// <summary>Leg sprite facing direction in degrees.</summary>
        public short LegFacingY;
        /// <summary>Whether leg animation plays in reverse.</summary>
        public bool ReverseLegs;
        /// <summary>Torso sprite facing direction in degrees.</summary>
        public short TorsoFacingY;
        /// <summary>Current torso animation clip name.</summary>
        public string TorsoClip;
        /// <summary>Current legs animation clip name.</summary>
        public string LegsClip;
        /// <summary>Whether the player is currently trapped in a bear trap.</summary>
        public bool InBearTrap;

        /// <summary>Serializes this message into the provided writer.</summary>
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
            writer.Put(InBearTrap);
        }

        /// <summary>Deserializes a PlayerStateMessage from the provided reader.</summary>
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
                LegsClip = reader.GetString(),
                InBearTrap = reader.GetBool()
            };
        }
    }

    /// <summary>
    /// Thin wrapper so message structs do not reference LiteNetLib writer types directly.
    /// </summary>
    public sealed class NetWriter
    {
        private readonly LiteNetLib.Utils.NetDataWriter _inner = new LiteNetLib.Utils.NetDataWriter();

        /// <summary>Writes a byte value.</summary>
        public void Put(byte value) => _inner.Put(value);
        /// <summary>Writes a short value.</summary>
        public void Put(short value) => _inner.Put(value);
        /// <summary>Writes an int value.</summary>
        public void Put(int value) => _inner.Put(value);
        /// <summary>Writes a float value.</summary>
        public void Put(float value) => _inner.Put(value);
        /// <summary>Writes a bool value.</summary>
        public void Put(bool value) => _inner.Put(value);
        /// <summary>Writes a string value (empty string if null).</summary>
        public void Put(string value) => _inner.Put(value ?? string.Empty);

        /// <summary>Resets the internal buffer for reuse.</summary>
        public void Reset() => _inner.Reset();
        /// <summary>Returns a copy of the written data as a byte array.</summary>
        public byte[] CopyData() => _inner.CopyData();
    }

    /// <summary>
    /// Thin wrapper so message structs do not reference LiteNetLib reader types directly.
    /// </summary>
    public sealed class NetReader
    {
        private readonly LiteNetLib.Utils.NetDataReader _inner;

        /// <summary>Constructs a reader over the given byte array.</summary>
        public NetReader(byte[] data)
        {
            _inner = new LiteNetLib.Utils.NetDataReader(data);
        }

        /// <summary>Reads a byte value.</summary>
        public byte GetByte() => _inner.GetByte();
        /// <summary>Reads a short value.</summary>
        public short GetShort() => _inner.GetShort();
        /// <summary>Reads an int value.</summary>
        public int GetInt() => _inner.GetInt();
        /// <summary>Reads a float value.</summary>
        public float GetFloat() => _inner.GetFloat();
        /// <summary>Reads a bool value.</summary>
        public bool GetBool() => _inner.GetBool();
        /// <summary>Reads a string value.</summary>
        public string GetString() => _inner.GetString();
    }

    /// <summary>
    /// Sent while a player is dragging an object, so the other side
    /// can replicate the position on its own instance.
    /// </summary>
    public struct DragSyncMessage
    {
        /// <summary>Drag target world position X.</summary>
        public float PosX;
        /// <summary>Drag target world position Y.</summary>
        public float PosY;
        /// <summary>Drag target world position Z.</summary>
        public float PosZ;
        /// <summary>Drag target rotation X.</summary>
        public float RotX;
        /// <summary>Drag target rotation Y.</summary>
        public float RotY;
        /// <summary>Drag target rotation Z.</summary>
        public float RotZ;
        /// <summary>Whether the player is currently dragging an object.</summary>
        public bool IsDragging;
        /// <summary>Name of the dragged GameObject.</summary>
        public string ObjectName;
        /// <summary>Item type identifier of the dragged object.</summary>
        public string ItemType;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(RotX); w.Put(RotY); w.Put(RotZ);
            w.Put(IsDragging);
            w.Put(ObjectName ?? "");
            w.Put(ItemType ?? "");
        }

        /// <summary>Deserializes a DragSyncMessage from the provided reader.</summary>
        public static DragSyncMessage Deserialize(NetReader r) => new DragSyncMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotX = r.GetFloat(), RotY = r.GetFloat(), RotZ = r.GetFloat(),
            IsDragging = r.GetBool(),
            ObjectName = r.GetString(),
            ItemType = r.GetString()
        };
    }

    /// <summary>
    /// Syncs a light source's on/off state and world position to the remote client.
    /// </summary>
    public struct LightStateMessage
    {
        /// <summary>Light world position X.</summary>
        public float PosX;
        /// <summary>Light world position Y.</summary>
        public float PosY;
        /// <summary>Light world position Z.</summary>
        public float PosZ;
        /// <summary>Whether the light is currently on.</summary>
        public bool IsOn;
        /// <summary>Name of the light item/prefab.</summary>
        public string ItemName;
        /// <summary>
        /// Item type identifier (<see cref="Item.invItem.type"/>), used on the receiving end
        /// to spawn the light on-demand when it doesn't exist locally.
        /// </summary>
        public string ItemType;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter writer)
        {
            writer.Put(PosX); writer.Put(PosY); writer.Put(PosZ);
            writer.Put(IsOn);
            writer.Put(ItemName ?? string.Empty);
            writer.Put(ItemType ?? string.Empty);
        }

        /// <summary>Deserializes a LightStateMessage from the provided reader.</summary>
        public static LightStateMessage Deserialize(NetReader reader)
        {
            return new LightStateMessage
            {
                PosX = reader.GetFloat(),
                PosY = reader.GetFloat(),
                PosZ = reader.GetFloat(),
                IsOn = reader.GetBool(),
                ItemName = reader.GetString(),
                ItemType = reader.GetString()
            };
        }
    }

    /// <summary>
    /// Notifies the remote client that an item has spawned into the world.
    /// </summary>
    public struct ItemSpawnMessage
    {
        /// <summary>Item type identifier.</summary>
        public string ItemType;
        /// <summary>Spawn position X.</summary>
        public float PosX;
        /// <summary>Spawn position Y.</summary>
        public float PosY;
        /// <summary>Spawn position Z.</summary>
        public float PosZ;
        /// <summary>Spawn rotation X.</summary>
        public float RotX;
        /// <summary>Spawn rotation Y.</summary>
        public float RotY;
        /// <summary>Spawn rotation Z.</summary>
        public float RotZ;

        /// <summary>Serializes this message into the provided writer.</summary>
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

        /// <summary>Deserializes an ItemSpawnMessage from the provided reader.</summary>
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

    /// <summary>
    /// A single entity's snapshot data within an EntityStateMessage payload.
    /// </summary>
    public struct EntitySnapshotNet
    {
        /// <summary>Stable entity ID assigned by CharacterTracker.</summary>
        public short Index;
        /// <summary>World position X.</summary>
        public float PosX;
        /// <summary>World position Y.</summary>
        public float PosY;
        /// <summary>World position Z.</summary>
        public float PosZ;
        /// <summary>Y-axis rotation in degrees.</summary>
        public float RotY;
        /// <summary>Name of the current animation clip.</summary>
        public string Clip;
        /// <summary>Current frame index within the animation clip (-1 if none).</summary>
        public short ClipFrame;
        /// <summary>Whether the entity is alive.</summary>
        public bool Alive;
        /// <summary>Health percentage (0–100) as a byte.</summary>
        public byte HealthPct;
        /// <summary>Entity prefab name (used for spawning on client).</summary>
        public string EntityName;
        /// <summary>Full prefab resource path (e.g. "Characters/NPC/NightTrader") for precise phantom spawning.</summary>
        public string PrefabPath;

        /// <summary>Serializes this snapshot into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(Index);
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(RotY);
            w.Put(Clip ?? "");
            w.Put(ClipFrame);
            w.Put(Alive);
            w.Put(HealthPct);
            w.Put(EntityName ?? "");
            w.Put(PrefabPath ?? "");
        }

        /// <summary>Deserializes an EntitySnapshotNet from the provided reader.</summary>
        public static EntitySnapshotNet Deserialize(NetReader r) => new EntitySnapshotNet
        {
            Index = r.GetShort(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotY = r.GetFloat(),
            Clip = r.GetString(),
            ClipFrame = r.GetShort(),
            Alive = r.GetBool(),
            HealthPct = r.GetByte(),
            EntityName = r.GetString(),
            PrefabPath = r.GetString()
        };
    }

    /// <summary>
    /// Wraps an array of entity snapshots for batched unreliable delivery.
    /// </summary>
    public struct EntityStateMessage
    {
        /// <summary>Array of entity snapshots in this batch.</summary>
        public EntitySnapshotNet[] Entities;

        /// <summary>Serializes the entity array into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            int count = Entities != null ? Entities.Length : 0;
            w.Put(count);
            for (int i = 0; i < count; i++)
                Entities[i].Serialize(w);
        }

        /// <summary>Deserializes an EntityStateMessage from the provided reader.</summary>
        public static EntityStateMessage Deserialize(NetReader r)
        {
            int count = r.GetInt();
            var arr = new EntitySnapshotNet[count];
            for (int i = 0; i < count; i++)
                arr[i] = EntitySnapshotNet.Deserialize(r);
            return new EntityStateMessage { Entities = arr };
        }
    }

    /// <summary>
    /// Sent when a player attacks an entity, identifying the target by a name hash.
    /// </summary>
    public struct PlayerAttackMessage
    {
        /// <summary>Hash of the target entity's name for identification.</summary>
        public short TargetNameHash;
        /// <summary>Amount of damage dealt.</summary>
        public int Damage;
        /// <summary>Attacker world position X at time of attack.</summary>
        public float AttackerPosX;
        /// <summary>Attacker world position Y at time of attack.</summary>
        public float AttackerPosY;
        /// <summary>Attacker world position Z at time of attack.</summary>
        public float AttackerPosZ;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(TargetNameHash);
            w.Put(Damage);
            w.Put(AttackerPosX); w.Put(AttackerPosY); w.Put(AttackerPosZ);
        }

        /// <summary>Deserializes a PlayerAttackMessage from the provided reader.</summary>
        public static PlayerAttackMessage Deserialize(NetReader r) => new PlayerAttackMessage
        {
            TargetNameHash = r.GetShort(),
            Damage = r.GetInt(),
            AttackerPosX = r.GetFloat(), AttackerPosY = r.GetFloat(), AttackerPosZ = r.GetFloat()
        };
    }

    /// <summary>
    /// Sent by the host to apply damage to the client's local player.
    /// </summary>
    public struct DamagePlayerMessage
    {
        /// <summary>Amount of damage to apply.</summary>
        public int Damage;
        /// <summary>Attacker world position X (for hit direction).</summary>
        public float AttackerPosX;
        /// <summary>Attacker world position Y.</summary>
        public float AttackerPosY;
        /// <summary>Attacker world position Z.</summary>
        public float AttackerPosZ;
        /// <summary>Whether the attack can cut the player in half.</summary>
        public bool CanCutInHalf;
        /// <summary>Whether to show the red damage screen flash.</summary>
        public bool ShowRedScreen;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(Damage);
            w.Put(AttackerPosX); w.Put(AttackerPosY); w.Put(AttackerPosZ);
            w.Put(CanCutInHalf);
            w.Put(ShowRedScreen);
        }

        /// <summary>Deserializes a DamagePlayerMessage from the provided reader.</summary>
        public static DamagePlayerMessage Deserialize(NetReader r) => new DamagePlayerMessage
        {
            Damage = r.GetInt(),
            AttackerPosX = r.GetFloat(), AttackerPosY = r.GetFloat(), AttackerPosZ = r.GetFloat(),
            CanCutInHalf = r.GetBool(),
            ShowRedScreen = r.GetBool()
        };
    }

    /// <summary>
    /// Notifies the remote side that the local player has died.
    /// </summary>
    public struct PlayerDiedMessage
    {
        /// <summary>Serializes this (empty) message.</summary>
        public void Serialize(NetWriter w) { }
        /// <summary>Deserializes a PlayerDiedMessage (no data).</summary>
        public static PlayerDiedMessage Deserialize(NetReader r) => new PlayerDiedMessage();
    }

    /// <summary>Type of entity sound event for host→client sync.</summary>
    public enum EntitySoundType : byte
    {
        Growl = 0,
        Attack1 = 1,
        Attack2 = 2,
        Death = 3,
        Curious = 4,
        Aggressive = 5,
        Defensive = 6,
        Escaping = 7,
        Idle = 8,
        GetHit = 9,
    }

    /// <summary>
    /// Host→Client: an entity on the host played a sound. The client replays
    /// it locally at the corresponding entity's position for spatial audio.
    /// </summary>
    public struct EntitySoundMessage
    {
        /// <summary>Host-side stable ID of the entity that played the sound.</summary>
        public short HostId;
        /// <summary>Which sound was played.</summary>
        public EntitySoundType SoundType;
        /// <summary>Loop name for IdleLoop sound type (empty for other types).</summary>
        public string LoopName;

        public void Serialize(NetWriter w)
        {
            w.Put(HostId);
            w.Put((byte)SoundType);
            w.Put(LoopName ?? string.Empty);
        }

        public static EntitySoundMessage Deserialize(NetReader r) => new EntitySoundMessage
        {
            HostId = r.GetShort(),
            SoundType = (EntitySoundType)r.GetByte(),
            LoopName = r.GetString()
        };
    }

    /// <summary>
    /// Client→Host: a world object was harvested/clicked-destroyed (mushroom, exp item, etc).
    /// The host finds the matching object and destroys it.
    /// </summary>
    public struct WorldObjectRemovedMessage
    {
        /// <summary>Rounded world position X (lookup key on receiver).</summary>
        public float PosX;
        /// <summary>Rounded world position Y.</summary>
        public float PosY;
        /// <summary>Rounded world position Z.</summary>
        public float PosZ;
        /// <summary>Name of the GameObject (fallback lookup).</summary>
        public string ObjectName;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(ObjectName ?? "");
        }

        public static WorldObjectRemovedMessage Deserialize(NetReader r) => new WorldObjectRemovedMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            ObjectName = r.GetString()
        };
    }

    /// <summary>
    /// Describes an inventory operation on a container (take, place, or remove).
    /// </summary>
    public enum ContainerAction : byte
    {
        /// <summary>Take an item from the container.</summary>
        TakeItem = 0,
        /// <summary>Place an item into the container.</summary>
        PlaceItem = 1,
        /// <summary>Remove an item from a slot.</summary>
        RemoveItem = 2
    }

    /// <summary>
    /// Syncs a container inventory operation (take/place/remove) to the remote client.
    /// </summary>
    public struct ContainerItemMessage
    {
        /// <summary>Container world position X.</summary>
        public float PosX;
        /// <summary>Container world position Y.</summary>
        public float PosY;
        /// <summary>Container world position Z.</summary>
        public float PosZ;
        /// <summary>Action performed (TakeItem, PlaceItem, RemoveItem).</summary>
        public ContainerAction Action;
        /// <summary>Slot index affected by the operation.</summary>
        public byte SlotIndex;
        /// <summary>Item type identifier involved.</summary>
        public string ItemType;
        /// <summary>Item stack amount.</summary>
        public int Amount;
        /// <summary>Item durability value.</summary>
        public float Durability;
        /// <summary>Item ammo count (for ranged weapons).</summary>
        public int Ammo;

        /// <summary>Serializes this message into the provided writer.</summary>
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

        /// <summary>Deserializes a ContainerItemMessage from the provided reader.</summary>
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

    /// <summary>
    /// Describes a barricade state change (built, destroyed, or damaged).
    /// </summary>
    public enum BarricadeAction : byte
    {
        /// <summary>Barricade was built.</summary>
        Built = 0,
        /// <summary>Barricade was destroyed.</summary>
        Destroyed = 1,
        /// <summary>Barricade was damaged (health reduced).</summary>
        Damaged = 2
    }

    /// <summary>
    /// Syncs a barricade/object state change (built/destroyed/damaged) to the remote client.
    /// IsWindow: 0 = door, 1 = window, 2 = generic item (wardrobe, furniture, etc.)
    /// </summary>
    public struct BarricadeEventMessage
    {
        /// <summary>Barricade world position X.</summary>
        public float PosX;
        /// <summary>Barricade world position Y.</summary>
        public float PosY;
        /// <summary>Barricade world position Z.</summary>
        public float PosZ;
        /// <summary>0 = door, 1 = window barricade, 2 = generic item.</summary>
        public byte IsWindow;
        /// <summary>Type of barricade action.</summary>
        public BarricadeAction Action;
        /// <summary>Remaining barricade health (for doors/windows) or remaining health (for items).</summary>
        public int Health;
        /// <summary>Whether this is a player-built barricade.</summary>
        public bool PlayerBarricade;
        /// <summary>Door main health (for main-door damage, -1 = no change).</summary>
        public int MainHealth;
        /// <summary>Original damage amount (for replaying FX/sounds on receiver, -1 = unknown).</summary>
        public int DamageAmount;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(IsWindow);
            w.Put((byte)Action);
            w.Put(Health);
            w.Put(PlayerBarricade);
            w.Put(MainHealth);
            w.Put(DamageAmount);
        }

        /// <summary>Deserializes a BarricadeEventMessage from the provided reader.</summary>
        public static BarricadeEventMessage Deserialize(NetReader r) => new BarricadeEventMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            IsWindow = r.GetByte(),
            Action = (BarricadeAction)r.GetByte(),
            Health = r.GetInt(),
            PlayerBarricade = r.GetBool(),
            MainHealth = r.GetInt(),
            DamageAmount = r.GetInt()
        };
    }

    /// <summary>
    /// Spawns a dropped-item wrapper (DroppedItem) on the remote peer.
    /// </summary>
    public struct DroppedItemSpawnMessage
    {
        /// <summary>Unique identifier to correlate pick-up events.</summary>
        public string Guid;
        /// <summary>"Items/DroppedItem" or "Items/DroppedItem_water".</summary>
        public string PrefabPath;
        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;
        /// <summary>Item type identifier (e.g. "healthPotion").</summary>
        public string ItemType;
        public int Amount;
        /// <summary>Current durability (absolute value, not 0-1 ratio).</summary>
        public float Durability;
        public int Ammo;

        public void Serialize(NetWriter w)
        {
            w.Put(Guid ?? string.Empty);
            w.Put(PrefabPath ?? string.Empty);
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(RotX); w.Put(RotY); w.Put(RotZ);
            w.Put(ItemType ?? string.Empty);
            w.Put(Amount);
            w.Put(Durability);
            w.Put(Ammo);
        }

        public static DroppedItemSpawnMessage Deserialize(NetReader r) => new DroppedItemSpawnMessage
        {
            Guid = r.GetString(),
            PrefabPath = r.GetString(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotX = r.GetFloat(), RotY = r.GetFloat(), RotZ = r.GetFloat(),
            ItemType = r.GetString(),
            Amount = r.GetInt(),
            Durability = r.GetFloat(),
            Ammo = r.GetInt()
        };
    }

    /// <summary>
    /// Notifies the remote peer that a networked dropped item was picked up.
    /// </summary>
    public struct DroppedItemPickupMessage
    {
        /// <summary>GUID of the dropped item being picked up.</summary>
        public string Guid;

        public void Serialize(NetWriter w) => w.Put(Guid ?? string.Empty);
        public static DroppedItemPickupMessage Deserialize(NetReader r) => new DroppedItemPickupMessage { Guid = r.GetString() };
    }

    /// <summary>
    /// Syncs the host's workbench upgrade level to the client.
    /// </summary>
    public struct WorkbenchLevelMessage
    {
        /// <summary>Current workbench upgrade level.</summary>
        public int Level;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w) => w.Put(Level);
        /// <summary>Deserializes a WorkbenchLevelMessage from the provided reader.</summary>
        public static WorkbenchLevelMessage Deserialize(NetReader r) => new WorkbenchLevelMessage { Level = r.GetInt() };
    }

    /// <summary>
    /// Categorises a journal item by its gameplay type.
    /// </summary>
    public enum JournalItemKind : byte
    {
        /// <summary>A readable note.</summary>
        Note = 0,
        /// <summary>A key item required for progression.</summary>
        Key = 1,
        /// <summary>A quest-related item.</summary>
        QuestItem = 2,
        /// <summary>An entry added to the player's journal.</summary>
        JournalEntry = 3
    }

    /// <summary>
    /// Syncs a journal/note/key item pickup to the remote client.
    /// </summary>
    public struct JournalItemMessage
    {
        /// <summary>Kind of journal item (note, key, quest item, journal entry).</summary>
        public JournalItemKind Kind;
        /// <summary>Item type identifier.</summary>
        public string Type;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put((byte)Kind);
            w.Put(Type ?? "");
        }

        /// <summary>Deserializes a JournalItemMessage from the provided reader.</summary>
        public static JournalItemMessage Deserialize(NetReader r) => new JournalItemMessage
        {
            Kind = (JournalItemKind)r.GetByte(),
            Type = r.GetString()
        };
    }

    /// <summary>
    /// Notifies the remote client of friendly fire damage from another player.
    /// </summary>
    public struct FriendlyFireMessage
    {
        /// <summary>Amount of friendly fire damage.</summary>
        public int Damage;
        /// <summary>Attacker world position X.</summary>
        public float AttackerPosX;
        /// <summary>Attacker world position Y.</summary>
        public float AttackerPosY;
        /// <summary>Attacker world position Z.</summary>
        public float AttackerPosZ;
        /// <summary>Whether the attack can cut the player in half.</summary>
        public bool CanCutInHalf;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(Damage);
            w.Put(AttackerPosX); w.Put(AttackerPosY); w.Put(AttackerPosZ);
            w.Put(CanCutInHalf);
        }

        /// <summary>Deserializes a FriendlyFireMessage from the provided reader.</summary>
        public static FriendlyFireMessage Deserialize(NetReader r) => new FriendlyFireMessage
        {
            Damage = r.GetInt(),
            AttackerPosX = r.GetFloat(), AttackerPosY = r.GetFloat(), AttackerPosZ = r.GetFloat(),
            CanCutInHalf = r.GetBool()
        };
    }

    /// <summary>
    /// Client → Host: the remote player made a noise (gunshot, melee hit, footstep, etc.).
    /// The host calls Character.alertInArea at the proxy's position.
    /// </summary>
    public struct PlayerSoundMessage
    {
        /// <summary>Range of the sound in game units.</summary>
        public float Range;
        /// <summary>Whether this sound is dangerous (triggers aggro).</summary>
        public bool DangerousSound;
        /// <summary>Volume level of the sound.</summary>
        public float Volume;
        /// <summary>Whether this is a gunshot (special AI reaction).</summary>
        public bool Gunshot;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(Range);
            w.Put(DangerousSound);
            w.Put(Volume);
            w.Put(Gunshot);
        }

        /// <summary>Deserializes a PlayerSoundMessage from the provided reader.</summary>
        public static PlayerSoundMessage Deserialize(NetReader r) => new PlayerSoundMessage
        {
            Range = r.GetFloat(),
            DangerousSound = r.GetBool(),
            Volume = r.GetFloat(),
            Gunshot = r.GetBool()
        };
    }

    /// <summary>
    /// Client → Host: the remote player aimed a weapon, triggering scareInArea.
    /// The host calls Character.scareInArea at the proxy's position.
    /// </summary>
    public struct PlayerScareMessage
    {
        /// <summary>Range of the scare effect in game units.</summary>
        public float Range;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w) => w.Put(Range);

        /// <summary>Deserializes a PlayerScareMessage from the provided reader.</summary>
        public static PlayerScareMessage Deserialize(NetReader r) => new PlayerScareMessage
        {
            Range = r.GetFloat()
        };
    }

    /// <summary>
    /// Client → Host: periodic sync of remote player's skill/effect state
    /// so the host's AI responds appropriately (shadowWard, FriendOfTheForest, etc.).
    /// </summary>
    public struct PlayerEffectSyncMessage
    {
        /// <summary>
        /// Bitmask of active effects:
        /// 1=shadowWard, 2=forestSpiritWard, 4=FriendOfTheForest,
        /// 8=EnemyOfTheForest, 16=invisible, 32=ignoreMe.
        /// </summary>
        public byte Flags;

        /// <summary>Whether the shadow ward effect is active.</summary>
        public bool HasShadowWard
        {
            get => (Flags & 1) != 0;
            set => Flags = (byte)((Flags & ~1) | (value ? 1 : 0));
        }

        /// <summary>Whether the forest spirit ward effect is active.</summary>
        public bool HasForestSpiritWard
        {
            get => (Flags & 2) != 0;
            set => Flags = (byte)((Flags & ~2) | (value ? 2 : 0));
        }

        /// <summary>Whether the Friend of the Forest effect is active.</summary>
        public bool FriendOfTheForest
        {
            get => (Flags & 4) != 0;
            set => Flags = (byte)((Flags & ~4) | (value ? 4 : 0));
        }

        /// <summary>Whether the Enemy of the Forest effect is active.</summary>
        public bool EnemyOfTheForest
        {
            get => (Flags & 8) != 0;
            set => Flags = (byte)((Flags & ~8) | (value ? 8 : 0));
        }

        /// <summary>Whether the player is invisible.</summary>
        public bool Invisible
        {
            get => (Flags & 16) != 0;
            set => Flags = (byte)((Flags & ~16) | (value ? 16 : 0));
        }

        /// <summary>Whether the player should be ignored by AI.</summary>
        public bool IgnoreMe
        {
            get => (Flags & 32) != 0;
            set => Flags = (byte)((Flags & ~32) | (value ? 32 : 0));
        }

        /// <summary>Serializes the flags byte into the provided writer.</summary>
        public void Serialize(NetWriter w) => w.Put(Flags);

        /// <summary>Deserializes a PlayerEffectSyncMessage from the provided reader.</summary>
        public static PlayerEffectSyncMessage Deserialize(NetReader r)
            => new PlayerEffectSyncMessage { Flags = r.GetByte() };
    }

    /// <summary>
    /// Sent to trigger a save on the remote peer so that both sides persist
    /// their state simultaneously.
    /// </summary>
    public struct SaveSyncMessage
    {
        /// <summary>Serializes this message (no fields needed — just a trigger).</summary>
        public void Serialize(NetWriter w) { }
        /// <summary>Deserializes a SaveSyncMessage.</summary>
        public static SaveSyncMessage Deserialize(NetReader r) => new SaveSyncMessage();
    }

    /// <summary>
    /// Host broadcasts current game time and day to the client so the
    /// day/night cycle stays in sync across peers.
    /// </summary>
    public struct TimeSyncMessage
    {
        /// <summary>Current in-game minute (0–1439).</summary>
        public int CurrentTime;
        /// <summary>Current in-game day number.</summary>
        public int Day;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(CurrentTime);
            w.Put(Day);
        }

        /// <summary>Deserializes a TimeSyncMessage from the provided reader.</summary>
        public static TimeSyncMessage Deserialize(NetReader r) => new TimeSyncMessage
        {
            CurrentTime = r.GetInt(),
            Day = r.GetInt()
        };
    }

    /// <summary>
    /// Either peer: player's active light (flashlight/torch/lantern) toggled.
    /// The receiver looks up the item by ItemType in ItemsDatabase to spawn
    /// the correct light/particle emitter prefabs on the proxy.
    /// </summary>
    public struct PlayerLightStateMessage
    {
        public bool LightOn;

        /// <summary>Current item type (used to look up emitter prefabs on receiver).</summary>
        public string ItemType;

        /// <summary>Flashlight Light2D.LightRadius (0 = unchanged).</summary>
        public float LightRadius;
        public float LightColorR, LightColorG, LightColorB;
        /// <summary>Flashlight Light2D.LightIntensity (0 = unchanged).</summary>
        public float LightIntensity;

        /// <summary>Non-empty when the held item has a lightEmitter (torch/lantern).</summary>
        public bool HasLightEmitter;

        public void Serialize(NetWriter w)
        {
            w.Put(LightOn);
            w.Put(ItemType ?? "");
            w.Put(LightRadius);
            w.Put(LightColorR); w.Put(LightColorG); w.Put(LightColorB);
            w.Put(LightIntensity);
            w.Put(HasLightEmitter);
        }

        public static PlayerLightStateMessage Deserialize(NetReader r) => new PlayerLightStateMessage
        {
            LightOn = r.GetBool(),
            ItemType = r.GetString(),
            LightRadius = r.GetFloat(),
            LightColorR = r.GetFloat(), LightColorG = r.GetFloat(), LightColorB = r.GetFloat(),
            LightIntensity = r.GetFloat(),
            HasLightEmitter = r.GetBool()
        };
    }

    /// <summary>
    /// Client→Host: player threw a throwable item.
    /// Host spawns the thrown item at the proxy position with matching trajectory.
    /// </summary>
    public struct ThrowableSpawnMessage
    {
        public string ItemType;
        public float PosX, PosY, PosZ;
        public float AimY;
        public float Distance;

        public void Serialize(NetWriter w)
        {
            w.Put(ItemType ?? "");
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(AimY);
            w.Put(Distance);
        }

        public static ThrowableSpawnMessage Deserialize(NetReader r) => new ThrowableSpawnMessage
        {
            ItemType = r.GetString(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            AimY = r.GetFloat(),
            Distance = r.GetFloat()
        };
    }

    /// <summary>
    /// Either peer: an explosion happened at a world position.
    /// Host runs the authoritative explosion; client spawns the visual effect.
    /// </summary>
    public struct ExplosionTriggerMessage
    {
        public float PosX, PosY, PosZ;
        public string ObjectName;
        /// <summary>Whether this explosion should propagate fire (ignite gasoline, etc.).</summary>
        public bool Flaming;
        /// <summary>Name of the explosionPrefab asset (for client-side visual spawn).</summary>
        public string PrefabName;
        /// <summary>Name of the explodeSound audio clip (for client-side audio).</summary>
        public string SoundId;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(ObjectName ?? "");
            w.Put(Flaming);
            w.Put(PrefabName ?? "");
            w.Put(SoundId ?? "");
        }

        public static ExplosionTriggerMessage Deserialize(NetReader r) => new ExplosionTriggerMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            ObjectName = r.GetString(),
            Flaming = r.GetBool(),
            PrefabName = r.GetString(),
            SoundId = r.GetString()
        };
    }

    /// <summary>
    /// Either peer: play a named audio clip at the remote proxy position.
    /// </summary>
    public struct PlayerAudioMessage
    {
        public string SoundId;
        public float Volume;
        /// <summary>World position where the sound originated. NaN if position is unknown.</summary>
        public float PosX, PosY, PosZ;

        public void Serialize(NetWriter w)
        {
            w.Put(SoundId ?? "");
            w.Put(Volume);
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
        }

        public static PlayerAudioMessage Deserialize(NetReader r) => new PlayerAudioMessage
        {
            SoundId = r.GetString(),
            Volume = r.GetFloat(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat()
        };
    }

    /// <summary>
    /// Either peer: a gasoline trail was spawned at a world position.
    /// The receiver spawns the trail at the same absolute position.
    /// </summary>
    public struct GasTrailSpawnMessage
    {
        public float PosX, PosY, PosZ;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
        }

        public static GasTrailSpawnMessage Deserialize(NetReader r) => new GasTrailSpawnMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat()
        };
    }

    /// <summary>
    /// Either peer: gasoline ignited at a world position (match/torch lighting puddle).
    /// </summary>
    public struct GasIgniteMessage
    {
        public float PosX, PosY, PosZ;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
        }

        public static GasIgniteMessage Deserialize(NetReader r) => new GasIgniteMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat()
        };
    }

    /// <summary>
    /// Either peer: the local player's torso or legs animation clip changed.
    /// Sent immediately on clip transition for precise animation sync.
    /// </summary>
    public struct PlayerAnimationMessage
    {
        /// <summary>Name of the torso animation clip playing (empty if not set by event).</summary>
        public string TorsoClip;
        /// <summary>Name of the legs animation clip playing (empty if not set by event).</summary>
        public string LegsClip;

        public void Serialize(NetWriter w)
        {
            w.Put(TorsoClip ?? "");
            w.Put(LegsClip ?? "");
        }

        public static PlayerAnimationMessage Deserialize(NetReader r) => new PlayerAnimationMessage
        {
            TorsoClip = r.GetString(),
            LegsClip = r.GetString()
        };
    }

    /// <summary>
    /// Either peer: the local player switched animation library (item equip).
    /// The receiver changes the proxy's torso animator library to match.
    /// </summary>
    public struct PlayerAnimLibraryMessage
    {
        /// <summary>Animation library name (e.g. "PlayerPistolAnims", "PlayerMeleeAnims", "PlayerNoneAnims").</summary>
        public string LibraryName;

        public void Serialize(NetWriter w)
        {
            w.Put(LibraryName ?? "");
        }

        public static PlayerAnimLibraryMessage Deserialize(NetReader r) => new PlayerAnimLibraryMessage
        {
            LibraryName = r.GetString()
        };
    }

    /// <summary>
    /// Host→Client: bullet impact visual FX (blood splatter, wall hit, muzzle flash).
    /// The client spawns the same effect at the given world position.
    /// </summary>
    public struct BulletImpactMessage
    {
        /// <summary>FX prefab name: "bullet_hit_1", "Shotsplat1", "Shotsplat", "Shotsplat_stay", or "MuzzleFlash".</summary>
        public string PrefabName;
        public string PoolName;
        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;

        public void Serialize(NetWriter w)
        {
            w.Put(PrefabName ?? "");
            w.Put(PoolName ?? "");
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(RotX); w.Put(RotY); w.Put(RotZ);
        }

        public static BulletImpactMessage Deserialize(NetReader r) => new BulletImpactMessage
        {
            PrefabName = r.GetString(),
            PoolName = r.GetString(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotX = r.GetFloat(), RotY = r.GetFloat(), RotZ = r.GetFloat()
        };
    }

    /// <summary>
    /// Either peer: player fired a weapon. The receiving side spawns muzzle flash
    /// and projectile visuals at the remote proxy position.
    /// </summary>
    public struct PlayerFiredWeaponMessage
    {
        /// <summary>The weapon item type (e.g. "pistol", "shotgun").</summary>
        public string ItemType;
        /// <summary>Player's Y-axis rotation (aim direction).</summary>
        public float AimY;
        /// <summary>World position of the player when firing.</summary>
        public float PosX, PosY, PosZ;
        /// <summary>Number of projectiles to spawn (weapon's projectileAmount).</summary>
        public int ProjectileCount;

        public void Serialize(NetWriter w)
        {
            w.Put(ItemType ?? "");
            w.Put(AimY);
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(ProjectileCount);
        }

        public static PlayerFiredWeaponMessage Deserialize(NetReader r) => new PlayerFiredWeaponMessage
        {
            ItemType = r.GetString(),
            AimY = r.GetFloat(),
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            ProjectileCount = r.GetInt()
        };
    }

    /// <summary>
    /// Either peer: synchronises the state of a fuel-powered saw (fuel level + inventory) 
    /// after a convert or add-fuel operation.
    /// </summary>
    public struct SawStateMessage
    {
        /// <summary>Saw world position X (lookup key on receiver).</summary>
        public float PosX;
        /// <summary>Saw world position Y.</summary>
        public float PosY;
        /// <summary>Saw world position Z.</summary>
        public float PosZ;
        /// <summary>Current fuel in the saw.</summary>
        public float Fuel;
        /// <summary>Current amount of wood logs in the saw's inventory.</summary>
        public int WoodLogAmount;
        /// <summary>Current amount of wood planks in the saw's inventory.</summary>
        public int WoodAmount;

        /// <summary>Serializes this message into the provided writer.</summary>
        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(Fuel);
            w.Put(WoodLogAmount);
            w.Put(WoodAmount);
        }

        /// <summary>Deserializes a SawStateMessage from the provided reader.</summary>
        public static SawStateMessage Deserialize(NetReader r) => new SawStateMessage
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Fuel = r.GetFloat(),
            WoodLogAmount = r.GetInt(),
            WoodAmount = r.GetInt()
        };
    }
}
