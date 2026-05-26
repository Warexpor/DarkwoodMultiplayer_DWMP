using System.Collections.Generic;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch]
    public static class ItemDoublePickupPatch
    {
        private static readonly HashSet<string> PlayerPlacedContainerKeys = new HashSet<string>();
        private static bool _disarmInProgress;

        private static readonly HashSet<string> KnownUpgradeItemTypes = new HashSet<string>
        {
            "meat",
            "exp_mushroom",
            "exp_bio1_nightMushroom_01"
        };

        private static bool IsUpgradeType(string type)
        {
            return type != null && KnownUpgradeItemTypes.Contains(type);
        }

        private static bool IsExpItem(InvItem invItem)
        {
            return invItem != null && (invItem.isExpItem || IsUpgradeType(invItem.type));
        }

        private static bool IsExpItemClass(InvItemClass invItemClass)
        {
            return invItemClass != null && invItemClass.baseClass != null &&
                (invItemClass.baseClass.isExpItem || IsUpgradeType(invItemClass.type));
        }

        private static string MakeContainerKey(Vector3 pos, int slotIdx)
        {
            return $"{pos.x:F2}:{pos.y:F2}:{pos.z:F2}:{slotIdx}";
        }

        public static void MarkContainerSlotPlayerPlaced(Vector3 pos, int slotIdx)
        {
            PlayerPlacedContainerKeys.Add(MakeContainerKey(pos, slotIdx));
        }

        private static bool IsPlayerPlacedSlot(InvSlot slot)
        {
            if (slot?.inventory == null) return false;
            if (slot.inventory.invType != Inventory.InvType.itemInv) return false;
            int idx = slot.inventory.slots.IndexOf(slot);
            if (idx < 0) return false;
            return PlayerPlacedContainerKeys.Contains(MakeContainerKey(slot.inventory.transform.position, idx));
        }

        private static void Log(string msg)
        {
            ModRuntime.Log?.LogInfo($"[ItemDouble] {msg}");
        }

        [HarmonyPatch(typeof(ItemsDatabase), "Awake")]
        [HarmonyPostfix]
        private static void DumpItemTypes()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null) return;
            Log("=== Upgrade-relevant item dump ===");
            foreach (var kvp in db.itemsDict)
            {
                InvItem prefab = db.getItem(kvp.Key, instantiate: false);
                if (prefab == null) continue;
                bool mutatedCat = prefab.categories != null && prefab.categories.Contains(InvItem.Category.mutated);
                bool isUpgrade = prefab.isExpItem || mutatedCat;
                if (isUpgrade || KnownUpgradeItemTypes.Contains(prefab.type))
                {
                    string cats = prefab.categories != null ? string.Join(",", prefab.categories) : "null";
                    Log($"  type='{prefab.type}', isExpItem={prefab.isExpItem}, categories=[{cats}]");
                }
            }
            Log("=== End dump ===");
        }

        [HarmonyPatch(typeof(Inventory), "addItemTypeToPlayer")]
        [HarmonyPrefix]
        private static void OnAddItemTypeToPlayer(Inventory __instance, string type, int amount, bool dropIfNoRoom)
        {
            Log($"addItemTypeToPlayer called: type='{type}', amount={amount}, disarmFlag={_disarmInProgress}");
        }

        [HarmonyPatch(typeof(Inventory), "addItemTypeToPlayer")]
        [HarmonyPrefix]
        private static void OnAddItemTypeToPlayer_DoDouble(string type, ref int amount)
        {
            if (!_disarmInProgress)
                return;
            _disarmInProgress = false;
            Log($"  --> DOUBLING from {amount} to {amount * 2}");
            amount *= 2;
        }

        [HarmonyPatch(typeof(Item), "disarm")]
        [HarmonyPrefix]
        private static void OnDisarm(Item __instance)
        {
            if (__instance.invItem == null)
            {
                Log("OnDisarm: invItem is NULL");
                return;
            }
            Log($"OnDisarm: type='{__instance.invItem.type}', isExpItem={__instance.invItem.isExpItem}, invItemAmount={__instance.invItemAmount}");
            if (!IsExpItem(__instance.invItem))
            {
                Log("  --> NOT an exp item (base class), skipping");
                return;
            }
            Log("  --> WILL double via addItemTypeToPlayer");
            _disarmInProgress = true;
        }

        [HarmonyPatch(typeof(InvSlot), "transferItemAllToPlayer")]
        [HarmonyPrefix]
        private static void OnTransferAllToPlayer(InvSlot __instance)
        {
            if (InvItemClass.isNull(__instance.invItem))
            {
                Log("OnTransferAllToPlayer: invItem is null");
                return;
            }
            Log($"OnTransferAllToPlayer: type='{__instance.invItem.type}', isExpItem={__instance.invItem.baseClass?.isExpItem}, amount={__instance.invItem.amount}");
            if (!IsExpItemClass(__instance.invItem))
            {
                Log("  --> NOT an exp item, skipping");
                return;
            }
            if (__instance.inventory != null && __instance.inventory.gameObject.GetComponent<DroppedItemIdentifier>() != null)
            {
                Log("  --> blocked by DroppedItemIdentifier");
                return;
            }
            if (IsPlayerPlacedSlot(__instance))
            {
                Log("  --> blocked by PlayerPlacedSlot");
                return;
            }
            Log($"  --> DOUBLING from {__instance.invItem.amount} to {__instance.invItem.amount * 2}");
            __instance.invItem.amount *= 2;
        }

        [HarmonyPatch(typeof(InvSlot), "grabItem")]
        [HarmonyPrefix]
        private static void OnGrabItem(InvSlot __instance)
        {
            if (InvItemClass.isNull(__instance.invItem))
            {
                Log("OnGrabItem: invItem is null");
                return;
            }
            Log($"OnGrabItem: type='{__instance.invItem.type}', isExpItem={__instance.invItem.baseClass?.isExpItem}, amount={__instance.invItem.amount}");
            if (!IsExpItemClass(__instance.invItem))
            {
                Log("  --> NOT an exp item, skipping");
                return;
            }
            if (IsPlayerPlacedSlot(__instance))
            {
                Log("  --> blocked by PlayerPlacedSlot");
                return;
            }
            Log($"  --> DOUBLING from {__instance.invItem.amount} to {__instance.invItem.amount * 2}");
            __instance.invItem.amount *= 2;
        }

        private static bool _shouldDoubleTransfer;

        [HarmonyPatch(typeof(InvSlot), "transferItemToPlayer")]
        [HarmonyPrefix]
        private static void OnTransferToPlayerPrefix(InvSlot __instance)
        {
            _shouldDoubleTransfer = false;
            if (InvItemClass.isNull(__instance.invItem))
            {
                Log("OnTransferToPlayerPrefix: invItem is null");
                return;
            }
            Log($"OnTransferToPlayerPrefix: type='{__instance.invItem.type}', isExpItem={__instance.invItem.baseClass?.isExpItem}, amount={__instance.invItem.amount}");
            if (!IsExpItemClass(__instance.invItem))
            {
                Log("  --> NOT an exp item, skipping");
                return;
            }
            if (IsPlayerPlacedSlot(__instance))
            {
                Log("  --> blocked by PlayerPlacedSlot");
                return;
            }
            Log("  --> will double");
            _shouldDoubleTransfer = true;
        }

        [HarmonyPatch(typeof(InvSlot), "transferItemToPlayer")]
        [HarmonyPostfix]
        private static void OnTransferToPlayerPostfix(InvSlot __instance)
        {
            if (!_shouldDoubleTransfer) return;
            _shouldDoubleTransfer = false;
            if (!IsExpItemClass(__instance.invItem)) return;
            Player player = Player.Instance;
            if (player == null) return;
            Log($"OnTransferToPlayerPostfix: adding extra copy of '{__instance.invItem.type}'");
            player.Inventory.addItemType(__instance.invItem.type, 1);
        }

        [HarmonyPatch(typeof(InvSlot), "placeItem")]
        [HarmonyPostfix]
        private static void OnPlaceItem(InvSlot __instance)
        {
            if (__instance.inventory == null || __instance.inventory.invType != Inventory.InvType.itemInv) return;
            int idx = __instance.inventory.slots.IndexOf(__instance);
            if (idx >= 0)
                MarkContainerSlotPlayerPlaced(__instance.inventory.transform.position, idx);
        }

        [HarmonyPatch(typeof(InvSlot), "controllerPlaceItem")]
        [HarmonyPostfix]
        private static void OnControllerPlaceItem(InvSlot __instance)
        {
            if (__instance.inventory == null || __instance.inventory.invType != Inventory.InvType.itemInv) return;
            int idx = __instance.inventory.slots.IndexOf(__instance);
            if (idx >= 0)
                MarkContainerSlotPlayerPlaced(__instance.inventory.transform.position, idx);
        }
    }
}
