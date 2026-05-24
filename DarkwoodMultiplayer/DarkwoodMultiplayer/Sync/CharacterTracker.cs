using System.Collections.Generic;
using DarkwoodMultiplayer.Networking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    /// <summary>Assigns stable IDs to Character instances and tracks them for network sync across their lifetime.</summary>
    public static class CharacterTracker
    {
        private static readonly List<Character> _characters = new List<Character>(64);
        private static readonly Dictionary<Character, short> _stableIdCache = new Dictionary<Character, short>(64);
        private static readonly object _lock = new object();
        private static short _nextId = 1;

        /// <summary>Returns the stable network ID for a character, assigning one if not yet cached.</summary>
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

        /// <summary>Assigns a new unique stable ID to the given character.</summary>
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

        /// <summary>Assigns a specific stable ID (from the host) to the given character on a client.</summary>
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

        /// <summary>Finds a character by its stable network ID.</summary>
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

        /// <summary>Returns a snapshot array of all tracked characters.</summary>
        public static Character[] GetAll()
        {
            lock (_lock)
            {
                return _characters.ToArray();
            }
        }

        /// <summary>Gets the number of currently tracked characters.</summary>
        public static int Count
        {
            get { lock (_lock) { return _characters.Count; } }
        }

        /// <summary>Registers a character for tracking, assigning an ID if needed.</summary>
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

        /// <summary>Removes a character from tracking.</summary>
        public static void Remove(Character c)
        {
            if (c == null) return;
            lock (_lock)
            {
                _characters.Remove(c);
                _stableIdCache.Remove(c);
            }
        }

        /// <summary>Clears all tracked characters and resets the ID counter.</summary>
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

    /// <summary>Harmony patch: registers characters with the tracker on Start and forces idle animation on clients.</summary>
    [HarmonyPatch(typeof(Character), "Start")]
    public static class CharacterStartPatch
    {
        private static void Postfix(Character __instance)
        {
            CharacterTracker.Add(__instance);

            // On clients, force non-remote characters into their idle clip
            // so they don't play the default T-pose animation before a sync arrives.
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

    /// <summary>Harmony patch: deregisters characters from the tracker on destroy.</summary>
    [HarmonyPatch(typeof(Character), "OnDestroy")]
    public static class CharacterDestroyPatch
    {
        private static void Prefix(Character __instance)
        {
            CharacterTracker.Remove(__instance);
        }
    }
}
