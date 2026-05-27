using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(ShadowCreature), "spawnMeleeSensor")]
    public static class ShadowAttackBidirectionalPatch
    {
        private const float LightProtectionSyncRange = 150f;

        private static bool Prefix(ShadowCreature __instance)
        {
            if (ModRuntime.Network == null)
                return true;

            Player localPlayer = Player.Instance;
            if (localPlayer == null)
                return true;

            // Local player's own protection always applies
            if (localPlayer.isInLight)
                return false;

            // Remote player's protection only applies within range
            var net = (LanNetworkManager)ModRuntime.Network;
            if (net.RemotePlayerHasLightProtection)
            {
                Transform remoteT = net.RemoteProxyTransform;
                if (remoteT != null)
                {
                    float dist = Vector3.Distance(localPlayer.transform.position, remoteT.position);
                    if (dist <= LightProtectionSyncRange)
                        return false;
                }
            }

            return true;
        }
    }
}
