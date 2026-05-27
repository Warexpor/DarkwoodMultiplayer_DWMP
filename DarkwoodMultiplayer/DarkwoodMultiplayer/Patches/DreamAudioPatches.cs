using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(AudioController))]
    [HarmonyPatch("_PlayAsSound")]
    public static class DreamAudioPlayPrefix
    {
        private static void Prefix(string audioID, float volume, Vector3 worldPosition)
        {
            if (!ShouldForward()) return;
            if (string.IsNullOrEmpty(audioID)) return;

            var net = ModRuntime.Network as LanNetworkManager;
            net?.Send(NetMessageType.DreamAudio,
                w => new DreamAudioMessage
                {
                    AudioID = audioID,
                    PosX = worldPosition.x,
                    PosY = worldPosition.y,
                    PosZ = worldPosition.z,
                    Volume = volume,
                    Pitch = 1f
                }.Serialize(w),
                LiteNetLib.DeliveryMethod.ReliableOrdered);
        }

        private static bool ShouldForward()
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return false;
            if (LanNetworkManager.IsApplyingRemoteState)
                return false;
            if (Dreams.Instance == null || !Dreams.Instance.dreaming)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(AudioController))]
    [HarmonyPatch("_PlayAsMusicOrAmbienceSound")]
    public static class DreamAudioMusicPrefix
    {
        private static void Prefix(string audioID, float volume, Vector3 worldPosition)
        {
            if (!ShouldForward()) return;
            if (string.IsNullOrEmpty(audioID)) return;

            var net = ModRuntime.Network as LanNetworkManager;
            net?.Send(NetMessageType.DreamAudio,
                w => new DreamAudioMessage
                {
                    AudioID = audioID,
                    PosX = worldPosition.x,
                    PosY = worldPosition.y,
                    PosZ = worldPosition.z,
                    Volume = volume,
                    Pitch = 1f
                }.Serialize(w),
                LiteNetLib.DeliveryMethod.ReliableOrdered);
        }

        private static bool ShouldForward()
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return false;
            if (LanNetworkManager.IsApplyingRemoteState)
                return false;
            if (Dreams.Instance == null || !Dreams.Instance.dreaming)
                return false;
            return true;
        }
    }
}
