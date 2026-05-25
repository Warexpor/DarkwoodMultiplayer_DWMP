using DarkwoodMultiplayer.Audio;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
    public static class AudioSuppressionPatch
    {
        private static bool Prefix(object[] __args, ref AudioObject __result)
        {
            // _PlayAsSound(string audioID, float volume, Vector3 worldPosition, Transform parentObj, ...)
            return AudioSuppressionLogic.SuppressFarAudio(__args, ref __result);
        }
    }

    [HarmonyPatch(typeof(AudioController), "_PlayAsMusicOrAmbienceSound")]
    public static class AudioAmbienceSuppressionPatch
    {
        private static bool Prefix(object[] __args, ref AudioObject __result)
        {
            // _PlayAsMusicOrAmbienceSound(string audioID, float volume, Vector3 worldPosition, Transform parentObj, ...)
            return AudioSuppressionLogic.SuppressFarAudio(__args, ref __result);
        }
    }

    internal static class AudioSuppressionLogic
    {
        internal static bool SuppressFarAudio(object[] __args, ref AudioObject __result)
        {
            Vector3 worldPosition = __args.Length > 2 ? (Vector3)__args[2] : Vector3.zero;
            Transform parentObj = __args.Length > 3 ? (Transform)__args[3] : null;

            Vector3 pos = worldPosition;
            if (pos == Vector3.zero && parentObj != null)
                pos = parentObj.position;

            Player local = Player.Instance;
            if (local == null)
                return true;

            if (pos == Vector3.zero)
                return true;

            float dist = Vector3.Distance(local.transform.position, pos);
            if (dist <= LocalAudioService.DefaultMaxAudioDistance)
                return true;

            __result = null;
            return false;
        }
    }
}
