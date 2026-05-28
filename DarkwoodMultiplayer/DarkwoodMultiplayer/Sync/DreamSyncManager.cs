using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using System;
using System.Collections;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    internal static class DreamSyncManager
    {
        private static bool _localDreamActive;
        private static bool _remoteDreamActive;
        private static string _currentDreamPreset;
        private static bool _isDualPresence;

        private static Vector3 _preDreamPosition;
        private static string _preDreamGridName;

        public static bool IsDreamActive => _localDreamActive || _remoteDreamActive;
        public static bool IsLocalDreamActive => _localDreamActive;
        public static bool IsDualPresence => _isDualPresence;
        public static string CurrentDreamPreset => _currentDreamPreset;

        public static void OnLocalDreamStarted(string presetName, Vector3 locationPosition)
        {
            if (_localDreamActive) return;
            _localDreamActive = true;
            _currentDreamPreset = presetName;
            _isDualPresence = FinalDreamsceneManager.IsDualPresenceDream(presetName);

            if (FinalDreamsceneManager.IsDeathTrackedDream(presetName))
                FinalDreamsceneManager.OnDreamStarted();

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DreamStarted,
                    w => new DreamStartedMessage
                    {
                        PresetName = presetName,
                        LocPosX = locationPosition.x,
                        LocPosY = locationPosition.y,
                        LocPosZ = locationPosition.z
                    }.Serialize(w),
                    LiteNetLib.DeliveryMethod.ReliableOrdered);
            }

            ModRuntime.Log?.LogInfo($"[DreamSync] Local dream started: {presetName}, dualPresence={_isDualPresence}, pos={locationPosition}");
        }

        public static void OnLocalDreamEnded()
        {
            if (!_localDreamActive) return;

            if (FinalDreamsceneManager.IsDeathTrackedDream(_currentDreamPreset))
                FinalDreamsceneManager.OnDreamEnded();

            _localDreamActive = false;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net != null && net.IsConnected)
            {
                net.Send(NetMessageType.DreamEnded,
                    w => new DreamEndedMessage { PresetName = _currentDreamPreset ?? "" }.Serialize(w),
                    LiteNetLib.DeliveryMethod.ReliableOrdered);
            }

            ModRuntime.Log?.LogInfo($"[DreamSync] Local dream ended: {_currentDreamPreset}");

            _currentDreamPreset = null;
            _isDualPresence = false;
        }

        public static void OnRemoteDreamStarted(string presetName, bool _, Vector3 locationPosition)
        {
            if (_remoteDreamActive) return;
            _remoteDreamActive = true;
            _currentDreamPreset = presetName;
            _isDualPresence = FinalDreamsceneManager.IsDualPresenceDream(presetName);

            if (FinalDreamsceneManager.IsDeathTrackedDream(presetName))
                FinalDreamsceneManager.OnDreamStarted();

            ModRuntime.Log?.LogInfo($"[DreamSync] Remote dream started: {presetName}, dualPresence={_isDualPresence}, pos={locationPosition}");

            if (_isDualPresence)
            {
                // Dual-presence (epilogue): both players enter the dream together.
                SavePreDreamState();
                ProcessRemoteDream(locationPosition);
                return;
            }

            // Regular dream: non-dreamer just freezes until it ends.
            // No scene loading, no spectating — loading the dream scene on either
            // side causes WorldGrid conflicts and entity sync desyncs.
            ApplyDreamFreeze();
        }

        private static void ProcessRemoteDream(Vector3 locationPosition)
        {
            ApplyDreamCameraEffects(_currentDreamPreset);
            ShowDreamTransition();
            Singleton<Controller>.Instance.StartCoroutine(LoadDreamSceneCoroutine(_currentDreamPreset, locationPosition, false));
        }

        public static void OnRemoteDreamEnded()
        {
            if (!_remoteDreamActive) return;

            if (FinalDreamsceneManager.IsDeathTrackedDream(_currentDreamPreset))
                FinalDreamsceneManager.OnDreamEnded();

            _remoteDreamActive = false;

            ModRuntime.Log?.LogInfo($"[DreamSync] Remote dream ended: {_currentDreamPreset}");

            if (_isDualPresence)
            {
                CleanupDreamScene(_currentDreamPreset);
                RemoveDreamCameraEffects(_currentDreamPreset);
                RestorePreDreamState();
            }
            else
            {
                RemoveDreamFreeze();
            }

            _currentDreamPreset = null;
            _isDualPresence = false;
        }

        public static void OnDisconnected()
        {
            FinalDreamsceneManager.OnDisconnected();

            if (_remoteDreamActive)
            {
                if (_isDualPresence)
                {
                    CleanupDreamScene(_currentDreamPreset);
                    RemoveDreamCameraEffects(_currentDreamPreset);
                    RestorePreDreamState();
                }
                else
                {
                    RemoveDreamFreeze();
                }
            }
            _localDreamActive = false;
            _remoteDreamActive = false;
            _currentDreamPreset = null;
            _isDualPresence = false;
            FreezeTracker.Reset();
        }

        private static void SavePreDreamState()
        {
            var player = Player.Instance;
            _preDreamPosition = player != null ? player._transform.position : Vector3.zero;
            _preDreamGridName = Singleton<WorldGrid>.Instance != null && Singleton<WorldGrid>.Instance.currentGrid != null
                ? Singleton<WorldGrid>.Instance.currentGrid.name
                : "World";
        }

        private static void RestorePreDreamState()
        {
            var player = Player.Instance;
            if (player != null)
            {
                player.invulnerable = false;
                if (player.immobilised)
                    player.stopImmobilise();
                player.switchVisibilty(true);
                player.teleportTo(_preDreamPosition, Quaternion.Euler(90f, 0f, 0f));
            }

            if (Singleton<WorldGrid>.Instance != null)
            {
                if (Singleton<WorldGrid>.Instance.currentGrid != null)
                    Singleton<WorldGrid>.Instance.currentGrid.leave();
                Singleton<WorldGrid>.Instance.setGrid(_preDreamGridName ?? "World");
                Vector3 restorePos = player != null ? player._transform.position : _preDreamPosition;
                Singleton<WorldGrid>.Instance.refreshPosition(restorePos, instant: true, force: true);
            }

            if (Singleton<UI>.Instance != null)
                Singleton<UI>.Instance.showVisibleUI();
        }

        private static IEnumerator LoadDreamSceneCoroutine(string locationName, Vector3 position, bool _)
        {
            yield return null;

            Location component = null;
            yield return StartLoadDreamScene(locationName, position, result => component = result);

            if (component == null)
            {
                yield break;
            }

            var player = Player.Instance;
            if (player == null) yield break;

            Vector3 spawnPos = component.playerSpawn != null
                ? component.playerSpawn.transform.position
                : position;

            player.teleportTo(spawnPos, Quaternion.Euler(90f, 0f, 0f));
            ApplyDreamCameraEffects(locationName);

            if (Singleton<WorldGrid>.Instance != null)
            {
                if (Singleton<WorldGrid>.Instance.currentGrid != null)
                    Singleton<WorldGrid>.Instance.currentGrid.leave();
                Singleton<WorldGrid>.Instance.setGrid(locationName);
                Singleton<WorldGrid>.Instance.refreshPosition(player._transform.position, instant: true, force: true);
            }

            ModRuntime.Log?.LogInfo($"[DreamSync] Player positioned at dream location: {locationName}");
        }

        private static IEnumerator StartLoadDreamScene(string locationName, Vector3 position, Action<Location> onComplete)
        {
            GameObject markerObj = Core.AddPrefab("LocationMarker",
                position,
                Quaternion.Euler(90f, 0f, 0f),
                null);

            if (markerObj == null)
            {
                ModRuntime.Log?.LogError("[DreamSync] Failed to create LocationMarker prefab");
                onComplete?.Invoke(null);
                yield break;
            }

            LocationMarker marker = markerObj.GetComponent<LocationMarker>();
            marker.locationName = locationName;

            if (Singleton<WorldGenerator>.Instance != null)
                OutsideLocations.createGrid(locationName, marker.transform.position);

            GameObject holder = markerObj;
            Transform parentTransform = null;
            if (Singleton<WorldGenerator>.Instance != null && Singleton<WorldGenerator>.Instance.OutsideLocationsGO != null)
            {
                holder = Singleton<WorldGenerator>.Instance.OutsideLocationsGO;
                parentTransform = holder.transform;
            }

            yield return marker.StartCoroutine(marker.spawnLocation(holder));

            if (marker.thisLocation == null)
            {
                ModRuntime.Log?.LogError("[DreamSync] marker.thisLocation is null after spawnLocation");
                onComplete?.Invoke(null);
                yield break;
            }

            Location component = marker.thisLocation.GetComponent<Location>();

            if (parentTransform != null)
                marker.thisLocation.transform.parent = parentTransform;

            Singleton<OutsideLocations>.Instance.spawnedLocations[locationName] = component;
            Dreams.Instance.dreamLocation = component;

            if (holder != markerObj)
                UnityEngine.Object.Destroy(markerObj);

            ModRuntime.Log?.LogInfo($"[DreamSync] Dream scene loaded: {locationName} at {position}");
            onComplete?.Invoke(component);
        }

        private static void CleanupDreamScene(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return;

            try
            {
                if (Singleton<WorldGrid>.Instance != null)
                {
                    var grid = Singleton<WorldGrid>.Instance.getGrid(locationName);
                    if (grid != null)
                        Singleton<WorldGrid>.Instance.grids.Remove(grid);
                }

                if (Singleton<OutsideLocations>.Instance != null &&
                    Singleton<OutsideLocations>.Instance.spawnedLocations.ContainsKey(locationName))
                {
                    Singleton<OutsideLocations>.Instance.spawnedLocations.Remove(locationName);
                }

                GameObject targetObj = null;
                if (Dreams.Instance != null && Dreams.Instance.dreamLocation != null && Dreams.Instance.dreamLocation.gameObject != null)
                {
                    string objName = Dreams.Instance.dreamLocation.gameObject.name.Replace("_done", "");
                    if (string.Equals(objName, locationName, StringComparison.OrdinalIgnoreCase))
                        targetObj = Dreams.Instance.dreamLocation.gameObject;
                }

                if (targetObj == null)
                    targetObj = GameObject.Find(locationName + "_done");

                if (targetObj != null)
                {
                    UnityEngine.Object.Destroy(targetObj, 2f);
                    if (Dreams.Instance != null)
                        Dreams.Instance.dreamLocation = null;
                }

                ModRuntime.Log?.LogInfo($"[DreamSync] Dream scene cleaned up: {locationName}");
            }
            catch (Exception ex)
            {
                ModRuntime.Log?.LogWarning($"[DreamSync] Error during dream scene cleanup: {ex.Message}");
            }
        }

        private static void ApplyDreamFreeze()
        {
            FreezeTracker.AddFreeze();
        }

        private static void RemoveDreamFreeze()
        {
            FreezeTracker.RemoveFreeze();
        }

        private static void ApplyDreamCameraEffects(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return;
            try
            {
                GameObject presetGO = Resources.Load("DreamPresets/" + presetName) as GameObject;
                if (presetGO != null)
                {
                    Core.modifyCamEffects(active: true, presetGO);
                    ModRuntime.Log?.LogInfo($"[DreamSync] Applied camera effects for dream: {presetName}");
                }
            }
            catch (Exception ex)
            {
                ModRuntime.Log?.LogWarning($"[DreamSync] Failed to apply camera effects: {ex.Message}");
            }
        }

        private static void RemoveDreamCameraEffects(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return;
            try
            {
                GameObject presetGO = Resources.Load("DreamPresets/" + presetName) as GameObject;
                if (presetGO != null)
                {
                    Core.modifyCamEffects(active: false, presetGO);
                    ModRuntime.Log?.LogInfo($"[DreamSync] Removed camera effects for dream: {presetName}");
                }
            }
            catch (Exception ex)
            {
                ModRuntime.Log?.LogWarning($"[DreamSync] Failed to remove camera effects: {ex.Message}");
            }
        }

        private static void ShowDreamTransition()
        {
            if (Singleton<UI>.Instance == null) return;

            try
            {
                var blackTop = Singleton<UI>.Instance.blackScreenTop;
                if (blackTop != null)
                {
                    var sprite = blackTop.GetComponent<tk2dBaseSprite>();
                    if (sprite != null)
                    {
                        Singleton<UI>.Instance.tweenBlackScreenTop(new Color(0f, 0f, 0f, 1f), 0.3f);

                        Singleton<Controller>.Instance.waitFramesAndRun(delegate
                        {
                            if (sprite.color.a != 0f)
                            {
                                Singleton<UI>.Instance.tweenBlackScreenTop(new Color(0f, 0f, 0f, 0f), 0.5f);
                            }
                        }, 1);
                    }
                }
            }
            catch (Exception ex)
            {
                ModRuntime.Log?.LogWarning($"[DreamSync] Failed to show dream transition: {ex.Message}");
            }
        }
    }
}
