using DarkwoodMultiplayer.Config;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    public sealed class LocalSecondPlayerManager : MonoBehaviour
    {
        public enum ControlledPlayer
        {
            Main,
            Second
        }

        private GameObject _secondPlayer;
        private Player _mainPlayer;
        private Player _secondPlayerComponent;
        private PlayerVisionController _mainVision;
        private PlayerVisionController _secondVision;
        private Transform _savedCamFollowTarget;

        private ControlledPlayer _active = ControlledPlayer.Main;
        private bool _autoSpawnSucceeded;

        public ControlledPlayer Active => _active;
        public bool HasSecondPlayer => _secondPlayer != null;
        public static bool IsControllingSecond { get; private set; }
        public static LocalSecondPlayerManager Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            CleanupSecondPlayer();
            Instance = null;
            IsControllingSecond = false;
        }

        public void Tick()
        {
            if (!ModConfig.IsLocalMode)
            {
                IsControllingSecond = false;
                return;
            }

            if (!HasSecondPlayer && !IsControllingSecond)
                return;

            HandleSceneLifecycle();
            if (!IsInPlaySession())
                return;

            PlayerControlRouter.EnsureMainRegistered();
            TryAutoSpawn();

            if (ModConfig.SpawnLocalPlayerKey.Value != KeyCode.None && Input.GetKeyDown(ModConfig.SpawnLocalPlayerKey.Value))
                SpawnSecondPlayer();

            if (ModConfig.SwitchControlKey.Value != KeyCode.None && Input.GetKeyDown(ModConfig.SwitchControlKey.Value))
                SwitchControl();

            ValidateSecondPlayerReference();
            EnforceVisionAndCamera();
            ApplyInactivePlayerLock();
        }

        private void HandleSceneLifecycle()
        {
            if (IsInPlaySession())
                return;

            CleanupSecondPlayer();
        }

        private static bool IsInPlaySession()
        {
            if (Core.loadingGame)
                return false;

            Player main = PlayerControlRouter.MainPlayer ?? Player.Instance;
            return main != null
                && main.gameObject.activeInHierarchy
                && main.alive;
        }

        private void ValidateSecondPlayerReference()
        {
            if (_secondPlayer == null)
                return;

            if (_secondPlayer)
                return;

            ModRuntime.Log?.LogWarning("Second player was destroyed — will respawn when possible.");
            CleanupSecondPlayer();
        }

        public void CleanupSecondPlayer()
        {
            IsControllingSecond = false;
            _active = ControlledPlayer.Main;
            _autoSpawnSucceeded = false;

            if (_mainPlayer != null)
                _mainPlayer.immobilised = false;

            CoopInputBridge.RestoreMainDelegates();

            if (_secondPlayer != null)
                Destroy(_secondPlayer);

            _secondPlayer = null;
            _secondPlayerComponent = null;
            PlayerControlRouter.ClearSecond();

            RestoreMainPlayerVision();
        }

        public void EnforceVisionAndCamera()
        {
            if (!ModConfig.IsLocalMode || _secondPlayer == null)
                return;

            Player main = _mainPlayer != null ? _mainPlayer : PlayerControlRouter.GetMainForVision();
            bool second = _active == ControlledPlayer.Second;
            bool activeFlashlight = PlayerVisionController.IsFlashlightActiveOn(Player.Instance);

            if (second)
            {
                _mainVision?.SetVisionConeEnabled(false);
                _mainVision?.SetFlashlightEnabled(false);

                PlayerVisionController.RefreshMainFov(main);
                _secondVision?.SyncFovConeFrom(main);
                _secondVision?.SetVisionConeEnabled(true);
                _secondVision?.SetFlashlightEnabled(activeFlashlight);
            }
            else
            {
                PlayerVisionController.RefreshMainFov(main);
                _secondVision?.SetAllVisionDisabled();

                _mainVision?.SetVisionConeEnabled(true);
                _mainVision?.SetFlashlightEnabled(activeFlashlight);
            }

            CamMain cam = Singleton<CamMain>.Instance;
            if (cam == null)
                return;

            cam.followTarget = second ? _secondPlayer.transform : _savedCamFollowTarget;
        }

        private void TryAutoSpawn()
        {
            if (_autoSpawnSucceeded || _secondPlayer != null)
                return;

            if (!ModConfig.AutoSpawnLocalSecondPlayer.Value)
                return;

            if (!IsMainPlayerReady())
                return;

            if (SpawnSecondPlayer())
                _autoSpawnSucceeded = true;
        }

        private static bool IsMainPlayerReady()
        {
            PlayerControlRouter.EnsureMainRegistered();

            Player main = PlayerControlRouter.MainPlayer;
            if (main == null)
                main = Player.Instance;

            return main != null
                && main.gameObject.activeInHierarchy
                && main.alive
                && main.GetComponent<CoopPlayerMarker>() == null;
        }

        public bool SpawnSecondPlayer()
        {
            if (!ModConfig.IsLocalMode)
            {
                ModRuntime.Log?.LogWarning("SpawnSecondPlayer ignored: PlayMode is not Local.");
                return false;
            }

            if (!IsInPlaySession())
            {
                ModRuntime.Log?.LogWarning("SpawnSecondPlayer ignored: not in an active play session.");
                return false;
            }

            if (_secondPlayer != null)
                return true;

            PlayerControlRouter.EnsureMainRegistered();
            _mainPlayer = PlayerControlRouter.MainPlayer ?? Player.Instance;

            if (_mainPlayer == null || !_mainPlayer.gameObject.activeInHierarchy)
            {
                ModRuntime.Log?.LogWarning(
                    "Cannot spawn second player yet — main Player not in scene (load a save first).");
                return false;
            }

            if (_mainPlayer.GetComponent<CoopPlayerMarker>() != null)
            {
                ModRuntime.Log?.LogError(
                    "Cannot spawn second player — main Player reference is invalid. Return to menu and reload.");
                return false;
            }

            CoopInputBridge.EnsureMainDelegatesStored();

            _mainVision = PlayerVisionController.From(_mainPlayer.gameObject);

            CamMain cam = Singleton<CamMain>.Instance;
            if (cam != null)
                _savedCamFollowTarget = cam.followTarget;

            _secondPlayer = PlayerProxyBuilder.CreatePlayerClone(
                _mainPlayer,
                "LocalSecondPlayer",
                new Vector3(5f, 0f, 0f),
                PlayerCloneKind.LocalSecond,
                ModRuntime.Log);

            if (_secondPlayer == null)
            {
                ModRuntime.Log?.LogError("Second player spawn failed — see earlier log lines.");
                return false;
            }

            _secondPlayerComponent = _secondPlayer.GetComponent<Player>();
            if (_secondPlayerComponent == null)
            {
                ModRuntime.Log?.LogError("Second player object has no Player component — destroying clone.");
                Destroy(_secondPlayer);
                _secondPlayer = null;
                return false;
            }

            _secondVision = PlayerVisionController.From(_secondPlayer);
            _secondVision?.SetAllVisionDisabled();
            _mainVision?.SetVisionConeEnabled(true);

            _active = ControlledPlayer.Main;
            IsControllingSecond = false;
            ApplyControlAndInput();

            ModRuntime.Log?.LogInfo(
                "Local co-op second player spawned — press "
                + ModConfig.SwitchControlKey.Value
                + " to switch control.");
            return true;
        }

        public void SwitchControl()
        {
            if (_secondPlayer == null || _secondPlayerComponent == null)
            {
                ModRuntime.Log?.LogWarning(
                    "No second player — press " + ModConfig.SpawnLocalPlayerKey.Value + " to spawn.");
                return;
            }

            _active = _active == ControlledPlayer.Main ? ControlledPlayer.Second : ControlledPlayer.Main;
            ApplyControlAndInput();

            ModRuntime.Log?.LogInfo("Now controlling: " + _active);
        }

        private void ApplyControlAndInput()
        {
            IsControllingSecond = _active == ControlledPlayer.Second;

            if (IsControllingSecond)
            {
                CoopInputBridge.BindToPlayer(_secondPlayerComponent);
                PlayerControlRouter.ClearPlayerInputState(_mainPlayer);
                if (_mainPlayer != null)
                    try { _mainPlayer.closeInventory(); } catch (System.Exception ex) { ModRuntime.Log?.LogWarning("closeInventory(main): " + ex.Message); }
            }
            else
            {
                CoopInputBridge.RestoreMainDelegates();
                PlayerControlRouter.ClearPlayerInputState(_secondPlayerComponent);
                if (_secondPlayerComponent != null)
                    try { _secondPlayerComponent.closeInventory(); } catch (System.Exception ex) { ModRuntime.Log?.LogWarning("closeInventory(second): " + ex.Message); }
            }

            ApplyInactivePlayerLock();
            EnforceVisionAndCamera();
        }

        private void ApplyInactivePlayerLock()
        {
            if (_mainPlayer != null)
                _mainPlayer.immobilised = IsControllingSecond;

            if (_secondPlayerComponent != null)
                _secondPlayerComponent.immobilised = !IsControllingSecond;
        }

        private void RestoreMainPlayerVision()
        {
            _mainVision?.SetVisionConeEnabled(true);
            _mainVision?.SetFlashlightEnabled(PlayerVisionController.IsFlashlightActiveOn(_mainPlayer));
            _secondVision?.SetAllVisionDisabled();

            CamMain cam = Singleton<CamMain>.Instance;
            if (cam != null && _savedCamFollowTarget != null)
                cam.followTarget = _savedCamFollowTarget;
        }
    }
}
