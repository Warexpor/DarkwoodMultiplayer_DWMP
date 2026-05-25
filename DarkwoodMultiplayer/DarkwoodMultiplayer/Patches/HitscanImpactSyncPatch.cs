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

                    float yRot = player.transform.eulerAngles.y;
                    Core.AddPrefab("FX/Bloodsplats/Shotsplat", hitPoint,
                        Quaternion.Euler(90f, yRot + Random.Range(-20f, 20f), 0f), null);

                    ModRuntime.Log?.LogInfo("[HitscanFX] host hit proxy, dmg=" + dmg);
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

                    ModRuntime.Log?.LogInfo("[HitscanFX] client hit proxy, dmg=" + dmg);
                }
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
