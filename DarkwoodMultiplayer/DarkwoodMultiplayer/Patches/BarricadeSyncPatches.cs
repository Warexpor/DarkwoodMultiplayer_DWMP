using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    internal static class BarricadeSyncHelpers
    {
        internal static void SendBarricadeEvent(Vector3 pos, bool isWindow, BarricadeAction action, int health, bool playerBarricade)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            Vector3 key = new Vector3((float)System.Math.Round(pos.x, 1), (float)System.Math.Round(pos.y, 1), (float)System.Math.Round(pos.z, 1));
            var msg = new BarricadeEventMessage
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsWindow = isWindow ? (byte)1 : (byte)0,
                Action = action,
                Health = health,
                PlayerBarricade = playerBarricade
            };
            LanNetworkManager.Instance.Send(NetMessageType.BarricadeEvent, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    [HarmonyPatch(typeof(Door), "barricade", new[] { typeof(bool) })]
    public static class DoorBarricadePatch
    {
        private static void Postfix(Door __instance, bool byPlayer)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (__instance.barricaded)
            {
                BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, false, BarricadeAction.Built, __instance.barricadeHealth, byPlayer);
            }
        }
    }

    [HarmonyPatch(typeof(Door), "destroyBarricade", new[] { typeof(bool) })]
    public static class DoorDestroyBarricadePatch
    {
        private static void Postfix(Door __instance, bool silent)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, false, BarricadeAction.Destroyed, 0, false);
        }
    }

    [HarmonyPatch(typeof(Window), "barricade", new[] { typeof(int), typeof(bool) })]
    public static class WindowBarricadePatch
    {
        private static void Postfix(Window __instance, int destHealth, bool byPlayer)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (__instance.barricaded)
            {
                BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, true, BarricadeAction.Built, __instance.barricadeHealth, byPlayer);
            }
        }
    }

    [HarmonyPatch(typeof(Window), "destroyBarricade", new[] { typeof(bool) })]
    public static class WindowDestroyBarricadePatch
    {
        private static void Postfix(Window __instance, bool silent)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, true, BarricadeAction.Destroyed, 0, false);
        }
    }

    [HarmonyPatch(typeof(Door), "getHit", new[] { typeof(int), typeof(Transform), typeof(bool), typeof(bool) })]
    public static class DoorGetHitPatch
    {
        private static void Postfix(Door __instance, int damage)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (!__instance.barricaded && __instance.barricadeHealth <= 0) return;

            BarricadeSyncHelpers.SendBarricadeEvent(
                __instance.transform.position, false,
                __instance.barricadeHealth <= 0 ? BarricadeAction.Destroyed : BarricadeAction.Damaged,
                __instance.barricadeHealth, __instance.playerBarricade);
        }
    }

    [HarmonyPatch(typeof(Window), "getHit", new[] { typeof(int), typeof(Transform), typeof(bool) })]
    public static class WindowGetHitPatch
    {
        private static void Postfix(Window __instance, int damage)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (!__instance.barricaded) return;

            BarricadeSyncHelpers.SendBarricadeEvent(
                __instance.transform.position, true,
                __instance.barricadeHealth <= 0 ? BarricadeAction.Destroyed : BarricadeAction.Damaged,
                __instance.barricadeHealth, __instance.playerBarricade);
        }
    }
}
