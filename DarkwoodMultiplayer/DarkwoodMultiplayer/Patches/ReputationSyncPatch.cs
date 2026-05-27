using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Synchronizes NPC reputation from host to client for non-morning-trader
    /// NPCs. Morning trader reputation (isNightTrader==true) stays per-player.
    /// </summary>
    [HarmonyPatch(typeof(NPC), "set_reputation")]
    public static class ReputationSyncPatch
    {
        private static void Prefix(NPC __instance, int value)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Host)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            if (__instance == null) return;

            // Skip morning traders — each player keeps their own rep
            Character charComp = __instance.GetComponent<Character>();
            if (charComp != null && charComp.isNightTrader)
                return;

            string npcName = __instance.name;
            if (string.IsNullOrEmpty(npcName))
                return;

            ModRuntime.Log?.LogInfo($"[RepSync] host broadcasting rep for '{npcName}': {value}");

            var msg = new ReputationSyncMessage
            {
                NpcName = npcName,
                Reputation = value
            };

            net.Send(NetMessageType.ReputationSync, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }
}
