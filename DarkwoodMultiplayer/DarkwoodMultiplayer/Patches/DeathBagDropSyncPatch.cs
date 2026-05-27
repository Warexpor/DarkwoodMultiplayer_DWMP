using System.Collections.Generic;
using DarkwoodMultiplayer;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Postfix on Player.dropBody().
    /// After the death bag is dropped locally, sends a DeathBagSpawnMessage
    /// to the remote peer so it also spawns the bag with matching contents.
    /// </summary>
    [HarmonyPatch(typeof(Player), "dropBody")]
    public static class DeathBagDropSyncPatch
    {
        private static void Postfix(Player __instance, Vector3 destPos)
        {
            if (LanNetworkManager.IsApplyingRemoteState) return;
            if (ModRuntime.Network == null || !ModRuntime.Network.IsConnected)
                return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null) return;

            // Find the DeathDrop that was just created at destPos
            // It was spawned with Core.AddPrefab, parented to Core.ItemContainer
            DeathDrop deathDrop = FindDeathDropAt(destPos);
            if (deathDrop == null)
            {
                ModRuntime.Log?.LogWarning("[Death] dropBody Postfix: could not find DeathDrop at " + destPos);
                return;
            }

            Inventory bagInv = deathDrop.GetComponent<Inventory>();
            if (bagInv == null) return;

            // Collect items from the bag
            var types = new List<string>();
            var amounts = new List<int>();
            var durabilities = new List<float>();
            var ammos = new List<int>();

            foreach (InvSlot slot in bagInv.slots)
            {
                if (!InvItemClass.isNull(slot.invItem))
                {
                    types.Add(slot.invItem.type);
                    amounts.Add(slot.invItem.amount);
                    durabilities.Add(slot.invItem.durability);
                    ammos.Add(slot.invItem.ammo);
                }
            }

            ModRuntime.Log?.LogInfo($"[Death] Syncing death bag at {destPos} with {types.Count} items");

            var msg = new DeathBagSpawnMessage
            {
                PosX = destPos.x, PosY = destPos.y, PosZ = destPos.z,
                ExpAmount = deathDrop.expAmount,
                ItemCount = types.Count,
                ItemTypes = types.ToArray(),
                ItemAmounts = amounts.ToArray(),
                ItemDurabilities = durabilities.ToArray(),
                ItemAmmos = ammos.ToArray()
            };

            net.Send(NetMessageType.DeathBagSpawn, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }

        private static DeathDrop FindDeathDropAt(Vector3 pos)
        {
            // The bag is parented to Core.ItemContainer
            GameObject container = Core.ItemContainer;
            if (container == null) return null;

            foreach (Transform child in container.transform)
            {
                DeathDrop dd = child.GetComponent<DeathDrop>();
                if (dd != null)
                {
                    // Check if this death drop is near the target position
                    float dist = Vector3.Distance(child.position, pos);
                    if (dist < 2f)
                        return dd;
                }
            }
            return null;
        }
    }
}
