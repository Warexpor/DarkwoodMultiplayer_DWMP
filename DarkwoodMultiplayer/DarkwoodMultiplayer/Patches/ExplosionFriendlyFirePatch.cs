using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// After any explosion runs on the host, checks if the remote proxy is within
    /// the blast radius and relays the damage to the client.
    /// Covers explosions that happen naturally on the host (not triggered by client).
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "explode")]
    public static class ExplosionFriendlyFirePatch
    {
        private static void Postfix(Explodes __instance)
        {
            var net = ModRuntime.Network as LanNetworkManager;
            if (net == null || net.Role != NetworkRole.Host) return;
            if (__instance == null) return;
            if (!__instance.affectsPlayer) return;
            try { if (__instance.transform == null) return; } catch { return; }

            var proxy = net.RemoteProxy;
            if (proxy == null) return;

            Transform proxyT = proxy.transform;
            float dist = Vector3.Distance(__instance.transform.position, proxyT.position);
            if (dist > __instance.radius) return;

            if (!Core.canSee(__instance.transform, proxyT)) return;

            float falloff = (__instance.radius - dist) / __instance.radius;
            if (falloff <= 0f) return;

            int damage = Mathf.Max(1, Mathf.RoundToInt(__instance.damage * falloff));
            Vector3 pos = proxyT.position;
            net.SendDamagePlayer(new DamagePlayerMessage
            {
                Damage = damage,
                AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z,
                ShowRedScreen = true
            });
            ModRuntime.Log?.LogInfo("[ExplosionFF] host explosion at " + __instance.transform.position
                + " dealt " + damage + " damage to proxy at " + pos);
        }
    }
}
