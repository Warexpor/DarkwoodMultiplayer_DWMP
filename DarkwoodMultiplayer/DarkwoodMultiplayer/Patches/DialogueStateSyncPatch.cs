using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// When a dialogue closes on the host, sends the NPC's dialogue state
    /// (alreadyShown, disabled, wantsToTalk) to the client so dialog
    /// progression stays in sync beyond just flag changes.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), "close")]
    public static class DialogueStateSyncPatch
    {
        private static void Prefix(DialogueWindow __instance)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected)
                return;
            if (LanNetworkManager.IsApplyingRemoteState)
                return;
            if (net.Role != NetworkRole.Host)
                return;

            NPC npc = __instance.npc;
            if (npc == null) return;

            var cd = npc.characterDialogue;
            if (cd?.dialogues == null || cd.dialogues.Count == 0)
                return;

            // Collect dialogue states
            var names = new List<string>(cd.dialogues.Count);
            var alreadyShown = new List<bool>(cd.dialogues.Count);
            var disabled = new List<bool>(cd.dialogues.Count);

            for (int i = 0; i < cd.dialogues.Count; i++)
            {
                var d = cd.dialogues[i];
                if (d == null) continue;
                names.Add(d.fullName ?? "");
                alreadyShown.Add(d.alreadyShown);
                disabled.Add(d.disabled);
            }

            var msg = new NPCDialogueSyncMessage
            {
                NpcName = npc.name,
                WantsToTalk = npc.wantsToTalk,
                DialogueCount = names.Count,
                DialogueNames = names.ToArray(),
                AlreadyShown = alreadyShown.ToArray(),
                Disabled = disabled.ToArray()
            };

            net.Send(NetMessageType.NPCDialogueSync, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);

            ModRuntime.Log?.LogInfo($"[DialogueSync] Sent state for '{npc.name}': {names.Count} dialogues");
        }
    }
}
