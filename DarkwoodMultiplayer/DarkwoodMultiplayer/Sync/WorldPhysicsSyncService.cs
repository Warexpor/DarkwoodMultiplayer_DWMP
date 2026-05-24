using System;
using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    /// <summary>
    /// Scans the local world for physics objects, doors, traps, and generators within range,
    /// builds snapshots for the host to broadcast, and applies received snapshots on clients.
    /// Provides per-frame interpolation for remote objects to smooth out network latency.
    /// </summary>
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

        private static int _objApplyLogCounter;
        private static float _objInterpLastLogTime;
        private static float _scanRadius = 40f;
        private static readonly Dictionary<int, GameObject> _knownTraps = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, bool> _trapResultCache = new Dictionary<int, bool>();

        private static readonly List<GeneratorState> _generators = new List<GeneratorState>();
        private static readonly Dictionary<Vector3, bool> _lastGeneratorOn = new Dictionary<Vector3, bool>();
        private static readonly Dictionary<Vector3, float> _lastGeneratorFuel = new Dictionary<Vector3, float>();

        private const float InterpFixedDuration = 0.25f;

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



        /// <summary>
        /// Scans non-player, non-static physics objects within <see cref="_scanRadius"/> of the local player,
        /// plus nearby doors, traps, and generators, and serializes changed state into <paramref name="msg"/>.
        /// Returns false when nothing has changed (avoids sending empty messages).
        /// </summary>
        /// <param name="msg">Output parameter populated with the current world snapshot.</param>
        /// <returns>True if any state was captured; false if everything is idle.</returns>
        public static bool TryBuildWorldSnapshot(out PhysicsStateMessage msg)
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

            int skippedPlayer = 0, skippedDoor = 0, skippedTrigger = 0, skippedStatic = 0, skippedNoRb = 0;

            for (int i = 0; i < hitCount && i < _overlap3D.Length; i++)
            {
                Collider col = _overlap3D[i];
                if (col == null) { continue; }

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

                // First sighting — record position but don't send until it moves
                if (!_lastPos.TryGetValue(trackingKey, out Vector3 last))
                {
                    _lastPos[trackingKey] = pos;
                    _lastMoveTime[trackingKey] = Time.time;
                    continue;
                }

                float distSq = Vector3.SqrMagnitude(pos - last);
                bool moved = distSq >= 0.0009f;
                // Keep sending for a short grace period after last movement
                // so transient stops don't cause objects to freeze mid-air
                if (!moved)
                {
                    if (_lastMoveTime.TryGetValue(trackingKey, out float lastMoved) && (Time.time - lastMoved) < 2.5f)
                        moved = true;
                }
                if (!moved)
                    continue;

                _lastPos[trackingKey] = pos;
                _lastMoveTime[trackingKey] = Time.time;

                _objectInterp.Remove(rootId);

                if (_objects.Count >= 128) break;

                Item itemComp = rootGo.GetComponent<Item>();
                string itemType = itemComp != null && itemComp.invItem != null ? itemComp.invItem.type : "";

                _objects.Add(new WorldObjectState
                {
                    Name = rootName, PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                    RotX = rot.x, RotY = rot.y, RotZ = rot.z,
                    ItemType = itemType
                });
            }

            SyncDoors();
            SyncTraps();
            SyncGenerators();

            if (_objects.Count == 0 && _doors.Count == 0 && _traps.Count == 0 && _generators.Count == 0)
                return false;

            msg = new PhysicsStateMessage
            {
                Objects = _objects.ToArray(),
                Doors = _doors.ToArray(),
                Traps = _traps.ToArray(),
                Generators = _generators.ToArray()
            };
            return true;
        }

        /// <summary>
        /// Scans tracked doors near the host player (or near the remote client proxy)
        /// and records any open/closed state changes since the last snapshot.
        /// </summary>
        private static void SyncDoors()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;

            DoorTracker.Cleanup();

            Player local = Player.Instance;
            if (local == null) return;
            Vector3 center = local.transform.position;

            // Also scan near the remote proxy so doors near the client player
            // are detected even if the host player is far away.
            Vector3 proxyCenter = center;
            if (ModRuntime.Network != null && ModRuntime.Network.RemoteProxyTransform != null)
                proxyCenter = ModRuntime.Network.RemoteProxyTransform.position;

            IList<Door> allDoors = DoorTracker.GetAll();
            for (int i = 0; i < allDoors.Count; i++)
            {
                Door door = allDoors[i];
                if (door == null) continue;

                float distToHost = Vector3.Distance(door.transform.position, center);
                float distToProxy = Vector3.Distance(door.transform.position, proxyCenter);
                if (distToHost > _scanRadius && distToProxy > _scanRadius) continue;

                Vector3 dp = door.transform.position;
                Vector3 key = new Vector3((float)Math.Round(dp.x, 1), (float)Math.Round(dp.y, 1), (float)Math.Round(dp.z, 1));

                bool opened = TraverseHack.ReadDoorOpened(door);
                if (!_lastDoorOpen.TryGetValue(key, out bool was) || was != opened)
                {
                    _lastDoorOpen[key] = opened;

                    if (_doors.Count < 64)
                    {
                        float bodyRotY = door.body != null ? door.body.eulerAngles.y : 0f;
                        _doors.Add(new DoorState
                        {
                            PosX = key.x, PosY = key.y, PosZ = key.z,
                            Opened = opened, BodyRotY = bodyRotY
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Scans previously detected traps and records any triggered state changes.
        /// </summary>
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

        /// <summary>
        /// Scans tracked generators near the host player and records any on/off or fuel changes.
        /// </summary>
        private static void SyncGenerators()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;

            Player local = Player.Instance;
            if (local == null) return;
            Vector3 center = local.transform.position;

            // Also scan for generators near the remote proxy so they are
            // synced even when the host player is far from them.
            Vector3 proxyCenter = center;
            if (ModRuntime.Network != null && ModRuntime.Network.RemoteProxyTransform != null)
                proxyCenter = ModRuntime.Network.RemoteProxyTransform.position;

            IList<Generator> allGens = GeneratorTracker.GetAll();
            for (int i = 0; i < allGens.Count; i++)
            {
                Generator gen = allGens[i];
                if (gen == null) continue;

                float distToHost = Vector3.Distance(gen.transform.position, center);
                float distToProxy = Vector3.Distance(gen.transform.position, proxyCenter);
                if (distToHost > _scanRadius && distToProxy > _scanRadius) continue;

                Vector3 dp = gen.transform.position;
                Vector3 key = new Vector3((float)Math.Round(dp.x, 1), (float)Math.Round(dp.y, 1), (float)Math.Round(dp.z, 1));
                bool isOn = gen.isOn;
                float fuel = gen.fuel;

                bool onChanged = !_lastGeneratorOn.TryGetValue(key, out bool was) || was != isOn;
                bool fuelChanged = false;
                if (!onChanged)
                {
                    if (!_lastGeneratorFuel.TryGetValue(key, out float lastFuel))
                        fuelChanged = true;
                    else if (Mathf.Abs(fuel - lastFuel) > 10f)
                        fuelChanged = true;
                }

                if (onChanged || fuelChanged)
                {
                    _lastGeneratorOn[key] = isOn;
                    _lastGeneratorFuel[key] = fuel;

                    if (_generators.Count < 8)
                    {
                        string itemType = "";
                        Item itemComp = gen.GetComponent<Item>();
                        if (itemComp != null && itemComp.invItem != null)
                            itemType = itemComp.invItem.type;

                        _generators.Add(new GeneratorState
                        {
                            PosX = key.x, PosY = key.y, PosZ = key.z,
                            IsOn = isOn, Fuel = fuel, ItemType = itemType
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Examines a trigger collider to determine if it belongs to a trap
        /// and caches the result so the scan-loop can skip it on subsequent frames.
        /// </summary>
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

        /// <summary>Returns true if the GameObject has a component with a trap-related boolean field.</summary>
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

        /// <summary>Reads the triggered/snapped/sprung/isTriggered field from a trap GameObject.</summary>
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

        /// <summary>Tries to read a boolean field via Harmony Traverse without throwing.</summary>
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

        /// <summary>
        /// Applies a received <see cref="PhysicsStateMessage"/> to the local world:
        /// positions objects (with interpolation targets), opens/closes doors,
        /// triggers traps, and syncs generators. Skips the local player and remote proxies.
        /// </summary>
        /// <param name="state">The snapshot to apply.</param>
        /// <param name="fromPeer">Identifier of the sender, used for logging.</param>
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

                    GameObject go = FindOrSpawnObject(obj);

                    if (go == null)
                    {
                        objFailed++;
                        continue;
                    }

                    if (go.GetComponent<Player>() != null || go.GetComponent<RemotePlayerProxy>() != null || go.name.Contains("DoorSensor"))
                    {
                        objSkipped++;
                        continue;
                    }

                    Vector3 pos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
                    Vector3 rot = new Vector3(obj.RotX, obj.RotY, obj.RotZ);

                    // When the host receives a snapshot from a client, apply the
                    // position instantly — the client's state is authoritative for
                    // objects near the remote player.  Interpolation is only needed
                    // on the client side for smooth visuals from host snapshots.
                    bool fromClient = fromPeer.Equals("client", StringComparison.OrdinalIgnoreCase);
                    if (fromClient)
                    {
                        Rigidbody rb = go.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.position = pos;
                            rb.rotation = Quaternion.Euler(rot);
                            rb.velocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                        else
                        {
                            go.transform.position = pos;
                            go.transform.rotation = Quaternion.Euler(rot);
                        }
                        // Also remove from interpolation so it doesn't fight the direct set
                        _objectInterp.Remove(go.GetInstanceID());
                    }
                    else
                    {
                        SetObjectTarget(go, pos, rot);
                    }
                    objApplied++;
                }
            }

            // Log summary every 15 applies to avoid spamming
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
                        TraverseHack.SetDoorOpened(door, ds.Opened, new Vector3(ds.OpenerPosX, ds.OpenerPosY, ds.OpenerPosZ), ds.OpenForce, ds.BodyRotY);
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

            if (state.Generators != null)
            {
                foreach (GeneratorState gs in state.Generators)
                {
                    Vector3 gPos = new Vector3(gs.PosX, gs.PosY, gs.PosZ);
                    Generator gen = FindGeneratorByPos(gPos);
                    if (gen == null)
                        gen = SpawnGenerator(gs);
                    if (gen == null) continue;

                    ApplyGeneratorState(gen, gs.IsOn, gs.Fuel);
                }
            }
        }

        /// <summary>Removes an object from the interpolation dictionary so it stops being smoothed.</summary>
        /// <param name="go">The GameObject to remove.</param>
        public static void RemoveObjectFromInterpolation(GameObject go)
        {
            if (go == null) return;
            _objectInterp.Remove(go.GetInstanceID());
        }

        /// <summary>
        /// Sets a new position/rotation target for an object and resets the interpolation
        /// state so it smoothly moves from its current position to the target over <see cref="InterpFixedDuration"/>.
        /// </summary>
        private static void SetObjectTarget(GameObject go, Vector3 targetPos, Vector3 targetRot)
        {
            int id = go.GetInstanceID();
            float now = Time.time;

            if (_objectInterp.TryGetValue(id, out var state))
            {
                float dur = state.TargetTime - state.PrevTime;
                if (dur > 0.001f)
                {
                    // Catch up the previous state to the current render-time lerp position
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

            state.Target = go;
            state.TargetPos = targetPos;
            state.TargetRot = targetRot;
            state.PrevTime = now;
            state.TargetTime = now + InterpFixedDuration;
            _objectInterp[id] = state;
        }

        /// <summary>
        /// Look up a GameObject by name, then by proximity, then by spawning it
        /// from the <see cref="ItemsDatabase"/> if the <see cref="WorldObjectState.ItemType"/>
        /// is known.  Returns null only when every strategy fails.
        /// </summary>
        private static GameObject FindOrSpawnObject(WorldObjectState obj)
        {
            // Strategy 1: name-based lookup (fastest)
            GameObject go = GameObject.Find(obj.Name);
            if (go != null) return go;

            Vector3 targetPos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);

            // Strategy 2: overlap sphere near the reported position
            Collider[] nearby = Physics.OverlapSphere(targetPos, 1.5f);
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null || nearby[i].attachedRigidbody == null) continue;
                GameObject candidate = nearby[i].attachedRigidbody.gameObject;
                if (candidate.name == obj.Name)
                    return candidate;
            }

            // Strategy 3: spawn from ItemsDatabase (cross-world-chunk support)
            if (string.IsNullOrEmpty(obj.ItemType))
                return null;

            if (Singleton<ItemsDatabase>.Instance == null || !Singleton<ItemsDatabase>.Instance.hasItem(obj.ItemType))
                return null;

            InvItem itemDef = Singleton<ItemsDatabase>.Instance.getItem(obj.ItemType, instantiate: false);
            if (itemDef == null || itemDef.item == null)
                return null;

            GameObject prefab = itemDef.item as GameObject;
            if (prefab == null)
                return null;

            Quaternion rot = Quaternion.Euler(obj.RotX, obj.RotY, obj.RotZ);
            GameObject spawned = Core.AddPrefab(prefab, targetPos, rot, null);
            if (spawned == null)
                spawned = UnityEngine.Object.Instantiate(prefab, targetPos, rot);

            if (spawned != null)
            {
                Rigidbody rb = spawned.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.position = targetPos;
                    rb.rotation = rot;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                ModRuntime.Log?.LogInfo("[ObjectApply] spawned \"" + obj.Name + "\" type=" + obj.ItemType + " at " + targetPos);
            }

            return spawned;
        }

        /// <summary>Finds a trap GameObject by position using collider overlap, Trigger component, and name-based fallback.</summary>
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

        /// <summary>
        /// Applies the triggered/untriggered state to a trap GameObject.
        /// When triggering, plays the sound, spawns the visual prefab, alerts nearby characters,
        /// switches the sprite, and cleans up the Item/Inventory components.
        /// Skips re-triggering if the trap is already in the target state.
        /// </summary>
        /// <param name="go">The trap GameObject.</param>
        /// <param name="triggered">Whether the trap should be set to triggered.</param>
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

        /// <summary>Tries to write a boolean field via Harmony Traverse without throwing.</summary>
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

        /// <summary>Finds a Door by position — first checks the tracker, then falls back to a scene-wide search.</summary>
        private static Door FindDoorByPos(Vector3 pos)
        {
            Door door = DoorTracker.FindByPosition(pos);
            if (door != null)
                return door;

            // Fallback: search all Door instances — catches doors that were
            // spawned dynamically after the tracker's Awake patch ran, or
            // doors from world-grid chunks the host has loaded.
            Door[] all = UnityEngine.Object.FindObjectsOfType<Door>();
            for (int i = 0; i < all.Length && i < 128; i++)
            {
                Door d = all[i];
                if (d == null) continue;
                if (Vector3.Distance(d.transform.position, pos) < 2f)
                {
                    DoorTracker.Add(d);
                    return d;
                }
            }
            return null;
        }

        /// <summary>
        /// Spawns a generator on-demand from <see cref="GeneratorState.ItemType"/>
        /// when it doesn't exist locally (e.g. remote player turned on a generator
        /// in an unloaded world chunk).
        /// </summary>
        private static Generator SpawnGenerator(GeneratorState gs)
        {
            if (string.IsNullOrEmpty(gs.ItemType))
                return null;

            if (Singleton<ItemsDatabase>.Instance == null || !Singleton<ItemsDatabase>.Instance.hasItem(gs.ItemType))
                return null;

            InvItem itemDef = Singleton<ItemsDatabase>.Instance.getItem(gs.ItemType, instantiate: false);
            if (itemDef == null || itemDef.item == null)
                return null;

            GameObject prefab = itemDef.item as GameObject;
            if (prefab == null)
                return null;

            Vector3 pos = new Vector3(gs.PosX, gs.PosY, gs.PosZ);
            Quaternion rot = Quaternion.identity;
            GameObject go = Core.AddPrefab(prefab, pos, rot, null);
            if (go == null)
                go = UnityEngine.Object.Instantiate(prefab, pos, rot);

            if (go == null) return null;

            Generator gen = go.GetComponent<Generator>();
            if (gen != null)
                GeneratorTracker.Add(gen);

            ModRuntime.Log?.LogInfo("[GeneratorSync] spawned type=" + gs.ItemType + " at " + pos);
            return gen;
        }

        /// <summary>Finds a Generator by position via the tracker.</summary>
        private static Generator FindGeneratorByPos(Vector3 pos)
        {
            Generator gen = GeneratorTracker.FindByPosition(pos);
            if (gen != null)
                return gen;

            // Fallback: search all loaded Generator instances — catches generators
            // that were spawned dynamically after the tracker's Start patch ran.
            Generator[] all = UnityEngine.Object.FindObjectsOfType<Generator>();
            for (int i = 0; i < all.Length && i < 32; i++)
            {
                Generator g = all[i];
                if (g == null) continue;
                if (Vector3.Distance(g.transform.position, pos) < 2f)
                {
                    GeneratorTracker.Add(g);
                    return g;
                }
            }
            return null;
        }

        /// <summary>
        /// Per-frame interpolation driver. Moves each tracked physics object from its previous
        /// position toward its target over a fixed 0.25 s window. Skips objects being dragged
        /// locally and removes stale entries that haven't received a new snapshot recently.
        /// On the host, resets velocity/angular velocity to prevent physics fighting the interpolation.
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

                // Skip objects being dragged by the local player — local physics
                // (HingeJoint) already drives the correct position. Interpolation
                // would fight the joint and cause jitter.
                Item item = s.Target.GetComponent<Item>();
                if (item != null && item.beingDragged)
                {
                    // Don't remove from interpolation yet; the drag might end
                    // and we want to resume smoothly. Just skip position update.
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

        /// <summary>Applies a generator's on/off state and fuel level to the local world.</summary>
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

        /// <summary>
        /// Applies a received light on/off state change from a remote peer.
        /// Looks up the light Item by position and name, then toggles it if needed.
        /// </summary>
        /// <param name="ls">The light state message.</param>
        /// <param name="fromPeer">Sender identifier for logging.</param>
        public static void ApplyLightState(LightStateMessage ls, string fromPeer)
        {
            Vector3 pos = new Vector3(ls.PosX, ls.PosY, ls.PosZ);
            Item item = FindLightByPos(pos, ls.ItemName);
            if (item == null)
            {
                // Try to spawn the light on-demand from ItemType
                if (!string.IsNullOrEmpty(ls.ItemType))
                {
                    var objState = new WorldObjectState
                    {
                        Name = ls.ItemName,
                        PosX = ls.PosX, PosY = ls.PosY, PosZ = ls.PosZ,
                        ItemType = ls.ItemType
                    };
                    GameObject go = FindOrSpawnObject(objState);
                    if (go != null)
                        item = go.GetComponent<Item>();
                }

                if (item == null)
                {
                    ModRuntime.Log?.LogInfo("[LightApply] " + ls.ItemName + " not found at " + pos);
                    return;
                }
            }

            ModRuntime.Log?.LogInfo("[LightApply] " + item.name + " isOn=" + ls.IsOn + " from " + fromPeer);

            if (ls.IsOn && !item.isOn)
                item.turnOn();
            else if (!ls.IsOn && item.isOn)
                item.turnOff();
        }

        /// <summary>Finds a light Item by position and optional name within a 1.5 unit radius.</summary>
        private static Item FindLightByPos(Vector3 pos, string name)
        {
            Collider[] nearby = Physics.OverlapSphere(pos, 5f);
            Item best = null;
            float bestDist = 5f;
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] == null) continue;
                Item item = nearby[i].GetComponentInParent<Item>();
                if (item == null) continue;
                if (!string.IsNullOrEmpty(name) && !item.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!item.isLight && !item.switchable)
                    continue;
                float d = Vector3.Distance(item.transform.position, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = item;
                }
            }
            if (best != null)
                return best;

            // Fallback: search all items by name when position-based lookup fails.
            // Some lights may be at different positions between host and client
            // due to world grid loading differences.
            if (!string.IsNullOrEmpty(name))
            {
                Item[] all = UnityEngine.Object.FindObjectsOfType<Item>();
                for (int i = 0; i < all.Length; i++)
                {
                    Item item = all[i];
                    if (item == null) continue;
                    if (!item.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!item.isLight && !item.switchable)
                        continue;
                    return item;
                }
            }
            return null;
        }

        /// <summary>Resets all cached state, tracked objects, and interpolation tables (used on scene change or disconnect).</summary>
        public static void Reset()
        {
            _lastPos.Clear();
            _lastMoveTime.Clear();
            _lastDoorOpen.Clear();
            _lastTrapTriggered.Clear();
            _knownTraps.Clear();
            _trapResultCache.Clear();
            _lastGeneratorOn.Clear();
            _objectInterp.Clear();
            DoorTracker.Clear();
            GeneratorTracker.Clear();
            CharacterTracker.Clear();
        }
    }

    /// <summary>Serializable snapshot of a physics object's transform (name, position, rotation).</summary>
    public struct WorldObjectState
    {
        /// <summary>Name of the GameObject (used as a lookup key on the receiving end).</summary>
        public string Name;
        /// <summary>World position X coordinate.</summary>
        public float PosX;
        /// <summary>World position Y coordinate.</summary>
        public float PosY;
        /// <summary>World position Z coordinate.</summary>
        public float PosZ;
        /// <summary>Euler rotation X angle.</summary>
        public float RotX;
        /// <summary>Euler rotation Y angle.</summary>
        public float RotY;
        /// <summary>Euler rotation Z angle.</summary>
        public float RotZ;
        /// <summary>
        /// Item type identifier (<see cref="Item.invItem.type"/>), used on the receiving end
        /// to spawn the object on-demand when it doesn't exist locally (e.g. unloaded world chunk).
        /// Empty for objects that have no <see cref="Item"/> component.
        /// </summary>
        public string ItemType;

        /// <summary>Serializes this state into a network writer.</summary>
        /// <param name="w">The network writer.</param>
        public void Serialize(NetWriter w)
        {
            w.Put(Name ?? ""); w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(RotX); w.Put(RotY); w.Put(RotZ);
            w.Put(ItemType ?? "");
        }
        /// <summary>Deserializes a state from a network reader.</summary>
        /// <param name="r">The network reader.</param>
        public static WorldObjectState Deserialize(NetReader r) => new WorldObjectState
        {
            Name = r.GetString(), PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            RotX = r.GetFloat(), RotY = r.GetFloat(), RotZ = r.GetFloat(),
            ItemType = r.GetString()
        };
    }

    /// <summary>Serializable snapshot of a door's open/close state, including swing rotation and opener info.</summary>
    public struct DoorState
    {
        /// <summary>Rounded world position X (serves as lookup key).</summary>
        public float PosX;
        /// <summary>Rounded world position Y.</summary>
        public float PosY;
        /// <summary>Rounded world position Z.</summary>
        public float PosZ;
        /// <summary>Whether the door is open.</summary>
        public bool Opened;
        /// <summary>World position X of the player who opened the door (used for knock-back).</summary>
        public float OpenerPosX;
        /// <summary>World position Y of the opener.</summary>
        public float OpenerPosY;
        /// <summary>World position Z of the opener.</summary>
        public float OpenerPosZ;
        /// <summary>Force magnitude applied when opening (from the original open call).</summary>
        public float OpenForce;
        /// <summary>Y-axis euler angle of the door's body, matching the sender's swing position.</summary>
        public float BodyRotY;

        /// <summary>Serializes this door state into a network writer.</summary>
        /// <param name="w">The network writer.</param>
        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(Opened);
            w.Put(OpenerPosX); w.Put(OpenerPosY); w.Put(OpenerPosZ);
            w.Put(OpenForce); w.Put(BodyRotY);
        }
        /// <summary>Deserializes a door state from a network reader.</summary>
        /// <param name="r">The network reader.</param>
        public static DoorState Deserialize(NetReader r) => new DoorState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Opened = r.GetBool(),
            OpenerPosX = r.GetFloat(), OpenerPosY = r.GetFloat(), OpenerPosZ = r.GetFloat(),
            OpenForce = r.GetFloat(), BodyRotY = r.GetFloat()
        };
    }

    /// <summary>Serializable snapshot of a trap's triggered state.</summary>
    public struct TrapState
    {
        /// <summary>Rounded world position X (serves as lookup key).</summary>
        public float PosX;
        /// <summary>Rounded world position Y.</summary>
        public float PosY;
        /// <summary>Rounded world position Z.</summary>
        public float PosZ;
        /// <summary>Whether the trap has been triggered (sprung/snapped).</summary>
        public bool Triggered;

        /// <summary>Serializes this trap state into a network writer.</summary>
        /// <param name="w">The network writer.</param>
        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(Triggered);
        }
        /// <summary>Deserializes a trap state from a network reader.</summary>
        /// <param name="r">The network reader.</param>
        public static TrapState Deserialize(NetReader r) => new TrapState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            Triggered = r.GetBool()
        };
    }

    /// <summary>Serializable snapshot of a generator's on/off state and fuel level.</summary>
    public struct GeneratorState
    {
        /// <summary>Rounded world position X (serves as lookup key).</summary>
        public float PosX;
        /// <summary>Rounded world position Y.</summary>
        public float PosY;
        /// <summary>Rounded world position Z.</summary>
        public float PosZ;
        /// <summary>Whether the generator is running.</summary>
        public bool IsOn;
        /// <summary>Current fuel level.</summary>
        public float Fuel;
        /// <summary>
        /// Item type identifier (<see cref="Item.invItem.type"/>), used on the receiving end
        /// to spawn the generator on-demand when it doesn't exist locally.
        /// </summary>
        public string ItemType;

        /// <summary>Serializes this generator state into a network writer.</summary>
        /// <param name="w">The network writer.</param>
        public void Serialize(NetWriter w)
        {
            w.Put(PosX); w.Put(PosY); w.Put(PosZ); w.Put(IsOn); w.Put(Fuel);
            w.Put(ItemType ?? "");
        }
        /// <summary>Deserializes a generator state from a network reader.</summary>
        /// <param name="r">The network reader.</param>
        public static GeneratorState Deserialize(NetReader r) => new GeneratorState
        {
            PosX = r.GetFloat(), PosY = r.GetFloat(), PosZ = r.GetFloat(),
            IsOn = r.GetBool(), Fuel = r.GetFloat(),
            ItemType = r.GetString()
        };
    }

    /// <summary>Top-level network message containing arrays of object, door, trap, and generator states.</summary>
    public struct PhysicsStateMessage
    {
        /// <summary>All physics object transforms in this snapshot.</summary>
        public WorldObjectState[] Objects;
        /// <summary>All door state changes in this snapshot.</summary>
        public DoorState[] Doors;
        /// <summary>All trap state changes in this snapshot.</summary>
        public TrapState[] Traps;
        /// <summary>All generator state changes in this snapshot.</summary>
        public GeneratorState[] Generators;

        /// <summary>Serializes the full message into a network writer.</summary>
        /// <param name="w">The network writer.</param>
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

            int gc = Generators != null ? Generators.Length : 0;
            w.Put(gc);
            for (int i = 0; i < gc; i++) Generators[i].Serialize(w);
        }

        /// <summary>Deserializes a full message from a network reader.</summary>
        /// <param name="r">The network reader.</param>
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

            int gc = r.GetInt();
            var generators = new GeneratorState[gc];
            for (int i = 0; i < gc; i++) generators[i] = GeneratorState.Deserialize(r);

            return new PhysicsStateMessage
            {
                Objects = objs, Doors = doors, Traps = traps, Generators = generators
            };
        }
    }

    /// <summary>
    /// Provides reflection-based helpers for reading and writing private fields
    /// on Door, Trigger, and other game types via Harmony Traverse.
    /// </summary>
    internal static class TraverseHack
    {
        /// <summary>Set true while we are applying a remote snapshot so patches can suppress re-broadcasts.</summary>
        public static bool ApplyingFromNetwork = false;

        /// <summary>Reads the private "opened" field from a Door instance.</summary>
        /// <param name="door">The door instance.</param>
        public static bool ReadDoorOpened(Door door)
        {
            var t = Traverse.Create(door);
            return t.Field("opened").GetValue<bool>();
        }

        /// <summary>
        /// Opens or closes a door: invokes the original open/close method via reflection,
        /// ensures the "opened" field matches, and syncs the door body's rotation
        /// so the receiver's door swing matches the sender's visual position.
        /// </summary>
        /// <param name="door">The door instance.</param>
        /// <param name="opened">True to open, false to close.</param>
        /// <param name="openerPos">Position of the player interacting with the door (used as open origin).</param>
        /// <param name="openForce">Force magnitude from the original open call.</param>
        /// <param name="bodyRotY">Target Y euler angle for the door body to match the sender's swing.</param>
        public static void SetDoorOpened(Door door, bool opened, Vector3 openerPos = default, float openForce = 0f, float bodyRotY = 0f)
        {
            InvokeDoorMethod(door, opened ? "open" : "close", openerPos, openForce);

            var t = Traverse.Create(door);
            if (t.Field("opened").GetValue<bool>() != opened)
                t.Field("opened").SetValue(opened);

            // Sync the door body's rotation so the receiver matches the sender's
            // physical swing position.  The body is what actually rotates open/closed.
            if (door.body != null)
            {
                Rigidbody doorBodyRB = door.body.GetComponent<Rigidbody>();
                if (doorBodyRB != null)
                {
                    Vector3 currentEuler = door.body.eulerAngles;
                    bool closeSnap = !opened && Mathf.Abs(Mathf.DeltaAngle(currentEuler.y, bodyRotY)) > 5f;
                    if (closeSnap || (opened && bodyRotY != 0f))
                    {
                        Quaternion targetRot = Quaternion.Euler(currentEuler.x, bodyRotY, currentEuler.z);
                        if (opened)
                        {
                            door.body.rotation = targetRot;
                            doorBodyRB.velocity = Vector3.zero;
                            doorBodyRB.angularVelocity = Vector3.zero;
                        }
                        else
                        {
                            door.body.rotation = targetRot;
                            doorBodyRB.constraints = RigidbodyConstraints.FreezeAll;
                            doorBodyRB.isKinematic = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Invokes the public or non-public "open" or "close" method on the Door type via reflection,
        /// matching the method's parameter signature. Falls back to toggling colliders and playing
        /// animation clips if reflection fails.
        /// </summary>
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
