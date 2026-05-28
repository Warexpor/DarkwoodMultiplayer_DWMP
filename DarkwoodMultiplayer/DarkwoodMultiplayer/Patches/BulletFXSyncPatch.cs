using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Core), "AddPooledPrefab", typeof(string), typeof(string), typeof(Vector3), typeof(Quaternion))]
    public static class HitscanImpactForwardPatch
    {
        private static void Prefix(string pool, string prefab, Vector3 position, Quaternion quaternion)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            if (pool != "FX") return;
            if (prefab != "bullet_hit_1" && prefab != "Shotsplat1") return;

            Player player = Player.Instance;
            if (player == null) return;
            if (InvItemClass.isNull(player.currentItem) || !player.currentItem.baseClass.isFirearm) return;
            if (player.currentItem.baseClass.item != null) return;

            net.SendBulletImpact(new BulletImpactMessage
            {
                PrefabName = prefab,
                PoolName = pool,
                PosX = position.x, PosY = position.y, PosZ = position.z,
                RotX = quaternion.eulerAngles.x,
                RotY = quaternion.eulerAngles.y,
                RotZ = quaternion.eulerAngles.z
            });
        }
    }

    [HarmonyPatch(typeof(Core), "AddPrefab", typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool))]
    public static class HitscanBloodPatch
    {
        private static void Prefix(string prefab, Vector3 position, Quaternion quaternion)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            if (prefab == null) return;
            if (!prefab.StartsWith("FX/Bloodsplats/")) return;

            // Forward all blood splatters — from entity hits, player damage,
            // friendly fire, anything. The ApplyingFromNetwork guard above
            // prevents re-forwarding when the other peer receives the message.
            net.SendBulletImpact(new BulletImpactMessage
            {
                PrefabName = prefab,
                PoolName = "",
                PosX = position.x, PosY = position.y, PosZ = position.z,
                RotX = quaternion.eulerAngles.x,
                RotY = quaternion.eulerAngles.y,
                RotZ = quaternion.eulerAngles.z
            });
        }
    }

    /// <summary>
    /// Forwards projectile weapon impact FX. These bullets have a Bullet component
    /// and a FastProjectile component; on collision, Bullet.onCollide is called.
    /// </summary>
    [HarmonyPatch(typeof(Bullet), "onCollide", typeof(Collider), typeof(Vector3))]
    public static class BulletFXSyncPatch
    {
        private static void Prefix(Bullet __instance, Collider collider, Vector3 hitPoint)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role == NetworkRole.Offline) return;

            if (__instance.objectThatSpawnedMe != null) return;

            int layer = collider.gameObject.layer;

            RemotePlayerProxy proxy = collider.GetComponentInParent<RemotePlayerProxy>();
            if (proxy != null)
            {
                float y = __instance.transform.eulerAngles.y;
                Quaternion rot = Quaternion.Euler(90f, y + Random.Range(-40f, 40f), 0f);
                Core.AddPooledPrefab("FX", "Shotsplat1", hitPoint, rot);
                return;
            }

            string prefabName = "";
            string poolName = "";
            Quaternion fxRot = Quaternion.identity;

            bool isWall = layer == 0 || layer == 15;
            bool isChar = layer == 11 || layer == 21;

            if (isWall)
            {
                prefabName = "bullet_hit_1";
                poolName = "FX";
                fxRot = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            }
            else if (isChar)
            {
                prefabName = "Shotsplat1";
                poolName = "FX";
                fxRot = Quaternion.Euler(90f, __instance.transform.eulerAngles.y + Random.Range(-40f, 40f), 0f);
            }
            else
            {
                return;
            }

            net.SendBulletImpact(new BulletImpactMessage
            {
                PrefabName = prefabName,
                PoolName = poolName,
                PosX = hitPoint.x, PosY = hitPoint.y, PosZ = hitPoint.z,
                RotX = fxRot.eulerAngles.x,
                RotY = fxRot.eulerAngles.y,
                RotZ = fxRot.eulerAngles.z
            });
        }
    }
}
