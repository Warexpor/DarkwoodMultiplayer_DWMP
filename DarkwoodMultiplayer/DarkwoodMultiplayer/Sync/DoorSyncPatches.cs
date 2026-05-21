using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    [HarmonyPatch(typeof(Door), "open")]
    public static class DoorOpenPatch
    {
        private static void Postfix(Door __instance)
        {
            if (ModRuntime.Network == null)
                return;

            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendDoorState(new DoorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Opened = true
            });
            ModRuntime.Log?.LogInfo("[DoorSync] send open " + __instance.name + " at " + key);
        }
    }

    [HarmonyPatch(typeof(Door), "close")]
    public static class DoorClosePatch
    {
        private static void Postfix(Door __instance)
        {
            if (ModRuntime.Network == null)
                return;

            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendDoorState(new DoorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Opened = false
            });
            ModRuntime.Log?.LogInfo("[DoorSync] send close " + __instance.name + " at " + key);
        }
    }
}
