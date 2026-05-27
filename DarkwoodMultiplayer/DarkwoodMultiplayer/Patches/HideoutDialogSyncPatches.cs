using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Detects when the local player opens an NPC dialog during the morning
    /// hideout (isAfterNight) and triggers a freeze on the remote player's world.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), "initiateDialogue")]
    public static class HideoutDialogOpenPatch
    {
        private static void Postfix(DialogueWindow __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            if (Singleton<Controller>.Instance == null || !Singleton<Controller>.Instance.isAfterNight)
                return;

            // If this is a shared dialog session (both players in same dialog),
            // skip the freeze — both players are already paused locally.
            if (Sync.DialogSyncManager.IsMirroring || Sync.DialogSyncManager.IsLeading)
                return;

            DialogFreezeManager.OnLocalDialogOpened();
        }
    }

    /// <summary>
    /// Detects when the local player closes an NPC dialog during the morning
    /// hideout and triggers an unfreeze on the remote player.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), "close")]
    public static class HideoutDialogClosePatch
    {
        private static void Prefix(DialogueWindow __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            if (LanNetworkManager.IsApplyingRemoteState)
                return;

            if (__instance.npc == null || __instance.tweening)
                return;

            if (Singleton<Controller>.Instance == null || !Singleton<Controller>.Instance.isAfterNight)
                return;

            // Skip freeze/unfreeze during shared dialog sessions
            if (Sync.DialogSyncManager.IsMirroring || Sync.DialogSyncManager.IsLeading)
                return;

            DialogFreezeManager.OnLocalDialogClosed();
        }
    }

    /// <summary>
    /// When the local player leaves the hideout (isAfterNight → false),
    /// reset the freeze state so the remote isn't stuck frozen.
    /// </summary>
    [HarmonyPatch(typeof(Controller), "endAfterNight")]
    public static class HideoutEndAfterNightPatch
    {
        private static void Prefix()
        {
            DialogFreezeManager.OnMorningEnded();
        }
    }

    /// <summary>
    /// When the host broadcasts a TimeSync that transitions to day-time
    /// (no longer in the after-night window), ensure any lingering freeze is cleared.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), "onTweenClose")]
    public static class HideoutDialogTweenClosePatch
    {
        private static void Prefix(DialogueWindow __instance)
        {
            if (!LanNetworkManager.IsApplyingRemoteState)
                return;

            // If remote player's dialog is closing and we were frozen,
            // the freeze will already have been removed by OnRemoteDialogState(false).
            // This is a safety net to catch any edge cases.
        }
    }
}
