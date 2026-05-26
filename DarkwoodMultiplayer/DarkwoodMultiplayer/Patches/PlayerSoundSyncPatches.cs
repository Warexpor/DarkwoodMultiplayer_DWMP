using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    internal static class PlayerAudioHelper
    {
        internal static void ForwardSound(string audioID, float volume, Vector3 position)
        {
            if (string.IsNullOrEmpty(audioID)) return;
            var net = ModRuntime.Network;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            // Skip footsteps — already handled by proxy leg animation events
            if (audioID.IndexOf("foot", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
            if (audioID.IndexOf("walk_clothes", System.StringComparison.OrdinalIgnoreCase) >= 0) return;

            // Skip personal status sounds that should only play on the local player
            if (audioID.IndexOf("player_low_health", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
            if (audioID.IndexOf("player_tired", System.StringComparison.OrdinalIgnoreCase) >= 0) return;

            net.SendPlayerAudio(new PlayerAudioMessage
            {
                SoundId = audioID,
                Volume = Mathf.Clamp01(volume),
                PosX = position.x, PosY = position.y, PosZ = position.z
            });
        }

        internal static bool IsPlayerTransform(Transform t)
        {
            if (t == null) return false;
            Player p = Player.Instance;
            return p != null && t == p.transform;
        }

        internal static bool IsEnemyTransform(Transform t)
        {
            if (t == null) return false;
            Player p = Player.Instance;
            if (p != null && t == p.transform) return false;
            return t.GetComponent<Character>() != null;
        }

        internal static bool IsNearPlayer(Vector3 pos)
        {
            Player p = Player.Instance;
            return p != null && Vector3.Distance(pos, p.transform.position) < 10f;
        }
    }

    /// <summary>Play(string audioID, Transform parentObj)</summary>
    [HarmonyPatch(typeof(AudioController), "Play", typeof(string), typeof(Transform))]
    public static class AudioPlayStrTrans
    {
        [HarmonyPrefix]
        private static void Prefix(string audioID, Transform parentObj)
        {
            if (parentObj == null) return;

            if (PlayerAudioHelper.IsPlayerTransform(parentObj))
            {
                Vector3 pos = parentObj.position;
                PlayerAudioHelper.ForwardSound(audioID, 1f, pos);
                return;
            }

            // Enemy sounds: only forward from host (authoritative AI) to client.
            // Skip if InsideCharacterSounds — EntitySoundSyncPatches already handles
            // CharacterSounds-routed sounds and would cause a double-play.
            if (!TraverseHack.InsideCharacterSounds
                && ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Host
                && PlayerAudioHelper.IsEnemyTransform(parentObj))
            {
                Vector3 pos = parentObj.position;
                if (PlayerAudioHelper.IsNearPlayer(pos))
                    PlayerAudioHelper.ForwardSound(audioID, 1f, pos);
            }
        }
    }

    /// <summary>Play(string audioID, Transform parentObj, float volume, float delay, float startTime)</summary>
    [HarmonyPatch(typeof(AudioController), "Play", typeof(string), typeof(Transform), typeof(float), typeof(float), typeof(float))]
    public static class AudioPlayStrTransFloatFloatFloat
    {
        [HarmonyPrefix]
        private static void Prefix(string audioID, Transform parentObj, float volume)
        {
            if (parentObj == null) return;

            if (PlayerAudioHelper.IsPlayerTransform(parentObj))
            {
                Vector3 pos = parentObj.position;
                PlayerAudioHelper.ForwardSound(audioID, volume, pos);
                return;
            }

            if (!TraverseHack.InsideCharacterSounds
                && ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Host
                && PlayerAudioHelper.IsEnemyTransform(parentObj))
            {
                Vector3 pos = parentObj.position;
                if (PlayerAudioHelper.IsNearPlayer(pos))
                    PlayerAudioHelper.ForwardSound(audioID, volume, pos);
            }
        }
    }

    /// <summary>Play(string audioID, Vector3 worldPosition, Transform parentObj = null)</summary>
    [HarmonyPatch(typeof(AudioController), "Play", typeof(string), typeof(Vector3), typeof(Transform))]
    public static class AudioPlayStrVecTrans
    {
        [HarmonyPrefix]
        private static void Prefix(string audioID, Vector3 worldPosition, Transform parentObj)
        {
            Vector3 pos = worldPosition;

            if (parentObj != null)
            {
                if (PlayerAudioHelper.IsPlayerTransform(parentObj))
                {
                    pos = parentObj.position;
                }
                else if (!TraverseHack.InsideCharacterSounds
                    && ModRuntime.Network != null && ModRuntime.Network.Role == NetworkRole.Host
                    && PlayerAudioHelper.IsEnemyTransform(parentObj))
                {
                    pos = parentObj.position;
                    if (!PlayerAudioHelper.IsNearPlayer(pos)) return;
                }
                else
                {
                    return;
                }
            }
            else
            {
                if (!PlayerAudioHelper.IsNearPlayer(worldPosition)) return;
            }

            PlayerAudioHelper.ForwardSound(audioID, 1f, pos);
        }
    }
}
