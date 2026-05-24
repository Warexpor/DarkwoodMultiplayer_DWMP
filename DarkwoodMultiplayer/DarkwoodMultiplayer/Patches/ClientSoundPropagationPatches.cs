using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Player), "fireWeapon")]
    public static class ClientFireWeaponSoundPatch
    {
        private static void Postfix(Player __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return;
            if (!ModRuntime.Network.IsConnected)
                return;
            if (__instance.invisible)
                return;
            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            InvItemClass item = __instance.currentItem;
            if (item == null || item.baseClass == null)
                return;

            var msg = new PlayerSoundMessage
            {
                Range = item.baseClass.attackSoundRange,
                DangerousSound = true,
                Volume = 1f,
                Gunshot = true
            };
            LanNetworkManager.Instance?.Send(NetMessageType.PlayerSound, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    [HarmonyPatch(typeof(Player), "aimScare")]
    public static class ClientAimScarePatch
    {
        private static void Postfix(Player __instance)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return;
            if (!ModRuntime.Network.IsConnected)
                return;
            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            var msg = new PlayerScareMessage { Range = 350f };
            LanNetworkManager.Instance?.Send(NetMessageType.PlayerScare, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }
}
