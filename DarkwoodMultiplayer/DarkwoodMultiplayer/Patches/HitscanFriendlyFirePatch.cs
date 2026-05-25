using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Player), "spawnBullet")]
    public static class HitscanFriendlyFirePatch
    {
        private static void Prefix(Player __instance, float aim)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Host) return;
            if (TraverseHack.ApplyingFromNetwork) return;
            if (InvItemClass.isNull(__instance.currentItem) || !__instance.currentItem.baseClass.isFirearm) return;
            if (__instance.currentItem.baseClass.item != null) return;

            RemotePlayerProxy proxy = RemotePlayerProxy.Instance;
            if (proxy == null) return;

            var savedState = Random.state;
            float num = Random.Range(0f - aim, aim);
            Random.state = savedState;

            Vector3 origin = __instance.transform.position + __instance.transform.up;
            Vector3 dir = Quaternion.Euler(0f, num, 0f) * __instance.transform.up;

            if (!Physics.Raycast(origin, dir, out RaycastHit hit, 1000f, 18909185) || hit.collider == null)
                return;

            if (hit.collider.GetComponentInParent<RemotePlayerProxy>() == null)
                return;

            int dmg = Mathf.Max(1, (int)__instance.currentItem.baseClass.damage);
            Vector3 pos = proxy.transform.position;

            net.Send(NetMessageType.DamagePlayer, w =>
            {
                new DamagePlayerMessage
                {
                    Damage = dmg,
                    AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z,
                    ShowRedScreen = true
                }.Serialize(w);
            }, DeliveryMethod.ReliableOrdered);

            ModRuntime.Log?.LogInfo("[HitscanFF] host hitscan hit proxy → sent " + dmg + " to client");

            // Check if target (proxy/client player) is in water
            CharBase proxyCharBase = proxy.GetComponent<CharBase>();
            bool targetInWater = proxyCharBase != null && proxyCharBase.inWater;

            // Spawn blood locally at hit point (matching spawnBullet behavior)
            float yRot = __instance.transform.eulerAngles.y;
            TraverseHack.ApplyingFromNetwork = true;
            try
            {
                string bloodPrefab = targetInWater ? "FX/Bloodsplats/Shotsplat" : "FX/Bloodsplats/Shotsplat_stay";
                Core.AddPrefab(bloodPrefab, hit.point,
                    Quaternion.Euler(90f, yRot + Random.Range(-20f, 20f), 0f), null);
                ModRuntime.Log?.LogInfo("[HitscanFF] spawned blood at " + hit.point);
            }
            finally { TraverseHack.ApplyingFromNetwork = false; }

            // Send BulletImpact to client with exact hit point so blood matches
            net.Send(NetMessageType.BulletImpact, w => new BulletImpactMessage
            {
                PrefabName = targetInWater ? "FX/Bloodsplats/Shotsplat" : "FX/Bloodsplats/Shotsplat_stay",
                PoolName = "",
                PosX = hit.point.x, PosY = hit.point.y, PosZ = hit.point.z,
                RotX = 90f,
                RotY = proxy.transform.eulerAngles.y + Random.Range(-40f, 40f),
                RotZ = 0f
            }.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }
}
