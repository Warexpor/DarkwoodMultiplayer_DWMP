using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    /// <summary>
    /// Tracks the positions of the host player and the most recently reported remote player
    /// for proximity queries used by AI alert/aggro logic.
    /// </summary>
    public static class PlayerPositionManager
    {
        private static Vector3 _hostPos;
        private static Vector3 _remotePos;
        private static float _remoteRotY;
        private static bool _hasRemote;
        private static float _lastRemoteUpdateTime;

        /// <summary>Records the host player's current position.</summary>
        public static void ReportHostPosition(Vector3 pos)
        {
            _hostPos = pos;
        }

        /// <summary>Updates the remote player's known position and rotation.</summary>
        public static void UpdateRemotePlayer(Vector3 pos, float rotY)
        {
            _remotePos = pos;
            _remoteRotY = rotY;
            _hasRemote = true;
            // Use game time so stale data expires even if frame rate stutters
            _lastRemoteUpdateTime = Time.time;
        }

        /// <summary>Whether a remote player was recently seen (within the last 3 seconds).</summary>
        public static bool HasRemotePlayer => _hasRemote && (Time.time - _lastRemoteUpdateTime) < 3f;
        /// <summary>Last reported position of the remote player.</summary>
        public static Vector3 RemotePlayerPosition => _remotePos;
        /// <summary>Last reported Y rotation of the remote player.</summary>
        public static float RemotePlayerRotY => _remoteRotY;

        /// <summary>
        /// Returns the position of the nearest player (host or remote) from a given point.
        /// </summary>
        public static Vector3 GetNearestPlayerPosition(Vector3 fromPos)
        {
            Vector3 nearest = _hostPos;
            float nearestSq = Vector3.SqrMagnitude(_hostPos - fromPos);
            if (_hasRemote)
            {
                float d = Vector3.SqrMagnitude(_remotePos - fromPos);
                if (d < nearestSq)
                {
                    nearest = _remotePos;
                }
            }
            return nearest;
        }

        /// <summary>
        /// Returns true if any player is within the given squared distance of a point.
        /// </summary>
        public static bool IsAnyPlayerWithinSq(Vector3 fromPos, float sqrDist)
        {
            if (Vector3.SqrMagnitude(_hostPos - fromPos) < sqrDist) return true;
            if (_hasRemote && Vector3.SqrMagnitude(_remotePos - fromPos) < sqrDist) return true;
            return false;
        }

        /// <summary>
        /// Returns the squared distance from a point to the nearest player.
        /// </summary>
        public static float SqrDistanceToNearestPlayer(Vector3 fromPos)
        {
            float d = Vector3.SqrMagnitude(_hostPos - fromPos);
            if (_hasRemote)
            {
                float dr = Vector3.SqrMagnitude(_remotePos - fromPos);
                if (dr < d) d = dr;
            }
            return d;
        }

        /// <summary>Clears the remote player state (e.g. on disconnect).</summary>
        public static void Clear()
        {
            _hasRemote = false;
        }
    }
}
