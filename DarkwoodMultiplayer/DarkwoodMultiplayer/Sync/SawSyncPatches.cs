using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    [HarmonyPatch(typeof(Saw), "addFuel")]
    public static class SawAddFuelPatch
    {
        private static void Postfix(Saw __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;
            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            var t = Traverse.Create(__instance);
            Inventory inv = t.Field("inventory").GetValue<Inventory>();

            int woodLogAmount = 0, woodAmount = 0;
            if (inv != null)
            {
                var logItem = inv.getItem("woodLog");
                if (!InvItemClass.isNull(logItem)) woodLogAmount = logItem.amount;
                var woodItem = inv.getItem("wood");
                if (!InvItemClass.isNull(woodItem)) woodAmount = woodItem.amount;
            }

            ModRuntime.Network.SendSawState(new SawStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                Fuel = __instance.fuel,
                WoodLogAmount = woodLogAmount,
                WoodAmount = woodAmount
            });
            ModRuntime.Log?.LogInfo("[SawSync] send addFuel at " + p + " fuel=" + __instance.fuel);
        }
    }

    [HarmonyPatch(typeof(Saw), "convert")]
    public static class SawConvertPatch
    {
        private static void Postfix(Saw __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;
            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            var t = Traverse.Create(__instance);
            Inventory inv = t.Field("inventory").GetValue<Inventory>();

            int woodLogAmount = 0, woodAmount = 0;
            if (inv != null)
            {
                var logItem = inv.getItem("woodLog");
                if (!InvItemClass.isNull(logItem)) woodLogAmount = logItem.amount;
                var woodItem = inv.getItem("wood");
                if (!InvItemClass.isNull(woodItem)) woodAmount = woodItem.amount;
            }

            ModRuntime.Network.SendSawState(new SawStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                Fuel = __instance.fuel,
                WoodLogAmount = woodLogAmount,
                WoodAmount = woodAmount
            });
            ModRuntime.Log?.LogInfo("[SawSync] send convert at " + p + " fuel=" + __instance.fuel);
        }
    }
}
