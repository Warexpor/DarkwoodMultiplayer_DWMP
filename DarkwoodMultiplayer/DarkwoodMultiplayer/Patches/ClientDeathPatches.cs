using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// When the client player dies, sends a PlayerDiedMessage to the
    /// host so it can handle death cleanup and respawn logic.
    /// </summary>
    [HarmonyPatch(typeof(Player), "onDeath")]
    public static class ClientDeathPatch
    {
        private static bool Prefix()
        {
            if (LanNetworkManager.IsApplyingRemoteState) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return true;
            if (!ModRuntime.Network.IsConnected)
                return true;

            ModRuntime.Log?.LogInfo("[Death] Client died — sending PlayerDiedMessage to host");
            LanNetworkManager.Instance.Send(NetMessageType.PlayerDied, w => new PlayerDiedMessage().Serialize(w), DeliveryMethod.ReliableOrdered);
            return true;
        }
    }
}
