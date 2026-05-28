using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    public static class ClientEntityInterpolationService
    {
        private class EntityInterpState
        {
            public Vector3 previousPosition;
            public Vector3 targetPosition;
            public float previousRotY;
            public float targetRotY;
            public float arrivalTime;
            public bool hasTarget;
            public bool alive;
            public bool isFirst;
            public float staleSince;
        }

        private static readonly Dictionary<short, EntityInterpState> _states = new Dictionary<short, EntityInterpState>(64);
        private static readonly Dictionary<short, Vector3> _displayPositions = new Dictionary<short, Vector3>(64);
        private static readonly Dictionary<short, float> _displayRotations = new Dictionary<short, float>(64);
        private static readonly List<short> _staleKeys = new List<short>(16);

        private const float SnapshotInterval = 0.1f;
        private const float MaxInterpDelay = 0.3f;
        private const float PendingMatchTimeout = 0.5f;
        private const float MatchRadius = 15f;
        private const float PhantomCleanupDelay = 5f;
        private const float UnmatchedCleanupDelay = 5f;

        private static int _lastApplyCount;
        private static int _lastSkippedCount;
        private static int _totalApplied;
        private static int _totalSkipped;
        private static int _snapshotCount;

        private static readonly HashSet<short> _hostSyncedIds = new HashSet<short>();
        private static readonly HashSet<short> _spawnedPhantomIds = new HashSet<short>();
        private static readonly HashSet<short> _audioStoppedIds = new HashSet<short>();
        private static readonly HashSet<short> _everHostSyncedIds = new HashSet<short>();
        private static readonly Dictionary<Character, float> _unmatchedSince = new Dictionary<Character, float>(64);
        private static bool _receivedFirstSnapshot;

        /// <summary>Whether at least one entity snapshot has been received from the host.</summary>
        public static bool HasReceivedFirstSnapshot => _receivedFirstSnapshot;

        private struct PendingEntry
        {
            public short HostId;
            public string EntityName;
            public string PrefabPath;
            public Vector3 Position;
            public float RotY;
            public string Clip;
            public short ClipFrame;
            public bool Alive;
            public float TimeAdded;
        }
        private static readonly List<PendingEntry> _pendingMatches = new List<PendingEntry>(16);

        public static bool IsHostSynced(short id)
        {
            return _hostSyncedIds.Contains(id);
        }

        public static bool IsHostSynced(Character c)
        {
            if (c == null) return false;
            if (CharacterTracker.TryGetStableId(c, out short id))
                return _hostSyncedIds.Contains(id);
            return false;
        }

        public static void ApplySnapshot(EntityStateMessage msg)
        {
            if (msg.Entities == null || msg.Entities.Length == 0)
            {
                if (_lastApplyCount > 0)
                    ModRuntime.Log?.LogInfo($"[ClientEntitySync] received empty snapshot (no entities)");
                _lastApplyCount = 0;
                return;
            }

            _receivedFirstSnapshot = true;
            _firstSnapshotTime = Time.time;

            int applied = 0;
            int skipped = 0;
            var sb = new System.Text.StringBuilder();
            var skippedSb = new System.Text.StringBuilder();

            for (int i = 0; i < msg.Entities.Length; i++)
            {
                EntitySnapshotNet e = msg.Entities[i];
                Vector3 targetPos = new Vector3(e.PosX, e.PosY, e.PosZ);

                if (sb.Length == 0)
                    sb.Append($"[ClientEntitySync] snapshot IDs: ");
                sb.Append($"{e.Index} ");

                Character c = CharacterTracker.FindByStableId(e.Index);
                if (c != null)
                {
                    // Verify the matched entity's name matches — FindByStableId can return
                    // the wrong entity when local stable IDs collide with host IDs.
                    string cname = c.name;
                    if (cname.EndsWith("(Clone)"))
                        cname = cname.Substring(0, cname.Length - 7);
                    bool nameMatches = string.Equals(cname, e.EntityName, System.StringComparison.OrdinalIgnoreCase);

                    if (nameMatches)
                    {
                        // If the matched entity is a phantom, check if a real local entity
                        // now exists nearby (e.g. world chunk just loaded). If so, replace
                        // the phantom with the real entity to avoid duplicates.
                        // IMPORTANT: exclude the phantom's own ID from the search so
                        // FindByPositionAndName doesn't return the phantom itself.
                        if (_spawnedPhantomIds.Contains(e.Index))
                        {
                            HashSet<short> exclude = new HashSet<short>(_hostSyncedIds) { e.Index };
                            Character real = CharacterTracker.FindByPositionAndName(targetPos, e.EntityName, MatchRadius, exclude);
                            if (real != null)
                            {
                                CharacterTracker.AssignId(real, e.Index);
                                _hostSyncedIds.Add(e.Index);
                                _everHostSyncedIds.Add(e.Index);
                                _spawnedPhantomIds.Remove(e.Index);
                                Object.Destroy(c.gameObject);
                                c = real;
                                ModRuntime.Log?.LogInfo($"[ClientEntitySync] replaced phantom with real entity: {e.EntityName}(id={e.Index})");
                            }
                        }
                        _hostSyncedIds.Add(e.Index);
                        _everHostSyncedIds.Add(e.Index);
                        UpdateInterpolation(c, e, targetPos, ref applied);
                        continue;
                    }

                    // Name mismatch — the stable ID hit a wrong local entity.
                    // Reset its ID so position-based matching can find the correct entity.
                    ModRuntime.Log?.LogInfo($"[ClientEntitySync] stable ID collision: id={e.Index} found {c.name} but expected {e.EntityName}");
                    CharacterTracker.AssignId(c, 0);
                }

                // Not found by ID — try position + name matching
                c = CharacterTracker.FindByPositionAndName(targetPos, e.EntityName, MatchRadius, _hostSyncedIds);
                if (c != null)
                {
                    CharacterTracker.AssignId(c, e.Index);
                    _hostSyncedIds.Add(e.Index);
                    _everHostSyncedIds.Add(e.Index);
                    EnsureEntityAwake(c);
                    ModRuntime.Log?.LogInfo($"[ClientEntitySync] matched by position: {e.EntityName}(id={e.Index}) at ({targetPos.x:F1},{targetPos.z:F1})");
                    UpdateInterpolation(c, e, targetPos, ref applied);
                    continue;
                }

                // Couldn't find locally — defer to pending match (retried each frame)
                _pendingMatches.Add(new PendingEntry
                {
                    HostId = e.Index,
                    EntityName = e.EntityName,
                    PrefabPath = e.PrefabPath,
                    Position = targetPos,
                    RotY = e.RotY,
                    Clip = e.Clip,
                    ClipFrame = e.ClipFrame,
                    Alive = e.Alive,
                    TimeAdded = Time.time
                });
                skipped++;
                if (skippedSb.Length == 0)
                    skippedSb.Append("[ClientEntitySync] PENDING: ");
                skippedSb.Append($"id={e.Index}({e.EntityName}) ");
            }

            _lastApplyCount = applied;
            _lastSkippedCount = skipped;
            _totalApplied += applied;
            _totalSkipped += skipped;

            _snapshotCount++;
            if (_snapshotCount % 10 == 0)
            {
                if (sb.Length > 0)
                    ModRuntime.Log?.LogInfo(sb.ToString());

                Character[] all = CharacterTracker.GetAll();
                var tb = new System.Text.StringBuilder();
                tb.Append($"[ClientEntitySync] tracker has {all.Length} chars: ");
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null)
                        tb.Append($"{CharacterTracker.GetStableId(all[i])}({all[i].name}) ");
                }
                ModRuntime.Log?.LogInfo(tb.ToString());
                ModRuntime.Log?.LogInfo($"[ClientEntitySync] applied={applied} pending={_pendingMatches.Count} hostSynced={_hostSyncedIds.Count}");
                if (skippedSb.Length > 0)
                    ModRuntime.Log?.LogInfo(skippedSb.ToString());
            }
        }

        private static void UpdateInterpolation(Character c, EntitySnapshotNet e, Vector3 targetPos, ref int applied)
        {
            EnsureEntityAwake(c);

            // Stop all AudioSources on first snapshot — the host is authoritative
            // for AI-managed sounds (growling, hissing, etc.) and the client's AI
            // is disabled, so looping sounds started before AI was disabled would
            // play forever.
            if (_audioStoppedIds.Add(e.Index))
            {
                AudioSource[] srcs = c.GetComponentsInChildren<AudioSource>(true);
                for (int si = 0; si < srcs.Length; si++)
                    srcs[si].Stop();
            }

            if (!_states.TryGetValue(e.Index, out var state))
            {
                state = new EntityInterpState { isFirst = true };
                _states[e.Index] = state;
            }
            state.staleSince = 0f;

            if (state.isFirst)
            {
                _displayPositions[e.Index] = c.transform.position;
                _displayRotations[e.Index] = c.transform.eulerAngles.y;
                state.isFirst = false;
            }

            state.previousPosition = _displayPositions[e.Index];
            state.previousRotY = _displayRotations[e.Index];
            state.targetPosition = targetPos;
            state.targetRotY = e.RotY;
            state.arrivalTime = Time.time;
            state.hasTarget = true;

            if (!e.Alive && c.alive)
            {
                ModRuntime.Log?.LogInfo($"[ClientEntityInterpolation] DETECTED DEATH: {c.name}(id={e.Index})");
                c.die();
            }

            state.alive = e.Alive;

            // Drive animation from snapshot for all entities (alive or dead).
            // On the host, the death animation is started by the AI components
            // (Enemy_Basic/Enemy_Dog) in their Update when they detect alive=false.
            // On the client those components are disabled, so c.die() alone won't
            // start the death animation — we must play the clip ourselves.
            tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
            if (anim != null)
            {
                if (!string.IsNullOrEmpty(e.Clip))
                {
                    bool clipChanged = anim.CurrentClip == null || anim.CurrentClip.name != e.Clip;
                    if (clipChanged && anim.GetClipByName(e.Clip) != null)
                        anim.Play(e.Clip);

                    if (e.ClipFrame >= 0 && anim.CurrentClip != null)
                    {
                        int maxFrame = anim.CurrentClip.frames.Length - 1;
                        if (maxFrame >= 0)
                            anim.SetFrame(Mathf.Clamp(e.ClipFrame, 0, maxFrame), false);
                    }
                }
                else if (c.alive && !anim.Playing)
                {
                    // Idle fallback for alive entities only — dead ones freeze
                    // on their last death frame (or whatever the snapshot supplies).
                    string idleClip = Traverse.Create(c).Field("idleAni").GetValue<string>();
                    if (!string.IsNullOrEmpty(idleClip) && anim.GetClipByName(idleClip) != null)
                        anim.Play(idleClip);
                }
            }

            applied++;
        }

        public static void TickLateUpdate()
        {
            float now = Time.time;

            // 1. Retry pending matches
            for (int i = _pendingMatches.Count - 1; i >= 0; i--)
            {
                PendingEntry p = _pendingMatches[i];

                // Try position matching again (tight radius)
                Character c = CharacterTracker.FindByPositionAndName(p.Position, p.EntityName, MatchRadius, _hostSyncedIds);
                if (c != null)
                {
                    CharacterTracker.AssignId(c, p.HostId);
                    _hostSyncedIds.Add(p.HostId);
                    _everHostSyncedIds.Add(p.HostId);
                    EnsureEntityAwake(c);
                    ModRuntime.Log?.LogInfo($"[ClientEntitySync] pending matched (tight): {p.EntityName}(id={p.HostId})");

                    if (!_states.TryGetValue(p.HostId, out var state))
                    {
                        state = new EntityInterpState { isFirst = true };
                        _states[p.HostId] = state;
                    }
                    state.staleSince = 0f;

                    _displayPositions[p.HostId] = c.transform.position;
                    _displayRotations[p.HostId] = c.transform.eulerAngles.y;
                    state.isFirst = false;

                    state.previousPosition = c.transform.position;
                    state.previousRotY = c.transform.eulerAngles.y;
                    state.targetPosition = p.Position;
                    state.targetRotY = p.RotY;
                    state.arrivalTime = now;
                    state.hasTarget = true;
                    state.alive = p.Alive;

                    if (!p.Alive && c.alive)
                        c.die();

                    _pendingMatches.RemoveAt(i);
                    continue;
                }

                // Timeout — first try to find and activate a real (inactive) entity,
                // then fall back to spawning a phantom.
                if (now - p.TimeAdded > PendingMatchTimeout)
                {
                    if (_hostSyncedIds.Contains(p.HostId))
                    {
                        ModRuntime.Log?.LogInfo($"[ClientEntitySync] dropping pending (already host-synced): {p.EntityName}(id={p.HostId})");
                        _pendingMatches.RemoveAt(i);
                        continue;
                    }

                    // Try to find an inactive character in the scene with matching name.
                    // This avoids creating duplicates for save-related entities that
                    // exist on the client but are deactivated by WorldGrid culling.
                    Character inactive = FindInactiveCharacter(p.EntityName, p.Position, MatchRadius * 2f);
                    if (inactive != null)
                    {
                        CharacterTracker.Add(inactive);
                        CharacterTracker.AssignId(inactive, p.HostId);
                        _hostSyncedIds.Add(p.HostId);
                        _everHostSyncedIds.Add(p.HostId);
                        EnsureEntityAwake(inactive);
                        ModRuntime.Log?.LogInfo($"[ClientEntitySync] activated existing entity: {p.EntityName}(id={p.HostId})");

                        if (!_states.TryGetValue(p.HostId, out var state))
                        {
                            state = new EntityInterpState { isFirst = true };
                            _states[p.HostId] = state;
                        }
                        state.staleSince = 0f;

                        _displayPositions[p.HostId] = inactive.transform.position;
                        _displayRotations[p.HostId] = inactive.transform.eulerAngles.y;
                        state.isFirst = false;

                        state.previousPosition = inactive.transform.position;
                        state.previousRotY = inactive.transform.eulerAngles.y;
                        state.targetPosition = p.Position;
                        state.targetRotY = p.RotY;
                        state.arrivalTime = now;
                        state.hasTarget = true;
                        state.alive = p.Alive;

                        if (!p.Alive && inactive.alive)
                            inactive.die();

                        _pendingMatches.RemoveAt(i);
                        continue;
                    }

                    c = SpawnEntityLocally(p.EntityName, p.PrefabPath, p.Position, p.RotY);
                    if (c != null)
                    {
                        CharacterTracker.AssignId(c, p.HostId);
                        _hostSyncedIds.Add(p.HostId);
                        _everHostSyncedIds.Add(p.HostId);
                        _spawnedPhantomIds.Add(p.HostId);
                        EnsureEntityAwake(c);
                        ModRuntime.Log?.LogInfo($"[ClientEntitySync] pending spawned: {p.EntityName}(id={p.HostId})");

                        if (!_states.TryGetValue(p.HostId, out var state))
                        {
                            state = new EntityInterpState { isFirst = true };
                            _states[p.HostId] = state;
                        }
                        state.staleSince = 0f;

                        _displayPositions[p.HostId] = p.Position;
                        _displayRotations[p.HostId] = p.RotY;
                        c.transform.position = p.Position;
                        state.isFirst = false;

                        state.previousPosition = p.Position;
                        state.previousRotY = p.RotY;
                        state.targetPosition = p.Position;
                        state.targetRotY = p.RotY;
                        state.arrivalTime = now;
                        state.hasTarget = true;
                        state.alive = p.Alive;

                        if (!p.Alive && c.alive)
                            c.die();
                    }

                    _pendingMatches.RemoveAt(i);
                }
            }

            // 2. Update interpolation + track stale entities
            _staleKeys.Clear();

            foreach (var kvp in _states)
            {
                short id = kvp.Key;
                EntityInterpState state = kvp.Value;

                if (!state.hasTarget)
                {
                    bool isPhantom = _spawnedPhantomIds.Contains(id);
                    if (isPhantom && state.staleSince > 0f && now - state.staleSince > PhantomCleanupDelay)
                    {
                        Character c = CharacterTracker.FindByStableId(id);
                        if (c != null)
                        {
                            ModRuntime.Log?.LogInfo($"[ClientEntitySync] destroying phantom: id={id}");
                            Object.Destroy(c.gameObject);
                        }
                        _staleKeys.Add(id);
                        _displayPositions.Remove(id);
                        _displayRotations.Remove(id);
                        _hostSyncedIds.Remove(id);
                        _spawnedPhantomIds.Remove(id);
                    }
                    else if (!isPhantom && state.staleSince > 0f && now - state.staleSince > PhantomCleanupDelay)
                    {
                        // Naturally-loaded entity — stop driving it and let local AI resume
                        Character c = CharacterTracker.FindByStableId(id);
                        if (c != null)
                        {
                            Rigidbody rb = c.GetComponent<Rigidbody>();
                            if (rb != null)
                                rb.isKinematic = false;
                        }
                        _staleKeys.Add(id);
                        _displayPositions.Remove(id);
                        _displayRotations.Remove(id);
                        _hostSyncedIds.Remove(id);
                    }
                    continue;
                }

                Character tracked = CharacterTracker.FindByStableId(id);
                if (tracked == null)
                {
                    _staleKeys.Add(id);
                    _displayPositions.Remove(id);
                    _displayRotations.Remove(id);
                    _hostSyncedIds.Remove(id);
                    _spawnedPhantomIds.Remove(id);
                    continue;
                }

                float elapsed = now - state.arrivalTime;

                if (elapsed > MaxInterpDelay)
                {
                    _displayPositions[id] = state.targetPosition;
                    _displayRotations[id] = state.targetRotY;
                    state.hasTarget = false;
                    state.staleSince = now;
                }
                else if (elapsed > SnapshotInterval)
                {
                    float extrapT = elapsed - SnapshotInterval;
                    Vector3 velocity = (state.targetPosition - state.previousPosition) / SnapshotInterval;
                    _displayPositions[id] = state.targetPosition + velocity * extrapT;
                    _displayRotations[id] = state.targetRotY;
                }
                else
                {
                    float t = elapsed / SnapshotInterval;
                    float smoothT = t * t * (3f - 2f * t);
                    _displayPositions[id] = Vector3.Lerp(state.previousPosition, state.targetPosition, smoothT);
                    _displayRotations[id] = Mathf.LerpAngle(state.previousRotY, state.targetRotY, smoothT);
                }

                Rigidbody rbPos = tracked.GetComponent<Rigidbody>();
                if (rbPos != null)
                    rbPos.MovePosition(_displayPositions[id]);
                else
                    tracked.transform.position = _displayPositions[id];
                Vector3 rot = tracked.transform.eulerAngles;
                rot.y = _displayRotations[id];
                tracked.transform.eulerAngles = rot;
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                _states.Remove(_staleKeys[i]);
            }

            // 3. Clean up unmatched client-only entities
            if (DreamSyncManager.IsLocalDreamActive) return;
            // After receiving the first host snapshot + grace period, destroy any
            // character that exists only on the client (not in host's save/night spawns).
            // This prevents ghost entities when host and client have divergent saves.
            if (!_receivedFirstSnapshot) return;
            if (now - _firstSnapshotTime < UnmatchedCleanupDelay) return;

            Player localPlayer = Player.Instance;
            Character[] allChars = CharacterTracker.GetAll();
            for (int i = 0; i < allChars.Length; i++)
            {
                Character c = allChars[i];
                if (c == null) continue;

                // Never destroy the local player or phantoms
                if (c == localPlayer || c.name.Contains("RemotePlayer"))
                    continue;

                if (!CharacterTracker.TryGetStableId(c, out short sid))
                    continue;

                // Skip if ever synced by the host (even if currently stale)
                if (_everHostSyncedIds.Contains(sid))
                {
                    _unmatchedSince.Remove(c);
                    continue;
                }

                // Also skip if currently host-synced
                if (_hostSyncedIds.Contains(sid))
                {
                    _unmatchedSince.Remove(c);
                    continue;
                }

                // Track how long this character has been unmatched
                if (!_unmatchedSince.TryGetValue(c, out float firstSeen))
                {
                    _unmatchedSince[c] = now;
                    continue;
                }

                if (now - firstSeen > UnmatchedCleanupDelay)
                {
                    ModRuntime.Log?.LogInfo($"[ClientEntitySync] destroying unmatched entity: {c.name}(sid={sid})");
                    _unmatchedSince.Remove(c);
                    Object.Destroy(c.gameObject);
                }
            }
        }

        private static float _firstSnapshotTime;

        private static void EnsureEntityAwake(Character c)
        {
            if (c == null) return;

            GameObject go = c.gameObject;
            if (!go.activeSelf)
                go.SetActive(true);

            if (!c.enabled)
                c.enabled = true;

            if (!c.isActive)
                c.isActive = true;

            tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
            if (anim != null && !anim.enabled)
                anim.enabled = true;

            tk2dBaseSprite sprite = c.sprite ?? c.GetComponent<tk2dBaseSprite>();
            if (sprite != null)
            {
                Renderer r = sprite.GetComponent<Renderer>();
                if (r != null && !r.enabled)
                    r.enabled = true;

                Color col = sprite.color;
                if (col.a <= 0f)
                    sprite.color = new Color(col.r, col.g, col.b, 1f);
            }

            // Rigidbody left non-kinematic so the client player can push entities via physics.
            // Host snapshots drive position via Rigidbody.MovePosition, which respects collisions.
        }

        /// <summary>
        /// Searches the entire scene (including inactive GameObjects) for a character
        /// whose name matches <paramref name="entityName"/> within <paramref name="radius"/>
        /// of <paramref name="position"/>.  This avoids spawning phantom duplicates when
        /// a save-related entity exists in the scene but is deactivated by WorldGrid culling.
        /// </summary>
        private static Character FindInactiveCharacter(string entityName, Vector3 position, float radius)
        {
            string searchName = entityName;
            if (searchName.EndsWith("(Clone)"))
                searchName = searchName.Substring(0, searchName.Length - 7);

            // findObjectsOfType includes ALL loaded Characters, even inactive ones.
            // Guard: only search if there are pending entities; this is called at most
            // once per entity before spawning.
            Character[] all = GameObject.FindObjectsOfType<Character>(true);
            float radiusSq = radius * radius;
            Character best = null;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < all.Length; i++)
            {
                Character c = all[i];
                if (c == null) continue;

                // Skip the local player and remote proxy
                if (c.name.Contains("Player"))
                    continue;

                // Skip if already host-synced or a phantom
                if (CharacterTracker.TryGetStableId(c, out short sid))
                {
                    if (_hostSyncedIds.Contains(sid) || _spawnedPhantomIds.Contains(sid))
                        continue;
                }

                string cname = c.name;
                if (cname.EndsWith("(Clone)"))
                    cname = cname.Substring(0, cname.Length - 7);
                if (!string.Equals(cname, searchName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                float dSq = (c.transform.position - position).sqrMagnitude;
                if (dSq < radiusSq && dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = c;
                }
            }
            return best;
        }

        private static Character SpawnEntityLocally(string entityName, string prefabPath, Vector3 position, float rotY)
        {
            if (string.IsNullOrEmpty(entityName) && string.IsNullOrEmpty(prefabPath))
                return null;

            string path = !string.IsNullOrEmpty(prefabPath) ? prefabPath : "Characters/" + entityName;
            try
            {
                Quaternion rotation = Quaternion.Euler(90f, rotY, 0f);

                GameObject go = Core.AddPrefab(path, position, rotation, null);
                if (go == null) return null;

                Character c = go.GetComponent<Character>();
                if (c == null)
                {
                    Object.Destroy(go);
                    return null;
                }

                // Force idle animation so the entity doesn't appear in T-pose
                // until the next host snapshot provides the correct clip.
                tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
                if (anim != null)
                {
                    string idleClip = Traverse.Create(c).Field("idleAni").GetValue<string>();
                    if (!string.IsNullOrEmpty(idleClip) && anim.GetClipByName(idleClip) != null)
                    {
                        if (anim.CurrentClip == null || anim.CurrentClip.name != idleClip)
                            anim.Play(idleClip);
                    }
                }

                ModRuntime.Log?.LogInfo($"[ClientEntitySync] spawned local entity: {path} at ({position.x:F1},{position.z:F1})");
                return c;
            }
            catch (System.Exception ex)
            {
                ModRuntime.Log?.LogError($"[ClientEntitySync] failed to spawn {path}: {ex.Message}");
                return null;
            }
        }

        public static void Reset()
        {
            _states.Clear();
            _displayPositions.Clear();
            _displayRotations.Clear();
            _hostSyncedIds.Clear();
            _spawnedPhantomIds.Clear();
            _audioStoppedIds.Clear();
            _everHostSyncedIds.Clear();
            _unmatchedSince.Clear();
            _pendingMatches.Clear();
            _lastApplyCount = 0;
            _lastSkippedCount = 0;
            _totalApplied = 0;
            _totalSkipped = 0;
            _receivedFirstSnapshot = false;
            _firstSnapshotTime = 0f;
        }

        public static void LogStats()
        {
            ModRuntime.Log?.LogInfo($"[ClientEntitySync] stats — total applied: {_totalApplied}, total skipped: {_totalSkipped}, hostSynced: {_hostSyncedIds.Count}, pending: {_pendingMatches.Count}");
        }
    }
}
