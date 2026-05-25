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
        internal static void SendBarricadeEvent(Vector3 pos, byte targetType, BarricadeAction action, int health, bool playerBarricade, int mainHealth = -1, int damageAmount = -1)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            Vector3 key = new Vector3((float)System.Math.Round(pos.x, 1), (float)System.Math.Round(pos.y, 1), (float)System.Math.Round(pos.z, 1));
            var msg = new BarricadeEventMessage
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsWindow = targetType,
                Action = action,
                Health = health,
                PlayerBarricade = playerBarricade,
                MainHealth = mainHealth,
                DamageAmount = damageAmount
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
                BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, 0, BarricadeAction.Built, __instance.barricadeHealth, byPlayer);
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
            BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, 0, BarricadeAction.Destroyed, 0, false);
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
                BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, 1, BarricadeAction.Built, __instance.barricadeHealth, byPlayer);
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
            BarricadeSyncHelpers.SendBarricadeEvent(__instance.transform.position, 1, BarricadeAction.Destroyed, 0, false);
        }
    }

    /// <summary>
    /// Syncs ALL door damage (barricade and main health) to the remote peer.
    /// </summary>
    [HarmonyPatch(typeof(Door), "getHit", new[] { typeof(int), typeof(Transform), typeof(bool), typeof(bool) })]
    public static class DoorGetHitPatch
    {
        private static void Postfix(Door __instance, int damage)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;

            if (__instance.barricaded)
            {
                BarricadeSyncHelpers.SendBarricadeEvent(
                    __instance.transform.position, 0,
                    __instance.barricadeHealth <= 0 ? BarricadeAction.Destroyed : BarricadeAction.Damaged,
                    __instance.barricadeHealth, __instance.playerBarricade,
                    __instance.destroyed ? -1 : __instance.health,
                    damage);
            }
            else
            {
                BarricadeSyncHelpers.SendBarricadeEvent(
                    __instance.transform.position, 0,
                    __instance.destroyed ? BarricadeAction.Destroyed : BarricadeAction.Damaged,
                    0, false, __instance.health, damage);
            }
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
                __instance.transform.position, 1,
                __instance.barricadeHealth <= 0 ? BarricadeAction.Destroyed : BarricadeAction.Damaged,
                __instance.barricadeHealth, __instance.playerBarricade);
        }
    }

    /// <summary>
    /// Syncs damage to destructible world items (wardrobes, furniture, etc.).
    /// Captures the position in a Prefix because Item.getHit → die() may move
    /// the transform before the Postfix runs.
    /// </summary>
    [HarmonyPatch(typeof(Item), "getHit", new[] { typeof(int), typeof(Transform), typeof(bool) })]
    public static class ItemGetHitPatch
    {
        private static Vector3 _prePos;

        [HarmonyPrefix]
        private static void Prefix(Item __instance)
        {
            _prePos = __instance.transform.position;
        }

        [HarmonyPostfix]
        private static void Postfix(Item __instance, int damage)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (!__instance.destructible) { ModRuntime.Log?.LogInfo("[ItemHit] not destructible: " + __instance.name); return; }

            Vector3 pos = _prePos;
            bool destroyed = __instance.destroyed;
            int health = __instance.health;

            ModRuntime.Log?.LogInfo("[ItemHit] " + __instance.name + " dmg=" + damage + " health=" + health + " destroyed=" + destroyed + " pos=" + pos);

            BarricadeSyncHelpers.SendBarricadeEvent(
                pos, 2,
                destroyed ? BarricadeAction.Destroyed : BarricadeAction.Damaged,
                health, false, damageAmount: damage);
        }
    }
}
