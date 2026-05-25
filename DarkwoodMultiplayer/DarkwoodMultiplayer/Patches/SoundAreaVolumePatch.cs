using DarkwoodMultiplayer.Audio;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    [HarmonyPatch(typeof(SoundArea), "Update")]
    public static class SoundAreaUpdatePatch
    {
        static void Postfix(SoundArea __instance)
        {
            if (__instance.soundAO == null) return;
            if (!__instance.onlyOneInstance) return;

            Transform src = __instance.source;
            if (src == null) src = __instance.transform;

            float dist = Vector3.Distance(Player.Instance._transform.position, src.position);
            float maxDist = __instance.maxSourceDistance > 0f
                ? __instance.maxSourceDistance
                : LocalAudioService.DefaultMaxAudioDistance;
            float minDist = __instance.minSourceDistance;

            float targetVol;
            if (dist > maxDist)
                targetVol = 0.001f;
            else if (dist < minDist)
                targetVol = __instance.volumeModifier;
            else
            {
                float t = (dist - minDist) / (maxDist - minDist);
                targetVol = Mathf.Max(Mathf.Lerp(__instance.volumeModifier, 0.001f, t), 0.001f);
            }

            float currentVol = __instance.soundAO.volume;
            if (Mathf.Abs(currentVol - targetVol) > 0.001f)
            {
                __instance.soundAO.volume = targetVol;
                __instance.soundAO.thisFrameVolume = targetVol;
            }
        }
    }

    [HarmonyPatch(typeof(SoundArea), "LateUpdate")]
    public static class SoundAreaLateUpdatePatch
    {
        static void Postfix(SoundArea __instance)
        {
            if (!__instance.onlyOneInstance) return;
            if (__instance.soundAO == null) return;
            __instance.soundAO.thisFrameVolume = 0f;
        }
    }
}
