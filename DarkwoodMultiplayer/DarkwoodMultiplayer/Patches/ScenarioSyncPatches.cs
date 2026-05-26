using DarkwoodMultiplayer.Networking;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(NightScenarios), "setCurrentScenario")]
    public static class ClientScenarioBlockPatch
    {
        private static bool Prefix()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.Role == NetworkRole.Client)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(NightScenarios), "setCurrentScenario")]
    public static class HostScenarioSyncPatch
    {
        private static void Postfix(NightScenarios __instance)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Host)
                return;
            if (__instance.currentScenario == null)
                return;

            net.SendScenarioSync(new ScenarioSyncMessage
            {
                ScenarioName = __instance.currentScenario.name
            });
        }
    }
}
