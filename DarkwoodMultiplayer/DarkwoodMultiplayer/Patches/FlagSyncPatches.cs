using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Syncs world flag changes from host to client so NPC dialog progression
    /// and other flag-driven state (quests, reputation, world events) stays
    /// consistent across peers.
    /// Host is authoritative — only host→client sync. Client flag changes
    /// are never broadcast to avoid conflicts.
    /// </summary>
    [HarmonyPatch(typeof(Flags), "setFlag", typeof(string), typeof(bool))]
    public static class FlagSyncBoolPatch
    {
        private static void Postfix(string flagName, bool activeModifier)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            if (net.Role != NetworkRole.Host)
                return;

            var msg = new FlagSyncMessage
            {
                Name = flagName,
                IsInt = false,
                BoolValue = activeModifier,
                IntValue = 0
            };
            net.Send(NetMessageType.FlagSync, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>
    /// Syncs int flag changes from host to client.
    /// </summary>
    [HarmonyPatch(typeof(Flags), "setFlag", typeof(string), typeof(int))]
    public static class FlagSyncIntPatch
    {
        private static void Postfix(string flagName, int amount)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            if (net.Role != NetworkRole.Host)
                return;

            var msg = new FlagSyncMessage
            {
                Name = flagName,
                IsInt = true,
                BoolValue = false,
                IntValue = amount
            };
            net.Send(NetMessageType.FlagSync, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }
}
