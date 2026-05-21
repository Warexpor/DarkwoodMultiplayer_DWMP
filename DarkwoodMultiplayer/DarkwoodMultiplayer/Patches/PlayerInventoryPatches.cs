using DarkwoodMultiplayer.Players;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Player), "getIntoInventory")]
    public static class GetIntoInventoryPatch
    {
        private static void Prefix(Player __instance)
        {
            bool isP2 = PlayerControlRouter.HasSecond && __instance == PlayerControlRouter.SecondPlayer;
            ModRuntime.Log?.LogInfo($"[getIntoInventory Patch] {(isP2 ? "P2" : "other")} called. heldItem={__instance.heldItem}, invOpen={__instance.Inventory?.open}");

            if (!PlayerControlRouter.HasSecond)
                return;

            if (__instance != PlayerControlRouter.SecondPlayer)
                return;

            if (__instance.heldItem != null)
            {
                UnityEngine.Object.Destroy(__instance.heldItem);
                __instance.heldItem = null;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "initiateOpenCloseInventory", new System.Type[0])]
    public static class InitiateOpenCloseInventoryNoParamPatch
    {
        private static void Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return;
            if (__instance != PlayerControlRouter.SecondPlayer)
                return;
            ModRuntime.Log?.LogInfo($"[P2 initiateOpenCloseInventory()] entered. wantToInv={__instance.wantToInventory}, gotInv={__instance.gotInventory}, gettingInv={__instance.gettingInventory}, hidingInv={__instance.hidingInventory}");
        }
    }

    [HarmonyPatch(typeof(Player), "closeInventory")]
    public static class CloseInventoryPatch
    {
        private static void Prefix(Player __instance)
        {
            if (!PlayerControlRouter.HasSecond)
                return;
            if (__instance == PlayerControlRouter.SecondPlayer)
                ModRuntime.Log?.LogInfo($"[P2 closeInventory] called. open={__instance.Inventory?.open}, craftOpen={__instance.Crafting?.open}");
        }
    }
}
