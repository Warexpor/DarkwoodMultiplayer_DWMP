using DarkwoodMultiplayer.Networking;

namespace DarkwoodMultiplayer.Sync
{
    internal static class DialogFreezeManager
    {
        /// <summary>
        /// Called by the host when an NPC dialog session starts (host-side).
        /// Sends a freeze message to the remote peer so both worlds freeze.
        /// </summary>
        public static void OnHostDialogOpened()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DialogFreeze,
                    w => new DialogFreezeMessage { Frozen = true }.Serialize(w),
                    LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// Called by the host when the NPC dialog session ends (host-side).
        /// Sends an unfreeze message to the remote peer.
        /// </summary>
        public static void OnHostDialogClosed()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DialogFreeze,
                    w => new DialogFreezeMessage { Frozen = false }.Serialize(w),
                    LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// Handles an incoming freeze/unfreeze message from the remote peer.
        /// Contributes to the unified FreezeTracker so all subsystems (dialog,
        /// dream, etc.) share a single freeze counter.
        /// </summary>
        public static void OnRemoteFreezeState(bool frozen)
        {
            if (frozen)
                FreezeTracker.AddFreeze();
            else
                FreezeTracker.RemoveFreeze();
        }

        /// <summary>
        /// Cleanup on disconnect.
        /// </summary>
        public static void OnDisconnected()
        {
            FreezeTracker.Reset();
        }
    }
}
