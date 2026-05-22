using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    public static class CharacterTracker
    {
        private static readonly List<Character> _characters = new List<Character>(64);
        private static readonly object _lock = new object();

        public static Character[] GetAll()
        {
            lock (_lock)
            {
                return _characters.ToArray();
            }
        }

        public static int Count
        {
            get { lock (_lock) { return _characters.Count; } }
        }

        public static void Add(Character c)
        {
            if (c == null) return;
            lock (_lock)
            {
                if (!_characters.Contains(c))
                    _characters.Add(c);
            }
        }

        public static void Remove(Character c)
        {
            if (c == null) return;
            lock (_lock)
            {
                _characters.Remove(c);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _characters.Clear();
            }
        }
    }

    [HarmonyPatch(typeof(Character), "Start")]
    public static class CharacterStartPatch
    {
        private static void Postfix(Character __instance)
        {
            CharacterTracker.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "OnDestroy")]
    public static class CharacterDestroyPatch
    {
        private static void Prefix(Character __instance)
        {
            CharacterTracker.Remove(__instance);
        }
    }
}