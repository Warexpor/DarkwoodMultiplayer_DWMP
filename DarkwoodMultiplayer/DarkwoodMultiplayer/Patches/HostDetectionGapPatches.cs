using System.Collections.Generic;
using System.Reflection;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Replaces Sniffer.Update entirely on the host.
    /// Checks BOTH the host player (Player.Instance) and the proxy for smell range,
    /// and sniffs the closest one. When the sniff completes, attacks whichever player
    /// triggered the sniff.
    /// </summary>
    [HarmonyPatch(typeof(Sniffer), "Update")]
    public static class HostSnifferUpdatePatch
    {
        /// <summary>Maps Sniffer → true if the current or next sniff is proxy-triggered.</summary>
        private static readonly Dictionary<Sniffer, bool> _sniffTargetIsProxy = new Dictionary<Sniffer, bool>();

        private static bool Prefix(Sniffer __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;
            if (__instance.disabled)
                return false;

            Character charComponent = __instance.GetComponent<Character>();
            if (charComponent == null)
                return false;

            // Entity is busy — skip all sniff logic
            if (charComponent.enemyInSight ||
                charComponent.behaviour == Character.Behaviour.chasingTarget ||
                charComponent.behaviour == Character.Behaviour.defensive ||
                charComponent.behaviour == Character.Behaviour.escaping)
                return false;

            Transform proxyT = RemoteProxyTransform();

            // --- Sniff lifecycle ---
            var tSniff = Traverse.Create(__instance);
            float timeStarted = tSniff.Field("timeStartedSniffing").GetValue<float>();

            if (__instance.sniffing)
            {
                if (Time.time - timeStarted > __instance.sniffTime)
                    StopSniffAndAttack(__instance, charComponent);
                return false;
            }

            if (__instance.canSniff)
            {
                // Check BOTH host and proxy for proximity
                bool hostInRange = Player.Instance != null &&
                    Core.trueDistance(__instance.transform.position, Player.Instance._transform.position) < __instance.radius;
                bool proxyInRange = proxyT != null &&
                    Core.trueDistance(__instance.transform.position, proxyT.position) < __instance.radius;

                if (!hostInRange && !proxyInRange)
                    return false; // neither in range

                float distToHost = hostInRange
                    ? Core.trueDistance(__instance.transform.position, Player.Instance._transform.position)
                    : float.MaxValue;
                float distToProxy = proxyInRange
                    ? Core.trueDistance(__instance.transform.position, proxyT.position)
                    : float.MaxValue;

                // Sniff the closer player (or host if equal)
                bool sniffProxy = proxyInRange && (!hostInRange || distToProxy < distToHost);

                _sniffTargetIsProxy[__instance] = sniffProxy;

                __instance.sniffing = true;
                tSniff.Field("timeStartedSniffing").SetValue(Time.time);
                AudioController.Play(__instance.sniffSound, __instance.transform);
                return false;
            }

            // Cooldown — same as original (cooldownTime from sniff start)
            if (Time.time - timeStarted > __instance.cooldownTime)
                __instance.canSniff = true;
            return false;
        }

        private static void StopSniffAndAttack(Sniffer __instance, Character charComponent)
        {
            __instance.sniffing = false;
            __instance.canSniff = false;

            if (charComponent == null || !charComponent.alive)
                return;

            // Check if entity wants to flee instead of attacking
            if (charComponent.behaviour == Character.Behaviour.escaping)
                return;

            // Wake up sleeping entities so they can attack
            if (charComponent.sleeping)
            {
                charComponent.wakeup();
                charComponent.sleeping = false;
            }

            bool sniffProxy = _sniffTargetIsProxy.Remove(__instance);

            if (sniffProxy)
            {
                Transform proxyT = RemoteProxyTransform();
                if (proxyT == null) return;

                CharBase proxyCB = proxyT.GetComponent<CharBase>();
                if (proxyCB == null || proxyCB.invisible || proxyCB.ignoreMe)
                    return;

                charComponent.attackCharacter(proxyT);
            }
            else
            {
                Player host = Player.Instance;
                if (host == null || host.invisible || host.ignoreMe)
                    return;

                charComponent.attackPlayer();
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
    /// Applies EnemyOfTheForest / FriendOfTheForest effects when the
    /// remote proxy is seen near an animalAggressive entity, overriding
    /// its default behaviour.
    /// </summary>
    [HarmonyPatch(typeof(Character), "onSeeEnemyNear")]
    public static class HostOnSeeEnemyNearPatch
    {
        private static void Postfix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;
            if (__instance.target == null)
                return;
            if (__instance.faction != Faction.animalAggressive)
                return;

            RemotePlayerProxy proxy = __instance.target.GetComponent<RemotePlayerProxy>();
            if (proxy == null)
                return;

            if (proxy.RemoteHasEnemyOfTheForest)
            {
                // EnemyOfTheForest: always chase/attack
                if (__instance.behaviour != Character.Behaviour.chasingTarget)
                    __instance.setBehaviour(Character.Behaviour.chasingTarget);
            }
            else if (proxy.RemoteHasFriendOfTheForest)
            {
                // FriendOfTheForest: switch from chase to defensive (non-aggressive)
                __instance.setBehaviour(Character.Behaviour.defensive);
            }
        }
    }

    /// <summary>
    /// Ensures growl audio + alertCharactersInArea fire even when the
    /// target is the remote proxy. Vanilla growl is skipped for non-Player targets.
    /// </summary>
    [HarmonyPatch(typeof(Character), "growl")]
    public static class HostGrowlPatch
    {
        private static readonly MethodInfo _alertCharactersInArea =
            AccessTools.Method(typeof(Character), "alertCharactersInArea", new[] { typeof(float), typeof(bool) });

        private static void Prefix(Character __instance, out bool __state)
        {
            __state = false;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;
            if (__instance.target == null)
                return;
            if (__instance.target.GetComponent<RemotePlayerProxy>() == null)
                return;

            __state = __instance.isRoutineActive("waitToGrowl");
        }

        private static void Postfix(Character __instance, bool __state)
        {
            if (__state)
                return;

            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;
            if (__instance.target == null)
                return;
            if (__instance.target.GetComponent<RemotePlayerProxy>() == null)
                return;

            if (__instance.sounds != null && !__instance.sleeping)
                __instance.sounds.playGrowl();

            _alertCharactersInArea.Invoke(__instance, new object[] { 500f, false });
        }
    }

    /// <summary>
    /// The original checkForNewEnemyCloserThanTarget just picks the first valid
    /// entry in charactersInSight regardless of distance. This Prefix replaces
    /// it with a version that actually finds the CLOSEST enemy, so entities
    /// switch between host and proxy based on proximity.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkForNewEnemyCloserThanTarget")]
    public static class HostCheckForCloserEnemyPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;

            if (__instance.charactersInSight.Count == 0)
                return false;

            Transform currentTarget = __instance.target;
            if (currentTarget == null)
                return false;

            float currentDist = Core.trueDistance(__instance.transform.position, currentTarget.position);
            float closestDist = currentDist;
            Transform closestTransform = null;

            for (int i = 0; i < __instance.charactersInSight.Count; i++)
            {
                CharBase cb = __instance.charactersInSight[i];
                if (cb == null || !cb.alive) continue;
                if (!__instance.attacksFaction(cb.faction)) continue;
                if (cb.transform == currentTarget) continue;

                float d = Core.trueDistance(__instance.transform.position, cb.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    closestTransform = cb.transform;
                }
            }

            if (closestTransform != null)
                __instance.attackCharacter(closestTransform);

            return false;
        }
    }
}
