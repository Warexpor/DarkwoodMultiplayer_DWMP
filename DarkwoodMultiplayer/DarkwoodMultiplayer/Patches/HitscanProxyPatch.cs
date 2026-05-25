using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Hitscan weapon proxy hits are now handled by HitscanImpactSyncPatch
    /// (Physics.Raycast Postfix) which avoids the double-raycast issue and
    /// also forwards non-proxy impact FX. This patch remains as a no-op guard
    /// so the original spawnBullet always runs for hitscan weapons.
    /// </summary>
    [HarmonyPatch(typeof(Player), "spawnBullet", typeof(float))]
    public static class HitscanProxyPatch
    {
        private static bool Prefix(Player __instance, float aim)
        {
            return true;
        }
    }
}
