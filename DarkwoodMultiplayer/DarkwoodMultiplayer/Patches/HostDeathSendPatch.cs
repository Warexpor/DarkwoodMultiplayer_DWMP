using DarkwoodMultiplayer;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// When the HOST player dies, sends a PlayerDiedMessage to the client
    /// so it can handle bag spawn, proxy cleanup, and night-death tracking.
    /// </summary>
    [HarmonyPatch(typeof(Player), "onDeath")]
    public static class HostDeathSendPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return true;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;
            if (!ModRuntime.Network.IsConnected)
                return true;

            // Final Dreamscene: skip normal death, handle dream death instead
            if (FinalDreamsceneManager.IsActive)
            {
                ModRuntime.Log?.LogInfo("[Death] Host died during Final Dreamscene — handling dream death");
                FinalDreamsceneManager.OnLocalDeathInDream();
                return true;
            }

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return true;

            Vector3 pos = __instance._transform.position;
            Controller ctrl = Singleton<Controller>.Instance;
            bool isNight = ctrl != null && ctrl.isHardNight && !Core.isDay();

            ModRuntime.Log?.LogInfo($"[Death] Host died at {pos}, isNight={isNight}");

            net.Send(NetMessageType.PlayerDied,
                w => new PlayerDiedMessage
                {
                    PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                    IsNight = isNight,
                    HasDropBag = __instance.Inventory != null && __instance.Inventory.getAllItems().Count > 1
                }.Serialize(w),
                DeliveryMethod.ReliableOrdered);

            if (isNight)
            {
                DeathStateTracker.OnLocalNightDeath(pos);
            }
            else
            {
                DeathStateTracker.OnLocalDayDeath();
            }

            return true;
        }
    }
}
