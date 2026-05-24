using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Shared helper methods for barricade event synchronization (build,
    /// damage, destroy) between host and clients.
    /// </summary>
    internal static class BarricadeSyncHelpers
    {
        internal static void SendBarricadeEvent(Vector3 pos, bool isWindow, BarricadeAction action, int health, bool playerBarricade)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            // Round position to avoid floating-point mismatch between peers
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

    /// <summary>
    /// Syncs barricade build event on doors to remote clients.
    /// </summary>
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

    /// <summary>
    /// Syncs barricade destruction on doors to remote clients.
    /// </summary>
    [HarmonyPatch(typeof(Door), "destroyBarricade", new[] { typeof(bool) })]
    public static class DoorDestroyBarricadePatch
    {
        private static void Postfix(Door __instance, bool silent)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, false, BarricadeAction.Destroyed, 0, false);
        }
    }

    /// <summary>
    /// Syncs barricade build event on windows to remote clients.
    /// </summary>
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

    /// <summary>
    /// Syncs barricade destruction on windows to remote clients.
    /// </summary>
    [HarmonyPatch(typeof(Window), "destroyBarricade", new[] { typeof(bool) })]
    public static class WindowDestroyBarricadePatch
    {
        private static void Postfix(Window __instance, bool silent)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, true, BarricadeAction.Destroyed, 0, false);
        }
    }

    /// <summary>
    /// Syncs barricade damage/health changes on doors to remote clients
    /// after getHit is called (including destruction when health reaches zero).
    /// </summary>
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

    /// <summary>
    /// Syncs barricade damage/health changes on windows to remote clients
    /// after getHit is called (including destruction when health reaches zero).
    /// </summary>
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
