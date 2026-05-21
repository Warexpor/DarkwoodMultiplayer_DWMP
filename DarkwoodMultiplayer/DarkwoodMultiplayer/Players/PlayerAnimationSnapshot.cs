using System.Reflection;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    public static class PlayerAnimationSnapshot
    {
        private static readonly FieldInfo WantToReverseLegsField =
            typeof(Player).GetField("wantToReverseLegs", BindingFlags.Instance | BindingFlags.NonPublic);
        public static SecondPlayerAnimController.LocomotionState ReadLocomotion(Player player)
        {
            if (player == null)
                return SecondPlayerAnimController.LocomotionState.Idle;

            if (player.running)
                return SecondPlayerAnimController.LocomotionState.Run;

            Vector3 vel = player.Rigidbody != null ? player.Rigidbody.velocity : Vector3.zero;
            float speed = new Vector2(vel.x, vel.z).magnitude;
            if (speed >= 0.35f)
                return SecondPlayerAnimController.LocomotionState.Walk;

            return SecondPlayerAnimController.LocomotionState.Idle;
        }

        public static short ReadTorsoFacingY(Player player)
        {
            if (player == null)
                return 0;

            return (short)Mathf.RoundToInt(player.transform.eulerAngles.y);
        }

        public static short ReadLegFacingY(Player player)
        {
            if (player == null)
                return 0;

            Transform legs = player.transform.Find("PlayerLegs");
            float y = legs != null ? legs.eulerAngles.y : player.transform.eulerAngles.y;
            return (short)Mathf.RoundToInt(y);
        }

        public static bool ReadReverseLegs(Player player)
        {
            if (player == null || WantToReverseLegsField == null)
                return false;

            object value = WantToReverseLegsField.GetValue(player);
            return value is bool b && b;
        }

        public static bool ReadFlipX(Player player)
        {
            tk2dSprite sprite = player != null ? player.GetComponentInChildren<tk2dSprite>(true) : null;
            return sprite != null && sprite.FlipX;
        }

        public static string ReadTorsoClip(Player player)
        {
            if (player == null || player.torsoAnimator == null)
                return null;
            var clip = player.torsoAnimator.CurrentClip;
            if (clip == null || clip.name == "Idle")
            {
                Vector3 vel = player.Rigidbody != null ? player.Rigidbody.velocity : Vector3.zero;
                float speed = new Vector2(vel.x, vel.z).magnitude;
                if (speed < 0.35f && !player.running)
                    return null;
            }
            return clip != null ? clip.name : null;
        }

        public static string ReadLegsClip(Player player)
        {
            if (player == null)
                return null;

            if (!player.running)
            {
                Vector3 vel = player.Rigidbody != null ? player.Rigidbody.velocity : Vector3.zero;
                float speed = new Vector2(vel.x, vel.z).magnitude;
                if (speed < 0.35f)
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
