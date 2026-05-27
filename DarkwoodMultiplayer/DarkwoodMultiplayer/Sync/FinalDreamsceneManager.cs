using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Spectator;
using LiteNetLib;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    internal static class FinalDreamsceneManager
    {
        private static bool _isActive;
        private static bool _localDeadInDream;
        private static bool _remoteDeadInDream;

        public static bool IsActive => _isActive;
        public static bool IsLocalDead => _localDeadInDream;
        public static bool IsRemoteDead => _remoteDeadInDream;
        public static bool AreBothDead => _isActive && _localDeadInDream && _remoteDeadInDream;

        private static readonly HashSet<string> DualPresenceDreams = new HashSet<string>
        {
            "dream_village_cellar",
            "dream_doctor_01",
            "dream_onechance_01_2"
        };

        private static readonly HashSet<string> DeathTrackedDreams = new HashSet<string>
        {
            "dream_village_cellar",
            "dream_doctor_01",
            "dream_onechance_01_2"
        };

        public static bool IsDualPresenceDream(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return false;
            if (DualPresenceDreams.Contains(presetName)) return true;
            return presetName.StartsWith("epilog_");
        }

        public static bool IsDeathTrackedDream(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return false;
            return DeathTrackedDreams.Contains(presetName);
        }

        public static void OnDreamStarted()
        {
            _isActive = true;
            _localDeadInDream = false;
            _remoteDeadInDream = false;
            ModRuntime.Log?.LogInfo("[FinalDreamscene] Dream started — death tracking active");
        }

        public static void OnDreamEnded()
        {
            if (!_isActive) return;
            _isActive = false;
            _localDeadInDream = false;
            _remoteDeadInDream = false;
            ModRuntime.Log?.LogInfo("[FinalDreamscene] Dream ended — state reset");
        }

        /// <summary>
        /// Called when the LOCAL player dies during the Final Dreamscene.
        /// Sends a death notification to the remote and enters spectator mode.
        /// </summary>
        public static void OnLocalDeathInDream()
        {
            if (!_isActive || _localDeadInDream) return;
            _localDeadInDream = true;

            ModRuntime.Log?.LogInfo("[FinalDreamscene] Local player died in dream");

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.FinalDreamsceneDeath,
                    w => new FinalDreamsceneDeathMessage { IsDead = true }.Serialize(w),
                    DeliveryMethod.ReliableOrdered);
            }

            EnterDreamSpectator();
        }

        /// <summary>
        /// Called when the REMOTE player died in the Final Dreamscene.
        /// If both are now dead, triggers the dream end.
        /// </summary>
        public static void OnRemoteDeathInDream()
        {
            if (!_isActive || _remoteDeadInDream) return;
            _remoteDeadInDream = true;

            ModRuntime.Log?.LogInfo("[FinalDreamscene] Remote player died in dream");

            if (AreBothDead)
            {
                ModRuntime.Log?.LogInfo("[FinalDreamscene] Both dead — ending dream");
                EndDreamForBoth();
            }
        }

        /// <summary>Called when disconnecting during the Final Dreamscene.</summary>
        public static void OnDisconnected()
        {
            _isActive = false;
            _localDeadInDream = false;
            _remoteDeadInDream = false;
        }

        private static void EnterDreamSpectator()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            Transform followTarget = null;
            if (net.Role == NetworkRole.Host)
                followTarget = net.RemoteProxyTransform;
            else
            {
                var proxy = Players.RemotePlayerProxy.Instance;
                if (proxy != null) followTarget = proxy.transform;
            }

            if (followTarget == null)
            {
                ModRuntime.Log?.LogWarning("[FinalDreamscene] No remote target for spectator");
                return;
            }

            SpectatorModeController.EnsureExists();
            SpectatorModeController.Instance.ForceEnter(followTarget);
        }

        private static void EndDreamForBoth()
        {
            var player = Player.Instance;
            if (player != null)
            {
                player.invulnerable = false;
                if (player.immobilised)
                    player.stopImmobilise();
                player.switchVisibilty(true);
            }

            if (Singleton<Dreams>.Instance != null && Singleton<Dreams>.Instance.dreaming)
            {
                ModRuntime.Log?.LogInfo("[FinalDreamscene] Calling Dreams.endDreaming()");
                Singleton<Dreams>.Instance.endDreaming();
            }
            else
            {
                ModRuntime.Log?.LogWarning("[FinalDreamscene] Dreams.Instance not dreaming — cleaning up directly");
                var cam = Singleton<CamMain>.Instance;
                if (cam != null) cam.followTarget = player != null ? player.transform : null;
            }

            var spec = SpectatorModeController.Instance;
            if (spec != null && spec.IsSpectating)
                spec.ExitAndRespawn();

            _isActive = false;
            _localDeadInDream = false;
            _remoteDeadInDream = false;
        }
    }
}
