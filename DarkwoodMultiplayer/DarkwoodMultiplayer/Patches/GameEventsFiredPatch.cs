using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(GameEvents), "fire")]
    public static class GameEventsFiredPatch
    {
        private static void Prefix(GameEvents __instance, out bool __state)
        {
            __state = __instance.fired;
        }

        private static void Postfix(GameEvents __instance, bool __state)
        {
            if (ModRuntime.Network == null)
                return;

            var net = (LanNetworkManager)ModRuntime.Network;
            if (net.Role != NetworkRole.Host)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            // If fired was already true, the method returned early (no actual fire)
            if (__state)
                return;

            // fired was false before, is now true -> event actually fired
            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(
                Mathf.Round(p.x * 10f) / 10f,
                Mathf.Round(p.y * 10f) / 10f,
                Mathf.Round(p.z * 10f) / 10f);

            net.SendGameEventsFired(new GameEventsFiredMessage
            {
                PosX = key.x, PosY = key.y, PosZ = key.z
            });
            ModRuntime.Log?.LogInfo("[GameEventsSync] fired at " + key);
        }
    }
}
