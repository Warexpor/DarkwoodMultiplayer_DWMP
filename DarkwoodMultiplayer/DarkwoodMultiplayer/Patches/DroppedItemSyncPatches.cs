using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Syncs inventory-item drops and pickups between host and client.
    /// Each dropped item gets a GUID so that when either player picks it up
    /// the remote copy is destroyed too.
    /// </summary>
    internal static class DroppedItemSyncHelpers
    {
        internal static void SendDrop(Transform spawned, InvItemClass item, string prefabPath)
        {
            if (spawned == null) { ModRuntime.Log?.LogInfo("[SendDrop] spawned is null"); return; }
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) { ModRuntime.Log?.LogInfo("[SendDrop] net not connected"); return; }
            if (LanNetworkManager.IsApplyingRemoteState) { ModRuntime.Log?.LogInfo("[SendDrop] applying remote state"); return; }

            string guid = System.Guid.NewGuid().ToString("N");
            ModRuntime.Log?.LogInfo("[SendDrop] adding identifier guid=" + guid + " to " + spawned.name);

            var ident = spawned.gameObject.AddComponent<DroppedItemIdentifier>();
            ident.Id = guid;
            DroppedItemIdentifier.Register(ident);

            Vector3 pos = spawned.position;
            Vector3 euler = spawned.eulerAngles;

            int amt = item.amount;
            float dur = item.durability;
            int ammo = 0;
            if (item.baseClass != null && item.baseClass.hasAmmo)
                ammo = item.ammo;

            net.SendDroppedItemSpawn(new DroppedItemSpawnMessage
            {
                Guid = guid,
                PrefabPath = prefabPath,
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                RotX = euler.x, RotY = euler.y, RotZ = euler.z,
                ItemType = item.type,
                Amount = amt,
                Durability = dur,
                Ammo = ammo
            });
        }

        internal static void SendPickup(Item worldItem)
        {
            ModRuntime.Log?.LogInfo("[SendPickup] called for " + (worldItem != null ? worldItem.name : "null"));

            var ident = worldItem != null ? worldItem.GetComponent<DroppedItemIdentifier>() : null;
            if (ident == null) { ModRuntime.Log?.LogInfo("[SendPickup] no DroppedItemIdentifier found"); return; }
            if (string.IsNullOrEmpty(ident.Id)) { ModRuntime.Log?.LogInfo("[SendPickup] identifier has empty Id"); return; }

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) { ModRuntime.Log?.LogInfo("[SendPickup] net not connected"); return; }
            if (LanNetworkManager.IsApplyingRemoteState) { ModRuntime.Log?.LogInfo("[SendPickup] applying remote state"); return; }

            ModRuntime.Log?.LogInfo("[SendPickup] sending pickup for guid=" + ident.Id);
            net.SendDroppedItemPickup(new DroppedItemPickupMessage { Guid = ident.Id });
        }

        internal static InvItemClass GetItemFromSpawned(Transform t)
        {
            Inventory inv = t.GetComponent<Inventory>();
            if (inv == null || inv.slots == null || inv.slots.Count == 0) return null;
            InvItemClass item = inv.slots[0].invItem;
            if (InvItemClass.isNull(item)) return null;
            return item;
        }
    }

    /// <summary>
    /// Intercepts Inventory-slot drop — Player.spawnDroppedInvItem(InvItemClass).
    /// Adds a GUID and sends a spawn message to the remote peer.
    /// </summary>
    [HarmonyPatch(typeof(Player), "spawnDroppedInvItem", typeof(InvItemClass))]
    public static class PlayerDropItemPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, Transform __result)
        {
            if (__result == null) return;
            InvItemClass item = DroppedItemSyncHelpers.GetItemFromSpawned(__result);
            if (item == null) return;
            string prefab = __instance.inWater ? "Items/DroppedItem_water" : "Items/DroppedItem";
            DroppedItemSyncHelpers.SendDrop(__result, item, prefab);
        }
    }

    /// <summary>
    /// Intercepts the alternate drop path — Player.spawnDroppedInvItemm(bool, string, int).
    /// </summary>
    [HarmonyPatch(typeof(Player), "spawnDroppedInvItemm", typeof(bool), typeof(string), typeof(int))]
    public static class PlayerDropItemmPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, Transform __result)
        {
            if (__result == null) return;
            InvItemClass item = DroppedItemSyncHelpers.GetItemFromSpawned(__result);
            if (item == null) return;
            string prefab = __instance.inWater ? "Items/DroppedItem_water" : "Items/DroppedItem";
            DroppedItemSyncHelpers.SendDrop(__result, item, prefab);
        }
    }

    /// <summary>
    /// When a player picks up a networked dropped item, notify the remote peer
    /// to destroy its copy.
    /// </summary>
    [HarmonyPatch(typeof(Item), "getDroppedItem")]
    public static class PlayerPickupDroppedItemPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Item __instance)
        {
            ModRuntime.Log?.LogInfo("[PickupPrefix] getDroppedItem called on " + __instance.name);

            // Block pickup when a remote player is trapped in this bear trap
            if (ModRuntime.Network is LanNetworkManager net && net.RemoteInBearTrap)
            {
                string name = __instance.name.ToLowerInvariant();
                if (TrapNameHelper.IsTrap(name))
                {
                    ModRuntime.Log?.LogInfo("[PickupPrefix] blocked pickup of \""
                        + __instance.name + "\" — remote player still trapped");
                    return false;
                }
            }

            DroppedItemSyncHelpers.SendPickup(__instance);
            return true;
        }
    }

    /// <summary>
    /// Helper for identifying trap GameObjects by name.
    /// </summary>
    internal static class TrapNameHelper
    {
        public static bool IsTrap(string name)
        {
            return name.Contains("trap") || name.Contains("bear") || name.Contains("snap") || name.Contains("animal");
        }
    }
}
