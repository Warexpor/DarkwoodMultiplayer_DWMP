using DarkwoodMultiplayer.Networking;
using UnityEngine;

namespace DarkwoodMultiplayer.Sync
{
    internal static class DreamAudioPlayer
    {
        private static AudioSource[] _sources;
        private static int _nextSource;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            var go = new GameObject("DreamAudioPlayer");
            Object.DontDestroyOnLoad(go);
            _sources = new AudioSource[12];
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = go.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
                _sources[i].spatialBlend = 1f;
                _sources[i].rolloffMode = AudioRolloffMode.Linear;
                _sources[i].minDistance = 30f;
                _sources[i].maxDistance = 500f;
            }
            _initialized = true;
            ModRuntime.Log?.LogInfo("[DreamAudioPlayer] Initialized with 12 sources");
        }

        public static void PlayForwardedAudio(DreamAudioMessage msg)
        {
            Initialize();

            if (string.IsNullOrEmpty(msg.AudioID)) return;

            AudioClip clip = ResolveClip(msg.AudioID);
            if (clip == null)
            {
                ModRuntime.Log?.LogWarning($"[DreamAudioPlayer] Could not resolve clip for: {msg.AudioID}");
                return;
            }

            var source = _sources[_nextSource % _sources.Length];
            _nextSource++;

            source.transform.position = new Vector3(msg.PosX, msg.PosY, msg.PosZ);
            source.volume = msg.Volume;
            source.pitch = msg.Pitch;
            source.spatialBlend = 1f;
            source.PlayOneShot(clip, 1f);
        }

        private static AudioClip ResolveClip(string audioID, int depth = 0)
        {
            if (depth > 5 || string.IsNullOrEmpty(audioID)) return null;

            AudioItem item = AudioController.GetAudioItem(audioID);
            if (item == null || item.subItems == null || item.subItems.Length == 0)
                return null;

            foreach (var sub in item.subItems)
            {
                if (sub == null) continue;

                if (sub.SubItemType == AudioSubItemType.Clip && sub.Clip != null)
                    return sub.Clip;

                if (sub.SubItemType == AudioSubItemType.Item && !string.IsNullOrEmpty(sub.ItemModeAudioID))
                {
                    AudioClip clip = ResolveClip(sub.ItemModeAudioID, depth + 1);
                    if (clip != null) return clip;
                }
            }

            return null;
        }

        public static void Cleanup()
        {
            if (!_initialized) return;
            if (_sources != null && _sources.Length > 0)
            {
                var go = _sources[0].gameObject;
                if (go != null)
                    Object.Destroy(go);
            }
            _sources = null;
            _initialized = false;
            _nextSource = 0;
        }
    }
}
