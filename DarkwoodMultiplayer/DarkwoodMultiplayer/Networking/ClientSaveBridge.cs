namespace DarkwoodMultiplayer.Networking
{
    /// <summary>
    /// Reads Darkwood save/world state from the active game and records what LAN clients should match.
    /// </summary>
    public static class ClientSaveBridge
    {
        private static WorldSessionMessage _pendingHostSession;
        private static string _lastClientSaveNote;

        public static string LastClientSaveNote => _lastClientSaveNote;

        public static string GetActiveSaveSlotName()
        {
            if (Core.currentProfile != null)
                return "profile_" + Core.currentProfile.id;

            return "unknown";
        }

        public static int GetWorldSeed()
        {
            if (Singleton<WorldGenerator>.Instance != null)
                return Singleton<WorldGenerator>.Instance.chapterID * 100000;

            return 0;
        }

        public static int GetChapterId()
        {
            if (Singleton<WorldGenerator>.Instance != null)
                return Singleton<WorldGenerator>.Instance.chapterID;

            if (Core.currentProfile != null)
                return Core.currentProfile.chapter;

            return 0;
        }

        public static int GetDayIndex()
        {
            if (Core.currentProfile != null)
                return Core.currentProfile.day;

            return 0;
        }

        public static string GetBigLocationName()
        {
            Player player = Player.Instance;
            if (player == null || player.whereAmI == null || player.whereAmI.bigLocation == null)
                return string.Empty;

            return player.whereAmI.bigLocation.name;
        }

        public static void NoteClientShouldMatchHost(WorldSessionMessage session)
        {
            _pendingHostSession = session;
            _lastClientSaveNote =
                "Client should load the same save profile as host ("
                + session.SaveSlotName
                + ", chapter "
                + session.ChapterId
                + ", day "
                + session.DayIndex
                + "). Use the same BepInEx profile/save before connecting. Full inventory/world replication is planned for v1.0.";
        }

        public static bool TryGetPendingHostSession(out WorldSessionMessage session)
        {
            session = _pendingHostSession;
            return session.SaveSlotName != null;
        }
    }
}
