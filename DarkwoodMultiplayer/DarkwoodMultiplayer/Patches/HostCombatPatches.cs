using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Intercepts MeleeSensor.OnTriggerEnter on the host when the hit
    /// target is the remote proxy. Sends a DamagePlayerMessage to the
    /// client and drains host weapon durability. Skips vanilla hit logic
    /// since the proxy is not a real Player and would be ignored.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter", new[] { typeof(Collider) })]
    public static class HostMeleeSensorPatch
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return true;

            RemotePlayerProxy proxy = _collider.GetComponentInParent<RemotePlayerProxy>();
            if (proxy == null)
                return true;

            // Don't damage client if proxy's CharBase is dead (e.g. after client died
            // and before respawn). The proxy is immortal during normal play but set to
            // dead explicitly by HandlePlayerDied on the host.
            CharBase proxyCB = proxy.GetComponent<CharBase>();
            if (proxyCB == null || !proxyCB.alive)
                return true;

            bool isPlayer = __instance.type == MeleeSensor.MeleeSensorType.player;

            // Drain weapon durability if it's the host player attacking
            if (isPlayer && Player.Instance != null && Player.Instance.currentItem != null)
            {
                Player.Instance.currentItem.drainDurability(__instance.itemDurabilityDrain);
            }

            float strengthMod = 1f;
            if (__instance.attackerTransform != null)
            {
                CharBase atkCB = __instance.attackerTransform.GetComponent<CharBase>();
                if (atkCB != null)
                    strengthMod = atkCB.strengthModifier;
            }
            int dmg = Mathf.Max(1, (int)((float)__instance.damage * strengthMod));
            Vector3 atkPos = __instance.attackerTransform != null
                ? __instance.attackerTransform.position
                : proxy.transform.position;

            var msg = new DamagePlayerMessage
            {
                Damage = dmg,
                AttackerPosX = atkPos.x,
                AttackerPosY = atkPos.y,
                AttackerPosZ = atkPos.z,
                CanCutInHalf = dmg >= 80,
                ShowRedScreen = true
            };
            LanNetworkManager.Instance?.Send(NetMessageType.DamagePlayer, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);

            return false;
        }
    }
}
