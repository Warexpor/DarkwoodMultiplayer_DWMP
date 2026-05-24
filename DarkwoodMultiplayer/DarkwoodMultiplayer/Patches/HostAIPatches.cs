using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    internal static class ProxyDistanceHelper
    {
        internal static bool ProxyIsFar(Character c)
        {
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;
            Transform proxyT = LanNetworkManager.Instance?.RemoteProxy?.transform;
            if (proxyT == null)
                return true;
            float dist = (c.transform.position - proxyT.position).magnitude;
            float range = (float)c.farViewDistance * c.aniSightRangeModifier;
            Sniffer sniffer = c.GetComponent<Sniffer>();
            if (sniffer != null && sniffer.radius > range)
                range = sniffer.radius;
            return dist > range + 50f;
        }
    }

    /// <summary>
    /// Augments Character.canSeeEnemy on the host so NPCs react to both
    /// the host player and the remote proxy for detection, targeting,
    /// and fear/ward effects.
    /// </summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    public static class HostCanSeeEnemyPatch
    {
        private static void Postfix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (__instance.dummy || __instance.blind || !__instance.alive)
                return;

            Transform proxyT = RemoteProxyTransform();
            if (proxyT == null) return;

            // --- CASE 3: Entity has no target but host is visible ---
            // Must run BEFORE the ProxyDistanceHelper guard so host-target
            // acquisition happens even when the proxy has fled. Handles the
            // onlyAttackPlayer=true case where canSeeEnemy can't set target.
            if (__instance.aggressiveness != Aggressiveness.neutral &&
                __instance.aggressiveness != Aggressiveness.follower)
            {
                CharBase hostCB = Player.Instance?.GetComponent<CharBase>();
                if (hostCB != null && __instance.charactersInSight.Contains(hostCB) && !hostCB.invisible && !hostCB.ignoreMe)
                {
                    bool acquiringHost = __instance.target == null ||
                        (__instance.target != hostCB.transform && __instance.behaviour != Character.Behaviour.chasingTarget);
                    if (acquiringHost)
                    {
                        __instance.attackCharacter(hostCB.transform);
                    }
                }
            }

            // Don't modify entity behavior for proxy-specific cases when the
            // proxy is outside detection range (CASE 1 and CASE 2).
            if (ProxyDistanceHelper.ProxyIsFar(__instance))
                return;

            // --- CASE 1: Entity is already chasing the proxy ---
            // Check if the host is detectable and add to charactersInSight
            // so checkForNewEnemyCloserThanTarget (called from checkStuff
            // every ~2.5-3.5s) can switch to the closer player.
            if (__instance.target != null && __instance.target.GetComponent<RemotePlayerProxy>() != null)
            {
                Player hostPlayer = Player.Instance;
                if (hostPlayer == null) return;

                CharBase hostCB = hostPlayer.GetComponent<CharBase>();
                if (hostCB == null || hostCB.invisible || hostCB.ignoreMe) return;
                if (__instance.charactersInSight.Contains(hostCB)) return;

                Vector3 toHost = hostPlayer.transform.position - __instance.transform.position;
                float distToHost = toHost.magnitude;

                // Path A: visual detection with FOV
                if (distToHost <= (float)__instance.farViewDistance * __instance.aniSightRangeModifier &&
                    Vector3.Angle(toHost, __instance.transform.up) <= (float)__instance.fieldOfViewRange)
                {
                    if (Physics.Raycast(__instance.transform.position, toHost, out var hostHit, distToHost, 18909185))
                    {
                        if (hostHit.collider.GetComponentInParent<Player>() != null)
                        {
                            __instance.charactersInSight.Add(hostCB);
                            __instance.canSeeEnemyFar = true;
                            if (distToHost < (float)__instance.nearViewDistance * __instance.aniSightRangeModifier)
                                __instance.canSeeEnemyNear = true;
                        }
                    }
                }
                // Path B: smell detection — bypass FOV if host is within sniff radius
                else
                {
                    Sniffer sniffer = __instance.GetComponent<Sniffer>();
                    if (sniffer != null && distToHost < sniffer.radius)
                    {
                        if (Physics.Raycast(__instance.transform.position, toHost, out var sniffHit, distToHost, 18909185))
                        {
                            if (sniffHit.collider.GetComponentInParent<Player>() != null)
                                __instance.charactersInSight.Add(hostCB);
                        }
                    }
                }
                return;
            }

            // --- CASE 2: Entity is NOT yet chasing the proxy ---
            // Force-set target when proxy is within range and line-of-sight,
            // even if canSeeEnemy was not called or onlyAttackPlayer blocked it
            Vector3 toRemote = proxyT.position - __instance.transform.position;
            float dist = toRemote.magnitude;
            float maxDist = (float)__instance.farViewDistance * __instance.aniSightRangeModifier;

            if (dist > maxDist) return;
            if (Vector3.Angle(toRemote, __instance.transform.up) > (float)__instance.fieldOfViewRange) return;

            // Don't redirect neutral entities (rabbits, pigs, etc.)
            if (__instance.aggressiveness == Aggressiveness.neutral)
                return;

            Collider myCollider = __instance.GetComponent<Collider>();
            if (Physics.Raycast(__instance.transform.position, toRemote, out var hit, dist, 18909185))
            {
                if (hit.collider == null || (myCollider != null && hit.collider == myCollider)) return;
                if (hit.collider.GetComponentInParent<RemotePlayerProxy>() == null) return;

                // Wake up sleeping enemies so they react to the proxy
                if (__instance.sleeping)
                    __instance.wakeup();

                __instance.canSeeEnemyFar = true;
                __instance.stopRoutine("lostEnemy", true);

                // The original canSeeEnemy already detects the proxy via
                // charactersInViewRange — do NOT override target for the proxy
                // when the host is also visible. Preserve vanilla host targeting.
                CharBase hostCharBase = Player.Instance?.GetComponent<CharBase>();
                bool hostVisible = hostCharBase != null && __instance.charactersInSight.Contains(hostCharBase);

                if (!hostVisible)
                {
                    // Only the proxy is visible — ensure target is set to proxy
                    // (original may have missed it due to onlyAttackPlayer etc.)
                    if (__instance.target == null || __instance.target != proxyT)
                    {
                        if (__instance.aggressiveness != Aggressiveness.neutral &&
                            __instance.behaviour != Character.Behaviour.chasingTarget &&
                            __instance.behaviour != Character.Behaviour.defensive &&
                            __instance.behaviour != Character.Behaviour.following &&
                            !__instance.canSeeEnemyNear &&
                            __instance.behaviour != Character.Behaviour.escaping &&
                            __instance.behaviour != Character.Behaviour.running)
                        {
                            __instance.stopAndListenTo(proxyT.position);
                        }
                        __instance.target = proxyT;
                    }
                }

                if (dist < (float)__instance.nearViewDistance * __instance.aniSightRangeModifier)
                {
                    __instance.canSeeEnemyNear = true;
                }

                // Remote player effect checks (shadowWard, forestSpiritWard, EnemyOfTheForest)
                RemotePlayerProxy proxy = hit.collider.GetComponentInParent<RemotePlayerProxy>();
                if (proxy != null)
                {
                    // EnemyOfTheForest overrides fear effects — animals always attack
                    if (!proxy.RemoteHasEnemyOfTheForest)
                    {
                        if (__instance.afraidOfHideout && proxy.RemoteHasShadowWard)
                        {
                            __instance.runAway(proxyT.position);
                            __instance.wantToDespawn = true;
                        }
                        if (__instance.afraidOfForestSpiritWard && proxy.RemoteHasForestSpiritWard)
                        {
                            __instance.runAway(proxyT.position);
                            __instance.blind = true;
                        }
                    }

                    // EnemyOfTheForest: animalAggressive entities always chase the proxy
                    if (proxy.RemoteHasEnemyOfTheForest && __instance.faction == Faction.animalAggressive)
                    {
                        __instance.target = proxyT;
                        __instance.canSeeEnemyFar = true;
                        if (dist < (float)__instance.nearViewDistance * __instance.aniSightRangeModifier)
                            __instance.canSeeEnemyNear = true;
                        if (__instance.behaviour != Character.Behaviour.chasingTarget)
                            __instance.attackCharacter(proxyT);
                    }
                }
            }

        }

        private static Transform RemoteProxyTransform()
        {
            if (LanNetworkManager.Instance != null && LanNetworkManager.Instance.RemoteProxy != null)
                return LanNetworkManager.Instance.RemoteProxy.transform;
            return null;
        }
    }

    /// <summary>
    /// Ensures sleeping entities wake up when the remote proxy triggers
    /// attackCharacter, since the proxy is not a real Player and vanilla
    /// attackCharacter skips wake-up for non-Player targets.
    /// </summary>
    [HarmonyPatch(typeof(Character), "attackCharacter")]
    public static class HostAttackCharacterPatch
    {
        private static bool Prefix(Character __instance, Transform destTransform)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (destTransform == null)
                return false;
            if (destTransform.GetComponent<RemotePlayerProxy>() == null)
                return true;

            if (__instance.sleeping)
            {
                __instance.wakeup();
                __instance.sleeping = false;
            }

            return true;
        }
    }

    /// <summary>
    /// Prevents despawning NPCs when the host is far away if the remote
    /// player is still close, keeping the entity alive for multiplayer.
    /// Re-checks distances in Postfix to clean up if both players leave range.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkStuff")]
    public static class HostCheckStuffPatch
    {
        private static readonly HashSet<Character> _suppressed = new HashSet<Character>();

        private static bool Prefix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;

            float distSq = PlayerPositionManager.SqrDistanceToNearestPlayer(__instance.transform.position);
            float distToHost = Core.trueDistance(Player.Instance._transform.position, __instance.transform.position);

            bool vanillaWouldRemove = false;
            if (__instance.temporarySpawned && distToHost > 3500f)
                vanillaWouldRemove = true;
            if (__instance.wantToDespawn && distToHost > 1500f)
                vanillaWouldRemove = true;

            if (!vanillaWouldRemove)
                return true;

            // Vanilla would remove because host is far;
            // keep alive if nearest player (host or remote) is close enough
            bool keepAlive = false;
            if (__instance.temporarySpawned && distSq <= 3500f * 3500f)
                keepAlive = true;
            if (__instance.wantToDespawn && distSq <= 1500f * 1500f)
                keepAlive = true;

            if (keepAlive)
            {
                _suppressed.Add(__instance);
                return false;
            }

            return true;
        }

        private static void Postfix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;

            if (!_suppressed.Remove(__instance))
                return;

            float distSq = PlayerPositionManager.SqrDistanceToNearestPlayer(__instance.transform.position);

            bool removed = false;
            if (__instance.temporarySpawned && distSq > 3500f * 3500f)
            {
                __instance.removeMe();
                removed = true;
            }
            if (!removed && __instance.wantToDespawn && distSq > 1500f * 1500f)
            {
                __instance.removeMe();
            }
        }
    }

    /// <summary>
    /// Forces Character.inSightOrCloseToPlayer to return true when the
    /// remote proxy is within 1000 units, preventing NPCs from being
    /// culled or going idle while the remote player is near.
    /// </summary>
    [HarmonyPatch(typeof(Character), "inSightOrCloseToPlayer")]
    public static class HostInSightOrCloseToPlayerPatch
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            if (__result) return;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host) return;
            if (!PlayerPositionManager.HasRemotePlayer) return;

            Transform proxyT = RemoteProxyTransform();
            if (proxyT == null) return;

            float dist = Core.trueDistance(__instance.transform.position, proxyT.position);
            if (dist < 1000f)
            {
                __result = true;
            }
        }

        private static Transform RemoteProxyTransform()
        {
            if (LanNetworkManager.Instance != null && LanNetworkManager.Instance.RemoteProxy != null)
                return LanNetworkManager.Instance.RemoteProxy.transform;
            return null;
        }
    }

    /// <summary>
    /// Redirects NPC fleeing/despawning behavior to run away from the
    /// nearest player (host or remote) instead of only the host.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkIfBeingChased")]
    public static class HostCheckIfBeingChasedPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;

            if (ProxyDistanceHelper.ProxyIsFar(__instance))
                return true;

            if (__instance.wantToDespawn)
            {
                Vector3 nearest = PlayerPositionManager.GetNearestPlayerPosition(__instance.transform.position);
                __instance.runAway(nearest);
                return false;
            }

            if (__instance.behaviour != Character.Behaviour.escaping)
                return false;

            float sqrDist = PlayerPositionManager.SqrDistanceToNearestPlayer(__instance.transform.position);
            if (sqrDist < 500f * 500f)
            {
                Vector3 nearest = PlayerPositionManager.GetNearestPlayerPosition(__instance.transform.position);
                __instance.runAway(nearest);
            }
            return false;
        }
    }

    /// <summary>
    /// Replicates vanilla Character.onCollideWith behavior for the remote
    /// proxy, since the proxy has a CharBase but no Player component and
    /// would otherwise be ignored by vanilla collision logic.
    /// </summary>
    [HarmonyPatch(typeof(Character), "onCollideWith")]
    public static class HostOnCollideWithProxyPatch
    {
        private static void Postfix(Character __instance, Collider _collider)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;
            if (__instance.dummy || !__instance.alive)
                return;

            RemotePlayerProxy proxy = _collider.GetComponentInParent<RemotePlayerProxy>();
            if (proxy == null)
                return;

            // Replicate vanilla Player collision behavior from Character.onCollideWith,
            // adapted for the proxy (which has CharBase but no Player component).
            // Vanilla flow:
            //   1. Sleeping → wakeup + return (don't react)
            //   2. Banshee  → initiateBansheeAttack + return
            //   3. Invisible/ignoreMe → skip
            //   4. Aggressiveness.neutral/follower → ignore
            //   5. Aggressiveness.flee/fleeAndDespawn → runAway
            //   6. attackOnSight/defensive/stalker → chase

            // Track contact like a Player collision
            if (!__instance.touchingColliders.Contains(_collider))
                __instance.touchingColliders.Add(_collider);

            if (__instance.sleeping)
            {
                if (!__instance.wakeUpOnlyManually)
                {
                    __instance.wakeup();
                }
                return; // Sleeping entities wake up but don't react further
            }

            CharBase proxyCB = proxy.GetComponent<CharBase>();
            if (proxyCB == null || proxyCB.invisible || proxyCB.ignoreMe)
                return;

            if (__instance.banshee)
            {
                __instance.Invoke("initiateBansheeAttack", 0f);
                return;
            }

            switch (__instance.aggressiveness)
            {
                case Aggressiveness.neutral:
                case Aggressiveness.follower:
                    return;

                case Aggressiveness.flee:
                case Aggressiveness.fleeAndDespawn:
                    __instance.runAway(proxy.transform.position);
                    return;

                default:
                    __instance.attackCharacter(proxy.transform);
                    break;
            }
        }
    }

    /// <summary>
    /// When an NPC starts chasing the remote proxy, registers it in the
    /// host player's charactersAttackingMe list so the host's UI/audio
    /// combat indicators trigger correctly.
    /// </summary>
    [HarmonyPatch(typeof(Character), "setBehaviour")]
    public static class HostSetBehaviourPatch
    {
        private static void Postfix(Character __instance, Character.Behaviour targetBehaviour)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host) return;
            if (!PlayerPositionManager.HasRemotePlayer) return;
            if (targetBehaviour != Character.Behaviour.chasingTarget) return;
            if (__instance.target == null) return;
            if (__instance.target == Player.Instance?.transform) return;
            if (__instance.target.GetComponent<RemotePlayerProxy>() == null) return;

            Player player = Player.Instance;
            if (player == null) return;

            bool alreadyAdded = false;
            for (int i = 0; i < player.charactersAttackingMe.Count; i++)
            {
                if (player.charactersAttackingMe[i] == __instance)
                {
                    alreadyAdded = true;
                    break;
                }
            }
            if (!alreadyAdded)
            {
                player.charactersAttackingMe.Add(__instance);
                player.checkInCombatChars();
            }
        }
    }

    /// <summary>
    /// Prevents MeleeSensor from hitting the same CharBase twice within the sensor's
    /// lifetime. This fixes double-damage on the proxy (which has multiple child colliders
    /// from the player clone, each triggering OnTriggerEnter independently).
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    public static class MeleeSensorDeduplicatePatch
    {
        internal static readonly Dictionary<int, HashSet<int>> _hitSets = new Dictionary<int, HashSet<int>>();

        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;

            int sensorId = __instance.GetInstanceID();
            if (!_hitSets.TryGetValue(sensorId, out var hitIds))
                return true;

            CharBase cb = _collider.GetComponentInParent<CharBase>();
            if (cb == null)
                return true;

            int cbId = cb.GetInstanceID();
            if (hitIds.Contains(cbId))
                return false;

            hitIds.Add(cbId);
            return true;
        }
    }

    /// <summary>
    /// Initializes a fresh hit-tracking set when a MeleeSensor is spawned,
    /// so each sensor instance gets its own deduplication state.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnSpawned")]
    public static class MeleeSensorOnSpawnedPatch
    {
        private static void Postfix(MeleeSensor __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;

            int sensorId = __instance.GetInstanceID();
            MeleeSensorDeduplicatePatch._hitSets[sensorId] = new HashSet<int>();
        }
    }

    /// <summary>
    /// Extends WorldGrid.refreshPosition to also activate grid nodes near
    /// the remote proxy, preventing proxy-visibility culling on the host.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), "refreshPosition")]
    public static class HostWorldGridProxyCullPatch
    {
        private static void Postfix(WorldGrid __instance, Vector3 pos, bool instant, bool force)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;

            Vector3 proxyPos = PlayerPositionManager.RemotePlayerPosition;
            if (proxyPos == Vector3.zero)
                return;

            float cullDistX = ((float)Screen.width + 500f * Mathf.Max(Core.ResolutionWidthModifier, Core.ResolutionHeightModifier)) / Singleton<Controller>.Instance.cameraZoom;
            float cullDistY = ((float)Screen.height + 500f * Mathf.Max(Core.ResolutionWidthModifier, Core.ResolutionHeightModifier)) / Singleton<Controller>.Instance.cameraZoom;

            var nodes = __instance.currentGrid.nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2 np = nodes[i].position;
                bool proxyNear = Mathf.Abs(proxyPos.x - np.x) <= cullDistX && Mathf.Abs(proxyPos.z - np.y) <= cullDistY;
                if (proxyNear)
                    nodes[i].enter(force);
            }
        }
    }
}
