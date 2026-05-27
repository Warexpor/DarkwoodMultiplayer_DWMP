using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(Physics), "Raycast", typeof(Vector3), typeof(Vector3), typeof(RaycastHit), typeof(float), typeof(int))]
    public static class HitscanImpactSyncPatch
    {
        private static void Postfix(bool __result, RaycastHit hitInfo, int layerMask)
        {
            if (!__result) return;
            if (layerMask != 18909185) return;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            Player player = Player.Instance;
            if (player == null) return;
            if (InvItemClass.isNull(player.currentItem) || player.currentItem.baseClass.item != null)
                return;

            Collider collider = hitInfo.collider;
            if (collider == null) return;

            Vector3 hitPoint = hitInfo.point;

            RemotePlayerProxy proxy = collider.GetComponentInParent<RemotePlayerProxy>();
            if (proxy != null)
            {
                int dmg = Mathf.Max(1, player.currentItem?.baseClass?.damage ?? 10);
                Vector3 proxyPos = proxy.transform.position;
                CharBase proxyCB = proxy.GetComponent<CharBase>();
                bool inWater = proxyCB != null && proxyCB.inWater;

                if (net.Role == NetworkRole.Host)
                {
                    net.Send(NetMessageType.DamagePlayer, w =>
                    {
                        new DamagePlayerMessage
                        {
                            Damage = dmg,
                            AttackerPosX = proxyPos.x, AttackerPosY = proxyPos.y, AttackerPosZ = proxyPos.z,
                            ShowRedScreen = true
                        }.Serialize(w);
                    }, DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    net.Send(NetMessageType.FriendlyFire, w =>
                    {
                        new FriendlyFireMessage
                        {
                            Damage = dmg,
                            AttackerPosX = proxyPos.x, AttackerPosY = proxyPos.y, AttackerPosZ = proxyPos.z
                        }.Serialize(w);
                    }, DeliveryMethod.ReliableOrdered);
                }

                float yRot = player.transform.eulerAngles.y;
                string bloodPrefab = inWater ? "FX/Bloodsplats/Shotsplat" : "FX/Bloodsplats/Shotsplat_stay";
                TraverseHack.ApplyingFromNetwork = true;
                try
                {
                    Core.AddPrefab(bloodPrefab, hitPoint,
                        Quaternion.Euler(90f, yRot + Random.Range(-20f, 20f), 0f), null);
                }
                finally { TraverseHack.ApplyingFromNetwork = false; }

                ModRuntime.Log?.LogInfo($"[HitscanFX] {(net.Role == NetworkRole.Host ? "host" : "client")} hit proxy, dmg=" + dmg);

                // Send BulletImpact so the other peer sees the same blood
                net.Send(NetMessageType.BulletImpact, w => new BulletImpactMessage
                {
                    PrefabName = bloodPrefab,
                    PoolName = "",
                    PosX = hitPoint.x, PosY = hitPoint.y, PosZ = hitPoint.z,
                    RotX = 90f,
                    RotY = yRot + Random.Range(-40f, 40f),
                    RotZ = 0f
                }.Serialize(w), DeliveryMethod.ReliableOrdered);

                return;
            }

            string prefabName = "";
            string poolName = "";
            Quaternion fxRot = Quaternion.identity;
            int layer = collider.gameObject.layer;

            if (layer == 0 || layer == 15)
            {
                prefabName = "bullet_hit_1";
                poolName = "FX";
                float rotY = Random.Range(0f, 360f);
                fxRot = Quaternion.Euler(90f, rotY, 0f);
            }
            else if (layer == 11 || layer == 21)
            {
                prefabName = "Shotsplat1";
                poolName = "FX";
                float yRot = player.transform.eulerAngles.y;
                fxRot = Quaternion.Euler(90f, yRot + Random.Range(-40f, 40f), 0f);
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
