using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// On the client, when the local player hits the remote proxy with
    /// a melee weapon, sends a FriendlyFireMessage to the host instead
    /// of applying damage locally (host is authoritative for proxy damage).
    /// </summary>
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

            float strengthMod = 1f;
            if (local != null)
            {
                CharBase cb = local.GetComponent<CharBase>();
                if (cb != null)
                    strengthMod = cb.strengthModifier;
            }
            int dmg = Mathf.Max(1, (int)((float)__instance.damage * strengthMod));
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

    /// <summary>
    /// On the client, when the local player hits any target (Character,
    /// Door, Window, Item) with a melee weapon, sends the attack to the
    /// host for authoritative damage processing and broadcasts the hit
    /// sound to other clients.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter", new[] { typeof(Collider) })]
    public static class ClientMeleeSensorPatch
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Client)
                return true;
            if (__instance.type != MeleeSensor.MeleeSensorType.player)
                return true;

            // Skip proxy hits — ClientFriendlyFirePatch handles those
            if (_collider.GetComponentInParent<RemotePlayerProxy>() != null)
                return true;

            // Send sound for all player melee hits (Character, Door, Window, Item)
            var soundMsg = new PlayerSoundMessage
            {
                Range = 600f,
                DangerousSound = false,
                Volume = 1f,
                Gunshot = false
            };
            LanNetworkManager.Instance?.Send(NetMessageType.PlayerSound, w => soundMsg.Serialize(w), DeliveryMethod.ReliableOrdered);

            Character c = _collider.GetComponent<Character>();
            if (c == null)
            {
                Rigidbody rb = _collider.attachedRigidbody;
                if (rb != null) c = rb.GetComponent<Character>();
            }
            if (c == null) return true;

            // Play hit sound locally since vanilla getHit is skipped
            if (c.sounds != null)
                c.sounds.playGetHitByAxe1();

            // Don't apply damage locally — host is authoritative
            short nameHash = CharacterTracker.GetStableId(c);

            Vector3 pos = Player.Instance != null
                ? Player.Instance.transform.position
                : c.transform.position;

            float strengthMod = 1f;
            if (Player.Instance != null)
            {
                CharBase cb = Player.Instance.GetComponent<CharBase>();
                if (cb != null)
                    strengthMod = cb.strengthModifier;
            }

            var msg = new PlayerAttackMessage
            {
                TargetNameHash = nameHash,
                Damage = (int)((float)__instance.damage * strengthMod),
                AttackerPosX = pos.x,
                AttackerPosY = pos.y,
                AttackerPosZ = pos.z
            };
            LanNetworkManager.Instance?.Send(NetMessageType.PlayerAttack, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);

            return false;
        }
    }
}
