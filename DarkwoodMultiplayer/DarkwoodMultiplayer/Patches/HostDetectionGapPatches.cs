using System.Reflection;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Sniffer), "Update")]
    public static class HostSnifferUpdatePatch
    {
        private static readonly MethodInfo _startSniffing =
            AccessTools.Method(typeof(Sniffer), "startSniffing");

        private static void Postfix(Sniffer __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;
            if (__instance.disabled)
                return;

            Transform proxyT = RemoteProxyTransform();
            if (proxyT == null) return;

            Character charComponent = __instance.GetComponent<Character>();
            if (charComponent == null)
                return;

            if (__instance.sniffing || !__instance.canSniff)
                return;

            float distToProxy = Core.trueDistance(__instance.transform.position, proxyT.position);
            float distToHost = Core.trueDistance(__instance.transform.position, Player.Instance._transform.position);

            if (distToProxy >= __instance.radius || distToProxy >= distToHost)
                return;

            if (charComponent.enemyInSight ||
                charComponent.behaviour == Character.Behaviour.chasingTarget ||
                charComponent.behaviour == Character.Behaviour.defensive ||
                charComponent.behaviour == Character.Behaviour.escaping)
                return;

            _startSniffing.Invoke(__instance, null);
        }

        private static Transform RemoteProxyTransform()
        {
            if (LanNetworkManager.Instance != null && LanNetworkManager.Instance.RemoteProxy != null)
                return LanNetworkManager.Instance.RemoteProxy.transform;
            return null;
        }
    }

    [HarmonyPatch(typeof(Sniffer), "stopSniffing")]
    public static class HostSnifferStopPatch
    {
        private static bool Prefix(Sniffer __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!PlayerPositionManager.HasRemotePlayer)
                return true;

            Character charComponent = __instance.GetComponent<Character>();
            if (charComponent == null)
                return true;

            Transform proxyT = RemoteProxyTransform();
            if (proxyT == null) return true;

            float distToProxy = Core.trueDistance(__instance.transform.position, proxyT.position);
            float distToHost = Core.trueDistance(__instance.transform.position, Player.Instance._transform.position);

            if (distToProxy >= __instance.radius || distToProxy >= distToHost)
                return true;

            CharBase proxyCharBase = proxyT.GetComponent<CharBase>();
            if (proxyCharBase == null || proxyCharBase.invisible || proxyCharBase.ignoreMe)
                return true;

            __instance.sniffing = false;
            __instance.canSniff = false;

            if (charComponent.behaviour != Character.Behaviour.escaping && charComponent.alive)
                charComponent.attackCharacter(proxyT);

            return false;
        }

        private static Transform RemoteProxyTransform()
        {
            if (LanNetworkManager.Instance != null && LanNetworkManager.Instance.RemoteProxy != null)
                return LanNetworkManager.Instance.RemoteProxy.transform;
            return null;
        }
    }

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

            // FriendOfTheForest: switch from chase to defensive
            if (proxy.RemoteHasFriendOfTheForest)
                __instance.setBehaviour(Character.Behaviour.defensive);
        }
    }

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
}
