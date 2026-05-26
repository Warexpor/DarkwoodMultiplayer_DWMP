using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Character), "getHit", new[] { typeof(float), typeof(Transform), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class ClientDamageRedirectPatch
    {
        private static bool Prefix(Character __instance, float Damage, Transform attackerTransform, bool byPlayer)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Client)
                return true;

            if (!ClientEntityInterpolationService.IsHostSynced(__instance))
                return true;

            // Case 1: Direct player damage (hitscan, thrown weapon direct hit)
            bool isPlayerDamage = attackerTransform != null && Player.Instance != null && attackerTransform == Player.Instance.transform;

            // Case 2: Local explosion AOE splash damage that will be re-enacted on the host
            bool isExplosionAOE = TraverseHack.IsInsideLocalExplosion;

            if (!isPlayerDamage && !isExplosionAOE)
                return true;

            short stableId = CharacterTracker.GetStableId(__instance);
            Vector3 pos = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;
            net.Send(NetMessageType.PlayerAttack, w => new PlayerAttackMessage
            {
                TargetNameHash = stableId,
                Damage = (int)Damage,
                AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z
            }.Serialize(w), DeliveryMethod.ReliableOrdered);

            return false;
        }
    }
}
