using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;

namespace DarkwoodMultiplayer.Patches
{
    internal static class JournalSyncHelpers
    {
        internal static void SendJournalItem(JournalItemKind kind, string type)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (string.IsNullOrEmpty(type)) return;
            var msg = new JournalItemMessage { Kind = kind, Type = type };
            LanNetworkManager.Instance.Send(NetMessageType.JournalItem, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    [HarmonyPatch(typeof(JournalNoteReference), "pickup")]
    public static class JournalNotePickupPatch
    {
        private static void Postfix(JournalNoteReference __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (Singleton<JournalDatabase>.Instance == null) return;
            JournalNote.Note note = Singleton<JournalDatabase>.Instance.getNote(__instance.noteName);
            if (note == null || string.IsNullOrEmpty(note.type)) return;
            JournalSyncHelpers.SendJournalItem(JournalItemKind.Note, note.type);
        }
    }

    [HarmonyPatch(typeof(KeyReference), "pickup")]
    public static class JournalKeyPickupPatch
    {
        private static void Postfix(KeyReference __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            JournalSyncHelpers.SendJournalItem(JournalItemKind.Key, __instance.type);
        }
    }

    [HarmonyPatch(typeof(QuestItemReference), "pickup")]
    public static class JournalQuestItemPickupPatch
    {
        private static void Postfix(QuestItemReference __instance)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            JournalSyncHelpers.SendJournalItem(JournalItemKind.QuestItem, __instance.type);
        }
    }

    [HarmonyPatch(typeof(Journal), "addJournalEntry")]
    public static class JournalEntryPatch
    {
        private static void Postfix(string type, bool noPopup)
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            JournalSyncHelpers.SendJournalItem(JournalItemKind.JournalEntry, type);
        }
    }

    [HarmonyPatch(typeof(CraftingRecipes), "doCraft")]
    public static class WorkbenchUpgradePatch
    {
        private static void Postfix()
        {
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected) return;
            if (Singleton<Controller>.Instance == null) return;
            int level = Singleton<Controller>.Instance.workbenchLevel;
            var msg = new WorkbenchLevelMessage { Level = level };
            LanNetworkManager.Instance.Send(NetMessageType.WorkbenchLevel, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }
}
