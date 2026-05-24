using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
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

            // Always force-set target when proxy is within range and line-of-sight,
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

                // Force target to proxy even if vanilla canSeeEnemy blocked it (onlyAttackPlayer etc.)
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

                if (dist < (float)__instance.nearViewDistance * __instance.aniSightRangeModifier)
                {
                    __instance.canSeeEnemyNear = true;
                }

                // Remote player effect checks (shadowWard, forestSpiritWard)
                RemotePlayerProxy proxy = hit.collider.GetComponentInParent<RemotePlayerProxy>();
                if (proxy != null)
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
            }
        }

        private static Transform RemoteProxyTransform()
        {
            if (LanNetworkManager.Instance != null && LanNetworkManager.Instance.RemoteProxy != null)
                return LanNetworkManager.Instance.RemoteProxy.transform;
            return null;
        }
    }

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

    [HarmonyPatch(typeof(Character), "attackPlayer")]
    public static class HostAttackPlayerPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;

            Transform proxyT = RemoteProxyTransform();
            if (proxyT == null) return true;

            Transform hostT = Player.Instance != null ? Player.Instance.transform : null;
            float distToHost = hostT != null ? Vector3.SqrMagnitude(__instance.transform.position - hostT.position) : float.MaxValue;
            float distToRemote = Vector3.SqrMagnitude(__instance.transform.position - proxyT.position);

            if (distToRemote < distToHost)
            {
                __instance.attackCharacter(proxyT);
                return false;
            }
            return true;
        }

        private static Transform RemoteProxyTransform()
        {
            if (LanNetworkManager.Instance != null && LanNetworkManager.Instance.RemoteProxy != null)
                return LanNetworkManager.Instance.RemoteProxy.transform;
            return null;
        }
    }

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

    [HarmonyPatch(typeof(Character), "checkIfBeingChased")]
    public static class HostCheckIfBeingChasedPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
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

            // Wake up first so attackCharacter doesn't return early
            if (__instance.sleeping)
                __instance.wakeup();

            __instance.attackCharacter(proxy.transform);
        }
    }

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
}
