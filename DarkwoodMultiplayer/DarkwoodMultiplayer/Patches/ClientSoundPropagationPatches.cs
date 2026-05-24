using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Broadcasts gunshot sound to other clients when the local player
    /// fires a weapon, using the weapon's configured sound range and
    /// marking it as a dangerous/gunshot sound for AI reaction.
    /// </summary>
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

    /// <summary>
    /// Broadcasts a scare (loud noise) to other clients when the local
    /// player performs an aim-scare, so AI on the host reacts as if
    /// the remote player made the noise.
    /// </summary>
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
