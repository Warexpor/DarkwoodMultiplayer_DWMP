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
        private static readonly Dictionary<Vector3, bool> _lastDoorOpen = new Dictionary<Vector3, bool>();
        private static readonly Dictionary<Vector3, bool> _lastTrapTriggered = new Dictionary<Vector3, bool>();

        private static float _scanRadius = 60f;
        private static readonly Dictionary<int, GameObject> _knownTraps = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, bool> _trapResultCache = new Dictionary<int, bool>();
        private static Door[] _cachedDoors;
        private static float _lastDoorScanTime;
        private const float DoorScanInterval = 1f;

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

            int skippedPlayer = 0, skippedDoor = 0, skippedKinematic = 0, skippedTrigger = 0, skippedStatic = 0, skippedNoRb = 0;
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
                bool isDoor = rootGo.GetComponent<Door>() != null;
                string rootName = rootGo.name;
                int rootId = rootGo.GetInstanceID();

                if (isPlayer) { skippedPlayer++; continue; }
                if (isRemoteProxy) { skippedPlayer++; continue; }
                if (rootName == "Player" || rootName == "PlayerLegs" || rootName == "RemotePlayer") { skippedPlayer++; continue; }
                if (isDoor) { skippedDoor++; continue; }
                if (rootName.Contains("DoorSensor")) { skippedDoor++; continue; }
                if (rb.isKinematic) { skippedKinematic++; continue; }

                string trackingKey = rootName + "_" + rootId;
                Vector3 pos = rootGo.transform.position;

                if (!_lastPos.TryGetValue(trackingKey, out Vector3 last))
                {
                    _lastPos[trackingKey] = pos;
                    continue;
                }

                float distSq = Vector3.SqrMagnitude(pos - last);
                if (distSq < 0.01f)
                    continue;

                _lastPos[trackingKey] = pos;
                includedObjects++;

                if (_objects.Count >= 128) break;

                _objects.Add(new WorldObjectState { Name = rootName, PosX = pos.x, PosY = pos.y, PosZ = pos.z });
            }

            SyncDoors();
            SyncTraps();

            if (_objects.Count == 0 && _doors.Count == 0 && _traps.Count == 0)
            {
                return false;
            }

            msg = new PhysicsStateMessage
            {
                Objects = _objects.ToArray(),
                Doors = _doors.ToArray(),
                Traps = _traps.ToArray()
            };
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
            if (!name.Contains("trap") && !name.Contains("bear") && !name.Contains("snap") && !name.Contains("animal"))
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

                    if (go.GetComponent<Player>() != null || go.GetComponent<RemotePlayerProxy>() != null || go.GetComponent<Door>() != null || go.name.Contains("DoorSensor"))
                    {
                        objSkipped++;
                        continue;
                    }

                    Vector3 pos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
                    go.transform.position = pos;
                    objApplied++;
                }
            }

            int doorApplied = 0, doorFailed = 0, doorSkipped = 0;
            if (state.Doors != null)
            {
                ModRuntime.Log?.LogInfo("[DoorRecv] " + state.Doors.Length + " door states from " + fromPeer);
                try
                {
                    TraverseHack.ApplyingFromNetwork = true;
                    foreach (DoorState ds in state.Doors)
                    {
                        Vector3 doorPos = new Vector3(ds.PosX, ds.PosY, ds.PosZ);
                        Door door = FindDoorByPos(doorPos);
                        if (door == null)
                        {
                            ModRuntime.Log?.LogInfo("[DoorRecv] not found at " + doorPos);
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
                        TraverseHack.SetDoorOpened(door, ds.Opened);
                        doorApplied++;
                    }
                }
                finally
                {
                    TraverseHack.ApplyingFromNetwork = false;
                }
                ModRuntime.Log?.LogInfo("[DoorRecv] applied=" + doorApplied + " failed=" + doorFailed + " skipped=" + doorSkipped + " from " + fromPeer);
            }

            int trapApplied = 0, trapSkipped = 0;
            if (state.Traps != null)
            {
                foreach (TrapState ts in state.Traps)
                {
                    GameObject go = FindTrapByPos(new Vector3(ts.PosX, ts.PosY, ts.PosZ));
                    if (go == null)
                    {
                        trapSkipped++;
                        continue;
                    }

                    ApplyTrapState(go, ts.Triggered);
                    trapApplied++;
                }
            }
        }

        private static GameObject FindTrapByPos(Vector3 pos)
        {
            Collider[] nearby = Physics.OverlapSphere(pos, 0.5f);
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

            tk2dSpriteAnimator anim = go.GetComponent<tk2dSpriteAnimator>();
            if (anim != null)
            {
                if (triggered)
                {
                    string clip = anim.GetClipByName("triggered") != null ? "triggered" : null;
                    clip = clip ?? (anim.GetClipByName("snap") != null ? "snap" : null);
                    clip = clip ?? (anim.GetClipByName("sprung") != null ? "sprung" : null);
                    if (clip != null) anim.Play(clip);
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
            Door[] all = UnityEngine.Object.FindObjectsOfType<Door>();
            foreach (Door d in all)
            {
                if (d == null) continue;
                float dist = Vector3.Distance(d.transform.position, pos);
                if (dist < 0.5f) return d;
            }
            return null;
        }

        public static void Reset()
        {
            _lastPos.Clear();
            _lastDoorOpen.Clear();
            _lastTrapTriggered.Clear();
            _knownTraps.Clear();
            _trapResultCache.Clear();
        }
    }

    public struct WorldObjectState
    {
        public string Name;
        public float PosX, PosY, PosZ;

        public void Serialize(NetWriter w)
        {
            w.Put(Name ?? ""); w.Put(PosX); w.Put(PosY); w.Put(PosZ);
        }
        public static WorldObjectState Deserialize(NetReader r) => new WorldObjectState
        {
            Name = r.GetString(), PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat()
        };
    }

    public struct DoorState
    {
        public float PosX, PosY, PosZ;
        public bool Opened;

        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(Opened);
        }
        public static DoorState Deserialize(NetReader r) => new DoorState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Opened = r.GetBool()
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

    public struct PhysicsStateMessage
    {
        public WorldObjectState[] Objects;
        public DoorState[] Doors;
        public TrapState[] Traps;

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

            return new PhysicsStateMessage { Objects = objs, Doors = doors, Traps = traps };
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

        public static void SetDoorOpened(Door door, bool opened)
        {
            InvokeDoorMethod(door, opened ? "open" : "close");

            // Ensure the field is set (in case the method didn't do it)
            var t = Traverse.Create(door);
            if (t.Field("opened").GetValue<bool>() != opened)
                t.Field("opened").SetValue(opened);
        }

        private static void InvokeDoorMethod(Door door, string methodName)
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
                            if (pt == typeof(Vector3)) args[i] = Vector3.zero;
                            else if (pt == typeof(Transform)) args[i] = door.transform;
                            else if (pt == typeof(float)) args[i] = 0f;
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
