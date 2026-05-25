using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Detects when a trap GameObject is destroyed directly (e.g. placed trap removed
    /// by the client player via right-click) and notifies the host so the matching
    /// trap there is also destroyed.
    /// </summary>
    [HarmonyPatch(typeof(UnityEngine.Object), "Destroy", new[] { typeof(UnityEngine.Object) })]
    public static class ObjectDestroyTrapPatch
    {
        private static void Prefix(UnityEngine.Object obj)
        {
            if (ModRuntime.Network == null) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            // Suppress when getDroppedItem already handles it (trap remains pickup)
            if (GetDroppedItemGuard.Inside) return;

            GameObject go = obj as GameObject;
            if (go == null) return;

            string name = go.name.ToLowerInvariant();
            if (!name.Contains("trap") && !name.Contains("bear") && !name.Contains("snap") && !name.Contains("animal"))
                return;

            Vector3 p = go.transform.position;
            Vector3 key = new Vector3(
                Mathf.Round(p.x * 10f) / 10f,
                Mathf.Round(p.y * 10f) / 10f,
                Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendWorldObjectRemoved(new WorldObjectRemovedMessage
            {
                PosX = key.x,
                PosY = key.y,
                PosZ = key.z,
                ObjectName = go.name
            });

            ModRuntime.Log?.LogInfo("[TrapDestroy] sent removed \"" + go.name + "\" at " + key);
        }
    }

    /// <summary>
    /// Set to true while inside getDroppedItem Prefix → Postfix so that any
    /// Object.Destroy hook can skip duplicate handling.
    /// </summary>
    internal static class GetDroppedItemGuard
    {
        public static bool Inside;
    }

    [HarmonyPatch(typeof(Item), "getDroppedItem")]
    public static class ItemGetDroppedItemPatch
    {
        private static void Prefix(Item __instance)
        {
            GetDroppedItemGuard.Inside = true;

            try
            {
                Inventory inv = HarmonyLib.Traverse.Create(__instance).Field("inventory").GetValue<Inventory>();
                if (inv != null && inv.slots.Count > 0
                    && !InvItemClass.isNull(inv.slots[0].invItem))
                {
                    inv.slots[0].invItem.amount *= 2;
                }
            }
            catch { }
        }

        private static void Postfix(Item __instance)
        {
            GetDroppedItemGuard.Inside = false;

            if (ModRuntime.Network == null) return;
            if (ModRuntime.Network.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            string name = __instance.name.ToLowerInvariant();
            bool isWorldObject = name.Contains("mushroom") || name.Contains("exp") || name.Contains("bio")
                || name.Contains("trap") || name.Contains("bear") || name.Contains("snap") || name.Contains("animal");

            if (!isWorldObject) return;

            Vector3 p = __instance.transform.position;
            Vector3 key = new Vector3(
                Mathf.Round(p.x * 10f) / 10f,
                Mathf.Round(p.y * 10f) / 10f,
                Mathf.Round(p.z * 10f) / 10f);

            ModRuntime.Network.SendWorldObjectRemoved(new WorldObjectRemovedMessage
            {
                PosX = key.x,
                PosY = key.y,
                PosZ = key.z,
                ObjectName = __instance.name
            });

            ModRuntime.Log?.LogInfo("[ItemHarvest] sent removed \"" + __instance.name + "\" at " + key);
        }
    }
}
