using DarkwoodMultiplayer.Networking;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    internal static class ClientWorldHelper
    {
        internal static bool IsClient => ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Client;
    }

    [HarmonyPatch(typeof(CharacterSpawner), "spawnNightChar")]
    public static class ClientDisableNightSpawnPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), "waitToSpawnShadow")]
    public static class ClientDisableShadowSpawnPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), "waitToSpawnWorm")]
    public static class ClientDisableWormSpawnPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), "despawnNocturnalCharacters")]
    public static class ClientDisableNocturnalDespawnPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), "spawnForestSpirit")]
    public static class ClientDisableForestSpiritPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }

    /// <summary>
    /// Blocks RandomEvent.fire() for the spawnRedneck type on the client.
    /// The host spawns the Redneck via spawnCharacterAround and syncs it
    /// via entity state snapshots. If the client also ran this, it would
    /// spawn a duplicate Redneck near the client player's position.
    /// </summary>
    [HarmonyPatch(typeof(RandomEvent), "fire", typeof(bool), typeof(bool))]
    public static class ClientBlockRedneckSpawnPatch
    {
        private static bool Prefix(RandomEvent __instance)
        {
            if (__instance.type != RandomEvent.Type.spawnRedneck) return true;
            if (!ClientWorldHelper.IsClient) return true;
            // Block this on client — host's copy will be synced via entity state
            return false;
        }
    }

}
