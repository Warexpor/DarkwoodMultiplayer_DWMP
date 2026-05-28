using BepInEx.Configuration;
using UnityEngine;

namespace DarkwoodMultiplayer.Config
{
    public static class ModConfig
    {
        public static ConfigEntry<string> ConnectAddress { get; private set; }
        public static ConfigEntry<int> ConnectPort { get; private set; }

        public static void Bind(ConfigFile config)
        {
            ConnectAddress = config.Bind("Network", "ConnectAddress", "127.0.0.1", "Default IP address shown in the connect field.");
            ConnectPort = config.Bind("Network", "ConnectPort", PluginInfo.DefaultPort, "Default UDP port for LAN connections.");
        }
    }
}
