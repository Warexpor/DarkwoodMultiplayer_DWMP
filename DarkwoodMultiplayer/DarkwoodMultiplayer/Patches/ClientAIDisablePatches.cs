using DarkwoodMultiplayer.Networking;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    internal static class ClientAIConditionalHelper
    {
        internal static bool ShouldSkipAI(Character c)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return false;
            if (c == null || c.name.Contains("RemotePlayer"))
                return false;

            // Before the first host snapshot arrives, freeze all NPCs to prevent
            // client-side AI from acting on stale save data (pre-sync window).
            if (!ClientEntityInterpolationService.HasReceivedFirstSnapshot)
                return true;

            return ClientEntityInterpolationService.IsHostSynced(c);
        }
    }

    [HarmonyPatch(typeof(Character), "Update")]
    public static class ClientCharacterUpdatePatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    public static class ClientAIDisableCanSeeEnemyPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "checkStuff")]
    public static class ClientAIDisableCheckStuffPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "checkForCharactersInViewRange")]
    public static class ClientAIDisableViewRangePatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "alertInArea")]
    public static class ClientAIDisableAlertInAreaPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "scareInArea")]
    public static class ClientAIDisableScareInAreaPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "heardSound")]
    public static class ClientAIDisableHeardSoundPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "alertCharactersInArea")]
    public static class ClientAIDisableAlertCharsPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "beAlerted")]
    public static class ClientAIDisableBeAlertedPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "runAway")]
    public static class ClientAIDisableRunAwayPatch
    {
        private static bool Prefix(Character __instance)
        {
            return !ClientAIConditionalHelper.ShouldSkipAI(__instance);
        }
    }

    [HarmonyPatch(typeof(Sniffer), "Update")]
    public static class ClientSnifferDisablePatch
    {
        private static bool Prefix(Sniffer __instance)
        {
            Character c = __instance.GetComponent<Character>();
            return !ClientAIConditionalHelper.ShouldSkipAI(c);
        }
    }

    [HarmonyPatch(typeof(AIPath), "Update")]
    public static class ClientAIPathDisablePatch
    {
        private static bool Prefix(AIPath __instance)
        {
            Character c = __instance.GetComponent<Character>();
            return !ClientAIConditionalHelper.ShouldSkipAI(c);
        }
    }
}
