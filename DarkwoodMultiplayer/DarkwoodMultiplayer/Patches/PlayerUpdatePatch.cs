using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Prevents input/selection on inactive player instances when a second
    /// player exists (e.g. the remote proxy).  All patches are no-ops when
    /// <see cref="PlayerControlRouter.HasSecond"/> is false.
    /// </summary>
    [HarmonyPatch(typeof(Player), "FindInput")]
    public static class FindInputImmobilisePatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return true;

            if (!PlayerControlRouter.IsActive(__instance))
                return false;

            return !__instance.immobilised;
        }
    }

    [HarmonyPatch(typeof(Player), "FindInputController")]
    public static class FindInputControllerImmobilisePatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return true;

            if (!PlayerControlRouter.IsActive(__instance))
                return false;

            return !__instance.immobilised;
        }
    }

    [HarmonyPatch(typeof(Player), "selectObject", new[] { typeof(Transform), typeof(bool) })]
    public static class SelectObjectActiveOnlyPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return true;

            return PlayerControlRouter.IsActive(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "selectObjectMouseAndKeyboard", new[] { typeof(Transform), typeof(bool) })]
    public static class SelectObjectMKActiveOnlyPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return true;

            return PlayerControlRouter.IsActive(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "selectObjectController", new[] { typeof(Transform), typeof(bool) })]
    public static class SelectObjectControllerActiveOnlyPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return true;

            return PlayerControlRouter.IsActive(__instance);
        }
    }
}
