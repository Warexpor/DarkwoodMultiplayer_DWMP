using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Tracks the index of a dialogue decision button for choice forwarding.
    /// Attached in addDecision Postfix.
    /// </summary>
    internal class DialogChoiceIndex : MonoBehaviour
    {
        public int Index;
    }

    /// <summary>
    /// Patches for real-time co-op NPC dialog.
    /// Host is authoritative for dialog state; the client mirrors it.
    /// Both players see the same dialog and both choices are processed.
    /// </summary>

    // ────────────────────────────────────────────
    // CLIENT SIDE: Initiate dialogue → join request
    // ────────────────────────────────────────────
    [HarmonyPatch(typeof(DialogueWindow), "initiateDialogue")]
    public static class DialogSyncInitiatePatch
    {
        private static bool Prefix(DialogueWindow __instance, NPC _npc)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return true;

            // Host processes dialog locally; just mark leading flag
            if (net.Role == NetworkRole.Host)
            {
                if (DialogSyncManager.HasActiveSession && DialogSyncManager.SessionNpcName == _npc.name)
                {
                    DialogSyncManager.IsLeading = true;
                }
                return true;
            }

            // Client: if not mirroring, send join request and skip local dialog
            if (DialogSyncManager.IsMirroring)
            {
                // Allow through during mirroring — dialog is already open
                return true;
            }

            // Send join request to host
            DialogSyncManager.RequestJoinDialog(_npc.name);

            // Don't open dialog locally — host will send state
            return false;
        }

        private static void Postfix(DialogueWindow __instance, NPC _npc)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            // Host just started a dialog — send initial state to client
            if (net.Role == NetworkRole.Host && __instance.npc != null)
            {
                DialogSyncManager.SessionNpcName = __instance.npc.name;
                DialogSyncManager.IsLeading = true;
                DialogSyncManager.SendStateUpdate();

                DialogFreezeManager.OnHostDialogOpened();
            }
        }
    }

    // ────────────────────────────────────────────
    // CLIENT SIDE: Decision button press → forward choice
    // ────────────────────────────────────────────
    [HarmonyPatch(typeof(DialogueButton), "onPress")]
    public static class DialogSyncDecisionPatch
    {
        private static bool Prefix(DialogueButton __instance)
        {
            if (!DialogSyncManager.IsMirroring)
                return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected)
                return false;

            var dci = __instance.GetComponent<DialogChoiceIndex>();
            int index = dci != null ? dci.Index : -1;

            ModRuntime.Log?.LogInfo($"[DialogSync] Client forwarding decision index={index}");

            net.Send(NetMessageType.DialogChoice,
                w => new DialogChoiceMessage
                {
                    NpcName = DialogSyncManager.SessionNpcName,
                    DecisionIndex = index
                }.Serialize(w),
                DeliveryMethod.ReliableOrdered);

            // Skip local outcome processing; host will send updated state
            return false;
        }
    }

    // ────────────────────────────────────────────
    // CLIENT SIDE: Track decision index on each button
    // ────────────────────────────────────────────
    [HarmonyPatch(typeof(DialogueWindow), "addDecision")]
    public static class DialogSyncAddDecisionPatch
    {
        private static int _nextIndex;

        private static void Prefix()
        {
            // The decisions in displayNextBoard are iterated in order with requirementsMet check.
            // We need to know which index the NEXT created button corresponds to.
            // reset at the start and count in Postfix
        }

        private static void Postfix(DialogueWindow __instance)
        {
            if (!DialogSyncManager.IsMirroring) return;

            // The last button added is at the end of menuOptions
            if (__instance.menuOptions.Count > 0)
            {
                var btn = __instance.menuOptions[__instance.menuOptions.Count - 1];
                if (btn != null && btn.GetComponent<DialogChoiceIndex>() == null)
                {
                    var dci = btn.gameObject.AddComponent<DialogChoiceIndex>();
                    dci.Index = _nextIndex++;
                }
            }
        }

        /// <summary>
        /// Called at the start of decision display to reset the counter.
        /// </summary>
        public static void ResetCounter()
        {
            _nextIndex = 0;
        }
    }

    // ────────────────────────────────────────────
    // CLIENT SIDE: Main menu option clicks → forward to host
    // ────────────────────────────────────────────
    [HarmonyPatch(typeof(DialogueWindow), "openTrade")]
    public static class DialogSyncTradePatch
    {
        private static bool Prefix()
        {
            if (!DialogSyncManager.IsMirroring) return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DialogChoice,
                    w => new DialogChoiceMessage { NpcName = DialogSyncManager.SessionNpcName, DecisionIndex = -1 }.Serialize(w),
                    DeliveryMethod.ReliableOrdered);
                ModRuntime.Log?.LogInfo("[DialogSync] Client forwarded trade option");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), "gossip")]
    public static class DialogSyncGossipPatch
    {
        private static bool Prefix()
        {
            if (!DialogSyncManager.IsMirroring) return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DialogChoice,
                    w => new DialogChoiceMessage { NpcName = DialogSyncManager.SessionNpcName, DecisionIndex = -2 }.Serialize(w),
                    DeliveryMethod.ReliableOrdered);
                ModRuntime.Log?.LogInfo("[DialogSync] Client forwarded gossip option");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), "enterItemsDialogue")]
    public static class DialogSyncShowItemsPatch
    {
        private static bool Prefix()
        {
            if (!DialogSyncManager.IsMirroring) return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DialogChoice,
                    w => new DialogChoiceMessage { NpcName = DialogSyncManager.SessionNpcName, DecisionIndex = -3 }.Serialize(w),
                    DeliveryMethod.ReliableOrdered);
                ModRuntime.Log?.LogInfo("[DialogSync] Client forwarded showItems option");
            }
            return false;
        }
    }

    // ────────────────────────────────────────────
    // BOTH SIDES: Dialog close
    // ────────────────────────────────────────────
    [HarmonyPatch(typeof(DialogueWindow), "close")]
    public static class DialogSyncClosePatch
    {
        private static bool Prefix(DialogueWindow __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return true;
            if (LanNetworkManager.IsApplyingRemoteState)
                return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return true;

            // Host: end the shared session
            if (net.Role == NetworkRole.Host && DialogSyncManager.IsLeading)
            {
                ModRuntime.Log?.LogInfo("[DialogSync] Host ending dialog session");
                DialogFreezeManager.OnHostDialogClosed();
                DialogSyncManager.EndSession();
                return true;
            }

            // Client mirroring close: notify host
            if (net.Role == NetworkRole.Client && DialogSyncManager.IsMirroring)
            {
                ModRuntime.Log?.LogInfo("[DialogSync] Client ending dialog session");
                net.Send(NetMessageType.DialogEnded,
                    w => new DialogEndedMessage { NpcName = DialogSyncManager.SessionNpcName }.Serialize(w),
                    DeliveryMethod.ReliableOrdered);
                DialogSyncManager.IsMirroring = false;
                DialogSyncManager.SessionNpcName = "";
                return true;
            }

            return true;
        }
    }

    // ────────────────────────────────────────────
    // HOST SIDE: Dialog state transitions → send updates
    // ────────────────────────────────────────────
    [HarmonyPatch(typeof(DialogueWindow), "displayDialogue")]
    public static class DialogSyncDisplayPatch
    {
        private static void Postfix(DialogueWindow __instance)
        {
            if (DialogSyncManager.IsLeading)
            {
                DialogSyncAddDecisionPatch.ResetCounter();
                DialogSyncManager.SendStateUpdate();
            }
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), "showMainOptions")]
    public static class DialogSyncMainOptionsPatch
    {
        private static void Postfix()
        {
            if (DialogSyncManager.IsLeading)
            {
                DialogSyncManager.SendStateUpdate();
            }
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), "closeTrade")]
    public static class DialogSyncCloseTradePatch
    {
        private static void Postfix()
        {
            if (DialogSyncManager.IsLeading)
            {
                DialogSyncManager.SendStateUpdate();
            }
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), "acceptTrade")]
    public static class DialogSyncAcceptTradePatch
    {
        private static void Postfix()
        {
            if (DialogSyncManager.IsLeading)
            {
                DialogSyncManager.SendStateUpdate();
            }
        }
    }

    [HarmonyPatch(typeof(DialogueWindow), "displayTraderMessage")]
    public static class DialogSyncTraderMessagePatch
    {
        private static void Postfix(DialogueWindow __instance, string _msg)
        {
            if (DialogSyncManager.IsLeading)
            {
                DialogSyncManager.SendStateUpdate();
            }
        }
    }

    // ────────────────────────────────────────────
    // BOTH SIDES: State update application patches
    // ────────────────────────────────────────────
    // These are called by DialogSyncManager.ApplyStateUpdate
    // They manipulate the DialogWindow to display host state

    /// <summary>
    /// Patches the DisplayNextBoard postfix to reset the decision counter
    /// at the start of decision rendering.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), "displayNextBoard")]
    public static class DialogSyncNextBoardPatch
    {
        private static void Prefix(DialogueWindow __instance)
        {
            if (DialogSyncManager.IsLeading)
            {
                DialogSyncAddDecisionPatch.ResetCounter();
            }
        }
    }
}
