using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Skips the morning trader reputation bonus if this player died at night.
    /// Each player tracks their own reputation independently; this patch ensures
    /// the "survived the night" system gates the bonus per-player.
    /// </summary>
    [HarmonyPatch(typeof(Controller), "startAfterNight")]
    public static class MorningRepPatch
    {
        private static bool Prefix(Controller __instance)
        {
            if (!DeathStateTracker.SkipMorningRepBonus)
                return true;

            ModRuntime.Log?.LogInfo("[MorningRep] Player died at night — skipping reputation bonus");
            DeathStateTracker.SkipMorningRepBonus = false;
            __instance.gaveAfterNightRewards = true;
            return true;
        }
    }
}
