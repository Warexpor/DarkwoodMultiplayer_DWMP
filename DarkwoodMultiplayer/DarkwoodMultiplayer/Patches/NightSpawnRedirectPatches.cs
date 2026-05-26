using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Redirects Forest Spirit to also spawn around the remote proxy when
    /// it is far from the host, so the client experiences these night events
    /// near their position.
    ///
    /// Forest Spirit spawn: spawnForestSpirit() is a one-shot IEnumerator,
    /// so we Prefix-skip+replace when the proxy is far.
    ///
    /// NightWorm: waitToSpawnWorm() is a forever-looping IEnumerator, so we
    /// use a Postfix on Core.AddPrefab to reposition the worm after spawn.
    /// </summary>

    // ─── Forest Spirit redirect ────────────────────────────────────────

    [HarmonyPatch(typeof(CharacterSpawner), "spawnForestSpirit")]
    public static class ForestSpiritRedirectPatch
    {
        private static bool Prefix(CharacterSpawner __instance)
        {
            if (!ShouldRedirect())
                return true;

            Transform proxyT = LanNetworkManager.Instance?.RemoteProxy?.transform;
            if (proxyT == null) return true;

            Vector3 vector = Random.onUnitSphere * 300f;
            vector.y = 0f;
            Vector3 destPosition = proxyT.position + vector;

            Core.AddPooledPrefab("FX", "ForestSpirit_fastSpawnEff", destPosition, Quaternion.identity);
            __instance.StartCoroutine(DelayedSpawnForestSpirit(destPosition));

            return false;
        }

        private static System.Collections.IEnumerator DelayedSpawnForestSpirit(Vector3 pos)
        {
            yield return new WaitForSeconds(Random.Range(7f, 9f));
            Core.AddPrefab("Characters/ForestSpirit2", pos, Quaternion.Euler(90f, 0f, 0f), null);
        }

        private static bool ShouldRedirect()
        {
            if (ModRuntime.Network?.Role != NetworkRole.Host) return false;
            if (!PlayerPositionManager.HasRemotePlayer) return false;

            Transform proxyT = LanNetworkManager.Instance?.RemoteProxy?.transform;
            if (proxyT == null) return false;

            float distToHost = Vector3.Distance(proxyT.position, Player.Instance.transform.position);
            if (distToHost < 1000f) return false;

            return Random.value < 0.5f;
        }
    }

    // ─── spawnCharacterAround redirect (covers spawnRedneck, etc.) ────

    [HarmonyPatch(typeof(CharacterSpawner), "spawnCharacterAround")]
    public static class SpawnCharacterAroundRedirectPatch
    {
        private static void Prefix(ref GameObject destGO, float distance)
        {
            if (ModRuntime.Network?.Role != NetworkRole.Host) return;
            if (NightSpawnGetFreeSpotPatch.InsideNightSpawn) return; // already handled by NightSpawnProxyPatch
            if (!PlayerPositionManager.HasRemotePlayer) return;

            Transform proxyT = LanNetworkManager.Instance?.RemoteProxy?.transform;
            if (proxyT == null) return;

            if (destGO != Player.Instance.gameObject) return;

            float distToHost = Vector3.Distance(proxyT.position, Player.Instance.transform.position);
            if (distToHost < 1000f) return;

            if (Random.value < 0.5f)
                destGO = proxyT.gameObject;
        }
    }

    // ─── NightWorm redirect (post-spawn reposition) ───────────────────

    [HarmonyPatch(typeof(Core), "AddPrefab", new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    public static class NightWormPostSpawnPatch
    {
        private static void Postfix(GameObject __result, string prefab)
        {
            if (__result == null || prefab != "characters/fakechars/NightWorms_01")
                return;
            if (ModRuntime.Network?.Role != NetworkRole.Host)
                return;
            if (!PlayerPositionManager.HasRemotePlayer)
                return;

            Transform proxyT = LanNetworkManager.Instance?.RemoteProxy?.transform;
            if (proxyT == null) return;

            float distToHost = Vector3.Distance(proxyT.position, Player.Instance.transform.position);
            if (distToHost < 1000f) return;
            if (Random.value > 0.5f) return;

            // Move spawned worm to be near the proxy so the client sees it
            Vector3 newPos = Core.randomPosAround(proxyT.position, 1500f, 2000f, canBeInside: true, mustBeInsideGraph: false);
            __result.transform.position = newPos;

            ModRuntime.Log?.LogInfo($"[NightWormRedirect] moved worm to proxy area ({newPos.x:F0},{newPos.z:F0})");
        }
    }
}
