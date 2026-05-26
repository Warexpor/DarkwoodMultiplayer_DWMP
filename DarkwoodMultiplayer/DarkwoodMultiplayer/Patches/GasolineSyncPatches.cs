using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Intercepts GasolineTrail spawns via Core.AddPrefab(string) and relays the
    /// position to the remote peer so both sides have matching gasoline puddles.
    /// </summary>
    [HarmonyPatch(typeof(Core), "AddPrefab", typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool))]
    public static class GasolineTrailSpawnPatch
    {
        private static float _lastGasSendTime;

        private static void Postfix(string prefab, Vector3 position, ref GameObject __result)
        {
            if (ModRuntime.Network == null) return;
            if (ModRuntime.Network.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            if (__result == null) return;
            if (prefab != "Items/GasolineTrail") return;

            // Throttle: send at most every 0.3s to avoid flooding the network
            float now = Time.unscaledTime;
            if (now - _lastGasSendTime < 0.3f) return;
            _lastGasSendTime = now;

            ModRuntime.Network.SendGasTrailSpawn(new GasTrailSpawnMessage
            {
                PosX = position.x, PosY = position.y, PosZ = position.z
            });
            ModRuntime.Log?.LogInfo("[GasTrailSync] sent trail at " + position);
        }
    }

    /// <summary>
    /// When a Liquid (gasoline puddle) starts burning on either peer, relays the
    /// ignition position to the other side so both see the fire.
    /// Suppressed during ApplyingFromNetwork to avoid infinite relay loops.
    /// </summary>
    [HarmonyPatch(typeof(Liquid), "startBurning")]
    public static class GasIgnitePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Liquid __instance, out bool __state)
        {
            __state = __instance.burning;
        }

        private static void Postfix(Liquid __instance, bool __state)
        {
            if (__state) return;
            var net = ModRuntime.Network;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            Vector3 pos = __instance.transform.position;
            net.SendGasIgnite(new GasIgniteMessage
            {
                PosX = pos.x, PosY = pos.y, PosZ = pos.z
            });
            ModRuntime.Log?.LogInfo("[GasIgniteSync] sent ignite at " + pos);
        }
    }
}
