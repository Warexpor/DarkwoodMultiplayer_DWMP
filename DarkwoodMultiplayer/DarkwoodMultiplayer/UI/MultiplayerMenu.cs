using DarkwoodMultiplayer.Config;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using UnityEngine;

namespace DarkwoodMultiplayer
{
    public sealed class MultiplayerMenu : MonoBehaviour
    {
        private static MultiplayerMenu _instance;

        private bool _visible;
        private string _connectAddress = "127.0.0.1";
        private string _portText = PluginInfo.DefaultPort.ToString();
        private Rect _windowRect;
        private Vector2 _scroll;
        private bool _windowRectInitialized;

        private LanNetworkManager Network => ModRuntime.Network;
        private LocalSecondPlayerManager Local => ModRuntime.LocalSecondPlayer;

        private static float UiScale => Mathf.Clamp(Screen.height / 900f, 1f, 2f);

        public static void ToggleVisible()
        {
            if (_instance != null)
                _instance._visible = !_instance._visible;
        }

        public static void EnsureExists()
        {
            if (_instance != null)
                return;

            GameObject go = new GameObject("DarkwoodMultiplayer_Menu");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MultiplayerMenu>();
            _instance.ResetWindowRect();
        }

        private void ResetWindowRect()
        {
            float scale = UiScale;
            float width = Mathf.Clamp(540f * scale, 440f, Screen.width * 0.65f);
            float height = Mathf.Clamp(520f * scale, 440f, Screen.height * 0.7f);
            _windowRect = new Rect(24f, 24f, width, height);
            _windowRectInitialized = true;
        }

        private void OnGUI()
        {
            if (!_windowRectInitialized)
                ResetWindowRect();

            if (!_visible)
                return;

            Matrix4x4 oldMatrix = GUI.matrix;
            float scale = UiScale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            Rect scaledRect = new Rect(
                _windowRect.x / scale,
                _windowRect.y / scale,
                _windowRect.width / scale,
                _windowRect.height / scale);

            scaledRect = GUI.Window(987654, scaledRect, DrawWindow, PluginInfo.Name + " v" + PluginInfo.Version);

            _windowRect = new Rect(
                scaledRect.x * scale,
                scaledRect.y * scale,
                scaledRect.width * scale,
                scaledRect.height * scale);

            GUI.matrix = oldMatrix;
        }

        private void DrawWindow(int id)
        {
            float innerWidth = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true)).width;
            if (innerWidth < 1f)
                innerWidth = _windowRect.width / UiScale - 28f;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            GUILayout.Label("Mode (config): " + ModConfig.Mode.Value, GUILayout.ExpandWidth(true));

            if (ModConfig.IsLocalMode)
                DrawLocalModeUi();
            else
                DrawLanModeUi(innerWidth);

            GUILayout.Space(12f);
            GUILayout.Label("Config file: BepInEx/config/" + PluginInfo.Guid + ".cfg", GUILayout.ExpandWidth(true));
            GUILayout.Label("Change PlayMode to LAN or Local, then restart the game.", GUILayout.ExpandWidth(true));
            GUILayout.Label("Menu toggle: F2, F3, Home, or Insert", GUILayout.ExpandWidth(true));

            GUILayout.EndScrollView();

            if (Event.current.type == EventType.Repaint)
            {
                float contentHeight = GUILayoutUtility.GetLastRect().yMax + 48f;
                float scaledHeight = contentHeight * UiScale;
                if (scaledHeight > _windowRect.height)
                    _windowRect.height = Mathf.Min(scaledHeight, Screen.height * 0.85f);
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawLanModeUi(float innerWidth)
        {
            GUILayout.Label("LAN co-op", GUILayout.ExpandWidth(true));
            GUILayout.Label("Status: " + (Network != null ? Network.StatusText : "No network"), GUILayout.ExpandWidth(true));

            if (!string.IsNullOrEmpty(ClientSaveBridge.LastClientSaveNote))
                GUILayout.Label(ClientSaveBridge.LastClientSaveNote, GUILayout.ExpandWidth(true));

            GUILayout.Space(8f);
            GUILayout.Label("Host IP:", GUILayout.ExpandWidth(true));
            _connectAddress = GUILayout.TextField(_connectAddress, GUILayout.ExpandWidth(true));

            GUILayout.Space(4f);
            GUILayout.Label("Port:", GUILayout.ExpandWidth(true));
            _portText = GUILayout.TextField(_portText, GUILayout.ExpandWidth(true));

            if (!int.TryParse(_portText, out int port))
                port = PluginInfo.DefaultPort;

            GUILayout.Space(10f);

            if (Network != null && Network.Role == NetworkRole.Offline)
            {
                if (GUILayout.Button("Host LAN game (port " + port + ")", GUILayout.Height(32f)))
                    Network.StartHost(port);

                if (GUILayout.Button("Connect to host", GUILayout.Height(32f)))
                    Network.ConnectToHost(_connectAddress.Trim(), port);
            }
            else if (Network != null)
            {
                if (GUILayout.Button("Disconnect", GUILayout.Height(32f)))
                    Network.StopNetwork();
            }
        }

        private void DrawLocalModeUi()
        {
            string control = Local != null ? Local.Active.ToString() : "Main";
            string second = Local != null && Local.HasSecondPlayer ? "spawned" : "not spawned";

            GUILayout.Label("Local co-op (split control)", GUILayout.ExpandWidth(true));
            GUILayout.Label("Controlling: " + control, GUILayout.ExpandWidth(true));
            GUILayout.Label("Second player: " + second, GUILayout.ExpandWidth(true));

            GUILayout.Space(8f);
            GUILayout.Label(ModConfig.SwitchControlKey.Value + " = switch Main / Second", GUILayout.ExpandWidth(true));
            GUILayout.Label(ModConfig.SpawnLocalPlayerKey.Value + " = spawn second player", GUILayout.ExpandWidth(true));

            GUILayout.Label("Second player: full game mechanics (inventory, interact, combat)", GUILayout.ExpandWidth(true));
            GUILayout.Label("FOV cone follows active player; flashlight if equipped", GUILayout.ExpandWidth(true));
            GUILayout.Label("While on Main: normal game controls", GUILayout.ExpandWidth(true));

            GUILayout.Space(10f);

            if (GUILayout.Button("Switch control (" + ModConfig.SwitchControlKey.Value + ")", GUILayout.Height(32f)))
                Local?.SwitchControl();

            if (GUILayout.Button("Spawn second player (" + ModConfig.SpawnLocalPlayerKey.Value + ")", GUILayout.Height(32f)))
                Local?.SpawnSecondPlayer();
        }
    }
}
