using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    /// <summary>Harmony patch: intercepts Door.open() and broadcasts the open state to all peers.</summary>
    [HarmonyPatch(typeof(Door), "open")]
    public static class DoorOpenPatch
    {
        private static void Postfix(Door __instance, float OpenForce)
        {
            if (ModRuntime.Network == null)
                return;

            // Don't re-broadcast if we're applying a remote snapshot
            if (TraverseHack.ApplyingFromNetwork)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            Vector3 openerPos = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;

            float bodyRotY = __instance.body != null ? __instance.body.eulerAngles.y : 0f;

            ModRuntime.Network.SendDoorState(new DoorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Opened = true,
                OpenerPosX = openerPos.x, OpenerPosY = openerPos.y, OpenerPosZ = openerPos.z,
                OpenForce = OpenForce, BodyRotY = bodyRotY
            });
            ModRuntime.Log?.LogInfo("[DoorSync] send open " + __instance.name + " at " + key + " force=" + OpenForce + " bodyY=" + bodyRotY);
        }
    }

    /// <summary>Harmony patch: intercepts Door.close() and broadcasts the close state to all peers.</summary>
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

            float bodyRotY = __instance.body != null ? __instance.body.eulerAngles.y : 0f;

            ModRuntime.Network.SendDoorState(new DoorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z, Opened = false,
                OpenerPosX = openerPos.x, OpenerPosY = openerPos.y, OpenerPosZ = openerPos.z,
                BodyRotY = bodyRotY
            });
            ModRuntime.Log?.LogInfo("[DoorSync] send close " + __instance.name + " at " + key + " bodyY=" + bodyRotY);
        }
    }

    /// <summary>
    /// Harmony patch: intercepts Trigger.switchToTriggered() (traps) and broadcasts the triggered state.
    /// This is more reliable than hooking OnAfterTrigger because switchToTriggered() is public
    /// and is the common final path for all trap triggering.
    /// </summary>
    [HarmonyPatch(typeof(Trigger), "switchToTriggered")]
    public static class TrapSwitchPatch
    {
        private static void Postfix(Trigger __instance)
        {
            if (ModRuntime.Network == null)
                return;

            // Prevent re-broadcasting when applying a remote snapshot
            if (TraverseHack.ApplyingFromNetwork)
                return;

            // Only sync objects whose name suggests they are a trap
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

    /// <summary>Harmony patch: intercepts Player.progressBarCompleted (item placement) and broadcasts the spawn.</summary>
    [HarmonyPatch(typeof(Player), "progressBarCompleted")]
    public static class TrapPlacementPatch
    {
        /// <summary>
        /// Set to true while inside progressBarCompleted, so ObjectDestroyTrapPatch
        /// can suppress fake removal messages caused by the inventory item being destroyed.
        /// </summary>
        internal static bool InsideTrapPlacement;

        private static string _pendingType;
        private static Vector3 _pendingPos;
        private static Quaternion _pendingRot;

        private static void Prefix(Player __instance)
        {
            InsideTrapPlacement = true;
            _pendingType = null;
            if (!__instance.placingItem) return;
            if (InvItemClass.isNull(__instance.currentItem)) return;
            ProxyItem proxy = __instance.proxyItem;
            if (proxy == null) return;

            // Capture the item type and placement transform before the placement completes
            _pendingType = __instance.currentItem.type;
            _pendingPos = proxy.transform.localPosition;
            _pendingRot = proxy.transform.rotation;
        }

        private static void Postfix(Player __instance)
        {
            InsideTrapPlacement = false;

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

    /// <summary>Harmony patch: intercepts Generator.turnOn() and broadcasts the state.</summary>
    [HarmonyPatch(typeof(Generator), "turnOn")]
    public static class GeneratorTurnOnPatch
    {
        private static void Postfix(Generator __instance)
        {
            if (ModRuntime.Network == null)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            Item itemComp = __instance.GetComponent<Item>();
            string itemType = itemComp != null && itemComp.invItem != null ? itemComp.invItem.type : "";

            ModRuntime.Network.SendGeneratorState(new GeneratorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = true, Fuel = __instance.fuel, ItemType = itemType
            });
            ModRuntime.Log?.LogInfo("[GeneratorSync] send turnOn at " + key + " type=" + itemType);
        }
    }

    /// <summary>Harmony patch: intercepts Generator.turnOff() and broadcasts the state.</summary>
    [HarmonyPatch(typeof(Generator), "turnOff")]
    public static class GeneratorTurnOffPatch
    {
        private static void Postfix(Generator __instance)
        {
            if (ModRuntime.Network == null)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            Item itemComp = __instance.GetComponent<Item>();
            string itemType = itemComp != null && itemComp.invItem != null ? itemComp.invItem.type : "";

            ModRuntime.Network.SendGeneratorState(new GeneratorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = false, Fuel = __instance.fuel, ItemType = itemType
            });
            ModRuntime.Log?.LogInfo("[GeneratorSync] send turnOff at " + key + " type=" + itemType);
        }
    }

    /// <summary>Harmony patch: intercepts Generator.powerDown() and broadcasts the state.</summary>
    [HarmonyPatch(typeof(Generator), "powerDown")]
    public static class GeneratorPowerDownPatch
    {
        private static void Postfix(Generator __instance)
        {
            if (ModRuntime.Network == null)
                return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(Mathf.Round(p.x * 10f) / 10f, Mathf.Round(p.y * 10f) / 10f, Mathf.Round(p.z * 10f) / 10f);

            Item itemComp = __instance.GetComponent<Item>();
            string itemType = itemComp != null && itemComp.invItem != null ? itemComp.invItem.type : "";

            ModRuntime.Network.SendGeneratorState(new GeneratorState
            {
                PosX = key.x, PosY = key.y, PosZ = key.z,
                IsOn = false, Fuel = __instance.fuel, ItemType = itemType
            });
            ModRuntime.Log?.LogInfo("[GeneratorSync] send powerDown at " + key + " type=" + itemType);
        }
    }

    /// <summary>Harmony patch: intercepts Item.turnOn (lights, switchable items) and broadcasts the state.</summary>
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
            string itemType = __instance.invItem != null ? __instance.invItem.type : "";
            ModRuntime.Network.SendLightState(new LightStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                IsOn = true, ItemName = __instance.name, ItemType = itemType
            });
            ModRuntime.Log?.LogInfo("[LightSync] send turnOn " + __instance.name + " type=" + itemType);
        }
    }

    /// <summary>Harmony patch: intercepts Item.turnOff (lights, switchable items) and broadcasts the state.</summary>
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
            string itemType = __instance.invItem != null ? __instance.invItem.type : "";
            ModRuntime.Network.SendLightState(new LightStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                IsOn = false, ItemName = __instance.name, ItemType = itemType
            });
            ModRuntime.Log?.LogInfo("[LightSync] send turnOff " + __instance.name + " type=" + itemType);
        }
    }

    /// <summary>Harmony patch: intercepts Item.empDisable and broadcasts the light-off state.</summary>
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
            string itemType = __instance.invItem != null ? __instance.invItem.type : "";
            ModRuntime.Network.SendLightState(new LightStateMessage
            {
                PosX = p.x, PosY = p.y, PosZ = p.z,
                IsOn = false, ItemName = __instance.name, ItemType = itemType
            });
            ModRuntime.Log?.LogInfo("[LightSync] send empDisable " + __instance.name + " type=" + itemType);
        }
    }
}
