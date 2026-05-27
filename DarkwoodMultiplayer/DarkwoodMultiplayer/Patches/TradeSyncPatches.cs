using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Synchronizes completed trades between peers.
    /// When one player buys items from a trader, those items are removed
    /// from the trader's inventory on both sides (shared assortment).
    /// Reputation is NOT synced — each player maintains their own reputation
    /// independently (per-player reputation).
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), "acceptTrade")]
    public static class TradeSyncAcceptPatch
    {
        private static readonly List<InvItemClass> _capturedTraderItems = new List<InvItemClass>();

        private static void Prefix(DialogueWindow __instance)
        {
            _capturedTraderItems.Clear();
            if (__instance.npc == null) return;

            var allItems = __instance.exchangeTrader.getAllItems();
            for (int i = 0; i < allItems.Count; i++)
            {
                if (!InvItemClass.isNull(allItems[i]))
                {
                    _capturedTraderItems.Add(allItems[i]);
                }
            }
        }

        private static void Postfix(DialogueWindow __instance)
        {
            var captured = new List<InvItemClass>(_capturedTraderItems);
            _capturedTraderItems.Clear();

            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            if (__instance.npc == null) return;

            if (captured.Count == 0) return;

            // Build a compact list of unique item types + amounts
            var typeDict = new Dictionary<string, int>();
            for (int i = 0; i < captured.Count; i++)
            {
                if (captured[i] == null) continue;
                string type = captured[i].type;
                if (string.IsNullOrEmpty(type)) continue;
                if (typeDict.ContainsKey(type))
                    typeDict[type] += captured[i].amount;
                else
                    typeDict[type] = captured[i].amount;
            }

            if (typeDict.Count == 0) return;

            string[] itemTypes = new string[typeDict.Count];
            int[] amounts = new int[typeDict.Count];
            int idx = 0;
            foreach (var kv in typeDict)
            {
                itemTypes[idx] = kv.Key;
                amounts[idx] = kv.Value;
                idx++;
            }

            var msg = new TradeSyncMessage
            {
                NpcName = __instance.npc.name,
                ItemCount = typeDict.Count,
                ItemTypes = itemTypes,
                Amounts = amounts
            };

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null)
            {
                ModRuntime.Log?.LogInfo($"[TradeSync] sending trade: {msg.NpcName} sold {msg.ItemCount} item types");
                net.Send(NetMessageType.TradeSync, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
            }
        }
    }

    /// <summary>
    /// Removes traded items from the local trader's inventory when notified
    /// by the remote peer. This keeps the merchant assortment shared.
    /// </summary>
    internal static class TradeSyncHandler
    {
        public static void HandleTradeSync(TradeSyncMessage msg)
        {
            if (string.IsNullOrEmpty(msg.NpcName)) return;
            if (msg.ItemCount <= 0 || msg.ItemTypes == null) return;

            NPC npc = FindNpcByName(msg.NpcName);
            if (npc == null)
            {
                ModRuntime.Log?.LogWarning($"[TradeSync] NPC '{msg.NpcName}' not found locally");
                return;
            }

            Inventory npcInv = npc.inventory;
            if (npcInv == null)
            {
                ModRuntime.Log?.LogWarning($"[TradeSync] NPC '{msg.NpcName}' has no inventory");
                return;
            }

            for (int i = 0; i < msg.ItemCount && i < msg.ItemTypes.Length; i++)
            {
                string type = msg.ItemTypes[i];
                if (string.IsNullOrEmpty(type)) continue;
                int amount = msg.Amounts != null && i < msg.Amounts.Length ? msg.Amounts[i] : 1;

                InvItemClass existing = npcInv.getItem(type);
                if (!InvItemClass.isNull(existing))
                {
                    int toRemove = Mathf.Min(amount, existing.amount);
                    existing.removeAmount(toRemove);
                    ModRuntime.Log?.LogInfo($"[TradeSync] removed {toRemove}x {type} from {msg.NpcName}");
                }
                else
                {
                    ModRuntime.Log?.LogInfo($"[TradeSync] item {type} not found in {msg.NpcName} (already removed)");
                }
            }

            npcInv.refreshReputation();

            // If the local player has the trader UI open for this NPC, refresh the display
            var dw = Singleton<UI>.Instance?.dialogueWindow;
            if (dw != null && dw.opened && dw.npc == npc && dw.currentMenu == DialogueWindow.CurrentMenu.trade)
            {
                npcInv.refreshIcons();
            }
        }

        private static NPC FindNpcByName(string name)
        {
            NPC[] all = UnityEngine.Object.FindObjectsOfType<NPC>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return all[i];
            }
            return null;
        }
    }
}
