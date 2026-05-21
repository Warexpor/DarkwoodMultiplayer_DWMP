using System.Reflection;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Toggles Darkwood vision the same way as Player.switchVisibilty / flashlight equip:
    /// FOV cone via PlayerFOVLogic (+ synced PlayerFOVLight / PlayerFOVLightDot) vs handheld Flashlight.
    /// </summary>
    public sealed class PlayerVisionController
    {
        private static readonly MethodInfo GetFovAngleMethod =
            typeof(Player).GetMethod("getFOVAngle", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly GameObject _root;
        private Light2D _fovLogic;
        private Light2D _fovLight;
        private Light2D _fovDot;
        private Light2D _lightDot;
        private Light2D _flashlight;
        private GameObject _shadow;

        private PlayerVisionController(GameObject root)
        {
            _root = root;
            BindLights();
        }

        public static PlayerVisionController From(GameObject root)
        {
            return root == null ? null : new PlayerVisionController(root);
        }

        private void BindLights()
        {
            Transform root = _root.transform;
            _fovLogic = FindLight(root, "PlayerFOVLogic");
            _fovLight = FindLight(root, "PlayerFOVLight");
            _fovDot = FindLight(root, "PlayerFOVLightDot");
            _lightDot = FindLight(root, "PlayerLightDot");
            _flashlight = FindLight(root, "Flashlight");

            Transform shadow = root.Find("Shadow");
            if (shadow != null)
                _shadow = shadow.gameObject;
        }

        private static Light2D FindLight(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            return child == null ? null : child.GetComponent<Light2D>();
        }

        /// <summary>
        /// Matches Player.switchVisibilty: shadow + ambient dot + FOV logic cone.
        /// </summary>
        public void SetVisionConeEnabled(bool enabled)
        {
            if (_fovLogic != null)
                _fovLogic.gameObject.SetActive(enabled);

            if (_fovLight != null)
                _fovLight.gameObject.SetActive(enabled);

            if (_fovDot != null)
                _fovDot.gameObject.SetActive(enabled);

            if (_lightDot != null)
                _lightDot.gameObject.SetActive(enabled);

            if (_shadow != null)
                _shadow.SetActive(enabled);
        }

        public void SetFlashlightEnabled(bool enabled)
        {
            if (_flashlight != null)
                _flashlight.gameObject.SetActive(enabled);
        }

        public void SetAllVisionDisabled()
        {
            SetVisionConeEnabled(false);
            SetFlashlightEnabled(false);
        }

        /// <summary>
        /// Copies live FOV cone angles/radius from another player (after getFOVAngle has run on the source).
        /// </summary>
        public void SyncFovConeFrom(PlayerVisionController source)
        {
            if (source == null)
                return;

            CopyLightCone(_fovLogic, source._fovLogic);
            CopyLightCone(_fovLight, source._fovLight);
            CopyLightCone(_fovDot, source._fovDot);
            CopyLightCone(_lightDot, source._lightDot);
        }

        public void SyncFovConeFrom(Player main)
        {
            if (main == null)
                return;

            RefreshMainFov(main);
            CopyLightCone(_fovLogic, main.FOVLogic);
            CopyLightCone(_fovLight, FindLight(main.transform, "PlayerFOVLight"));
            CopyLightCone(_fovDot, main.FOVDot);
            CopyLightCone(_lightDot, FindLight(main.transform, "PlayerLightDot"));
        }

        public static void RefreshMainFov(Player main)
        {
            if (main == null || GetFovAngleMethod == null)
                return;

            GetFovAngleMethod.Invoke(main, null);
        }

        public static bool IsFlashlightActiveOn(Player main)
        {
            if (main == null)
                return false;

            Transform flash = main.transform.Find("Flashlight");
            return flash != null && flash.gameObject.activeInHierarchy;
        }

        private static void CopyLightCone(Light2D dst, Light2D src)
        {
            if (dst == null || src == null)
                return;

            dst.LightConeAngle = src.LightConeAngle;
            dst.LightRadius = src.LightRadius;
            dst.LightColor = src.LightColor;
        }
    }
}
