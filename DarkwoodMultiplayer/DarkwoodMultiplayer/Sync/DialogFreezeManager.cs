using DarkwoodMultiplayer.Networking;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    /// <summary>
    /// Tracks whether either player is in an NPC dialog during the morning
    /// hideout (isAfterNight). When either player is in dialog, the world
    /// is frozen for both players via Core.pause/Core.unpause.
    /// </summary>
    internal static class DialogFreezeManager
    {
        private static bool _localInMorningDialog;
        private static bool _remoteInMorningDialog;
        private static int _freezeDepth;

        public static bool LocalInMorningDialog => _localInMorningDialog;
        public static bool RemoteInMorningDialog => _remoteInMorningDialog;
        public static bool IsFrozen => _freezeDepth > 0;

        public static void OnLocalDialogOpened()
        {
            if (_localInMorningDialog) return;

            _localInMorningDialog = true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.HideoutDialogState,
                    w => new HideoutDialogStateMessage { InDialog = true }.Serialize(w),
                    LiteNetLib.DeliveryMethod.ReliableOrdered);
            }

            ApplyFreeze();
        }

        public static void OnLocalDialogClosed()
        {
            if (!_localInMorningDialog) return;

            _localInMorningDialog = false;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.HideoutDialogState,
                    w => new HideoutDialogStateMessage { InDialog = false }.Serialize(w),
                    LiteNetLib.DeliveryMethod.ReliableOrdered);
            }

            RemoveFreeze();
        }

        public static void OnRemoteDialogState(bool inDialog)
        {
            _remoteInMorningDialog = inDialog;

            if (inDialog)
                ApplyFreeze();
            else
                RemoveFreeze();
        }

        /// <summary>
        /// Called when the local player leaves the hideout (endAfterNight)
        /// or when the network disconnects. Notifies the remote peer if
        /// we were in dialog, then resets all freeze state.
        /// </summary>
        public static void OnMorningEnded()
        {
            // Notify remote if we were in dialog so they unfreeze
            if (_localInMorningDialog)
            {
                var net = ModRuntime.Network as LanNetworkManager;
                if (net != null && net.IsConnected)
                {
                    net.Send(NetMessageType.HideoutDialogState,
                        w => new HideoutDialogStateMessage { InDialog = false }.Serialize(w),
                        LiteNetLib.DeliveryMethod.ReliableOrdered);
                }
            }

            bool wasFrozen = IsFrozen;
            _localInMorningDialog = false;
            _remoteInMorningDialog = false;
            _freezeDepth = 0;
            if (wasFrozen)
            {
                Core.unpause(1f);
            }
        }

        private static void ApplyFreeze()
        {
            if (_localInMorningDialog || _remoteInMorningDialog)
            {
                _freezeDepth++;
                if (_freezeDepth == 1)
                {
                    Core.pause(keepMusicAndEnviromental: true);
                }
            }
        }

        private static void RemoveFreeze()
        {
            if (!_localInMorningDialog && !_remoteInMorningDialog && _freezeDepth > 0)
            {
                _freezeDepth = 0;
                Core.unpause(1f);
            }
        }
    }
}
