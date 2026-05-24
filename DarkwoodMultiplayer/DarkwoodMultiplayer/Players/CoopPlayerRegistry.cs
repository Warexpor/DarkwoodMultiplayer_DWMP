using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Convenience queries across all active co-op player instances.
    /// </summary>
    public static class CoopPlayerRegistry
    {
        /// <summary>
        /// True when both the main and second local players are available.
        /// </summary>
        public static bool HasMultiplePlayers =>
            PlayerControlRouter.MainPlayer != null && PlayerControlRouter.SecondPlayer != null;

        /// <summary>
        /// Yields every registered Player (main, then second if present).
        /// </summary>
        public static IEnumerable<Player> AllPlayers()
        {
            if (PlayerControlRouter.MainPlayer != null)
                yield return PlayerControlRouter.MainPlayer;

            if (PlayerControlRouter.SecondPlayer != null)
                yield return PlayerControlRouter.SecondPlayer;
        }

        /// <summary>
        /// Returns the nearest alive Player to worldPos, or null if none found.
        /// </summary>
        public static Player GetNearestLivingPlayer(Vector3 worldPos)
        {
            Player best = null;
            float bestDist = float.MaxValue;

            foreach (Player player in AllPlayers())
            {
                if (player == null || !player.alive || !player.gameObject.activeInHierarchy)
                    continue;

                float dist = Core.trueDistance(worldPos, player.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = player;
                }
            }

            return best;
        }

        /// <summary>
        /// Returns the Transform of the nearest alive Player, or null.
        /// </summary>
        public static Transform GetNearestLivingPlayerTransform(Vector3 worldPos)
        {
            Player player = GetNearestLivingPlayer(worldPos);
            return player != null ? player.transform : null;
        }

        /// <summary>
        /// Populates outList with all registered players (main, then second if present).
        /// </summary>
        public static void GetAllPlayers(List<Player> outList)
        {
            if (PlayerControlRouter.MainPlayer != null)
                outList.Add(PlayerControlRouter.MainPlayer);

            if (PlayerControlRouter.SecondPlayer != null)
                outList.Add(PlayerControlRouter.SecondPlayer);
        }
    }
}
