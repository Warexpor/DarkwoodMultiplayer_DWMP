using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    internal static class ExplosionSpawnFlagTracker
    {
        public static bool IsInsideSpawnObjects;
        /// <summary>True when spawnObjects() is running for a host-synced ThrownItem (duplicate of client throw).</summary>
        public static bool IsHostSynced;
        /// <summary>The Explodes instance whose onActivate() is currently executing. Set in Prefix, used by AddPrefab Postfix to filter out explosionPrefab.</summary>
        public static Explodes CurrentExplodes;
    }

    /// <summary>
    /// Prefix/Postfix on Explodes.onActivate() to set IsInsideSpawnObjects before
    /// spawnObjects() runs, so ExplosionObjectSpawnSyncPatch can intercept the
    /// Core.AddPrefab calls.
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "onActivate", new System.Type[0])]
    public static class ExplosionOnActivatePrefix
    {
        [HarmonyPrefix]
        private static void Prefix(Explodes __instance)
        {
            var net = ModRuntime.Network;
            ModRuntime.Log?.LogInfo("[onActivatePfx] entered role=" + (net?.Role.ToString() ?? "null") + " obj=" + __instance?.name + " hasThrown=" + (__instance.GetComponent<ThrownItem>() != null));

            ExplosionSpawnFlagTracker.CurrentExplodes = __instance;
            ExplosionSpawnFlagTracker.IsInsideSpawnObjects = false;
            ExplosionSpawnFlagTracker.IsHostSynced = false;

            if (net == null || net.Role == NetworkRole.Offline) return;

            if (net.Role == NetworkRole.Host)
            {
                ThrownItem ti = __instance.GetComponent<ThrownItem>();
                if (ti != null && ti.objectThatSpawnedMe != null)
                {
                    Transform proxyT = net.RemoteProxyTransform;
                    if (proxyT != null && ti.objectThatSpawnedMe == proxyT)
                    {
                        ExplosionSpawnFlagTracker.IsHostSynced = true;
                        ModRuntime.Log?.LogInfo("[onActivatePfx] IsHostSynced=true");
                    }
                }
            }

            ExplosionSpawnFlagTracker.IsInsideSpawnObjects = true;
            ModRuntime.Log?.LogInfo("[onActivatePfx] IsInsideSpawnObjects=true");
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            ModRuntime.Log?.LogInfo("[onActivatePfx] POSTFIX clearing flags");
            ExplosionSpawnFlagTracker.IsInsideSpawnObjects = false;
            ExplosionSpawnFlagTracker.IsHostSynced = false;
            ExplosionSpawnFlagTracker.CurrentExplodes = null;
        }
    }

    [HarmonyPatch(typeof(Explodes), "explode")]
    public static class ExplosionDamageSkipPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            var net = ModRuntime.Network;
            if (net == null || net.Role != NetworkRole.Client) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            if (Sync.WorldPhysicsSyncService._suppressBroadcast) return;
            TraverseHack.IsInsideLocalExplosion = true;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            TraverseHack.IsInsideLocalExplosion = false;
        }
    }

    [HarmonyPatch(typeof(Core), "AddPrefab", typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool))]
    public static class ExplosionObjectSpawnSyncPatch
    {
        private static void Postfix(Object prefab, Vector3 position, Quaternion quaternion, ref GameObject __result)
        {
            bool flag = ExplosionSpawnFlagTracker.IsInsideSpawnObjects;
            var log = ModRuntime.Log;
            if (flag)
                log?.LogInfo("[SpawnSyncPF] ENTERED flag=true prefab=" + (prefab?.name ?? "null") + " role=" + (ModRuntime.Network?.Role.ToString() ?? "null"));

            if (!flag) return;
            var net = ModRuntime.Network;
            if (net == null || net.Role != NetworkRole.Host) { log?.LogInfo("[SpawnSyncPF] not host"); return; }
            if (TraverseHack.ApplyingFromNetwork) { log?.LogInfo("[SpawnSyncPF] applyingFromNetwork"); return; }
            if (__result == null || prefab == null) { log?.LogInfo("[SpawnSyncPF] null result|prefab"); return; }

            if (ExplosionSpawnFlagTracker.IsHostSynced) { log?.LogInfo("[SpawnSyncPF] IsHostSynced"); return; }

            if (ExplosionSpawnFlagTracker.CurrentExplodes != null)
            {
                Object ep = ExplosionSpawnFlagTracker.CurrentExplodes.explosionPrefab;
                if (ep != null && prefab == ep) { log?.LogInfo("[SpawnSyncPF] explosionPrefab match, skip"); return; }
            }

            string prefabName = prefab.name;
            if (string.IsNullOrEmpty(prefabName)) { log?.LogInfo("[SpawnSyncPF] empty name"); return; }

            Vector3 euler = quaternion.eulerAngles;
            log?.LogInfo("[SpawnSyncPF] SENDING " + prefabName + " at " + position + " rot=" + euler);
            net.SendExplosionSpawnObject(prefabName, position, euler);
        }
    }
}
