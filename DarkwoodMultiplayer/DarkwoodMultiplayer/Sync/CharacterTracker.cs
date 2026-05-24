using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    public static class CharacterTracker
    {
        private static readonly List<Character> _characters = new List<Character>(64);
        private static readonly Dictionary<Character, short> _stableIdCache = new Dictionary<Character, short>(64);
        private static readonly object _lock = new object();
        private static short _nextId = 1;

        public static short GetStableId(Character c)
        {
            if (c == null) return 0;
            lock (_lock)
            {
                if (_stableIdCache.TryGetValue(c, out var id))
                    return id;
            }
            return AssignId(c);
        }

        public static short AssignId(Character c)
        {
            if (c == null) return 0;
            short id;
            lock (_lock)
            {
                id = _nextId++;
                _stableIdCache[c] = id;
            }
            return id;
        }

        public static void AssignId(Character c, short id)
        {
            if (c == null) return;
            lock (_lock)
            {
                _stableIdCache[c] = id;
                if (!_characters.Contains(c))
                    _characters.Add(c);
            }
        }

        public static Character FindByStableId(short id)
        {
            lock (_lock)
            {
                for (int i = 0; i < _characters.Count; i++)
                {
                    if (_characters[i] != null && _stableIdCache.TryGetValue(_characters[i], out short sid) && sid == id)
                        return _characters[i];
                }
            }
            return null;
        }

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
                {
                    _characters.Add(c);
                    if (!_stableIdCache.ContainsKey(c))
                        _stableIdCache[c] = _nextId++;
                }
            }
        }

        public static void Remove(Character c)
        {
            if (c == null) return;
            lock (_lock)
            {
                _characters.Remove(c);
                _stableIdCache.Remove(c);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _characters.Clear();
                _stableIdCache.Clear();
                _nextId = 1;
            }
        }
    }

    [HarmonyPatch(typeof(Character), "Start")]
    public static class CharacterStartPatch
    {
        private static void Postfix(Character __instance)
        {
            CharacterTracker.Add(__instance);

            if (ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Client
                && __instance.name != null && !__instance.name.Contains("RemotePlayer"))
            {
                tk2dSpriteAnimator anim = __instance.GetComponent<tk2dSpriteAnimator>();
                if (anim != null)
                {
                    string idleClip = HarmonyLib.Traverse.Create(__instance).Field("idleAni").GetValue<string>();
                    if (!string.IsNullOrEmpty(idleClip) && anim.GetClipByName(idleClip) != null)
                    {
                        if (anim.CurrentClip == null || anim.CurrentClip.name != idleClip)
                            anim.Play(idleClip);
                    }
                }
            }
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
