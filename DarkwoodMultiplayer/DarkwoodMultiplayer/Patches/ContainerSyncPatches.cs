using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Shared helper methods for container (item-inventory) interaction sync.
    /// </summary>
    internal static class ContainerSyncHelpers
    {
        internal static bool IsContainer(InvSlot slot)
        {
            return slot.inventory != null && slot.inventory.invType == Inventory.InvType.itemInv;
        }

        internal static bool IsContainer(Inventory inv)
        {
            return inv != null && inv.invType == Inventory.InvType.itemInv;
        }

        internal static void SendContainerAction(ContainerAction action, Vector3 pos, int slotIdx, string itemType, int amount, float durability, int ammo, bool isPlayerPlaced = false)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (Core.loadingGame) return;
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (slotIdx < 0) return;

            var msg = new ContainerItemMessage
            {
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                Action = action,
                SlotIndex = (byte)slotIdx,
                ItemType = itemType ?? "",
                Amount = amount,
                Durability = durability,
                Ammo = ammo,
                IsPlayerPlaced = isPlayerPlaced
            };
            LanNetworkManager.Instance.Send(NetMessageType.ContainerItem, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>
    /// Syncs container item removal when a player grabs an item from a
    /// container slot (e.g. looting a crate).
    /// </summary>
    [HarmonyPatch(typeof(InvSlot), "grabItem")]
    public static class ContainerGrabItemPatch
    {
        private static string _type;
        private static int _amount;
        private static float _dur;
        private static int _ammo;
        private static bool _isContainer;
        private static Vector3 _pos;
        private static int _idx;

        private static void Prefix(InvSlot __instance)
        {
            _isContainer = ContainerSyncHelpers.IsContainer(__instance);
            if (!_isContainer) return;
            if (InvItemClass.isNull(__instance.invItem)) { _isContainer = false; return; }
            _type = __instance.invItem.type;
            _amount = __instance.invItem.amount;
            _dur = __instance.invItem.durability;
            _ammo = __instance.invItem.ammo;
            _pos = __instance.inventory.transform.position;
            _idx = __instance.inventory.slots.IndexOf(__instance);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            ContainerSyncHelpers.SendContainerAction(ContainerAction.RemoveItem, _pos, _idx, _type, _amount, _dur, _ammo);
            _isContainer = false;
        }
    }

    /// <summary>
    /// Syncs container item transfer to player inventory (single item
    /// from a container slot).
    /// </summary>
    [HarmonyPatch(typeof(InvSlot), "transferItemToPlayer")]
    public static class ContainerTransferItemPatch
    {
        private static bool _isContainer;
        private static string _type;
        private static int _amount;
        private static float _dur;
        private static int _ammo;
        private static Vector3 _pos;
        private static int _idx;

        private static void Prefix(InvSlot __instance)
        {
            _isContainer = ContainerSyncHelpers.IsContainer(__instance);
            if (!_isContainer) return;
            if (InvItemClass.isNull(__instance.invItem)) { _isContainer = false; return; }
            _type = __instance.invItem.type;
            _amount = 1;
            _dur = __instance.invItem.durability;
            _ammo = __instance.invItem.ammo;
            _pos = __instance.inventory.transform.position;
            _idx = __instance.inventory.slots.IndexOf(__instance);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            ContainerSyncHelpers.SendContainerAction(ContainerAction.TakeItem, _pos, _idx, _type, _amount, _dur, _ammo);
            _isContainer = false;
        }
    }

    /// <summary>
    /// Syncs transferring all items from a container slot to the player
    /// inventory (e.g. shift-click to grab a stack).
    /// </summary>
    [HarmonyPatch(typeof(InvSlot), "transferItemAllToPlayer")]
    public static class ContainerTransferAllPatch
    {
        private static bool _isContainer;
        private static string _type;
        private static int _amount;
        private static float _dur;
        private static int _ammo;
        private static Vector3 _pos;
        private static int _idx;

        private static void Prefix(InvSlot __instance)
        {
            _isContainer = ContainerSyncHelpers.IsContainer(__instance);
            if (!_isContainer) return;
            if (InvItemClass.isNull(__instance.invItem)) { _isContainer = false; return; }
            _type = __instance.invItem.type;
            _amount = __instance.invItem.amount;
            _dur = __instance.invItem.durability;
            _ammo = __instance.invItem.ammo;
            _pos = __instance.inventory.transform.position;
            _idx = __instance.inventory.slots.IndexOf(__instance);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            ContainerSyncHelpers.SendContainerAction(ContainerAction.RemoveItem, _pos, _idx, _type, _amount, _dur, _ammo);
            _isContainer = false;
        }
    }

    /// <summary>
    /// Syncs placing an item from the player's hand into a container slot.
    /// </summary>
    [HarmonyPatch(typeof(InvSlot), "placeItem")]
    public static class ContainerPlaceItemPatch
    {
        private static bool _isContainer;
        private static string _type;
        private static int _amount;
        private static float _dur;
        private static int _ammo;
        private static Vector3 _pos;
        private static int _idx;

        private static void Prefix(InvSlot __instance)
        {
            _isContainer = ContainerSyncHelpers.IsContainer(__instance);
            if (!_isContainer) return;

            var currentItem = Player.Instance?.currentItem;
            if (currentItem == null || InvItemClass.isNull(currentItem)) { _isContainer = false; return; }
            _type = currentItem.type;
            _amount = currentItem.amount;
            _dur = currentItem.durability;
            _ammo = currentItem.ammo;
            _pos = __instance.inventory.transform.position;
            _idx = __instance.inventory.slots.IndexOf(__instance);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            ContainerSyncHelpers.SendContainerAction(ContainerAction.PlaceItem, _pos, _idx, _type, _amount, _dur, _ammo, isPlayerPlaced: true);
            _isContainer = false;
        }
    }

    /// <summary>Slot state snapshot for change detection.</summary>
    internal struct SlotSnapshot
    {
        public int Index;
        public string Type;
        public int Amount;
        public float Durability;
        public int Ammo;
    }

    /// <summary>
    /// Shared helper for taking inventory snapshots and sending diffs.
    /// </summary>
    internal static class ContainerSnapshotHelper
    {
        internal static Dictionary<int, SlotSnapshot> TakeSnapshot(Inventory inv)
        {
            var dict = new Dictionary<int, SlotSnapshot>();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var slot = inv.slots[i];
                if (!InvItemClass.isNull(slot.invItem))
                    dict[i] = new SlotSnapshot { Index = i, Type = slot.invItem.type, Amount = slot.invItem.amount, Durability = slot.invItem.durability, Ammo = slot.invItem.ammo };
            }
            return dict;
        }

        internal static void SendDiff(Inventory inv, Dictionary<int, SlotSnapshot> before)
        {
            var after = TakeSnapshot(inv);
            Vector3 pos = inv.transform.position;

            foreach (var kv in after)
            {
                if (before.TryGetValue(kv.Key, out var prev))
                {
                    if (kv.Value.Type == prev.Type && kv.Value.Amount > prev.Amount)
                        ContainerSyncHelpers.SendContainerAction(ContainerAction.PlaceItem, pos, kv.Key, kv.Value.Type, kv.Value.Amount - prev.Amount, kv.Value.Durability, kv.Value.Ammo, isPlayerPlaced: true);
                }
                else
                {
                    ContainerSyncHelpers.SendContainerAction(ContainerAction.PlaceItem, pos, kv.Key, kv.Value.Type, kv.Value.Amount, kv.Value.Durability, kv.Value.Ammo, isPlayerPlaced: true);
                }
            }
        }
    }

    /// <summary>Syncs transferring 1 item from player inventory to the opened container.</summary>
    [HarmonyPatch(typeof(InvSlot), "transferItemToOpenedInv")]
    public static class ContainerTransferToOpenedInvPatch
    {
        private static bool _isContainer;
        private static Dictionary<int, SlotSnapshot> _snapshot;

        private static void Prefix(InvSlot __instance)
        {
            Inventory destInv = Player.Instance?.openedItemInventory2 ?? Player.Instance?.openedItemInventory;
            _isContainer = ContainerSyncHelpers.IsContainer(destInv);
            if (!_isContainer) return;
            _snapshot = ContainerSnapshotHelper.TakeSnapshot(destInv);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            Inventory destInv = Player.Instance?.openedItemInventory2 ?? Player.Instance?.openedItemInventory;
            if (destInv == null) { _isContainer = false; return; }
            ContainerSnapshotHelper.SendDiff(destInv, _snapshot);
            _isContainer = false;
            _snapshot = null;
        }
    }

    /// <summary>Syncs transferring all items from player inventory to the opened container.</summary>
    [HarmonyPatch(typeof(InvSlot), "transferItemAllToOpenedInv")]
    public static class ContainerTransferAllToOpenedInvPatch
    {
        private static bool _isContainer;
        private static Dictionary<int, SlotSnapshot> _snapshot;

        private static void Prefix(InvSlot __instance)
        {
            Inventory destInv = Player.Instance?.openedItemInventory2 ?? Player.Instance?.openedItemInventory;
            _isContainer = ContainerSyncHelpers.IsContainer(destInv);
            if (!_isContainer) return;
            _snapshot = ContainerSnapshotHelper.TakeSnapshot(destInv);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            Inventory destInv = Player.Instance?.openedItemInventory2 ?? Player.Instance?.openedItemInventory;
            if (destInv == null) { _isContainer = false; return; }
            ContainerSnapshotHelper.SendDiff(destInv, _snapshot);
            _isContainer = false;
            _snapshot = null;
        }
    }

    /// <summary>Syncs placing an item into a container slot via controller input.</summary>
    [HarmonyPatch(typeof(InvSlot), "controllerPlaceItem")]
    public static class ContainerControllerPlaceItemPatch
    {
        private static bool _isContainer;
        private static string _type;
        private static int _amount;
        private static float _dur;
        private static int _ammo;
        private static Vector3 _pos;
        private static int _idx;

        private static void Prefix(InvSlot __instance)
        {
            _isContainer = ContainerSyncHelpers.IsContainer(__instance);
            if (!_isContainer) return;

            var pickedUp = Singleton<Controller>.Instance?.pickedUpItem;
            if (pickedUp == null || InvItemClass.isNull(pickedUp)) { _isContainer = false; return; }
            _type = pickedUp.type;
            _amount = pickedUp.amount;
            _dur = pickedUp.durability;
            _ammo = pickedUp.ammo;
            _pos = __instance.inventory.transform.position;
            _idx = __instance.inventory.slots.IndexOf(__instance);
        }

        private static void Postfix(InvSlot __instance)
        {
            if (!_isContainer) return;
            ContainerSyncHelpers.SendContainerAction(ContainerAction.PlaceItem, _pos, _idx, _type, _amount, _dur, _ammo, isPlayerPlaced: true);
            _isContainer = false;
        }
    }
}
