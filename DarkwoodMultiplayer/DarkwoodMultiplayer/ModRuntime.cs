using BepInEx.Configuration;
using BepInEx.Logging;
using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.DebugTools;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer
{
    /// <summary>
    /// Entry-point runtime for the Darkwood Multiplayer mod.
    /// Owns the Harmony patcher, the network manager, and the in-game UI.
    /// </summary>
    public static class ModRuntime
    {
        /// <summary>BepInEx logger, shared across all systems.</summary>
        public static ManualLogSource Log;

        /// <summary>The LAN network manager singleton (host or client).</summary>
        public static LanNetworkManager Network { get; private set; }

        private static bool _running;
        private static Harmony _harmony;

        /// <summary>
        /// Called by <see cref="DarkwoodMultiplayerEntry.Awake"/> once on plugin load.
        /// Binds config, applies all Harmony patches, and boots the runtime GameObject.
        /// </summary>
        public static void Start(ManualLogSource log, ConfigFile config)
        {
            Log = log;

            try
            {
                ModConfig.Bind(config);

                _harmony = new Harmony(PluginInfo.Guid);
                _harmony.PatchAll();

                Log.LogInfo("================================================");
                Log.LogInfo("  " + PluginInfo.Name + " v" + PluginInfo.DisplayVersion);
                Log.LogInfo("  Config: BepInEx/config/" + PluginInfo.Guid + ".cfg");
                Log.LogInfo("  F2=menu, F3=save, F4=spectator, F5=debug tools");
                Log.LogInfo("================================================");

                EnsureRunning();
            }
            catch (System.Exception ex)
            {
                Log.LogError("ModRuntime.Start failed: " + ex);
            }
        }

        /// <summary>
        /// Ensure the persistent runtime GameObject exists with all required
        /// components (network manager, menu).
        /// </summary>
        public static void EnsureRunning()
        {
            if (_running)
                return;

            _running = true;

            GameObject root = new GameObject("DarkwoodMultiplayer_Runtime");
            Object.DontDestroyOnLoad(root);

            Network = root.AddComponent<LanNetworkManager>();
            root.AddComponent<EntitySpawnerUI>();

            MultiplayerMenu.EnsureExists();
            Spectator.SpectatorModeController.EnsureExists();
            ManualSaveGUI.EnsureExists();
        }

        /// <summary>Stop the network and unpatch Harmony (called on mod unload).</summary>
        public static void Stop()
        {
            Network?.StopNetwork();
            _harmony?.UnpatchSelf();
            _running = false;
        }
    }
}
