using DarkwoodMultiplayer.Networking;
using DarkwoodMultiplayer.Sync;
using HarmonyLib;
using LiteNetLib;

namespace DarkwoodMultiplayer.Patches
{
    /// <summary>
    /// Intercepts AI-driven CharacterSounds calls on the host and broadcasts
    /// them to the client. Covers sounds NOT already played by animation events
    /// or the existing hit/death sync.
    ///
    /// What the client plays locally without host sync:
    ///   - Footstep sounds (animation event → checkFrameTrigger)
    ///   - Attack1/Attack2 (animation event → checkFrameTrigger)
    ///   - GetHit (ClientMeleeSensorPatch → sounds.playGetHitByAxe1)
    ///   - Death (ClientEntityInterpolationService → c.die → die2)
    ///
    /// What this file syncs:
    ///   - Growl (AI behavior, not animation-tied)
    ///   - Curious (onHearSomething, AI behavior)
    ///   - Aggressive/Defensive state sounds
    ///   - Escaping (runAway/flee behavior)
    /// </summary>
    internal static class EntitySoundSyncHelper
    {
        internal static void Broadcast(CharacterSounds sounds, EntitySoundType type)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (sounds == null) return;
            Character c = sounds.character as Character;
            if (c == null) return;
            if (!CharacterTracker.TryGetStableId(c, out short hostId)) return;

            var msg = new EntitySoundMessage { HostId = hostId, SoundType = type };
            LanNetworkManager.Instance?.Send(NetMessageType.EntitySound, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }

        internal static void BroadcastIdleLoop(CharacterSounds sounds, string loopName)
        {
            if (ModRuntime.Network == null || ModRuntime.Network.Role != NetworkRole.Host)
                return;
            if (sounds == null || string.IsNullOrEmpty(loopName)) return;
            Character c = sounds.character as Character;
            if (c == null) return;
            if (!CharacterTracker.TryGetStableId(c, out short hostId)) return;

            var msg = new EntitySoundMessage { HostId = hostId, SoundType = EntitySoundType.Idle, LoopName = loopName };
            LanNetworkManager.Instance?.Send(NetMessageType.EntitySound, w => msg.Serialize(w), DeliveryMethod.ReliableOrdered);
        }
    }

    [HarmonyPatch(typeof(CharacterSounds), "playIdleLoop")]
    public static class HostIdleLoopPatch
    {
        [HarmonyPrefix]
        private static void Prefix() { TraverseHack.InsideCharacterSounds = true; }

        [HarmonyPostfix]
        private static void Postfix(CharacterSounds __instance, string loopName)
        {
            TraverseHack.InsideCharacterSounds = false;
            if (string.IsNullOrEmpty(loopName)) return;
            EntitySoundSyncHelper.BroadcastIdleLoop(__instance, loopName);
        }
    }

    [HarmonyPatch(typeof(CharacterSounds), "playGrowl")]
    public static class HostGrowlSoundPatch
    {
        [HarmonyPrefix]
        private static void Prefix() { TraverseHack.InsideCharacterSounds = true; }

        [HarmonyPostfix]
        private static void Postfix(CharacterSounds __instance)
        {
            TraverseHack.InsideCharacterSounds = false;
            EntitySoundSyncHelper.Broadcast(__instance, EntitySoundType.Growl);
        }
    }

    [HarmonyPatch(typeof(CharacterSounds), "playEscapingLoop")]
    public static class HostEscapingSoundPatch
    {
        [HarmonyPrefix]
        private static void Prefix() { TraverseHack.InsideCharacterSounds = true; }

        [HarmonyPostfix]
        private static void Postfix(CharacterSounds __instance)
        {
            TraverseHack.InsideCharacterSounds = false;
            EntitySoundSyncHelper.Broadcast(__instance, EntitySoundType.Escaping);
        }
    }

    [HarmonyPatch(typeof(CharacterSounds), "playSingleInstance")]
    public static class HostSingleInstanceSoundPatch
    {
        [HarmonyPrefix]
        private static void Prefix() { TraverseHack.InsideCharacterSounds = true; }

        [HarmonyPostfix]
        private static void Postfix(CharacterSounds __instance, string sound)
        {
            TraverseHack.InsideCharacterSounds = false;
            if (string.IsNullOrEmpty(sound)) return;

            // Only broadcast AI-behavior sounds that animation events don't cover
            if (sound == __instance.curious)
                EntitySoundSyncHelper.Broadcast(__instance, EntitySoundType.Curious);
            else if (sound == __instance.aggressive)
                EntitySoundSyncHelper.Broadcast(__instance, EntitySoundType.Aggressive);
            else if (sound == __instance.defensive)
                EntitySoundSyncHelper.Broadcast(__instance, EntitySoundType.Defensive);
        }
    }
}
