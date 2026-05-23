using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter", new[] { typeof(Collider) })]
    public static class ClientFriendlyFirePatch
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return true;
            if (__instance.type != MeleeSensor.MeleeSensorType.player)
                return true;

            RemotePlayerProxy proxy = _collider.GetComponentInParent<RemotePlayerProxy>();
            if (proxy == null)
                return true;

            Player local = Player.Instance;
            if (local != null && local.currentItem != null)
                local.currentItem.drainDurability(__instance.itemDurabilityDrain);

            int dmg = Mathf.Max(1, __instance.damage);
            Vector3 pos = local != null ? local.transform.position : proxy.transform.position;

            var msg = new FriendlyFireMessage
            {
                Damage = dmg,
                AttackerPosX = pos.x,
                AttackerPosY = pos.y,
                AttackerPosZ = pos.z,
                CanCutInHalf = dmg >= 80
            };
            LanNetworkManager.Instance?.Send(NetMessageType.FriendlyFire, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);

            return false;
        }
    }

    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter", new[] { typeof(Collider) })]
    public static class ClientMeleeSensorPatch
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return true;
            if (__instance.type != MeleeSensor.MeleeSensorType.player)
                return true;

            Character c = _collider.GetComponent<Character>();
            if (c == null)
            {
                Rigidbody rb = _collider.attachedRigidbody;
                if (rb != null) c = rb.GetComponent<Character>();
            }
            if (c == null) return true;

            // Don't apply damage locally — host is authoritative
            short nameHash = CharacterTracker.GetStableId(c);

            Vector3 pos = Player.Instance != null
                ? Player.Instance.transform.position
                : c.transform.position;

            var msg = new PlayerAttackMessage
            {
                TargetNameHash = nameHash,
                Damage = __instance.damage,
                AttackerPosX = pos.x,
                AttackerPosY = pos.y,
                AttackerPosZ = pos.z
            };
            LanNetworkManager.Instance?.Send(NetMessageType.PlayerAttack, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);

            return false;
        }
    }
}
