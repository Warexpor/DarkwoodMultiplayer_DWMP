using DarkwoodMultiplayer;
using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using UnityEngine;

namespace DarkwoodMultiplayer.Spectator
{
    public sealed class SpectatorModeController : MonoBehaviour
    {
        private bool _isSpectating;
        private bool _wasNoClip;
        private Transform _followTarget;
        private PlayerVisionController _proxyVision;
        private Transform _audioListener;
        private Vector3 _savedAudioListenerPosition;
        private Vector3 _savedPlayerPosition;
        private bool _savedPlayerInvisible;
        private bool _savedPlayerIgnoreMe;

        public static SpectatorModeController Instance { get; private set; }
        public bool IsSpectating => _isSpectating;
        /// <summary>Position of the spectated target, used by Harmony culling patch.</summary>
        public Vector3? FollowTargetPosition => _followTarget != null ? (Vector3?)_followTarget.position : null;
        /// <summary>Original player position before spectating — used by network sync to avoid pushing the remote player.</summary>
        public Vector3? NetworkPositionOverride => _isSpectating ? (Vector3?)_savedPlayerPosition : null;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("SpectatorModeController");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SpectatorModeController>();
        }

        /// <summary>Programmatically enter spectator mode, following the given target transform.</summary>
        public void ForceEnter(Transform target)
        {
            if (_isSpectating)
            {
                ForceExit();
            }

            var player = Player.Instance;
            if (player == null) return;

            var cam = Singleton<CamMain>.Instance;
            if (cam == null) return;

            EnterSpectate(target, player, cam);
            _isSpectating = true;
        }

        /// <summary>Exit spectator mode and restore the local player to active state.</summary>
        public void ExitAndRespawn()
        {
            if (!_isSpectating) return;

            var player = Player.Instance;
            if (player != null)
            {
                RestorePlayerPosition(player);
                player.switchVisibilty(true);
                ShowLocalExtraVision(player);
                if (player.immobilised)
                    player.stopImmobilise();
                player.invulnerable = false;
                player.noClipMode = false;
            }

            RestoreAudioListener(player);

            var cam = Singleton<CamMain>.Instance;
            if (cam != null && player != null)
            {
                cam.followTarget = player.transform;
            }

            if (_proxyVision != null)
            {
                _proxyVision.SetAllVisionDisabled();
                _proxyVision = null;
            }

            if (Singleton<UI>.Instance != null)
                Singleton<UI>.Instance.showVisibleUI();

            _followTarget = null;
            _isSpectating = false;

            DeathStateTracker.PreventSpectator = true;
            DeathStateTracker.Reset();

            ModRuntime.Log?.LogInfo("[Spectate] ExitAndRespawn — player restored");
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (_isSpectating)
            {
                if (_followTarget == null || _followTarget.gameObject == null)
                {
                    ForceExit();
                    return;
                }
                SyncProxyVision();
                SyncAudioListener();
                SyncPlayerPosition();
            }

            if (!Input.GetKeyDown(KeyCode.F4))
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || !net.IsConnected)
                return;

            var proxy = Players.RemotePlayerProxy.Instance;
            if (proxy == null)
                return;

            ToggleSpectate(proxy.transform);
        }

        private void SyncProxyVision()
        {
            if (_proxyVision == null)
                return;

            var player = Player.Instance;
            if (player == null)
                return;

            bool flashlightOn = PlayerVisionController.IsFlashlightActiveOn(player);
            _proxyVision.SetFlashlightEnabled(flashlightOn);
        }

        private void SyncAudioListener()
        {
            if (_audioListener == null || _followTarget == null) return;
            _audioListener.position = _followTarget.position;
            _audioListener.rotation = Quaternion.identity;
        }

        private void SyncPlayerPosition()
        {
            var player = Player.Instance;
            if (player == null || _followTarget == null) return;

            Vector3 pos = player._transform.position;
            pos.x = _followTarget.position.x;
            pos.z = _followTarget.position.z;
            player._transform.position = pos;
            if (player.Rigidbody != null)
                player.Rigidbody.position = pos;
        }

        private static void HideLocalExtraVision(Player player)
        {
            Transform t = player.transform;
            SetActiveIfExists(t, "PlayerFOVLight", false);
            SetActiveIfExists(t, "PlayerFOVLightDot", false);
        }

        private static void ShowLocalExtraVision(Player player)
        {
            Transform t = player.transform;
            SetActiveIfExists(t, "PlayerFOVLight", true);
            SetActiveIfExists(t, "PlayerFOVLightDot", true);
        }

        private static void SetActiveIfExists(Transform root, string name, bool active)
        {
            Transform child = root.Find(name);
            if (child != null)
                child.gameObject.SetActive(active);
        }

        private void ToggleSpectate(Transform remoteTransform)
        {
            _isSpectating = !_isSpectating;

            var player = Player.Instance;
            if (player == null) return;

            var cam = Singleton<CamMain>.Instance;
            if (cam == null) return;

            if (_isSpectating)
                EnterSpectate(remoteTransform, player, cam);
            else
                ExitSpectate(player, cam);
        }

        private void EnterSpectate(Transform remoteTransform, Player player, CamMain cam)
        {
            _followTarget = remoteTransform;
            _wasNoClip = player.noClipMode;

            cam.followTarget = remoteTransform;

            player.switchVisibilty(false);
            HideLocalExtraVision(player);

            // Teleport player to target so game logic (audio triggers, AI proximity) uses correct position
            _savedPlayerPosition = player._transform.position;
            _savedPlayerInvisible = player.invisible;
            _savedPlayerIgnoreMe = player.ignoreMe;
            player.invisible = true;
            player.ignoreMe = true;
            Vector3 tpPos = remoteTransform.position;
            tpPos.y = player._transform.position.y;
            player._transform.position = tpPos;
            if (player.Rigidbody != null)
                player.Rigidbody.position = tpPos;

            // Move AudioListener to follow target so host hears world audio from target's position
            _audioListener = player.transform.Find("AudioListener");
            if (_audioListener != null)
            {
                _savedAudioListenerPosition = _audioListener.position;
                _audioListener.position = remoteTransform.position;
                _audioListener.rotation = Quaternion.identity;
            }

            var proxyGo = remoteTransform.gameObject;
            _proxyVision = PlayerVisionController.From(proxyGo);
            if (_proxyVision != null)
            {
                _proxyVision.SetVisionConeEnabled(true);
                _proxyVision.SyncFovConeFrom(player);
                bool flashlightOn = PlayerVisionController.IsFlashlightActiveOn(player);
                _proxyVision.SetFlashlightEnabled(flashlightOn);
            }

            player.immobilise();
            player.invulnerable = true;
            player.noClipMode = true;

            if (Singleton<UI>.Instance != null)
                Singleton<UI>.Instance.hideVisibleUI();

            ModRuntime.Log?.LogInfo("[Spectate] Entered spectator mode");
        }

        private void ExitSpectate(Player player, CamMain cam)
        {
            _followTarget = null;

            cam.followTarget = null;

            if (_proxyVision != null)
            {
                _proxyVision.SetAllVisionDisabled();
                _proxyVision = null;
            }

            RestorePlayerPosition(player);
            RestoreAudioListener(player);
            player.stopImmobilise();
            player.switchVisibilty(true);
            ShowLocalExtraVision(player);
            player.invulnerable = false;
            player.noClipMode = _wasNoClip;

            if (Singleton<UI>.Instance != null)
                Singleton<UI>.Instance.showVisibleUI();

            ModRuntime.Log?.LogInfo("[Spectate] Exited spectator mode");
        }

        public void ForceExit()
        {
            _isSpectating = false;
            _followTarget = null;

            var player = Player.Instance;
            if (player != null)
            {
                var cam = Singleton<CamMain>.Instance;
                if (cam != null)
                    cam.followTarget = null;

                if (player.immobilised)
                    player.stopImmobilise();

                player.invulnerable = false;
                player.noClipMode = false;

                if (player.alive)
                {
                    player.switchVisibilty(true);
                    ShowLocalExtraVision(player);
                }

                RestorePlayerPosition(player);
                RestoreAudioListener(player);
            }

            if (_proxyVision != null)
            {
                _proxyVision.SetAllVisionDisabled();
                _proxyVision = null;
            }

            if (Singleton<UI>.Instance != null)
                Singleton<UI>.Instance.showVisibleUI();

            ModRuntime.Log?.LogInfo("[Spectate] Force exited (follow target lost)");
        }

        private void RestorePlayerPosition(Player player)
        {
            player.invisible = _savedPlayerInvisible;
            player.ignoreMe = _savedPlayerIgnoreMe;
            player._transform.position = _savedPlayerPosition;
            if (player.Rigidbody != null)
                player.Rigidbody.position = _savedPlayerPosition;
        }

        private void RestoreAudioListener(Player player)
        {
            if (_audioListener != null)
            {
                _audioListener.position = _savedAudioListenerPosition;
                _audioListener.rotation = Quaternion.identity;
            }
            _audioListener = null;
        }
    }
}
