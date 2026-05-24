using DarkwoodMultiplayer.Networking;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// On the CLIENT side, most AI routines must be suppressed because entity
    /// positions are driven by interpolation from host snapshots, not local AI.
    /// These patches return false (skip original) for the client role.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Update")]
    public static class ClientCharacterUpdatePatch
    {
        /// <summary>Skip Character.Update on the client (AI is host-authoritative).</summary>
        private static bool Prefix(Character __instance)
        {
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
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "checkStuff")]
    public static class ClientAIDisableCheckStuffPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "checkForCharactersInViewRange")]
    public static class ClientAIDisableViewRangePatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "alertInArea")]
    public static class ClientAIDisableAlertInAreaPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "scareInArea")]
    public static class ClientAIDisableScareInAreaPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "heardSound")]
    public static class ClientAIDisableHeardSoundPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "alertCharactersInArea")]
    public static class ClientAIDisableAlertCharsPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "beAlerted")]
    public static class ClientAIDisableBeAlertedPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "runAway")]
    public static class ClientAIDisableRunAwayPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Sniffer), "Update")]
    public static class ClientSnifferDisablePatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(AIPath), "Update")]
    public static class ClientAIPathDisablePatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client) return true;
            return false;
        }
    }
}
