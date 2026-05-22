using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    [HarmonyPatch(typeof(Door), "open")]
    public static class DoorOpenPatch
    {
        private static void Postfix(Door __instance, float OpenForce)
        {
            if (ModRuntime.Network == null)
                return;

            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            Vector3 openerPos = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;

            ModRuntime.Network.SendDoorState(new DoorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Opened = true,
                OpenerPosX = openerPos.x, OpenerPosY = openerPos.y, OpenerPosZ = openerPos.z,
                OpenForce = OpenForce
            });
            ModRuntime.Log?.LogInfo("[DoorSync] send open " + __instance.name + " at " + key + " force=" + OpenForce);
        }
    }

    [HarmonyPatch(typeof(Door), "close")]
    public static class DoorClosePatch
    {
        private static void Postfix(Door __instance)
        {
            if (ModRuntime.Network == null)
                return;

            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            Vector3 openerPos = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;

            ModRuntime.Network.SendDoorState(new DoorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Opened = false,
                OpenerPosX = openerPos.x, OpenerPosY = openerPos.y, OpenerPosZ = openerPos.z
            });
            ModRuntime.Log?.LogInfo("[DoorSync] send close " + __instance.name + " at " + key);
        }
    }

    [HarmonyPatch(typeof(Trigger), "OnAfterTrigger")]
    public static class TrapTriggerPatch
    {
        private static void Postfix(Trigger __instance, Collider other, bool doConnectChain)
        {
            if (ModRuntime.Network == null)
                return;

            string name = __instance.name.ToLowerInvariant();
            if (!name.Contains("trap") && !name.Contains("bear") && !name.Contains("snap") && !name.Contains("animal") && !name.Contains("mushroom"))
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendTrapState(new TrapState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Triggered = true
            });
            ModRuntime.Log?.LogInfo("[TrapSync] send triggered " + __instance.name + " at " + key);
        }
    }

    [HarmonyPatch(typeof(Player), "progressBarCompleted")]
    public static class TrapPlacementPatch
    {
        private static string _pendingType;
        private static Vector3 _pendingPos;
        private static Quaternion _pendingRot;

        private static void Prefix(Player __instance)
        {
            _pendingType = null;
            if (!__instance.placingItem) return;
            if (InvItemClass.isNull(__instance.currentItem)) return;
            ProxyItem proxy = __instance.proxyItem;
            if (proxy == null) return;

            _pendingType = __instance.currentItem.type;
            _pendingPos = proxy.transform.localPosition;
            _pendingRot = proxy.transform.rotation;
        }

        private static void Postfix(Player __instance)
        {
            if (string.IsNullOrEmpty(_pendingType))
                return;
            if (ModRuntime.Network == null)
                return;

            Vector3 euler = _pendingRot.eulerAngles;
            ModRuntime.Network.SendItemSpawn(new ItemSpawnMessage
            {
                ItemType = _pendingType,
                PosX = _pendingPos.x, PosY = _pendingPos.y, PosZ = _pendingPos.z,
                RotX = euler.x, RotY = euler.y, RotZ = euler.z
            });
            ModRuntime.Log?.LogInfo("[ItemSpawn] sent " + _pendingType + " at " + _pendingPos);
        }
    }

    [HarmonyPatch(typeof(Generator), "turnOn")]
    public static class GeneratorTurnOnPatch
    {
        private static void Postfix(Generator __instance)
        {
            if (ModRuntime.Network == null)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendGeneratorState(new GeneratorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = true, Fuel = __instance.fuel
            });
            ModRuntime.Log?.LogInfo("[GeneratorSync] send turnOn at " + key);
        }
    }

    [HarmonyPatch(typeof(Generator), "turnOff")]
    public static class GeneratorTurnOffPatch
    {
        private static void Postfix(Generator __instance)
        {
            if (ModRuntime.Network == null)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendGeneratorState(new GeneratorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = false, Fuel = __instance.fuel
            });
            ModRuntime.Log?.LogInfo("[GeneratorSync] send turnOff at " + key);
        }
    }

    [HarmonyPatch(typeof(Generator), "powerDown")]
    public static class GeneratorPowerDownPatch
    {
        private static void Postfix(Generator __instance)
        {
            if (ModRuntime.Network == null)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendGeneratorState(new GeneratorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = false, Fuel = __instance.fuel
            });
            ModRuntime.Log?.LogInfo("[GeneratorSync] send powerDown at " + key);
        }
    }

    [HarmonyPatch(typeof(Item), "turnOn")]
    public static class ItemTurnOnPatch
    {
        private static void Postfix(Item __instance)
        {
            if (ModRuntime.Network == null)
                return;
            if (__instance.GetComponent<Generator>() != null)
                return;
            if (!__instance.isLight && !__instance.switchable)
                return;

            Vector3 p = __instance.transform.position;
            ModRuntime.Network.SendLightState(new LightStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                IsOn = true, ItemName = __instance.name
            });
            ModRuntime.Log?.LogInfo("[LightSync] send turnOn " + __instance.name);
        }
    }

    [HarmonyPatch(typeof(Item), "turnOff")]
    public static class ItemTurnOffPatch
    {
        private static void Postfix(Item __instance)
        {
            if (ModRuntime.Network == null)
                return;
            if (__instance.GetComponent<Generator>() != null)
                return;
            if (!__instance.isLight && !__instance.switchable)
                return;

            Vector3 p = __instance.transform.position;
            ModRuntime.Network.SendLightState(new LightStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                IsOn = false, ItemName = __instance.name
            });
            ModRuntime.Log?.LogInfo("[LightSync] send turnOff " + __instance.name);
        }
    }

    [HarmonyPatch(typeof(Item), "empDisable")]
    public static class ItemEmpDisablePatch
    {
        private static void Postfix(Item __instance)
        {
            if (ModRuntime.Network == null)
                return;
            if (__instance.GetComponent<Generator>() != null)
                return;
            if (!__instance.isLight && !__instance.switchable)
                return;

            Vector3 p = __instance.transform.position;
            ModRuntime.Network.SendLightState(new LightStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                IsOn = false, ItemName = __instance.name
            });
            ModRuntime.Log?.LogInfo("[LightSync] send empDisable " + __instance.name);
        }
    }
}
