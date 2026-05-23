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
            if (!PlayerPositionManager.HasRemotePlayer)
                return;
            if (__instance.dummy || __instance.blind || !__instance.alive)
                return;

            Transform proxyT = RemoteProxyTransform();
            if (proxyT == null) return;

            Vector3 toRemote = proxyT.position - __instance.transform.position;
            float dist = toRemote.magnitude;
            float maxDist = (float)__instance.farViewDistance * __instance.aniSightRangeModifier;

            if (dist > maxDist) return;
            if (Vector3.Angle(toRemote, __instance.transform.up) > (float)__instance.fieldOfViewRange) return;

            Collider myCollider = __instance.GetComponent<Collider>();
            if (Physics.Raycast(__instance.transform.position, toRemote, out var hit, dist, 18909185))
            {
                if (hit.collider == null || (myCollider != null && hit.collider == myCollider)) return;
                if (hit.collider.GetComponent<RemotePlayerProxy>() == null) return;

                __instance.canSeeEnemyFar = true;
                __instance.stopRoutine("lostEnemy", true);

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
                return false;
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
        private static void Postfix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;

            float distSq = PlayerPositionManager.SqrDistanceToNearestPlayer(__instance.transform.position);

            if (__instance.temporarySpawned && distSq > 3500f * 3500f)
            {
                __instance.removeMe();
                return;
            }

            if (__instance.wantToDespawn && distSq > 1500f * 1500f)
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
