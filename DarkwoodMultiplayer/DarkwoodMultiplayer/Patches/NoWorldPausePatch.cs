using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    internal static class PauseSuppression
    {
        internal static int SuppressPause;
        internal static int SuppressUnpause;
    }

    [HarmonyPatch(typeof(Core), "pause")]
    internal static class CorePauseMultiplayerPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network != null && PauseSuppression.SuppressPause > 0)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Core), "unpause")]
    internal static class CoreUnpauseMultiplayerPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network != null && PauseSuppression.SuppressUnpause > 0)
                return false;
            return true;
        }

        // Prevent vanilla Core.unpause (e.g. dialog close) from unfreezing
        // the world when a multiplayer freeze source (dialog freeze message,
        // remote dream) is still active.
        private static void Postfix()
        {
            if (ModRuntime.Network != null && FreezeTracker.IsFrozen)
            {
                if (!Core.Paused)
                    Core.pause(keepMusicAndEnviromental: true);
            }
        }
    }

    [HarmonyPatch(typeof(Map), "open")]
    internal static class MapOpenNoPausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause--;
        }
    }

    [HarmonyPatch(typeof(Map), "close")]
    internal static class MapCloseNoUnpausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause--;
        }
    }

    [HarmonyPatch(typeof(Journal), "open")]
    internal static class JournalOpenNoPausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause--;
        }
    }

    [HarmonyPatch(typeof(Journal), "close")]
    internal static class JournalCloseNoUnpausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause--;
        }
    }

    [HarmonyPatch(typeof(Journal), "showNote")]
    internal static class JournalShowNoteNoPausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause--;
        }
    }

    [HarmonyPatch(typeof(Journal), "hideNote")]
    internal static class JournalHideNoteNoUnpausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause--;
        }
    }

    [HarmonyPatch(typeof(Padlock), "activate")]
    internal static class PadlockActivateNoPausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressPause--;
        }
    }

    [HarmonyPatch(typeof(Padlock), "deactivate")]
    internal static class PadlockDeactivateNoUnpausePatch
    {
        private static void Prefix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause++;
        }
        private static void Postfix()
        {
            if (ModRuntime.Network != null)
                PauseSuppression.SuppressUnpause--;
        }
    }
}
