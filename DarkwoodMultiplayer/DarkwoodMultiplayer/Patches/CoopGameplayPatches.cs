using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Inventory), "Start")]
    public static class InventoryStartPatch
    {
        private static void Postfix(Inventory __instance)
        {
            Player player = __instance.GetComponent<Player>();
            if (player == null || player.GetComponent<CoopPlayerMarker>() == null)
                return;

            CoopPlayerBootstrap.SanitizeInventorySlots(player);
        }
    }

    [HarmonyPatch(typeof(Character), "attackPlayer")]
    public static class CharacterAttackPlayerPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (!ModConfig.IsLocalMode || !CoopPlayerRegistry.HasMultiplePlayers)
                return true;

            Transform target = CoopPlayerRegistry.GetNearestLivingPlayerTransform(__instance.transform.position);
            if (target == null)
                return true;

            __instance.attackCharacter(target);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "isInSight")]
    public static class PlayerIsInSightPatch
    {
        private static bool _isInSightRecursing;

        private static void Postfix(
            Player __instance,
            Transform destTransform,
            bool canBeFarAway,
            int radius,
            ref bool __result)
        {
            if (__result || !ModConfig.IsLocalMode || !CoopPlayerRegistry.HasMultiplePlayers || _isInSightRecursing)
                return;

            _isInSightRecursing = true;
            try
            {
                foreach (Player other in CoopPlayerRegistry.AllPlayers())
                {
                    if (other == null || other == __instance)
                        continue;

                    if (other.isInSight(destTransform, canBeFarAway, radius))
                    {
                        __result = true;
                        return;
                    }
                }
            }
            finally
            {
                _isInSightRecursing = false;
            }
        }
    }

}
