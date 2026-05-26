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
            // Suppress during trap placement (the inventory item destruction should not trigger removal)
            if (Sync.TrapPlacementPatch.InsideTrapPlacement) return;

            GameObject go = obj as GameObject;
            if (go == null) return;

            string name = go.name.ToLowerInvariant();
            if (!name.Contains("trap") && !name.Contains("bear") && !name.Contains("snap") && !name.Contains("animal"))
                return;

            if (name.Contains("audioobject"))
                return;

            // Skip ProxyItem (placement preview) destruction — not a real world trap
            if (go.GetComponent<ProxyItem>() != null)
                return;

            // Skip objects that are children of the player (held inventory visuals)
            Player localPlayer = Player.Instance;
            if (localPlayer != null && go.transform.IsChildOf(localPlayer.transform))
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
}
