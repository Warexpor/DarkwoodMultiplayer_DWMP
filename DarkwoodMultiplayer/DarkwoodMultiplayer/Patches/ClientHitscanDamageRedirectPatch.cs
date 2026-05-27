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
            try
            {
                var net = ModRuntime.Network as LanNetworkManager;
                if (net == null || net.Role != NetworkRole.Client)
                    return true;

                bool isPlayerDamage = attackerTransform != null && Player.Instance != null && attackerTransform == Player.Instance.transform;
                bool isProjectileDamage = attackerTransform == null && TraverseHack.IsInsidePlayerBulletCollision;
                bool isExplosionAOE = TraverseHack.IsInsideLocalExplosion;

                if (!isPlayerDamage && !isProjectileDamage && !isExplosionAOE)
                    return true;

                bool isSynced = ClientEntityInterpolationService.IsHostSynced(__instance);
                short stableId = isSynced ? CharacterTracker.GetStableId(__instance) : (short)0;
                Vector3 pos = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;
                Vector3 targetPos = __instance.transform.position;

                net.Send(NetMessageType.PlayerAttack, w => new PlayerAttackMessage
                {
                    TargetNameHash = stableId,
                    Damage = (int)Damage,
                    AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z,
                    TargetName = __instance.name,
                    TargetPosX = targetPos.x, TargetPosY = targetPos.y, TargetPosZ = targetPos.z
                }.Serialize(w), DeliveryMethod.ReliableOrdered);

                ModRuntime.Log?.LogInfo($"[DamageRedirect] sent PlayerAttack: target={__instance.name} id={stableId} dmg={(int)Damage} synced={isSynced}");

                // Projectile weapons: always block local getHit (host is authoritative)
                if (isProjectileDamage)
                    return false;

                return !isSynced;
            }
            catch (System.Exception ex)
            {
                ModRuntime.Log?.LogInfo($"[DamageRedirect] EXCEPTION in Prefix: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return true;
            }
        }
    }
}
