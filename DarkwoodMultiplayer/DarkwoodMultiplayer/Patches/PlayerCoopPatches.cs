using DarkwoodMultiplayer.Players;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Player), "registerMe")]
    public static class PlayerRegisterMePatch
    {
        private static bool Prefix(Player __instance)
        {
            if (PlayerProxyBuilder.IsSpawningCoopClone
                || __instance.GetComponent<CoopPlayerMarker>() != null)
            {
                PlayerControlRouter.RegisterSecond(__instance);
                return false;
            }

            if (PlayerControlRouter.MainPlayer != null && __instance != PlayerControlRouter.MainPlayer)
            {
                PlayerControlRouter.RegisterSecond(__instance);
                return false;
            }

            PlayerControlRouter.RegisterMain(__instance);
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "Instance", MethodType.Getter)]
    public static class PlayerInstanceGetterPatch
    {
        private static bool Prefix(ref Player __result)
        {
            if (PlayerControlRouter.TryGetActiveOverride(out Player active))
            {
                __result = active;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "Start")]
    public static class PlayerStartPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!PlayerProxyBuilder.IsSpawningCoopClone
                && __instance.GetComponent<CoopPlayerMarker>() == null)
                return true;

            CoopPlayerBootstrap.RunLightweightStart(__instance, PlayerControlRouter.MainPlayer);
            return false;
        }
    }
}
