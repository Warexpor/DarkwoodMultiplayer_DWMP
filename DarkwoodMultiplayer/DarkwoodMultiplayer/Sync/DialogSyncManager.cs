using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    internal static class DialogSyncManager
    {
        public static bool IsMirroring { get; set; }
        public static bool IsLeading { get; set; }
        public static string SessionNpcName { get; set; } = "";
        public static bool HasActiveSession => !string.IsNullOrEmpty(SessionNpcName);

        internal static DialogStateUpdateMessage _pendingState;

        public static void RequestJoinDialog(string npcName)
        {
            if (string.IsNullOrEmpty(npcName)) return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) return;
            if (net.Role != NetworkRole.Client) return;

            SessionNpcName = npcName;

            net.Send(NetMessageType.DialogJoinRequest,
                w => new DialogJoinRequestMessage { NpcName = npcName }.Serialize(w),
                DeliveryMethod.ReliableOrdered);
        }

        public static void HandleJoinRequest(DialogJoinRequestMessage msg)
        {
            if (string.IsNullOrEmpty(msg.NpcName)) return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) return;
            if (net.Role != NetworkRole.Host) return;

            if (HasActiveSession && SessionNpcName != msg.NpcName)
                EndSession();

            SessionNpcName = msg.NpcName;

            NPC npc = FindNpcByName(msg.NpcName);
            if (npc == null)
            {
                ModRuntime.Log?.LogWarning($"[DialogSync] Join request: NPC '{msg.NpcName}' not found");
                EndSession();
                return;
            }

            var dw = Singleton<UI>.Instance?.dialogueWindow;
            if (dw == null) return;

            if (dw.npc == null || dw.npc != npc)
            {
                if (dw.npc != null && dw.gameObject.activeInHierarchy)
                    dw.close();

                IsLeading = true;
                npc.talkTo();
            }
            else
            {
                IsLeading = true;
            }

            SendStateUpdate();
        }

        public static void SendStateUpdate()
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) return;
            if (net.Role != NetworkRole.Host) return;
            if (!HasActiveSession) return;

            var dw = Singleton<UI>.Instance?.dialogueWindow;
            if (dw == null || dw.npc == null)
            {
                EndSession();
                return;
            }

            int boardIndex = Traverse.Create(dw).Field("currentBoard").GetValue<int>();

            var msg = new DialogStateUpdateMessage
            {
                NpcName = SessionNpcName,
                StateType = GetStateType(dw),
                DialogueName = dw.currentDialogue?.fullName ?? "",
                BoardIndex = boardIndex,
                DecisionCount = 0,
                DecisionIndexes = new int[0],
                DecisionTexts = new string[0],
                MessageText = "",
                IsTradeActive = dw.currentMenu == DialogueWindow.CurrentMenu.trade && dw.npc.trader
            };

            if (dw.currentDialogue != null && boardIndex >= 0 &&
                boardIndex < dw.currentDialogue.boards.Count)
            {
                var board = dw.currentDialogue.boards[boardIndex];
                if (board.decisions != null)
                {
                    int count = board.decisions.Count;
                    msg.DecisionCount = count;
                    msg.DecisionIndexes = new int[count];
                    msg.DecisionTexts = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        msg.DecisionIndexes[i] = i;
                        msg.DecisionTexts[i] = board.decisions[i].name ?? "";
                    }
                }
            }

            net.Send(NetMessageType.DialogStateUpdate,
                w => msg.Serialize(w),
                DeliveryMethod.ReliableOrdered);
        }

        public static void HandleChoice(DialogChoiceMessage msg)
        {
            if (string.IsNullOrEmpty(msg.NpcName)) return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected) return;
            if (net.Role != NetworkRole.Host) return;

            var dw = Singleton<UI>.Instance?.dialogueWindow;
            if (dw == null || dw.npc == null || dw.npc.name != msg.NpcName)
            {
                ModRuntime.Log?.LogWarning($"[DialogSync] Choice for '{msg.NpcName}' but no active dialog");
                return;
            }

            ModRuntime.Log?.LogInfo($"[DialogSync] Host processing client choice index={msg.DecisionIndex}");

            if (msg.DecisionIndex >= 0 && msg.DecisionIndex < dw.menuOptions.Count)
            {
                var btn = dw.menuOptions[msg.DecisionIndex];
                if (btn != null)
                {
                    btn.getClicked();
                }
            }
        }

        public static void EndSession()
        {
            if (HasActiveSession)
            {
                var net = ModRuntime.Network as LanNetworkManager;
                if (net != null && net.IsConnected)
                {
                    net.Send(NetMessageType.DialogEnded,
                        w => new DialogEndedMessage { NpcName = SessionNpcName }.Serialize(w),
                        DeliveryMethod.ReliableOrdered);
                }
            }

            IsLeading = false;
            IsMirroring = false;
            SessionNpcName = "";
        }

        public static void ApplyStateUpdate(DialogStateUpdateMessage msg)
        {
            if (string.IsNullOrEmpty(msg.NpcName)) return;

            var dw = Singleton<UI>.Instance?.dialogueWindow;
            if (dw == null) return;

            NPC npc = FindNpcByName(msg.NpcName);
            if (npc == null)
            {
                ModRuntime.Log?.LogWarning($"[DialogSync] State update for '{msg.NpcName}' but NPC not found");
                return;
            }

            if (dw.npc == null || dw.npc != npc)
            {
                IsMirroring = true;
                SessionNpcName = msg.NpcName;

                if (dw.npc != null && dw.gameObject.activeInHierarchy)
                    dw.close();

                dw.initiateDialogue(npc);
            }

            if (msg.StateType == 0)
            {
                if (dw.gameObject.activeInHierarchy)
                    dw.close();
                IsMirroring = false;
                SessionNpcName = "";
                return;
            }

            _pendingState = msg;
        }

        public static void HandleSessionEnded(DialogEndedMessage msg)
        {
            if (string.IsNullOrEmpty(msg.NpcName)) return;
            if (msg.NpcName != SessionNpcName) return;

            var dw = Singleton<UI>.Instance?.dialogueWindow;
            if (dw != null && dw.gameObject.activeInHierarchy)
                dw.close();

            IsMirroring = false;
            SessionNpcName = "";
        }

        private static byte GetStateType(DialogueWindow dw)
        {
            if (dw.displayingDialogue && dw.currentDialogue != null)
                return 1;
            if (dw.currentMenu == DialogueWindow.CurrentMenu.main)
                return 2;
            if (dw.currentMenu == DialogueWindow.CurrentMenu.trade)
                return 3;
            if (dw.currentMenu == DialogueWindow.CurrentMenu.showItems)
                return 4;
            return 2;
        }

        private static NPC FindNpcByName(string name)
        {
            NPC[] all = Object.FindObjectsOfType<NPC>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return all[i];
            }
            return null;
        }
    }
}
