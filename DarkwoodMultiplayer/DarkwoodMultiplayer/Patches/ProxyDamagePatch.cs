using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(CharBase), "getHit",
        typeof(float), typeof(Transform),
        typeof(bool), typeof(bool), typeof(bool),
        typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
    public static class ProxyDamagePatch
    {
        private static bool Prefix(CharBase __instance, float damage, Transform attackerTransform,
            bool CanCutInHalf, bool byPlayer, bool canInterrupt)
        {
            RemotePlayerProxy proxy = __instance.GetComponent<RemotePlayerProxy>();
            if (proxy == null) return true;

            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role == NetworkRole.Offline) return true;

            Vector3 pos = proxy.transform.position;
            int dmg = Mathf.Max(1, (int)damage);

            if (net.Role == NetworkRole.Host)
            {
                net.Send(NetMessageType.DamagePlayer, w =>
                {
                    new DamagePlayerMessage
                    {
                        Damage = dmg,
                        AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z,
                        ShowRedScreen = true
                    }.Serialize(w);
                }, DeliveryMethod.ReliableOrdered);

                ModRuntime.Log?.LogInfo("[ProxyDmg] host proxy took " + dmg + " damage — sent to client");
            }
            else
            {
                net.Send(NetMessageType.FriendlyFire, w =>
                {
                    new FriendlyFireMessage
                    {
                        Damage = dmg,
                        AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z,
                        CanCutInHalf = CanCutInHalf
                    }.Serialize(w);
                }, DeliveryMethod.ReliableOrdered);

                ModRuntime.Log?.LogInfo("[ProxyDmg] client proxy took " + dmg + " damage — sent to host");
            }

            return false;
        }
    }
}
