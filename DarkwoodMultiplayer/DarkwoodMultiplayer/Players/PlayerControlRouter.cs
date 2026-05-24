using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Tracks references to the main scene Player and optionally a remote/second Player.
    /// Used by other systems (vision, input) to locate the correct character instance.
    /// </summary>
    public static class PlayerControlRouter
    {
        private static Player _main;
        private static Player _second;

        /// <summary>The original main player from the scene (tagged "Player").</summary>
        public static Player MainPlayer => _main;

        /// <summary>A secondary player (remote proxy or local clone).</summary>
        public static Player SecondPlayer => _second;

        /// <summary>Whether a secondary player reference is registered.</summary>
        public static bool HasSecond => _second != null;

        /// <summary>
        /// Ensure the main player is registered.  Scans the scene for the tagged
        /// "Player" GameObject if <see cref="_main"/> is null.
        /// </summary>
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

        /// <summary>
        /// If a second player is active and being controlled, returns it via
        /// <paramref name="player"/> and returns true.  Otherwise returns false.
        /// </summary>
        public static bool TryGetActiveOverride(out Player player)
        {
            player = null;
            return false;
        }

        /// <summary>Register the main scene player.</summary>
        public static void RegisterMain(Player player)
        {
            if (player == null || player.GetComponent<CoopPlayerMarker>() != null)
                return;

            _main = player;
        }

        /// <summary>Register a secondary player.</summary>
        public static void RegisterSecond(Player player)
        {
            if (player == null)
                return;

            _second = player;
        }

        /// <summary>Clear the secondary player reference (e.g. on destroy).</summary>
        public static void ClearSecond()
        {
            _second = null;
        }

        /// <summary>
        /// Whether the given <paramref name="player"/> should run its Update loop.
        /// Always true in LAN mode (both host and client players tick).
        /// </summary>
        public static bool ShouldRunPlayerUpdate(Player player)
        {
            return true;
        }

        /// <summary>Get the main player for vision/cone purposes.</summary>
        public static Player GetMainForVision()
        {
            EnsureMainRegistered();
            return _main != null ? _main : Player.Instance;
        }

        /// <summary>Whether the given <paramref name="player"/> is the active one.</summary>
        public static bool IsActive(Player player)
        {
            if (player == null)
                return false;

            return true;
        }

        /// <summary>Zero out input state on a player (used when switching control).</summary>
        public static void ClearPlayerInputState(Player player)
        {
            if (player == null)
                return;

            var t = HarmonyLib.Traverse.Create(player);
            t.Field("inputMovement").SetValue(Vector3.zero);
            t.Field("inputRotation").SetValue(Vector3.zero);
            player.running = false;
        }
    }
}
