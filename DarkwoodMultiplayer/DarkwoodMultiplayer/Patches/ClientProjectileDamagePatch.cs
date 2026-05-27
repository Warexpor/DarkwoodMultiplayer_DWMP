using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Bullet), "onCollide", typeof(Collider), typeof(Vector3))]
    public static class ClientProjectileDamagePatch
    {
        private static void Prefix(Bullet __instance)
        {
            if (__instance.objectThatSpawnedMe != null) return;
            if (ModRuntime.Network is LanNetworkManager net && net.Role == NetworkRole.Client)
                TraverseHack.IsInsidePlayerBulletCollision = true;
        }

        private static void Postfix()
        {
            TraverseHack.IsInsidePlayerBulletCollision = false;
        }
    }
}
