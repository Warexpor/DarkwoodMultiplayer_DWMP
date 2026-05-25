using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Helper used by both PlayerLightTogglePatch and PlayerLightOnSwitchPatch.
    /// </summary>
    internal static class LightStateHelper
    {
        internal static PlayerLightStateMessage BuildLightState(Player __instance)
        {
            var msg = new PlayerLightStateMessage { LightOn = false };
            if (InvItemClass.isNull(__instance.currentItem) || !__instance.currentItem.activated)
                return msg;

            msg.LightOn = true;
            msg.ItemType = __instance.currentItem.type;

            if (__instance.currentItem.baseClass.isFlashlight)
            {
                Light2D flash = HarmonyLib.Traverse.Create(__instance).Field("Flashlight").GetValue<Light2D>();
                if (flash != null)
                {
                    msg.LightRadius = flash.LightRadius;
                    msg.LightColorR = flash.LightColor.r;
                    msg.LightColorG = flash.LightColor.g;
                    msg.LightColorB = flash.LightColor.b;
                    msg.LightIntensity = flash.LightIntensity;
                }
            }
            else if (__instance.currentItem.baseClass.lightEmitter != null)
            {
                msg.HasLightEmitter = true;
            }

            return msg;
        }
    }

    [HarmonyPatch(typeof(Player), "onActivateItem")]
    public static class PlayerLightTogglePatch
    {
        private static void Postfix(Player __instance)
        {
            if (ModRuntime.Network == null) return;
            if (ModRuntime.Network.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            ModRuntime.Network.SendPlayerLightState(LightStateHelper.BuildLightState(__instance));
        }
    }

    /// <summary>
    /// Also sync light state when switching items (torch/flashlight might activate on equip).
    /// </summary>
    [HarmonyPatch(typeof(Player), "onDoneSwitchingItem")]
    public static class PlayerLightOnSwitchPatch
    {
        private static void Postfix(Player __instance)
        {
            if (ModRuntime.Network == null) return;
            if (ModRuntime.Network.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            ModRuntime.Network.SendPlayerLightState(LightStateHelper.BuildLightState(__instance));
        }
    }

    /// <summary>
    /// Captures throwable item spawn data and sends it to the host.
    /// The host spawns the thrown item so its trajectory and explosion are authoritative.
    /// </summary>
    [HarmonyPatch(typeof(Player), "throwItem")]
    public static class ThrowableSyncPatch
    {
        private static string _capturedItemType;
        private static float _capturedAimY;
        private static float _capturedDistance;

        private static bool Prefix(Player __instance)
        {
            if (ModRuntime.Network == null) return true;
            if (ModRuntime.Network.Role == NetworkRole.Offline) return true;
            if (TraverseHack.ApplyingFromNetwork) return true;

            try
            {
                if (!InvItemClass.isNull(__instance.currentItem))
                    _capturedItemType = __instance.currentItem.type;
                _capturedAimY = __instance.transform.eulerAngles.y;
                _capturedDistance = Mathf.Clamp(__instance.distanceToCursor(), 10f, 370f);
            }
            catch { _capturedItemType = null; }

            return true;
        }

        private static void Postfix(Player __instance)
        {
            if (string.IsNullOrEmpty(_capturedItemType)) return;
            if (ModRuntime.Network == null) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            Vector3 pos = __instance.transform.position;
            ModRuntime.Network.SendThrowableSpawn(new ThrowableSpawnMessage
            {
                ItemType = _capturedItemType,
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                AimY = _capturedAimY,
                Distance = _capturedDistance
            });

            ModRuntime.Log?.LogInfo("[ThrowableSync] sent " + _capturedItemType + " from " + pos + " aimY=" + _capturedAimY);

            _capturedItemType = null;
        }
    }

    /// <summary>
    /// When an Explodes component activates on either peer, relays the explosion
    /// position to the other side. The host runs the authoritative explosion;
    /// the client spawns the visual effect (prefab + sound).
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "onActivate", new System.Type[0])]
    public static class ExplosionTriggerPatch
    {
        private static void Postfix(Explodes __instance)
        {
            var net = ModRuntime.Network;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            if (Sync.WorldPhysicsSyncService._suppressBroadcast) return;

            bool flaming = false;
            try { flaming = (bool)HarmonyLib.Traverse.Create(__instance).Field("flaming").GetValue(); } catch { }

            string prefabName = "";
            try
            {
                var prefab = (UnityEngine.Object)HarmonyLib.Traverse.Create(__instance).Field("explosionPrefab").GetValue();
                if (prefab != null) prefabName = prefab.name;
            }
            catch { }

            string soundId = "";
            try { soundId = (string)HarmonyLib.Traverse.Create(__instance).Field("explodeSound").GetValue(); } catch { }

            Vector3 pos = __instance.transform.position;
            net.SendExplosionTrigger(new ExplosionTriggerMessage
            {
                PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                ObjectName = __instance.name,
                Flaming = flaming,
                PrefabName = prefabName,
                SoundId = soundId
            });

            ModRuntime.Log?.LogInfo("[ExplosionSync] sent explosion at " + pos + " name=" + __instance.name + " flaming=" + flaming);
        }
    }
}
