using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Redirects a portion of night-time enemy spawns to occur around the
    /// remote proxy instead of always around the host player. This ensures
    /// the client sees enemies near their character.
    ///
    /// Mechanism: a Prefix on getFreeSpotAround checks a per-frame flag
    /// that is active only during spawnNightChar. When set, ~50% of spawns
    /// use the proxy's position as the origin instead of the host player's.
    ///
    /// Because getFreeSpotAround runs before the indoor/outdoor ground
    /// verification, redirected spawns still respect map boundaries.
    /// </summary>
    [HarmonyPatch(typeof(CharacterSpawner), "getFreeSpotAround")]
    public static class NightSpawnGetFreeSpotPatch
    {
        // Set by spawNightChar Prefix, cleared by Postfix
        internal static bool InsideNightSpawn;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(CharacterSpawner __instance, ref GameObject destGO, float distance)
        {
            if (!InsideNightSpawn)
                return;
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;

            Transform proxyT = LanNetworkManager.Instance?.RemoteProxy?.transform;
            if (proxyT == null)
                return;

            // Only redirect if proxy is far enough from host to matter
            float distToHost = Vector3.Distance(proxyT.position, Player.Instance.transform.position);
            if (distToHost < 1000f)
                return;

            // ~50% chance: spawn around the proxy instead of the host
            if (Random.value < 0.5f)
            {
                destGO = proxyT.gameObject;
            }
        }
    }

    /// <summary>Sets the InsideNightSpawn flag around spawnNightChar.</summary>
    [HarmonyPatch(typeof(CharacterSpawner), "spawnNightChar")]
    public static class NightSpawnFlagPatch
    {
        private static void Prefix()
        {
            NightSpawnGetFreeSpotPatch.InsideNightSpawn = true;
        }

        private static void Postfix()
        {
            NightSpawnGetFreeSpotPatch.InsideNightSpawn = false;
        }
    }
}
