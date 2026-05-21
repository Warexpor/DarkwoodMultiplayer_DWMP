using DarkwoodMultiplayer.Config;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Routes Player.Instance and Update to the active co-op character in Local mode.
    /// </summary>
    public static class PlayerControlRouter
    {
        private static Player _main;
        private static Player _second;

        public static Player MainPlayer => _main;
        public static Player SecondPlayer => _second;
        public static bool HasSecond => _second != null;

        public static void EnsureMainRegistered()
        {
            if (_main != null)
                return;

            Player scenePlayer = ResolveSceneMainPlayer();
            if (scenePlayer != null)
                RegisterMain(scenePlayer);
        }

        private static Player ResolveSceneMainPlayer()
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                Player taggedPlayer = tagged.GetComponent<Player>();
                if (taggedPlayer != null && taggedPlayer.GetComponent<CoopPlayerMarker>() == null)
                    return taggedPlayer;
            }

            Player instance = Player.Instance;
            if (instance != null && instance.GetComponent<CoopPlayerMarker>() == null)
                return instance;

            return null;
        }

        public static bool TryGetActiveOverride(out Player player)
        {
            if (ModConfig.IsLocalMode && LocalSecondPlayerManager.IsControllingSecond && _second != null)
            {
                player = _second;
                return true;
            }

            player = null;
            return false;
        }

        public static void RegisterMain(Player player)
        {
            if (player == null || player.GetComponent<CoopPlayerMarker>() != null)
                return;

            _main = player;
        }

        public static void RegisterSecond(Player player)
        {
            if (player == null)
                return;

            _second = player;
        }

        public static void ClearSecond()
        {
            _second = null;
        }

        public static bool ShouldRunPlayerUpdate(Player player)
        {
            return true;
        }

        public static Player GetMainForVision()
        {
            EnsureMainRegistered();
            return _main != null ? _main : Player.Instance;
        }

        public static bool IsActive(Player player)
        {
            if (player == null)
                return false;

            if (!ModConfig.IsLocalMode)
                return true;

            if (LocalSecondPlayerManager.IsControllingSecond)
                return player == _second;

            return player == _main;
        }

        public static void ClearPlayerInputState(Player player)
        {
            if (player == null)
                return;

            var t = HarmonyLib.Traverse.Create(player);
            t.Field("inputMovement").SetValue(UnityEngine.Vector3.zero);
            t.Field("inputRotation").SetValue(UnityEngine.Vector3.zero);
            player.running = false;
        }
    }
}
