using DarkwoodMultiplayer;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Spectator;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Suppresses skipDay during night-time first-death (only one player dead).
    /// When both players are dead at night, allows skipDay (→ morning advance).
    /// On suppression, enters spectator mode for the dead player.
    /// </summary>
    [HarmonyPatch(typeof(Controller), "skipDay")]
    public static class NightDeathSkipDayPatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return true;

            // Only the host calls skipDay — client always suppresses
            if (ModRuntime.Network.Role != NetworkRole.Host)
            {
                if (DeathStateTracker.LocalNightDeath)
                {
                    ModRuntime.Log?.LogInfo("[Death] Client suppressing skipDay (only host advances time)");
                    EnterNightDeathSpectator();
                }
                return false;
            }

            if (!DeathStateTracker.LocalNightDeath)
                return true;

            if (DeathStateTracker.BothDeadAtNight)
            {
                ModRuntime.Log?.LogInfo("[Death] Both dead at night — host allowing skipDay");
                return true;
            }

            ModRuntime.Log?.LogInfo("[Death] First night death — host suppressing skipDay, entering spectator");
            EnterNightDeathSpectator();
            return false;
        }

        private static void EnterNightDeathSpectator()
        {
            if (DeathStateTracker.PreventSpectator)
            {
                ModRuntime.Log?.LogInfo("[Death] PreventSpectator flag set — skipping spectator entry");
                return;
            }
            if (DeathStateTracker.BothDeadAtNight)
            {
                ModRuntime.Log?.LogInfo("[Death] Both dead — skipping spectator (morning incoming)");
                return;
            }

            Player player = Player.Instance;
            if (player == null) return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            Transform followTarget = null;
            if (ModRuntime.Network.Role == NetworkRole.Host)
            {
                followTarget = net.RemoteProxyTransform;
            }
            else
            {
                var proxy = Players.RemotePlayerProxy.Instance;
                if (proxy != null) followTarget = proxy.transform;
            }

            if (followTarget == null)
            {
                ModRuntime.Log?.LogWarning("[Death] No remote target for spectator — forcing exit");
                DeathStateTracker.Reset();
                return;
            }

            SpectatorModeController.EnsureExists();
            var spec = SpectatorModeController.Instance;
            if (spec != null)
            {
                spec.ForceEnter(followTarget);
            }

            DeathStateTracker.LocalBagSynced = true;
        }
    }

    /// <summary>
    /// Suppresses SaveManager.Save() during night-time first-death,
    /// so the death state isn't persisted until both players die.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), "Save")]
    public static class NightDeathSavePatch
    {
        private static bool Prefix()
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return true;

            // Only the host saves
            if (ModRuntime.Network.Role != NetworkRole.Host)
            {
                if (DeathStateTracker.LocalNightDeath)
                {
                    ModRuntime.Log?.LogInfo("[Death] Client suppressing Save (only host persists)");
                    return false;
                }
                return true;
            }

            if (!DeathStateTracker.LocalNightDeath)
                return true;

            if (DeathStateTracker.BothDeadAtNight)
            {
                ModRuntime.Log?.LogInfo("[Death] Both dead — host allowing Save");
                return true;
            }

            ModRuntime.Log?.LogInfo("[Death] First night death — host suppressing Save");
            return false;
        }
    }

    /// <summary>
    /// Postfix on onDeath: if the local player died at night and both are now dead,
    /// notify the remote that morning should advance. If only the remote died,
    /// mark the state and trigger bag spawn.
    /// </summary>
    [HarmonyPatch(typeof(Player), "onDeath")]
    public static class NightDeathOnDeathPatch
    {
        private static void Postfix(Player __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            if (DeathStateTracker.BothDeadAtNight)
            {
                ModRuntime.Log?.LogInfo("[Death] Both dead at night — sending morning trigger to remote");
                net.Send(NetMessageType.NightDeathState,
                    w => new NightDeathStateMessage { IsDead = true, BothDeadTrigger = true }.Serialize(w),
                    DeliveryMethod.ReliableOrdered);
                DeathStateTracker.Reset();
            }
        }
    }
}
