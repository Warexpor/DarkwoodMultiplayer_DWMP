using System.Reflection;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Reads current animation state from a Player for network replication.
    /// </summary>
    public static class PlayerAnimationSnapshot
    {
        private static readonly FieldInfo WantToReverseLegsField =
            typeof(Player).GetField("wantToReverseLegs", BindingFlags.Instance | BindingFlags.NonPublic);

        // Minimum velocity magnitude before legs show walking animation
        private const float LegAnimThreshold = 55f;

        /// <summary>
        /// Determines the locomotion state (Idle/Walk/Run) from the player's rigidbody velocity and running flag.
        /// </summary>
        public static SecondPlayerAnimController.LocomotionState ReadLocomotion(Player player)
        {
            if (player == null)
                return SecondPlayerAnimController.LocomotionState.Idle;

            if (player.running)
                return SecondPlayerAnimController.LocomotionState.Run;

            Vector3 vel = player.Rigidbody != null ? player.Rigidbody.velocity : Vector3.zero;
            float speed = new Vector2(vel.x, vel.z).magnitude;
            if (speed >= LegAnimThreshold)
                return SecondPlayerAnimController.LocomotionState.Walk;

            return SecondPlayerAnimController.LocomotionState.Idle;
        }

        /// <summary>
        /// Reads the torso's Y rotation as a short.
        /// </summary>
        public static short ReadTorsoFacingY(Player player)
        {
            if (player == null)
                return 0;

            return (short)Mathf.RoundToInt(player.transform.eulerAngles.y);
        }

        /// <summary>
        /// Reads the legs object's Y rotation (falls back to the root transform if PlayerLegs is missing).
        /// </summary>
        public static short ReadLegFacingY(Player player)
        {
            if (player == null)
                return 0;

            Transform legs = player.transform.Find("PlayerLegs");
            float y = legs != null ? legs.eulerAngles.y : player.transform.eulerAngles.y;
            return (short)Mathf.RoundToInt(y);
        }

        /// <summary>
        /// Reads the private wantToReverseLegs field via reflection.
        /// </summary>
        public static bool ReadReverseLegs(Player player)
        {
            if (player == null || WantToReverseLegsField == null)
                return false;

            object value = WantToReverseLegsField.GetValue(player);
            return value is bool b && b;
        }

        /// <summary>
        /// Reads whether the player's sprite is flipped horizontally.
        /// </summary>
        public static bool ReadFlipX(Player player)
        {
            tk2dSprite sprite = player != null ? player.GetComponentInChildren<tk2dSprite>(true) : null;
            return sprite != null && sprite.FlipX;
        }

        /// <summary>
        /// Reads the name of the currently playing torso clip. Returns null if the player is idling (no meaningful animation to sync).
        /// </summary>
        public static string ReadTorsoClip(Player player)
        {
            if (player == null || player.torsoAnimator == null)
                return null;
            var clip = player.torsoAnimator.CurrentClip;
            // Suppress Idle clip transmission; remote can derive idle from state
            if (clip == null || clip.name == "Idle")
            {
                Vector3 vel = player.Rigidbody != null ? player.Rigidbody.velocity : Vector3.zero;
                float speed = new Vector2(vel.x, vel.z).magnitude;
                if (speed < LegAnimThreshold && !player.running)
                    return null;
            }
            return clip != null ? clip.name : null;
        }

        /// <summary>
        /// Reads the name of the currently playing legs clip. Returns null if the player is stationary (no walk/run animation).
        /// </summary>
        public static string ReadLegsClip(Player player)
        {
            if (player == null)
                return null;

            if (!player.running)
            {
                Vector3 vel = player.Rigidbody != null ? player.Rigidbody.velocity : Vector3.zero;
                float speed = new Vector2(vel.x, vel.z).magnitude;
                if (speed < LegAnimThreshold)
                    return null;
            }

            Transform legsT = player.transform.Find("PlayerLegs");
            if (legsT == null)
                return null;
            tk2dSpriteAnimator legsAnim = legsT.GetComponent<tk2dSpriteAnimator>();
            if (legsAnim == null)
                return null;
            var clip = legsAnim.CurrentClip;
            return clip != null ? clip.name : null;
        }
    }
}
