using BepInEx.Configuration;

namespace DarkwoodMultiplayer.Config
{
    /// <summary>
    /// Central configuration binding for the Darkwood Multiplayer mod.
    /// All settings are read from the BepInEx config file at startup.
    /// </summary>
    public static class ModConfig
    {
        /// <summary>IP address or hostname of the host to connect to.</summary>
        public static ConfigEntry<string> ConnectAddress { get; private set; }

        /// <summary>UDP port used for the LAN connection.</summary>
        public static ConfigEntry<int> ConnectPort { get; private set; }

        /// <summary>
        /// Register all config entries with the BepInEx configuration system.
        /// Called once during mod startup (<see cref="ModRuntime.Start"/>).
        /// </summary>
        public static void Bind(ConfigFile config)
        {
            ConnectAddress = config.Bind(
                "Network",
                "ConnectAddress",
                "127.0.0.1",
                "Default IP address shown in the connect field.");

            ConnectPort = config.Bind(
                "Network",
                "ConnectPort",
                PluginInfo.DefaultPort,
                "Default UDP port for LAN connections.");
        }
    }
}
