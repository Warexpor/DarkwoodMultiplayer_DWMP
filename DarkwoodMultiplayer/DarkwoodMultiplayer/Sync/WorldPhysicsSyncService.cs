using System;
using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    public static class WorldPhysicsSyncService
    {
        private static readonly List<WorldObjectState> _objects = new List<WorldObjectState>();
        private static readonly List<DoorState> _doors = new List<DoorState>();
        private static readonly List<TrapState> _traps = new List<TrapState>();
        private static readonly Collider[] _overlap3D = new Collider[512];
        private static readonly Dictionary<string, Vector3> _lastPos = new Dictionary<string, Vector3>();
        private static readonly Dictionary<string, float> _lastMoveTime = new Dictionary<string, float>();
        private static readonly Dictionary<Vector3, bool> _lastDoorOpen = new Dictionary<Vector3, bool>();
        private static readonly Dictionary<Vector3, bool> _lastTrapTriggered = new Dictionary<Vector3, bool>();

        private static int _sendLogCounter;
        private static int _objApplyLogCounter;
        private static int _entityRecvLogCounter;
        private static float _objInterpLastLogTime;
        private static float _scanRadius = 40f;
        private static readonly Dictionary<int, GameObject> _knownTraps = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, bool> _trapResultCache = new Dictionary<int, bool>();
        private static Door[] _cachedDoors;
        private static float _lastDoorScanTime;
        private const float DoorScanInterval = 3f;

        private static readonly List<EntitySnapshot> _entities = new List<EntitySnapshot>();
        // Tracks dead entities already sent (map from InstanceID to Vector3.zero marker)
        private static readonly Dictionary<int, Vector3> _entityDeadSent = new Dictionary<int, Vector3>();
        private static bool _entitiesInRange;
        private const float EntityScanRadius = 500f;


        private static readonly List<GeneratorState> _generators = new List<GeneratorState>();
        private static readonly Dictionary<Vector3, bool> _lastGeneratorOn = new Dictionary<Vector3, bool>();
        private static Generator[] _cachedGenerators;
        private static float _lastElectricityScanTime;
        private const float ElectricityScanInterval = 3f;

        /// How long a single interpolated move should take. Slightly longer than
        /// the snapshot interval (~0.2s) so transitions overlap smoothly.
        private const float InterpFixedDuration = 0.25f;

        // Client-side per-frame entity interpolation (prevents AI tug-of-war)
        private struct EntityInterpState
        {
            public Vector3 PrevPos;
            public float PrevTime;
            public Vector3 TargetPos;
            public float TargetTime;
            public float TargetRotY;
            public string Clip;
            public short ClipFrame;
        }
        private static readonly Dictionary<int, EntityInterpState> _entityInterp = new Dictionary<int, EntityInterpState>();
        private static readonly List<int> _interpDeadKeys = new List<int>();

        // Client-side per-frame object interpolation (smooth movement for physics objects)
        private struct ObjectInterpState
        {
            public GameObject Target;
            public Vector3 PrevPos;
            public float PrevTime;
            public Vector3 TargetPos;
            public float TargetTime;
            public Vector3 PrevRot;
            public Vector3 TargetRot;
        }
        private static readonly Dictionary<int, ObjectInterpState> _objectInterp = new Dictionary<int, ObjectInterpState>();
        private static readonly List<int> _objectInterpDeadKeys = new List<int>();



        // Apply-path caches (avoid per-entity FindObjectsOfType in ApplySnapshot)
        private static Door[] _cachedDoorsApply;
        private static float _lastDoorApplyTime;
        private static Generator[] _cachedGeneratorsApply;
        private static float _lastGeneratorApplyTime;
        private const float ApplyCacheDuration = 2f;

        public static bool TryBuildSnapshot(out PhysicsStateMessage msg)
        {
            msg = default;
            Player local = Player.Instance;
            if (local == null) return false;

            Vector3 center = local.transform.position;

            int hitCount = Physics.OverlapSphereNonAlloc(center, _scanRadius, _overlap3D);

            _objects.Clear();
            _doors.Clear();
            _traps.Clear();
            _generators.Clear();
            _entitiesInRange = false;

            int skippedPlayer = 0, skippedDoor = 0, skippedTrigger = 0, skippedStatic = 0, skippedNoRb = 0;
            int includedObjects = 0;

            for (int i = 0; i < hitCount && i < _overlap3D.Length; i++)
            {
                Collider col = _overlap3D[i];
                if (col == null) { continue; }

                // Detect traps even for trigger colliders
                if (col.isTrigger)
                {
                    DetectTrap(col);
                    skippedTrigger++;
                    continue;
                }

                Rigidbody rb = col.attachedRigidbody;
                if (rb == null) { skippedNoRb++; continue; }

                GameObject rootGo = rb.gameObject;
                if (rootGo == null || rootGo.isStatic) { skippedStatic++; continue; }

                bool isPlayer = rootGo.GetComponent<Player>() != null;
                bool isRemoteProxy = rootGo.GetComponent<RemotePlayerProxy>() != null;
                string rootName = rootGo.name;
                int rootId = rootGo.GetInstanceID();

                if (isPlayer) { skippedPlayer++; continue; }
                if (isRemoteProxy) { skippedPlayer++; continue; }
                if (rootName == "Player" || rootName == "PlayerLegs" || rootName == "RemotePlayer") { skippedPlayer++; continue; }
                if (rootName.Contains("DoorSensor")) { skippedDoor++; continue; }

                string trackingKey = rootName + "_" + rootId;
                Vector3 pos = rootGo.transform.position;
                Vector3 rot = rootGo.transform.eulerAngles;

                if (!_lastPos.TryGetValue(trackingKey, out Vector3 last))
                {
                    _lastPos[trackingKey] = pos;
                    _lastMoveTime[trackingKey] = Time.time;
                    continue;
                }

                float distSq = Vector3.SqrMagnitude(pos - last);
                bool moved = distSq >= 0.0009f; // ~3 cm
                if (!moved)
                {
                    // Still send the last-known position for up to 1s after movement stops
                    // so the remote side gets the final resting place.
                    if (_lastMoveTime.TryGetValue(trackingKey, out float lastMoved) && (Time.time - lastMoved) < 2.5f)
                    {
                        moved = true;
                    }
                }
                if (!moved)
                    continue;

                _lastPos[trackingKey] = pos;
                _lastMoveTime[trackingKey] = Time.time;
                includedObjects++;

                // Local physics is now driving this object — remove any stale
                // interpolation entry so UpdateObjectInterpolation doesn't fight it.
                _objectInterp.Remove(rootId);

                if (_objects.Count >= 128) break;

                _objects.Add(new WorldObjectState { Name = rootName, PosX = pos.x, PosY = pos.y, PosZ = pos.z, RotX = rot.x, RotY = rot.y, RotZ = rot.z });
            }

            SyncDoors();
            SyncTraps();
            SyncEntities();
            SyncGenerators();

            // Always send if entities are in range (enables entity heartbeat)
            if (!_entitiesInRange && _objects.Count == 0 && _doors.Count == 0 && _traps.Count == 0 && _entities.Count == 0 && _generators.Count == 0)
            {
                return false;
            }

            msg = new PhysicsStateMessage
            {
                Objects = _objects.ToArray(),
                Doors = _doors.ToArray(),
                Traps = _traps.ToArray(),
                Entities = _entities.ToArray(),
                Generators = _generators.ToArray()
            };
            if (++_sendLogCounter % 30 == 0)
                ModRuntime.Log?.LogInfo("[SendSnapshot] objects=" + _objects.Count + " doors=" + _doors.Count
                    + " traps=" + _traps.Count + " entities=" + _entities.Count + " gens=" + _generators.Count);
            return true;
        }

        private static void SyncDoors()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;

            float now = Time.time;
            if (now - _lastDoorScanTime >= DoorScanInterval)
            {
                _cachedDoors = UnityEngine.Object.FindObjectsOfType<Door>();
                _lastDoorScanTime = now;
            }

            if (_cachedDoors == null) return;

            Player local = Player.Instance;
            if (local == null) return;
            Vector3 center = local.transform.position;

            for (int i = 0; i < _cachedDoors.Length; i++)
            {
                Door door = _cachedDoors[i];
                if (door == null) continue;

                float dist = Vector3.Distance(door.transform.position, center);
                if (dist > _scanRadius) continue;

                Vector3 dp = door.transform.position;
                Vector3 key = new Vector3((float)Math.Round(dp.x, 1), (float)Math.Round(dp.y, 1), (float)Math.Round(dp.z, 1));

                bool opened = TraverseHack.ReadDoorOpened(door);
                if (!_lastDoorOpen.TryGetValue(key, out bool was) || was != opened)
                {
                    _lastDoorOpen[key] = opened;

                    if (_doors.Count < 64)
                    {
                        _doors.Add(new DoorState
                        {
                            PosX = key.x, PosY = key.y, PosZ = key.z,
                            Opened = opened
                        });
                    }
                }
            }
        }

        private static void SyncTraps()
        {
            // Remove dead entries
            List<int> dead = null;
            foreach (var kv in _knownTraps)
            {
                if (kv.Value == null)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null)
                foreach (int id in dead)
                    _knownTraps.Remove(id);

            foreach (GameObject go in _knownTraps.Values)
            {
                if (go == null) continue;
                Vector3 pos = go.transform.position;
                Vector3 key = new Vector3((float)Math.Round(pos.x, 1), (float)Math.Round(pos.y, 1), (float)Math.Round(pos.z, 1));
                bool triggered = ReadTrapTriggered(go);
                bool changed = !_lastTrapTriggered.TryGetValue(key, out bool was) || was != triggered;
                if (changed)
                {
                    _lastTrapTriggered[key] = triggered;
                    if (_traps.Count < 32)
                        _traps.Add(new TrapState { PosX = key.x, PosY = key.y, PosZ = key.z, Triggered = triggered });
                }
            }
        }
        private static void SyncEntities()
        {
            // Host only: host is authority for all entity state.
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;

            _entities.Clear();

            Player local = Player.Instance;
            if (local == null) return;
            Vector3 center = local.transform.position;

            Character[] all = CharacterTracker.GetAll();

            int aliveInRange = 0, sentCount = 0, suppressedDead = 0;
            for (int idx = 0; idx < all.Length; idx++)
            {
                Character c = all[idx];
                if (c == null) continue;
                if (c.name.Contains("RemotePlayer")) continue;
                float dist = Vector3.Distance(c.transform.position, center);
                if (dist > EntityScanRadius)
                    continue;

                _entitiesInRange = true;

                if (_entities.Count >= 32) break;
                int id = c.GetInstanceID();
                string name = c.name;
                Vector3 pos = c.transform.position;
                Vector3 rot = c.transform.eulerAngles;

                tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
                string clip = (anim != null && anim.CurrentClip != null) ? anim.CurrentClip.name : "";
                byte hp = (byte)Mathf.Clamp((c.Health / Mathf.Max(c.maxHealth, 1f)) * 100f, 0, 100);
                bool aliveNow = c.alive;
                aliveInRange++;
                short clipFrame = anim != null && anim.CurrentClip != null ? (short)anim.CurrentFrame : (short)-1;

                // Dead entity: send once, then suppress
                if (!aliveNow)
                {
                    if (_entityDeadSent.ContainsKey(id))
                    {
                        suppressedDead++;
                        continue;
                    }
                    _entityDeadSent[id] = Vector3.zero;
                    _entities.Add(new EntitySnapshot
                    {
                        PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                        RotY = rot.y, Name = name, Clip = clip,
                        ClipFrame = clipFrame,
                        Alive = false, HealthPct = hp,
                        Index = (short)idx
                    });
                    sentCount++;
                    continue;
                }

                // Always send alive entities (no change tracking)
                _entities.Add(new EntitySnapshot
                {
                    PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                    RotY = rot.y, Name = name, Clip = clip,
                    ClipFrame = clipFrame,
                    Alive = true, HealthPct = hp,
                    Index = (short)idx
                });
                sentCount++;
            }

            if (sentCount > 0 && _sendLogCounter % 30 == 0)
                ModRuntime.Log?.LogInfo("[EntitySync] aliveInRange=" + aliveInRange + " sent=" + sentCount + " deadSuppressed=" + suppressedDead);
        }

        private static void SyncGenerators()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;

            RefreshElectricityCache();

            if (_cachedGenerators == null) return;

            Player local = Player.Instance;
            if (local == null) return;
            Vector3 center = local.transform.position;

            for (int i = 0; i < _cachedGenerators.Length; i++)
            {
                Generator gen = _cachedGenerators[i];
                if (gen == null) continue;

                float dist = Vector3.Distance(gen.transform.position, center);
                if (dist > _scanRadius) continue;

                Vector3 dp = gen.transform.position;
                Vector3 key = new Vector3((float)Math.Round(dp.x, 1), (float)Math.Round(dp.y, 1), (float)Math.Round(dp.z, 1));
                bool isOn = gen.isOn;

                if (!_lastGeneratorOn.TryGetValue(key, out bool was) || was != isOn)
                {
                    _lastGeneratorOn[key] = isOn;

                    if (_generators.Count < 8)
                        _generators.Add(new GeneratorState
                        {
                            PosX = key.x, PosY = key.y, PosZ = key.z,
                            IsOn = isOn, Fuel = gen.fuel
                        });
                }
            }
        }

        private static void RefreshElectricityCache()
        {
            float now = Time.time;
            if (now - _lastElectricityScanTime >= ElectricityScanInterval)
            {
                _cachedGenerators = UnityEngine.Object.FindObjectsOfType<Generator>();
                _lastElectricityScanTime = now;
            }
        }

        private static void DetectTrap(Collider col)
        {
            if (col == null) return;
            GameObject root = col.gameObject;
            Rigidbody rb = col.attachedRigidbody;
            if (rb != null) root = rb.gameObject;
            if (root == null) return;

            int id = root.GetInstanceID();
            if (_knownTraps.ContainsKey(id)) return;

            // Already classified
            if (_trapResultCache.TryGetValue(id, out bool knownIsTrap))
            {
                if (knownIsTrap && !_knownTraps.ContainsKey(id))
                    _knownTraps[id] = root;
                return;
            }

            // Quick name check
            string name = root.name.ToLowerInvariant();
            if (!name.Contains("trap") && !name.Contains("bear") && !name.Contains("snap") && !name.Contains("animal") && !name.Contains("mushroom"))
            {
                _trapResultCache[id] = false;
                return;
            }

            // Verify by checking for a "triggered"/"snapped"/"sprung" bool field
            if (HasTrapField(root))
            {
                _trapResultCache[id] = true;
                _knownTraps[id] = root;
            }
            else
            {
                _trapResultCache[id] = false;
            }
        }

        private static bool HasTrapField(GameObject go)
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null) continue;
                Traverse t = Traverse.Create(comp);
                bool val;
                if (TryReadBool(t, "triggered", out val)) return true;
                if (TryReadBool(t, "snapped", out val)) return true;
                if (TryReadBool(t, "sprung", out val)) return true;
                if (TryReadBool(t, "isTriggered", out val)) return true;
            }
            return false;
        }

        private static bool ReadTrapTriggered(GameObject go)
        {
            Component[] allComponents = go.GetComponents<Component>();
            foreach (Component comp in allComponents)
            {
                if (comp == null) continue;
                Traverse t = Traverse.Create(comp);
                bool val;
                if (TryReadBool(t, "triggered", out val)) return val;
                if (TryReadBool(t, "snapped", out val)) return val;
                if (TryReadBool(t, "sprung", out val)) return val;
                if (TryReadBool(t, "isTriggered", out val)) return val;
            }
            return false;
        }

        private static bool TryReadBool(Traverse t, string field, out bool val)
        {
            val = false;
            try
            {
                var f = t.Field(field);
                if (f.FieldExists())
                {
                    val = f.GetValue<bool>();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void ApplySnapshot(PhysicsStateMessage state, string fromPeer = "host")
        {
            int objApplied = 0, objSkipped = 0, objFailed = 0;
            if (state.Objects != null)
            {
                foreach (WorldObjectState obj in state.Objects)
                {
                    if (obj.Name == "Player" || obj.Name == "PlayerLegs" || obj.Name == "RemotePlayer" || obj.Name.Contains("DoorSensor"))
                    {
                        objSkipped++;
                        continue;
                    }

                    GameObject go = GameObject.Find(obj.Name);
                    if (go == null)
                    {
                        Vector3 targetPos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
                        Collider[] nearby = Physics.OverlapSphere(targetPos, 1.5f);
                        GameObject fallback = null;
                        for (int i = 0; i < nearby.Length; i++)
                        {
                            if (nearby[i] != null && nearby[i].attachedRigidbody != null)
                            {
                                GameObject candidate = nearby[i].attachedRigidbody.gameObject;
                                if (candidate.name == obj.Name)
                                {
                                    fallback = candidate;
                                    break;
                                }
                            }
                        }

                        if (fallback != null)
                            go = fallback;
                        else
                        {
                            objFailed++;
                            continue;
                        }
                    }

                    if (go.GetComponent<Player>() != null || go.GetComponent<RemotePlayerProxy>() != null || go.name.Contains("DoorSensor"))
                    {
                        objSkipped++;
                        continue;
                    }

                    Vector3 pos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
                    Vector3 rot = new Vector3(obj.RotX, obj.RotY, obj.RotZ);

                    SetObjectTarget(go, pos, rot);
                    objApplied++;
                }
            }

            if ((objApplied > 0 || objFailed > 0) && ++_objApplyLogCounter % 15 == 0)
                ModRuntime.Log?.LogInfo("[ObjectApply] applied=" + objApplied + " skipped=" + objSkipped + " failed=" + objFailed + " from " + fromPeer);

            int doorApplied = 0, doorFailed = 0, doorSkipped = 0;
            if (state.Doors != null)
            {
                try
                {
                    TraverseHack.ApplyingFromNetwork = true;
                    foreach (DoorState ds in state.Doors)
                    {
                        Vector3 doorPos = new Vector3(ds.PosX, ds.PosY, ds.PosZ);
                        Door door = FindDoorByPos(doorPos);
                        if (door == null)
                        {
                            doorFailed++;
                            continue;
                        }

                        bool currentOpened = TraverseHack.ReadDoorOpened(door);
                        if (currentOpened == ds.Opened)
                        {
                            doorSkipped++;
                            continue;
                        }

                        ModRuntime.Log?.LogInfo("[DoorApply] " + door.name + " " + (ds.Opened ? "OPEN" : "CLOSE") + " from " + fromPeer);
                        TraverseHack.SetDoorOpened(door, ds.Opened, new Vector3(ds.OpenerPosX, ds.OpenerPosY, ds.OpenerPosZ), ds.OpenForce);
                        doorApplied++;
                    }
                }
                finally
                {
                    TraverseHack.ApplyingFromNetwork = false;
                }
                if (doorApplied > 0 || doorFailed > 0)
                    ModRuntime.Log?.LogInfo("[DoorRecv] applied=" + doorApplied + " failed=" + doorFailed + " skipped=" + doorSkipped + " from " + fromPeer);
            }

            int trapApplied = 0, trapSkipped = 0;
            if (state.Traps != null)
            {
                try
                {
                    foreach (TrapState ts in state.Traps)
                    {
                        Vector3 tPos = new Vector3(ts.PosX, ts.PosY, ts.PosZ);
                        GameObject go = FindTrapByPos(tPos);
                        if (go == null)
                        {
                            trapSkipped++;
                            continue;
                        }

                        ModRuntime.Log?.LogInfo("[TrapApply] " + go.name + " at " + tPos + " triggered=" + ts.Triggered);
                        ApplyTrapState(go, ts.Triggered);
                        trapApplied++;
                    }
                }
                catch (Exception ex)
                {
                    ModRuntime.Log?.LogError("[TrapApply] Exception: " + ex);
                }
                if (trapApplied > 0 || trapSkipped > 0)
                    ModRuntime.Log?.LogInfo("[TrapRecv] applied=" + trapApplied + " skipped=" + trapSkipped + " from " + fromPeer);
            }

            if (state.Entities != null)
            {
                int entityApplied = 0, entitySkipped = 0;
                try
                {
                    bool isHost = ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Host;

                    foreach (EntitySnapshot es in state.Entities)
                    {
                        Vector3 ePos = new Vector3(es.PosX, es.PosY, es.PosZ);
                        Character c = FindCharacterByNamePos(es.Name, ePos, es.Index);
                        if (c == null)
                        {
                            entitySkipped++;
                            continue;
                        }

                        // Host receives client's player state, not entity snapshots (host is authority).
                        // Client always applies host positions with lerp.
                        bool applyPos = !isHost;

                        ApplyEntityState(c, es, applyPos);
                        entityApplied++;
                    }
                }
                catch (Exception ex)
                {
                    ModRuntime.Log?.LogError("[EntityApply] Exception: " + ex);
                }
                if (entityApplied > 0 && ++_entityRecvLogCounter % 30 == 0)
                    ModRuntime.Log?.LogInfo("[EntityRecv] applied=" + entityApplied + " skipped=" + entitySkipped + " from " + fromPeer);
            }

            if (state.Generators != null)
            {
                foreach (GeneratorState gs in state.Generators)
                {
                    Vector3 gPos = new Vector3(gs.PosX, gs.PosY, gs.PosZ);
                    Generator gen = FindGeneratorByPos(gPos);
                    if (gen == null) continue;

                    ApplyGeneratorState(gen, gs.IsOn, gs.Fuel);
                }
            }
        }

        private static GameObject FindTrapByPos(Vector3 pos)
        {
            Collider[] nearby = Physics.OverlapSphere(pos, 1.5f);
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null) continue;
                GameObject root = nearby[i].gameObject;
                Rigidbody rb = nearby[i].attachedRigidbody;
                if (rb != null) root = rb.gameObject;
                if (root == null) continue;
                if (HasTrapField(root))
                    return root;
            }

            // Fallback: search Trigger components by position
            Trigger[] triggers = UnityEngine.Object.FindObjectsOfType<Trigger>();
            Vector3 rounded = new Vector3((float)Math.Round(pos.x, 1), (float)Math.Round(pos.y, 1), (float)Math.Round(pos.z, 1));
            foreach (Trigger t in triggers)
            {
                if (t == null) continue;
                Vector3 tp = t.transform.position;
                Vector3 tk = new Vector3((float)Math.Round(tp.x, 1), (float)Math.Round(tp.y, 1), (float)Math.Round(tp.z, 1));
                if (tk == rounded)
                    return t.gameObject;
            }

            // Last resort: wider search by name + position
            Collider[] wide = Physics.OverlapSphere(pos, 5f);
            for (int i = 0; i < wide.Length; i++)
            {
                if (wide[i] == null) continue;
                GameObject root = wide[i].gameObject;
                Rigidbody rb = wide[i].attachedRigidbody;
                if (rb != null) root = rb.gameObject;
                if (root == null) continue;
                Vector3 rp = root.transform.position;
                Vector3 rk = new Vector3((float)Math.Round(rp.x, 1), (float)Math.Round(rp.y, 1), (float)Math.Round(rp.z, 1));
                if (rk == rounded && (root.name.ToLowerInvariant().Contains("mushroom") || root.name.ToLowerInvariant().Contains("trap") || root.name.ToLowerInvariant().Contains("bear")))
                    return root;
            }

            return null;
        }

        private static void ApplyTrapState(GameObject go, bool triggered)
        {
            if (go == null) return;

            bool current = ReadTrapTriggered(go);
            if (current == triggered) return;

            Component[] allComponents = go.GetComponents<Component>();
            foreach (Component comp in allComponents)
            {
                if (comp == null) continue;
                Traverse t = Traverse.Create(comp);
                if (TryWriteBool(t, "triggered", triggered)) break;
                if (TryWriteBool(t, "snapped", triggered)) break;
                if (TryWriteBool(t, "sprung", triggered)) break;
                if (TryWriteBool(t, "isTriggered", triggered)) break;
            }

            if (triggered)
            {
                Trigger trig = go.GetComponent<Trigger>();

                // Play explosion sound
                if (trig != null && !string.IsNullOrEmpty(trig.activateSound))
                    AudioController.Play(trig.activateSound, go.transform);

                // Spawn explosion visual prefab
                if (trig != null && trig.prefabToSpawn != null)
                {
                    Vector3 spawnPos = go.transform.position + new Vector3(0f, 1f, 0f);
                    try
                    {
                        Core.AddPrefab(trig.prefabToSpawn, spawnPos, Quaternion.Euler(90f, 0f, 0f), null);
                    }
                    catch
                    {
                        UnityEngine.Object.Instantiate(trig.prefabToSpawn, spawnPos, Quaternion.Euler(90f, 0f, 0f));
                    }
                }

                // Alert characters in radius
                if (trig != null && trig.alertRadius > 0f)
                    Character.alertInArea(go.transform.position, trig.alertRadius, dangerousSound: false, 1f);

                // Visual sprite + name change (matches original game's OnAfterTrigger call via waitFramesAndRun)
                if (trig != null)
                    trig.switchToTriggered();

                // Cancel disarm in progress
                Item item = go.GetComponent<Item>();
                if (item != null)
                    item.onTriggerFire();

                // Destroy Item only if the prefab is configured to remove it
                // (if dontDestroyItemAfterTriggering is true, Item stays for hover/name display)
                if (trig == null || !trig.dontDestroyItemAfterTriggering)
                {
                    if (item != null)
                        UnityEngine.Object.Destroy(item);
                }

                // Destroy Inventory only if configured to remove it
                if (trig == null || !trig.dontRemoveInventoryAfterTriggering)
                {
                    Inventory inv = go.GetComponent<Inventory>();
                    if (inv != null)
                        UnityEngine.Object.Destroy(inv);
                    if (item != null)
                        item.invItem = null;
                }
            }
        }

        private static bool TryWriteBool(Traverse t, string field, bool val)
        {
            try
            {
                var f = t.Field(field);
                if (f.FieldExists())
                {
                    f.SetValue(val);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static Door FindDoorByPos(Vector3 pos)
        {
            Door[] all = GetCachedDoorsApply();
            foreach (Door d in all)
            {
                if (d == null) continue;
                float dist = Vector3.Distance(d.transform.position, pos);
                if (dist < 0.5f) return d;
            }
            return null;
        }

        private static Door[] GetCachedDoorsApply()
        {
            float now = Time.time;
            if (_cachedDoorsApply == null || now - _lastDoorApplyTime > ApplyCacheDuration)
            {
                _cachedDoorsApply = UnityEngine.Object.FindObjectsOfType<Door>();
                _lastDoorApplyTime = now;
            }
            return _cachedDoorsApply;
        }

        private static Generator[] GetCachedGeneratorsApply()
        {
            float now = Time.time;
            if (_cachedGeneratorsApply == null || now - _lastGeneratorApplyTime > ApplyCacheDuration)
            {
                _cachedGeneratorsApply = UnityEngine.Object.FindObjectsOfType<Generator>();
                _lastGeneratorApplyTime = now;
            }
            return _cachedGeneratorsApply;
        }

        private static Character FindCharacterByNamePos(string name, Vector3 pos, short index = -1)
        {
            if (string.IsNullOrEmpty(name)) return null;

            Character[] all = CharacterTracker.GetAll();

            // Phase 1: match by CharacterTracker index (deterministic — same save file order).
            if (index >= 0 && index < all.Length)
            {
                Character c = all[index];
                if (c != null && !c.name.Contains("RemotePlayer") && c.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            // Phase 2: name + position (80m radius)
            Character best = null;
            float bestDist = EntityScanRadius;
            int candidates = 0;
            foreach (Character c in all)
            {
                if (c == null) continue;
                if (c.name.Contains("RemotePlayer")) continue;
                bool nameMatch = c.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || c.name.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(c.name, StringComparison.OrdinalIgnoreCase);
                if (!nameMatch) continue;
                candidates++;
                float d = Vector3.Distance(c.transform.position, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            if (best != null)
                return best;

            // Phase 3: name-only for unique names (avoids wrong-match on duplicates like 3×"Dog")
            Character nameOnlyMatch = null;
            int nameOnlyCount = 0;
            foreach (Character c in all)
            {
                if (c == null) continue;
                if (c.name.Contains("RemotePlayer")) continue;
                if (!c.name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                nameOnlyCount++;
                nameOnlyMatch = c;
            }

            if (nameOnlyCount == 1 && nameOnlyMatch != null)
                return nameOnlyMatch;

            ModRuntime.Log?.LogWarning("[EntityMatch] FAILED to find " + name + " (index=" + index + ", name-only=" + nameOnlyCount + ") — total chars: " + all.Length);
            return null;
        }

        /// <summary>
        /// Called when a snapshot is received. Stores interpolation keyframe and
        /// applies immediate state (death). Position and animation are applied
        /// per-frame via UpdateEntityInterpolation (LateUpdate) to override client AI.
        /// </summary>
        private static void ApplyEntityState(Character c, EntitySnapshot es, bool applyPos)
        {
            Vector3 targetPos = new Vector3(es.PosX, es.PosY, es.PosZ);
            float dist = Vector3.Distance(c.transform.position, targetPos);

            // Store interpolation target + clip for per-frame LateUpdate application
            if (applyPos)
                SetEntityTarget(c, targetPos, es.RotY, es.Clip, es.ClipFrame);

            // Rotation — set immediately too for responsiveness
            Vector3 euler = c.transform.eulerAngles;
            euler.y = es.RotY;
            c.transform.eulerAngles = euler;

            // Death — must happen immediately (not lerpable)
            bool didKill = false;
            if (!es.Alive && c.alive)
            {
                c.die();
                didKill = true;
            }

            if (dist > 0.5f || didKill)
                ModRuntime.Log?.LogInfo("[EntityApply] " + es.Name
                    + " applyPos=" + applyPos
                    + " dist=" + dist.ToString("F1")
                    + " die=" + didKill
                    + " hp=" + es.HealthPct);
        }

        /// <summary>
        /// Records a new interpolation keyframe for a character known to the client.
        /// Stores clip info so per-frame LateUpdate can override client AI animation.
        /// </summary>
        private static void SetEntityTarget(Character c, Vector3 targetPos, float rotY, string clip = null, short clipFrame = -1)
        {
            int id = c.GetInstanceID();
            float now = Time.time;

            if (_entityInterp.TryGetValue(id, out var state))
            {
                state.PrevPos = state.TargetPos;
                state.PrevTime = state.TargetTime;
            }
            else
            {
                state.PrevPos = c.transform.position;
                state.PrevTime = now;
            }

            state.TargetPos = targetPos;
            state.TargetTime = now;
            state.TargetRotY = rotY;
            state.Clip = clip;
            state.ClipFrame = clipFrame;
            _entityInterp[id] = state;
        }

        /// <summary>
        /// Records a new interpolation keyframe for a physics object known to the client.
        /// </summary>
        private static void SetObjectTarget(GameObject go, Vector3 targetPos, Vector3 targetRot)
        {
            int id = go.GetInstanceID();
            float now = Time.time;

            if (_objectInterp.TryGetValue(id, out var state))
            {
                // Compute current interpolated visual position as new PrevPos
                // so the next interpolation seamlessly continues from where we are,
                // avoiding a snap to the old full-TargetPos.
                float dur = state.TargetTime - state.PrevTime;
                if (dur > 0.001f)
                {
                    float t = Mathf.Clamp01((now - state.PrevTime) / dur);
                    state.PrevPos = Vector3.Lerp(state.PrevPos, state.TargetPos, t);
                    Quaternion prevRotQ = Quaternion.Euler(state.PrevRot);
                    Quaternion targetRotQ = Quaternion.Euler(state.TargetRot);
                    state.PrevRot = Quaternion.Slerp(prevRotQ, targetRotQ, t).eulerAngles;
                }
                else
                {
                    state.PrevPos = state.TargetPos;
                    state.PrevRot = state.TargetRot;
                }
            }
            else
            {
                state.Target = go;
                state.PrevPos = go.transform.position;
                state.PrevRot = go.transform.eulerAngles;
            }

            // Always set in case the go changed (e.g. scene reload)
            state.Target = go;
            state.TargetPos = targetPos;
            state.TargetRot = targetRot;
            state.PrevTime = now;
            state.TargetTime = now + InterpFixedDuration;
            _objectInterp[id] = state;
        }

        /// <summary>
        /// Called every frame on the client. Interpolates entity positions between the
        /// previous and latest host snapshot, overriding any client-AI movement.
        /// </summary>
        public static void UpdateEntityInterpolation()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role == NetworkRole.Host)
                return;

            _interpDeadKeys.Clear();
            float now = Time.time;

            foreach (var kvp in _entityInterp)
            {
                var s = kvp.Value;

                // Remove stale entries (no snapshot for >1.5s)
                if (now - s.TargetTime > 1.5f)
                {
                    _interpDeadKeys.Add(kvp.Key);
                    continue;
                }

                Character c = null;
                Character[] all = CharacterTracker.GetAll();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].GetInstanceID() == kvp.Key)
                    {
                        c = all[i];
                        break;
                    }
                }
                if (c == null || !c.alive)
                {
                    _interpDeadKeys.Add(kvp.Key);
                    continue;
                }

                // Compute interpolation factor between prev and target snapshots
                float duration = s.TargetTime - s.PrevTime;
                float elapsed = now - s.PrevTime;
                float t = duration > 0.001f ? Mathf.Clamp01(elapsed / duration) : 1f;

                c.transform.position = Vector3.Lerp(s.PrevPos, s.TargetPos, t);

                Vector3 euler = c.transform.eulerAngles;
                euler.y = s.TargetRotY;
                c.transform.eulerAngles = euler;

                // Animation — apply every frame in LateUpdate to override client AI
                if (!string.IsNullOrEmpty(s.Clip))
                {
                    tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
                    if (anim != null)
                    {
                        bool clipChanged = anim.CurrentClip == null || anim.CurrentClip.name != s.Clip;
                        if (clipChanged && anim.GetClipByName(s.Clip) != null)
                            anim.Play(s.Clip);

                        if (s.ClipFrame >= 0 && anim.CurrentClip != null)
                        {
                            int maxFrame = anim.CurrentClip.frames.Length - 1;
                            if (maxFrame >= 0)
                                anim.SetFrame(Mathf.Clamp(s.ClipFrame, 0, maxFrame), false);
                        }
                    }
                }
            }

            foreach (int key in _interpDeadKeys)
                _entityInterp.Remove(key);
        }

        /// <summary>
        /// Called every frame on the client. Interpolates physics object transforms
        /// between the most recent host snapshots for smooth visual movement.
        /// </summary>
        public static void UpdateObjectInterpolation()
        {
            if (ModRuntime.Network == null)
                return;

            _objectInterpDeadKeys.Clear();
            float now = Time.time;

            if (_objectInterp.Count > 0 && now - _objInterpLastLogTime >= 30f)
            {
                _objInterpLastLogTime = now;
                ModRuntime.Log?.LogInfo("[ObjInterp] active=" + _objectInterp.Count);
            }

            foreach (var kvp in _objectInterp)
            {
                var s = kvp.Value;

                if (s.Target == null)
                {
                    _objectInterpDeadKeys.Add(kvp.Key);
                    continue;
                }

                // Remove stale entries: no new snapshot received for a while.
                // TargetTime is in the future (PrevTime + InterpFixedDuration), so
                // the effective timeout is InterpFixedDuration + 1.5s since PrevTime.
                if (s.PrevTime > 0 && now - s.PrevTime > (InterpFixedDuration + 1.5f))
                {
                    _objectInterpDeadKeys.Add(kvp.Key);
                    continue;
                }

                // Fixed-duration interpolation — move from PrevPos to TargetPos
                // smoothly over InterpFixedDuration seconds.
                float duration = s.TargetTime - s.PrevTime; // always InterpFixedDuration
                float elapsed = now - s.PrevTime;
                float t = duration > 0.001f ? Mathf.Clamp01(elapsed / duration) : 1f;

                Vector3 lerpPos = Vector3.Lerp(s.PrevPos, s.TargetPos, t);
                Quaternion prevRotQ = Quaternion.Euler(s.PrevRot);
                Quaternion targetRotQ = Quaternion.Euler(s.TargetRot);
                Quaternion lerpRot = Quaternion.Slerp(prevRotQ, targetRotQ, t);

                Rigidbody rb = s.Target.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = lerpPos;
                    rb.rotation = lerpRot;
                    if (ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Host)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
                else
                {
                    s.Target.transform.position = lerpPos;
                    s.Target.transform.rotation = lerpRot;
                }
            }

            foreach (int key in _objectInterpDeadKeys)
                _objectInterp.Remove(key);
        }

        private static Generator FindGeneratorByPos(Vector3 pos)
        {
            Generator[] all = GetCachedGeneratorsApply();
            foreach (Generator g in all)
            {
                if (g == null) continue;
                float dist = Vector3.Distance(g.transform.position, pos);
                if (dist < 0.5f) return g;
            }
            return null;
        }

        private static void ApplyGeneratorState(Generator gen, bool isOn, float fuel)
        {
            if (gen.isOn == isOn)
            {
                // Still sync fuel level even if state matches
                var tGen = Traverse.Create(gen);
                tGen.Field("fuel").SetValue(fuel);
                return;
            }

            if (isOn)
                gen.turnOn();
            else
                gen.turnOff();

            // Override fuel to host value
            var t = Traverse.Create(gen);
            t.Field("fuel").SetValue(fuel);
        }

        public static void ApplyLightState(LightStateMessage ls, string fromPeer)
        {
            Vector3 pos = new Vector3(ls.PosX, ls.PosY, ls.PosZ);
            Item item = FindLightByPos(pos, ls.ItemName);
            if (item == null)
            {
                ModRuntime.Log?.LogInfo("[LightApply] " + ls.ItemName + " not found at " + pos);
                return;
            }

            ModRuntime.Log?.LogInfo("[LightApply] " + item.name + " isOn=" + ls.IsOn + " from " + fromPeer);

            if (ls.IsOn && !item.isOn)
                item.turnOn();
            else if (!ls.IsOn && item.isOn)
                item.turnOff();
        }

        private static Item FindLightByPos(Vector3 pos, string name)
        {
            Collider[] nearby = Physics.OverlapSphere(pos, 1.5f);
            Item best = null;
            float bestDist = 1.5f;
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null) continue;
                Item item = nearby[i].GetComponentInParent<Item>();
                if (item == null) continue;
                if (!string.IsNullOrEmpty(name) && !item.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                float d = Vector3.Distance(item.transform.position, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = item;
                }
            }
            return best;
        }

        public static void Reset()
        {
            _lastPos.Clear();
            _lastMoveTime.Clear();
            _lastDoorOpen.Clear();
            _lastTrapTriggered.Clear();
            _knownTraps.Clear();
            _trapResultCache.Clear();
            _lastGeneratorOn.Clear();
            _entityDeadSent.Clear();
            _entityInterp.Clear();
            _objectInterp.Clear();
            _cachedDoorsApply = null;
            _cachedGeneratorsApply = null;
            CharacterTracker.Clear();
        }
    }

    public struct WorldObjectState
    {
        public string Name;
        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ;

        public void Serialize(NetWriter w)
        {
            w.Put(Name ?? ""); w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(RotX); w.Put(RotY); w.Put(RotZ);
        }
        public static WorldObjectState Deserialize(NetReader r) => new WorldObjectState
        {
            Name = r.GetString(), PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotX = r.GetFloat(), RotY = r.GetFloat(), RotZ = r.GetFloat()
        };
    }

    public struct DoorState
    {
        public float PosX, PosY, PosZ;
        public bool Opened;
        public float OpenerPosX, OpenerPosY, OpenerPosZ;
        public float OpenForce;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(Opened);
            w.Put(OpenerPosX); w.Put(OpenerPosY); w.Put(OpenerPosZ);
            w.Put(OpenForce);
        }
        public static DoorState Deserialize(NetReader r) => new DoorState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Opened = r.GetBool(),
            OpenerPosX = r.GetFloat(), OpenerPosY = r.GetFloat(), OpenerPosZ = r.GetFloat(),
            OpenForce = r.GetFloat()
        };
    }

    public struct TrapState
    {
        public float PosX, PosY, PosZ;
        public bool Triggered;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(Triggered);
        }
        public static TrapState Deserialize(NetReader r) => new TrapState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Triggered = r.GetBool()
        };
    }

    public struct EntitySnapshot
    {
        public float PosX, PosY, PosZ;
        public float RotY;
        public string Name;
        public string Clip;
        public short ClipFrame; // current frame index of the animation clip (-1 if no anim)
        public bool Alive;
        public byte HealthPct;
        public short Index; // index in CharacterTracker list (deterministic across host/client)

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(RotY);
            w.Put(Name ?? ""); w.Put(Clip ?? "");
            w.Put(ClipFrame); w.Put(Alive); w.Put(HealthPct); w.Put(Index);
        }
        public static EntitySnapshot Deserialize(NetReader r) => new EntitySnapshot
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotY = r.GetFloat(),
            Name = r.GetString(), Clip = r.GetString(),
            ClipFrame = r.GetShort(), Alive = r.GetBool(), HealthPct = r.GetByte(),
            Index = r.GetShort()
        };
    }

    public struct GeneratorState
    {
        public float PosX, PosY, PosZ;
        public bool IsOn;
        public float Fuel;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ);
            w.Put(IsOn); w.Put(Fuel);
        }
        public static GeneratorState Deserialize(NetReader r) => new GeneratorState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            IsOn = r.GetBool(), Fuel = r.GetFloat()
        };
    }

    public struct PhysicsStateMessage
    {
        public WorldObjectState[] Objects;
        public DoorState[] Doors;
        public TrapState[] Traps;
        public EntitySnapshot[] Entities;
        public GeneratorState[] Generators;

        public void Serialize(NetWriter w)
        {
            int oc = Objects != null ? Objects.Length : 0;
            w.Put(oc);
            for (int i = 0; i < oc; i++) Objects[i].Serialize(w);

            int dc = Doors != null ? Doors.Length : 0;
            w.Put(dc);
            for (int i = 0; i < dc; i++) Doors[i].Serialize(w);

            int tc = Traps != null ? Traps.Length : 0;
            w.Put(tc);
            for (int i = 0; i < tc; i++) Traps[i].Serialize(w);

            int ec = Entities != null ? Entities.Length : 0;
            w.Put(ec);
            for (int i = 0; i < ec; i++) Entities[i].Serialize(w);

            int gc = Generators != null ? Generators.Length : 0;
            w.Put(gc);
            for (int i = 0; i < gc; i++) Generators[i].Serialize(w);
        }

        public static PhysicsStateMessage Deserialize(NetReader r)
        {
            int oc = r.GetInt();
            var objs = new WorldObjectState[oc];
            for (int i = 0; i < oc; i++) objs[i] = WorldObjectState.Deserialize(r);

            int dc = r.GetInt();
            var doors = new DoorState[dc];
            for (int i = 0; i < dc; i++) doors[i] = DoorState.Deserialize(r);

            int tc = r.GetInt();
            var traps = new TrapState[tc];
            for (int i = 0; i < tc; i++) traps[i] = TrapState.Deserialize(r);

            int ec = r.GetInt();
            var entities = new EntitySnapshot[ec];
            for (int i = 0; i < ec; i++) entities[i] = EntitySnapshot.Deserialize(r);

            int gc = r.GetInt();
            var generators = new GeneratorState[gc];
            for (int i = 0; i < gc; i++) generators[i] = GeneratorState.Deserialize(r);

            return new PhysicsStateMessage
            {
                Objects = objs, Doors = doors, Traps = traps, Entities = entities,
                Generators = generators
            };
        }
    }

    internal static class TraverseHack
    {
        public static bool ApplyingFromNetwork = false;

        public static bool ReadDoorOpened(Door door)
        {
            var t = Traverse.Create(door);
            return t.Field("opened").GetValue<bool>();
        }

        public static void SetDoorOpened(Door door, bool opened, Vector3 openerPos = default, float openForce = 0f)
        {
            InvokeDoorMethod(door, opened ? "open" : "close", openerPos, openForce);

            var t = Traverse.Create(door);
            if (t.Field("opened").GetValue<bool>() != opened)
                t.Field("opened").SetValue(opened);
        }

        private static void InvokeDoorMethod(Door door, string methodName, Vector3 openerPos = default, float openForce = 0f)
        {
            try
            {
                bool opening = (methodName == "open");
                bool invoked = false;

                // Try calling the original method via reflection (handles colliders, animation, internal state)
                try
                {
                    var methods = typeof(Door).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        foreach (var m in methods)
                        {
                            if (m.Name != methodName) continue;
                            var pars = m.GetParameters();
                            object[] args = new object[pars.Length];
                            for (int i = 0; i < pars.Length; i++)
                            {
                                Type pt = pars[i].ParameterType;
                                if (pt == typeof(Vector3)) args[i] = opening ? openerPos : Vector3.zero;
                                else if (pt == typeof(Transform))
                                {
                                    // Don't pass a transform so open() doesn't overwrite openerPos with the door's position
                                    if (opening && openerPos != default)
                                        args[i] = null;
                                    else
                                        args[i] = door.transform;
                                }
                                else if (pt == typeof(float)) args[i] = opening ? openForce : 0f;
                            else if (pt == typeof(bool)) args[i] = opening;
                            else if (pt == typeof(int)) args[i] = 0;
                            else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                        }
                        m.Invoke(door, args);
                        invoked = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    ModRuntime.Log?.LogWarning("[DoorReflect] failed for " + methodName + ": " + ex.Message);
                }

                if (invoked)
                    return;

                // Fallback: toggle colliders and play animation
                foreach (Collider c in door.GetComponentsInChildren<Collider>(true))
                {
                    if (c != null && !c.isTrigger)
                        c.enabled = !opening;
                }

                tk2dSpriteAnimator anim = door.GetComponentInChildren<tk2dSpriteAnimator>();
                if (anim != null)
                {
                    string clip = methodName;
                    if (anim.GetClipByName(clip) != null) anim.Play(clip);
                    else if (anim.GetClipByName(opening ? "Open" : "Close") != null) anim.Play(opening ? "Open" : "Close");
                }
            }
            catch (Exception ex)
            {
                ModRuntime.Log?.LogWarning("[DoorAnim] " + ex.Message);
            }
        }
    }
}
