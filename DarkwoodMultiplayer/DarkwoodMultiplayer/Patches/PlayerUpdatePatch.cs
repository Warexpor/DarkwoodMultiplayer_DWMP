using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Player), "Update")]
    public static class PlayerUpdatePatch
    {
        private static void Postfix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return;

            if (LocalSecondPlayerManager.Instance == null)
                return;

            Player active = LocalSecondPlayerManager.IsControllingSecond
                ? PlayerControlRouter.SecondPlayer
                : PlayerControlRouter.MainPlayer;

            if (__instance != active)
                return;

            if (LocalSecondPlayerManager.IsControllingSecond)
                PlayerVisionController.RefreshMainFov(PlayerControlRouter.GetMainForVision());

            LocalSecondPlayerManager.Instance.EnforceVisionAndCamera();
        }
    }

    [HarmonyPatch(typeof(Player), "FindInput")]
    public static class FindInputImmobilisePatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return true;

            if (!PlayerControlRouter.IsActive(__instance))
                return false;

            if (__instance == PlayerControlRouter.SecondPlayer)
                ModRuntime.Log?.LogInfo($"[P2 FindInput] immobilised={__instance.immobilised} entering");
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
