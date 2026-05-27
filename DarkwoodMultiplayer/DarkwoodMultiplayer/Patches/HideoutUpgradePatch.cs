using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(ExperienceMachine), "enable")]
    public static class HideoutUpgradeEnablePatch
    {
        private static void Postfix(ExperienceMachine __instance)
        {
            if (ModRuntime.Network == null)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(
                Mathf.Round(p.x * 10f) / 10f,
                Mathf.Round(p.y * 10f) / 10f,
                Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendHideoutUpgrade(new HideoutUpgradeMessage
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = true
            });
            ModRuntime.Log?.LogInfo("[HideoutUpgrade] enable at " + key);
        }
    }

    [HarmonyPatch(typeof(ExperienceMachine), "disable")]
    public static class HideoutUpgradeDisablePatch
    {
        private static void Postfix(ExperienceMachine __instance)
        {
            if (ModRuntime.Network == null)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(
                Mathf.Round(p.x * 10f) / 10f,
                Mathf.Round(p.y * 10f) / 10f,
                Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendHideoutUpgrade(new HideoutUpgradeMessage
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = false
            });
            ModRuntime.Log?.LogInfo("[HideoutUpgrade] disable at " + key);
        }
    }
}
