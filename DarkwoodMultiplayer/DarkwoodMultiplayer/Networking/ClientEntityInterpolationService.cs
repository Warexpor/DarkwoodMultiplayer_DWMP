using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using DarkwoodMultiplayer.Players;

namespace DarkwoodMultiplayer.Networking
{
    /// <summary>
    /// Receives entity snapshots from the host and interpolates remote entity positions,
    /// rotations, and animations on the client for smooth visual playback.
    /// </summary>
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
            public bool spawnedLocally;
            public float staleSince;
        }

        private static readonly Dictionary<short, EntityInterpState> _states = new Dictionary<short, EntityInterpState>(64);
        private static readonly Dictionary<short, Vector3> _displayPositions = new Dictionary<short, Vector3>(64);
        private static readonly Dictionary<short, float> _displayRotations = new Dictionary<short, float>(64);
        private static readonly List<short> _staleKeys = new List<short>(16);

        private const float SnapshotInterval = 0.1f;
        private const float MaxInterpDelay = 0.3f;

        private static int _lastApplyCount;
        private static int _lastSkippedCount;
        private static int _totalApplied;
        private static int _totalSkipped;
        private static int _snapshotCount;

        /// <summary>
        /// Processes a received entity state message: updates interpolation targets,
        /// spawns missing entities, synchronises animation clips and alive state.
        /// </summary>
        public static void ApplySnapshot(EntityStateMessage msg)
        {
            if (msg.Entities == null || msg.Entities.Length == 0)
            {
                if (_lastApplyCount > 0)
                    ModRuntime.Log?.LogInfo($"[ClientEntitySync] received empty snapshot (no entities)");
                _lastApplyCount = 0;
                return;
            }

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
                bool justSpawned = false;
                if (c == null)
                {
                    // Try to spawn the entity on the client so future snapshots find it
                    c = SpawnEntityLocally(e);
                    if (c == null)
                    {
                        skipped++;
                        if (skippedSb.Length == 0)
                            skippedSb.Append("[ClientEntitySync] SKIPPED (no local match): ");
                        skippedSb.Append($"id={e.Index}({e.EntityName}) ");
                        continue;
                    }
                    // Assign the host's stable ID so we match future snapshots
                    CharacterTracker.AssignId(c, e.Index);
                    justSpawned = true;
                }

                // Ensure entity is fully active for rendering
                EnsureEntityAwake(c);

                if (!_states.TryGetValue(e.Index, out var state))
                {
                    state = new EntityInterpState { isFirst = true };
                    _states[e.Index] = state;
                }

                if (justSpawned)
                    state.spawnedLocally = true;
                state.staleSince = 0f;

                if (state.isFirst)
                {
                    // First snapshot: snap directly to position instead of interpolating from origin
                    _displayPositions[e.Index] = targetPos;
                    _displayRotations[e.Index] = e.RotY;
                    c.transform.position = targetPos;
                    state.isFirst = false;
                }

                state.previousPosition = _displayPositions[e.Index];
                state.previousRotY = _displayRotations[e.Index];
                state.targetPosition = targetPos;
                state.targetRotY = e.RotY;
                state.arrivalTime = Time.time;
                state.hasTarget = true;

                if (!string.IsNullOrEmpty(e.Clip))
                {
                    tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
                    if (anim != null)
                    {
                        bool clipChanged = anim.CurrentClip == null || anim.CurrentClip.name != e.Clip;
                        // Guard with GetClipByName to avoid crash if host uses a clip the client doesn't have
                        if (clipChanged && anim.GetClipByName(e.Clip) != null)
                            anim.Play(e.Clip);

                        if (e.ClipFrame >= 0 && anim.CurrentClip != null)
                        {
                            int maxFrame = anim.CurrentClip.frames.Length - 1;
                            if (maxFrame >= 0)
                                anim.SetFrame(Mathf.Clamp(e.ClipFrame, 0, maxFrame), false);
                        }
                    }
                }
                else
                {
                    // Host entity is dormant (far from host, no clip playing).
                    // Fall back to idle so client entity doesn't freeze on the last received frame.
                    tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
                    if (anim != null && !anim.Playing)
                    {
                        string idleClip = Traverse.Create(c).Field("idleAni").GetValue<string>();
                        if (!string.IsNullOrEmpty(idleClip) && anim.GetClipByName(idleClip) != null)
                            anim.Play(idleClip);
                    }
                }

                if (!e.Alive && state.alive)
                {
                    // Entity just died on the host — replicate locally
                    if (c.alive)
                        c.die();
                }
                state.alive = e.Alive;

                applied++;
            }

            _lastApplyCount = applied;
            _lastSkippedCount = skipped;
            _totalApplied += applied;
            _totalSkipped += skipped;

            _snapshotCount++;
            // Log every 10th snapshot for diagnostics without flooding the console
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
                ModRuntime.Log?.LogInfo($"[ClientEntitySync] applied={applied} skipped={skipped}");
                if (skippedSb.Length > 0)
                    ModRuntime.Log?.LogInfo(skippedSb.ToString());
            }
        }

        private const float PhantomCleanupDelay = 5f;

        /// <summary>
        /// Called every frame after the main update; interpolates all tracked entities
        /// toward their target positions and cleans up stale locally-spawned phantoms.
        /// </summary>
        public static void TickLateUpdate()
        {
            float now = Time.time;
            _staleKeys.Clear();

            foreach (var kvp in _states)
            {
                short id = kvp.Key;
                EntityInterpState state = kvp.Value;

                if (!state.hasTarget)
                {
                    // No target means no snapshots arriving — track stale time
                    // and destroy locally-spawned phantoms after PhantomCleanupDelay
                    if (state.spawnedLocally && state.staleSince > 0f && now - state.staleSince > PhantomCleanupDelay)
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
                    }
                    continue;
                }

                Character tracked = CharacterTracker.FindByStableId(id);
                if (tracked == null)
                {
                    _staleKeys.Add(id);
                    _displayPositions.Remove(id);
                    _displayRotations.Remove(id);
                    continue;
                }

                float elapsed = now - state.arrivalTime;

                if (elapsed > MaxInterpDelay)
                {
                    // No updates for too long — snap to last known target and stop
                    _displayPositions[id] = state.targetPosition;
                    _displayRotations[id] = state.targetRotY;
                    state.hasTarget = false;
                    state.staleSince = now;
                }
                else if (elapsed > SnapshotInterval)
                {
                    // Interpolation finished, next snapshot hasn't arrived yet.
                    // Extrapolate using velocity from the last known segment
                    // so the entity keeps moving instead of freezing.
                    float extrapT = elapsed - SnapshotInterval;
                    Vector3 velocity = (state.targetPosition - state.previousPosition) / SnapshotInterval;
                    _displayPositions[id] = state.targetPosition + velocity * extrapT;
                    _displayRotations[id] = state.targetRotY;
                }
                else
                {
                    // Normal interpolation between previous and target
                    float t = elapsed / SnapshotInterval;
                    // Smoothstep to ease in/out and avoid jerky movement
                    float smoothT = t * t * (3f - 2f * t);
                    _displayPositions[id] = Vector3.Lerp(state.previousPosition, state.targetPosition, smoothT);
                    _displayRotations[id] = Mathf.LerpAngle(state.previousRotY, state.targetRotY, smoothT);
                }

                tracked.transform.position = _displayPositions[id];
                Vector3 rot = tracked.transform.eulerAngles;
                rot.y = _displayRotations[id];
                tracked.transform.eulerAngles = rot;
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                _states.Remove(_staleKeys[i]);
            }
        }

        /// <summary>
        /// Ensures a character GameObject and its render/animator components are fully active
        /// so the client can see it even if it was loaded in a dormant state.
        /// </summary>
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

            // Force sprite alpha to fully visible
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

            // Make rigidbody kinematic to let the host control physics, avoiding local physics interference
            Rigidbody rb = c.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.isKinematic = true;
        }

        /// <summary>
        /// Attempts to spawn an entity locally from its prefab path using the host's snapshot data.
        /// Returns null if the prefab does not exist or lacks a Character component.
        /// </summary>
        private static Character SpawnEntityLocally(EntitySnapshotNet e)
        {
            if (string.IsNullOrEmpty(e.EntityName))
                return null;

            try
            {
                string prefabPath = "Characters/" + e.EntityName;
                Vector3 position = new Vector3(e.PosX, e.PosY, e.PosZ);
                Quaternion rotation = Quaternion.Euler(90f, e.RotY, 0f);

                GameObject go = Core.AddPrefab(prefabPath, position, rotation, null);
                if (go == null) return null;

                Character c = go.GetComponent<Character>();
                if (c == null)
                {
                    Object.Destroy(go);
                    return null;
                }

                ModRuntime.Log?.LogInfo($"[ClientEntitySync] spawned local entity: {e.EntityName}(id={e.Index}) at ({position.x:F1},{position.z:F1})");
                return c;
            }
            catch (System.Exception ex)
            {
                ModRuntime.Log?.LogError($"[ClientEntitySync] failed to spawn {e.EntityName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Clears all interpolation state (typically on disconnect or scene change).</summary>
        public static void Reset()
        {
            _states.Clear();
            _displayPositions.Clear();
            _displayRotations.Clear();
            _lastApplyCount = 0;
            _lastSkippedCount = 0;
            _totalApplied = 0;
            _totalSkipped = 0;
        }

        /// <summary>Logs cumulative apply/skip statistics.</summary>
        public static void LogStats()
        {
            ModRuntime.Log?.LogInfo($"[ClientEntitySync] stats — total applied: {_totalApplied}, total skipped (no local match): {_totalSkipped}");
        }
    }
}
