using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Fires immediately when the local player's torso or legs animator begins playing a new clip.
    /// Sends the clip name to the remote peer so the proxy mirrors the exact animation frame-accurate,
    /// catching transient clips (hit, dodge, attack, item switch, reload, etc.) that the 30 Hz
    /// PlayerState snapshot might miss.
    /// </summary>
    [HarmonyPatch(typeof(tk2dSpriteAnimator), "Play", typeof(string))]
    public static class PlayerAnimationTriggerPatch
    {
        private static void Prefix(tk2dSpriteAnimator __instance, string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var net = ModRuntime.Network;
            if (net == null || net.Role == NetworkRole.Offline) return;
            if (TraverseHack.ApplyingFromNetwork) return;

            Player local = Player.Instance;
            if (local == null) return;

            // Only intercept the local player's own animators
            tk2dSpriteAnimator torsoAnim = local.torsoAnimator;
            Transform legsT = local.transform.Find("PlayerLegs");
            tk2dSpriteAnimator legsAnim = legsT != null ? legsT.GetComponent<tk2dSpriteAnimator>() : null;

            bool isTorso = __instance == torsoAnim;
            bool isLegs = legsAnim != null && __instance == legsAnim;
            if (!isTorso && !isLegs) return;

            // Skip if the animator is already actively playing the requested clip.
            // doAnims() calls Play() every frame — this filters out redundant calls,
            // but allows re-sending when a non-looping clip finishes and restarts
            // (e.g. double barrel reload loop cycles).
            if (__instance.Playing && __instance.CurrentClip?.name == name)
                return;

            net.SendPlayerAnimation(new PlayerAnimationMessage
            {
                TorsoClip = isTorso ? name : (torsoAnim != null ? torsoAnim.CurrentClip?.name : ""),
                LegsClip = isLegs ? name : (legsAnim != null ? legsAnim.CurrentClip?.name : "")
            });
        }
    }
}
