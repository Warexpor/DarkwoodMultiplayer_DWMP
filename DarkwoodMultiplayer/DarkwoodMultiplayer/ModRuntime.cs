using BepInEx.Configuration;
using BepInEx.Logging;
using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer
{
    public static class ModRuntime
    {
        public static ManualLogSource Log;
        public static LanNetworkManager Network { get; private set; }
        public static LocalSecondPlayerManager LocalSecondPlayer { get; private set; }

        private static bool _running;
        private static Harmony _harmony;

        public static void Start(ManualLogSource log, ConfigFile config)
        {
            Log = log;

            try
            {
                ModConfig.Bind(config);

                _harmony = new Harmony(PluginInfo.Guid);
                _harmony.PatchAll();

                Log.LogInfo("================================================");
                Log.LogInfo("  " + PluginInfo.Name + " v" + PluginInfo.Version);
                Log.LogInfo("  Config: BepInEx/config/" + PluginInfo.Guid + ".cfg");
                Log.LogInfo("  PlayMode = " + ModConfig.Mode.Value);
                if (ModConfig.IsLocalMode)
                {
                    if (ModConfig.SwitchControlKey.Value != KeyCode.None)
                        Log.LogInfo("  Local: " + ModConfig.SwitchControlKey.Value + " = switch player");
                    if (ModConfig.SpawnLocalPlayerKey.Value != KeyCode.None)
                        Log.LogInfo("  Local: " + ModConfig.SpawnLocalPlayerKey.Value + " = spawn second player");
                }
                else
                {
                    Log.LogInfo("  LAN: M / F2 / F3 / Home = menu");
                }
                Log.LogInfo("================================================");

                EnsureRunning();
            }
            catch (System.Exception ex)
            {
                Log.LogError("ModRuntime.Start failed: " + ex);
            }
        }

        public static void EnsureRunning()
        {
            if (_running)
                return;

            _running = true;

            GameObject root = new GameObject("DarkwoodMultiplayer_Runtime");
            Object.DontDestroyOnLoad(root);

            Network = root.AddComponent<LanNetworkManager>();
            LocalSecondPlayer = root.AddComponent<LocalSecondPlayerManager>();

            MultiplayerMenu.EnsureExists();
        }

        public static void Stop()
        {
            Network?.StopNetwork();
            _harmony?.UnpatchSelf();
            _running = false;
        }
    }
}
