using DarkwoodMultiplayer.Networking;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    /// <summary>
    /// Applies received NPCDialogueSync messages to the local NPC,
    /// mirroring the host's dialog progression (alreadyShown, disabled, wantsToTalk).
    /// </summary>
    internal static class NPCDialogueSyncHandler
    {
        public static void HandleNPCDialogueSync(NPCDialogueSyncMessage msg)
        {
            if (string.IsNullOrEmpty(msg.NpcName))
                return;

            NPC npc = FindNpcByName(msg.NpcName);
            if (npc == null)
            {
                ModRuntime.Log?.LogWarning($"[NPCDialogueSync] NPC '{msg.NpcName}' not found");
                return;
            }

            // Sync wantsToTalk
            npc.wantsToTalk = msg.WantsToTalk;

            // Sync dialogue states (alreadyShown, disabled)
            var cd = npc.characterDialogue;
            if (cd == null)
            {
                ModRuntime.Log?.LogWarning($"[NPCDialogueSync] NPC '{msg.NpcName}' has no characterDialogue");
                return;
            }

            if (cd.dialogues == null || cd.dialogues.Count == 0)
                return;

            for (int i = 0; i < msg.DialogueCount; i++)
            {
                string name = msg.DialogueNames?[i];
                if (string.IsNullOrEmpty(name)) continue;

                // Find matching dialogue by fullName
                var dialogue = cd.dialogues.Find(d => d.fullName == name);
                if (dialogue == null) continue;

                if (i < msg.AlreadyShown.Length)
                    dialogue.alreadyShown = msg.AlreadyShown[i];
                if (i < msg.Disabled.Length)
                    dialogue.disabled = msg.Disabled[i];
            }

            ModRuntime.Log?.LogInfo($"[NPCDialogueSync] Applied state for '{msg.NpcName}': {msg.DialogueCount} dialogues, wantsToTalk={msg.WantsToTalk}");
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
