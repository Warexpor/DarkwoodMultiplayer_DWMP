using UnityEngine;

namespace DarkwoodMultiplayer.Audio
{
    public static class LocalAudioService
    {
        public const float DefaultMaxAudioDistance = 500f;

        public static float DistanceToLocalPlayer(Vector3 worldPosition)
        {
            Player local = Player.Instance;
            if (local == null) return float.MaxValue;
            return Vector3.Distance(local.transform.position, worldPosition);
        }

        public static bool IsNearLocalPlayer(Vector3 worldPosition, float maxDistance = DefaultMaxAudioDistance)
        {
            return DistanceToLocalPlayer(worldPosition) <= maxDistance;
        }

        public static bool IsNearLocalPlayer(Component targetComponent, float maxDistance = DefaultMaxAudioDistance)
        {
            if (targetComponent == null) return false;
            return IsNearLocalPlayer(targetComponent.transform.position, maxDistance);
        }
    }
}
