using DarkwoodMultiplayer.Networking;
using UnityEngine;

namespace DarkwoodMultiplayer
{
    /// <summary>
    /// Tracks death state for night-death coordination between host and client.
    /// Handles: first-night-death → spectator mode, second-night-death → morning advance.
    /// </summary>
    public static class DeathStateTracker
    {
        /// <summary>True when the LOCAL player died during a hard night (first death at night).</summary>
        public static bool LocalNightDeath { get; private set; }

        /// <summary>True when the REMOTE player died during a hard night.</summary>
        public static bool RemoteNightDeath { get; private set; }

        /// <summary>Position where the local player died (for bag spawn sync).</summary>
        public static Vector3 LocalDeathPosition { get; private set; }

        /// <summary>Position where the remote player died (for bag spawn on local world).</summary>
        public static Vector3 RemoteDeathPosition { get; private set; }

        /// <summary>Whether the local player's death bag has been synced to the remote.</summary>
        public static bool LocalBagSynced { get; set; }

        /// <summary>
        /// True when both players are dead during a hard night,
        /// meaning the host should trigger skipDay + Save.
        /// </summary>
        public static bool BothDeadAtNight => LocalNightDeath && RemoteNightDeath;

        /// <summary>
        /// If true, the morning rep bonus will be skipped in
        /// Controller.startAfterNight. Set alongside LocalNightDeath
        /// and consumed by MorningRepPatch. Kept separate from
        /// LocalNightDeath because DeathStateTracker.Reset() may
        /// clear that before startAfterNight runs.
        /// </summary>
        public static bool SkipMorningRepBonus { get; set; }

        /// <summary>
        /// Set to true to prevent spectator entry (used after BothDeadTrigger
        /// arrives before the local death spectator code would run).
        /// </summary>
        public static bool PreventSpectator { get; set; }

        /// <summary>
        /// Resets all state (called when morning starts or game resets).
        /// </summary>
        public static void Reset()
        {
            LocalNightDeath = false;
            RemoteNightDeath = false;
            LocalDeathPosition = Vector3.zero;
            RemoteDeathPosition = Vector3.zero;
            LocalBagSynced = false;
            PreventSpectator = false;
            SkipMorningRepBonus = false;
        }

        /// <summary>
        /// Called when the local player dies at night.
        /// </summary>
        public static void OnLocalNightDeath(Vector3 pos)
        {
            LocalNightDeath = true;
            SkipMorningRepBonus = true;
            LocalDeathPosition = pos;
            LocalBagSynced = false;
            ModRuntime.Log?.LogInfo($"[DeathState] Local night death at {pos}");
        }

        /// <summary>
        /// Called when the remote player dies at night (host received death message).
        /// </summary>
        public static void OnRemoteNightDeath(Vector3 pos)
        {
            RemoteNightDeath = true;
            RemoteDeathPosition = pos;
            ModRuntime.Log?.LogInfo($"[DeathState] Remote night death at {pos}");
        }

        /// <summary>
        /// Called when a player dies during the day (respawn normal, no spectator).
        /// </summary>
        public static void OnLocalDayDeath()
        {
            LocalNightDeath = false;
            ModRuntime.Log?.LogInfo("[DeathState] Local day death (normal respawn)");
        }

        public static void OnRemoteDayDeath()
        {
            RemoteNightDeath = false;
            ModRuntime.Log?.LogInfo("[DeathState] Remote day death (normal respawn)");
        }
    }
}
