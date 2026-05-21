using BepInEx.Configuration;
using UnityEngine;

namespace DarkwoodMultiplayer.Config
{
    public static class ModConfig
    {
        public static ConfigEntry<PlayMode> Mode { get; private set; }
        public static ConfigEntry<KeyCode> SpawnLocalPlayerKey { get; private set; }
        public static ConfigEntry<KeyCode> SwitchControlKey { get; private set; }
        public static ConfigEntry<bool> AutoSpawnLocalSecondPlayer { get; private set; }
        public static ConfigEntry<bool> UseArrowKeysForSecondPlayer { get; private set; }
        public static ConfigEntry<float> LocalMaxSpeed { get; private set; }
        public static ConfigEntry<float> LocalAcceleration { get; private set; }
        public static ConfigEntry<float> LocalFriction { get; private set; }

        public static bool IsLanMode => Mode.Value == PlayMode.LAN;
        public static bool IsLocalMode => Mode.Value == PlayMode.Local;

        public static void Bind(ConfigFile config)
        {
            Mode = config.Bind(
                "General",
                "PlayMode",
                PlayMode.Local,
                "LAN = multiplayer over network. Local = spawn a second player on this PC for animation/movement testing.");

            SpawnLocalPlayerKey = config.Bind(
                "Local",
                "SpawnSecondPlayerKey",
                KeyCode.F8,
                "Spawn the local test second player (Local mode only). Set to None to use GUI menu only.");

            SwitchControlKey = config.Bind(
                "Local",
                "SwitchControlKey",
                KeyCode.F9,
                "Switch control between main player and local second player. Set to None to use GUI menu only.");

            AutoSpawnLocalSecondPlayer = config.Bind(
                "Local",
                "AutoSpawnSecondPlayer",
                false,
                "Automatically spawn the second player when entering the game in Local mode.");

            UseArrowKeysForSecondPlayer = config.Bind(
                "Local",
                "UseArrowKeysForSecond",
                false,
                "Deprecated — local second player always uses WASD + mouse look.");

            LocalMaxSpeed = config.Bind(
                "Local",
                "MaxSpeed",
                65f,
                "Top speed for local second player (game walk velocity is typically ~50-70).");

            LocalAcceleration = config.Bind("Local", "Acceleration", 220f, "Acceleration for local second player.");
            LocalFriction = config.Bind("Local", "Friction", 200f, "Friction for local second player.");
        }
    }
}
