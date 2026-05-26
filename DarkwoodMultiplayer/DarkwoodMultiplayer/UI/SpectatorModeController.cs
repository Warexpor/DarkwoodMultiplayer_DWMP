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

        public static SpectatorModeController Instance { get; private set; }
        public bool IsSpectating => _isSpectating;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("SpectatorModeController");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SpectatorModeController>();
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

            _proxyVision.CopyFovValuesFrom(player);

            bool flashlightOn = PlayerVisionController.IsFlashlightActiveOn(player);
            _proxyVision.SetFlashlightEnabled(flashlightOn);
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

            player.stopImmobilise();
            player.switchVisibilty(true);
            player.invulnerable = false;
            player.noClipMode = _wasNoClip;

            if (Singleton<UI>.Instance != null)
                Singleton<UI>.Instance.showVisibleUI();

            ModRuntime.Log?.LogInfo("[Spectate] Exited spectator mode");
        }

        private void ForceExit()
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
                    player.switchVisibilty(true);
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
    }
}
