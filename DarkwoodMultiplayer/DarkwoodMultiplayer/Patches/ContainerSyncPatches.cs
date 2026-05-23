using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    internal static class ContainerSyncHelpers
    {
        internal static bool IsContainer(InvSlot slot)
        {
            return slot.inventory != null && slot.inventory.invType == Inventory.InvType.itemInv;
        }

        internal static void SendContainerAction(ContainerAction action, Vector3 pos, int slotIdx, string itemType, int amount, float durability, int ammo)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
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
                Ammo = ammo
            };
            LanNetworkManager.Instance.Send(NetMessageType.ContainerItem, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

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
            ContainerSyncHelpers.SendContainerAction(ContainerAction.PlaceItem, _pos, _idx, _type, _amount, _dur, _ammo);
            _isContainer = false;
        }
    }

}
