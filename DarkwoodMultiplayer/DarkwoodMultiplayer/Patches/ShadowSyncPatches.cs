using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Player), "tryToSpawnShadow")]
    public static class HostShadowSyncPatch
    {
        private static void Postfix()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Host)
                return;

            net.SendShadowEvent(new ShadowEventMessage());
            ModRuntime.Log?.LogInfo("[ShadowSync] sent trigger to client");
        }
    }

    [HarmonyPatch(typeof(Core), "AddPrefab", new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    public static class ShadowCaptureOnSpawnPatch
    {
        private static void Postfix(GameObject __result, string prefab)
        {
            if (__result == null || prefab != "characters/fakechars/shadow")
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Host)
                return;
            if (!net.IsConnected)
                return;

            Vector3 pos = __result.transform.position;
            float rotY = __result.transform.rotation.eulerAngles.y;
            net.SendShadowSpawn(new ShadowSpawnMessage
            {
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                RotY = rotY
            });
        }
    }
}
