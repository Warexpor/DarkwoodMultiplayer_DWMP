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

    [HarmonyPatch(typeof(NightScenarios), "setCurrentScenario")]
    public static class ClientDisableScenarioSelectPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }

    [HarmonyPatch(typeof(NightScenarios), "Start")]
    public static class ClientDisableScenarioStartPatch
    {
        private static bool Prefix()
        {
            return !ClientWorldHelper.IsClient;
        }
    }
}
