using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    public static class DoorTracker
    {
        private static readonly List<Door> _doors = new List<Door>(64);
        private static float _lastCleanupTime;
        private const float CleanupInterval = 30f;

        public static IList<Door> GetAll() => _doors;

        public static void Add(Door d)
        {
            if (d == null) return;
            if (!_doors.Contains(d))
                _doors.Add(d);
        }

        public static Door FindByPosition(Vector3 pos, float maxDist = 0.5f)
        {
            for (int i = 0; i < _doors.Count; i++)
            {
                Door d = _doors[i];
                if (d == null) continue;
                if (Vector3.Distance(d.transform.position, pos) < maxDist)
                    return d;
            }
            return null;
        }

        public static void Cleanup()
        {
            float now = Time.time;
            if (now - _lastCleanupTime < CleanupInterval)
                return;
            _lastCleanupTime = now;
            _doors.RemoveAll(d => d == null);
        }

        public static void Clear() { _doors.Clear(); }
    }

    public static class GeneratorTracker
    {
        private static readonly List<Generator> _generators = new List<Generator>(16);

        public static IList<Generator> GetAll() => _generators;

        public static void Add(Generator g)
        {
            if (g == null) return;
            if (!_generators.Contains(g))
                _generators.Add(g);
        }

        public static void Remove(Generator g)
        {
            if (g == null) return;
            _generators.Remove(g);
        }

        public static Generator FindByPosition(Vector3 pos, float maxDist = 0.5f)
        {
            for (int i = 0; i < _generators.Count; i++)
            {
                Generator g = _generators[i];
                if (g == null) continue;
                if (Vector3.Distance(g.transform.position, pos) < maxDist)
                    return g;
            }
            return null;
        }

        public static void Clear() { _generators.Clear(); }
    }

    [HarmonyPatch(typeof(Door), "Awake")]
    public static class DoorAwakePatch
    {
        private static void Postfix(Door __instance)
        {
            DoorTracker.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Generator), "Start")]
    public static class GeneratorStartPatch
    {
        private static void Postfix(Generator __instance)
        {
            GeneratorTracker.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Generator), "OnDestroy")]
    public static class GeneratorDestroyPatch
    {
        private static void Prefix(Generator __instance)
        {
            GeneratorTracker.Remove(__instance);
        }
    }
}
