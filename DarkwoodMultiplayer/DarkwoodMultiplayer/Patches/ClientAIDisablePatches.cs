using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Character), "Update")]
    public static class ClientCharacterUpdatePatch
    {
        private static bool Prefix(Character __instance)
        {
            if (!ModConfig.IsLanMode)
                return true;

            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return true;

            if (__instance.name.Contains("RemotePlayer"))
                return true;

            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    public static class ClientAIDisableCanSeeEnemyPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "checkStuff")]
    public static class ClientAIDisableCheckStuffPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "checkForCharactersInViewRange")]
    public static class ClientAIDisableViewRangePatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "alertInArea")]
    public static class ClientAIDisableAlertInAreaPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "scareInArea")]
    public static class ClientAIDisableScareInAreaPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "heardSound")]
    public static class ClientAIDisableHeardSoundPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "alertCharactersInArea")]
    public static class ClientAIDisableAlertCharsPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "beAlerted")]
    public static class ClientAIDisableBeAlertedPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "runAway")]
    public static class ClientAIDisableRunAwayPatch
    {
        private static bool Prefix()
        {
            if (!ModConfig.IsLanMode) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }
}
