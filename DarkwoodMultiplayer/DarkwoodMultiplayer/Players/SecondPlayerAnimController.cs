using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Drives torso and leg animations for the second player (local co-op or remote proxy) based on network snapshots.
    /// </summary>
    public sealed class SecondPlayerAnimController : MonoBehaviour
    {
        /// <summary>
        /// Movement state used to select the correct animation set.
        /// </summary>
        public enum LocomotionState : byte
        {
            /// <summary>Standing still.</summary>
            Idle = 0,
            /// <summary>Walking.</summary>
            Walk = 1,
            /// <summary>Running.</summary>
            Run = 2
        }

        // Blend speed for slerping legs rotation back to body-aligned standing pose
        private const float LegBlendSpeed = 15f;

        private tk2dSpriteAnimator _torsoAnimator;
        private tk2dSpriteAnimator _legsAnimator;
        private tk2dBaseSprite _torsoSprite;

        // Fallback animation library used when the clone lacks certain torso clips
        private tk2dSpriteAnimation _noneAnimsLib;
        private LocomotionState _state = LocomotionState.Idle;
        private bool _flipX;
        private short _networkLegFacingY;
        private bool _networkReverseLegs;
        private bool _hasNetworkLegFacing;

        // True after FeetNeutral has fired during idle — guards against rotating
        // the legs while the walk animation is still cycling to its neutral frame.
        private bool _feetNeutralReached;

        /// <summary>Current locomotion state.</summary>
        public LocomotionState State => _state;
        /// <summary>Current horizontal flip state.</summary>
        public bool FlipX => _flipX;

        private void Awake()
        {
            _torsoAnimator = GetComponent<tk2dSpriteAnimator>();
            _torsoSprite = GetComponent<tk2dBaseSprite>();
            _noneAnimsLib = Resources.Load("PlayerNoneAnims", typeof(tk2dSpriteAnimation)) as tk2dSpriteAnimation;

            Transform legsTransform = transform.Find("PlayerLegs");
            if (legsTransform != null)
            {
                _legsAnimator = legsTransform.GetComponent<tk2dSpriteAnimator>();

                Renderer legsRenderer = legsTransform.GetComponent<Renderer>();
                if (legsRenderer != null)
                    legsRenderer.enabled = true;

                if (_legsAnimator != null)
                    _legsAnimator.AnimationEventTriggered += OnLegsAnimationEvent;
            }

            if (_torsoAnimator == null)
                ModRuntime.Log?.LogWarning("SecondPlayerAnimController: no torso tk2dSpriteAnimator on root.");

            if (_legsAnimator == null)
                ModRuntime.Log?.LogWarning("SecondPlayerAnimController: no PlayerLegs / legs animator found.");
            else
                ModRuntime.Log?.LogInfo("SecondPlayerAnimController: torso + legs animators ready.");
        }

        // Stops legs animation when the feet-neutral event fires during idle
        private void OnLegsAnimationEvent(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip, int frameNum)
        {
            if (_state != LocomotionState.Idle)
                return;

            if (clip.GetFrame(frameNum).eventInfo == "FeetNeutral")
            {
                _legsAnimator?.Stop();
                _feetNeutralReached = true;
                // Align to body at the neutral frame so the rotation is already
                // correct when the blend in LateUpdate takes over.
                if (_legsAnimator != null)
                    _legsAnimator.transform.rotation = transform.rotation;
            }
        }

        // Continuously blend legs back to standing rotation when idling
        private void LateUpdate()
        {
            if (_state == LocomotionState.Idle && _legsAnimator != null && _feetNeutralReached)
            {
                ResetLegsToStanding();
            }
        }

        /// <summary>
        /// Applies a full animation snapshot from the network, updating torso, legs, facing, and flip.
        /// </summary>
        public void ApplyNetworkSnapshot(
            LocomotionState state,
            bool flipX,
            short legFacingY,
            bool reverseLegs,
            short torsoFacingY,
            string torsoClip,
            string legsClip)
        {
            _networkLegFacingY = legFacingY;
            _networkReverseLegs = reverseLegs;
            _hasNetworkLegFacing = true;

            Vector3 euler = transform.eulerAngles;
            euler.y = torsoFacingY;
            transform.eulerAngles = euler;

            if (flipX != _flipX)
                SetFlipX(flipX);

            bool wasMoving = _state == LocomotionState.Walk || _state == LocomotionState.Run;
            _state = state;
            bool isMoving = state == LocomotionState.Walk || state == LocomotionState.Run;

            if (!string.IsNullOrEmpty(torsoClip))
                PlayTorso(torsoClip);
            else if (state == LocomotionState.Idle)
                StopTorso();
            else
                ApplyLocomotion(state, Vector3.zero, legFacingY, reverseLegs);

            if (!string.IsNullOrEmpty(legsClip))
            {
                PlayLegs(legsClip);
            }
            else if (state == LocomotionState.Walk)
            {
                PlayLegs(reverseLegs ? "LegsWalkReverse" : "LegsWalk");
            }
            else if (state == LocomotionState.Run)
            {
                PlayLegs("LegsRun");
            }
            else if (wasMoving && !isMoving && _legsAnimator != null && _legsAnimator.Playing)
            {
                // Walk clips reach FeetNeutral naturally and are stopped by
                // OnLegsAnimationEvent.  Run clips have no FeetNeutral event
                // so we must stop them immediately.
                if (_legsAnimator.CurrentClip != null &&
                    _legsAnimator.CurrentClip.name.IndexOf("Run") >= 0)
                {
                    _legsAnimator.Stop();
                    _feetNeutralReached = true;
                    if (_legsAnimator != null)
                        _legsAnimator.transform.rotation = transform.rotation;
                }
            }

            if (state == LocomotionState.Walk)
            {
                _feetNeutralReached = false;
                AlignLegsToFacing(legFacingY, snapRunToBody: false);
            }
            else if (state == LocomotionState.Run)
            {
                _feetNeutralReached = false;
                AlignLegsToFacing(legFacingY, snapRunToBody: true);
            }
        }

        /// <summary>
        /// Convenience overload that applies only locomotion state and flip, keeping existing facing.
        /// </summary>
        public void ApplySnapshot(LocomotionState state, bool flipX)
        {
            ApplyNetworkSnapshot(state, flipX, (short)transform.eulerAngles.y, false, (short)transform.eulerAngles.y, null, null);
        }

        private void ApplyLocomotion(
            LocomotionState state,
            Vector3 velocity,
            short legFacingY,
            bool reverseLegs)
        {
            _state = state;

            if (Mathf.Abs(velocity.x) > 0.01f)
                SetFlipX(velocity.x < 0f);

            switch (state)
            {
                case LocomotionState.Run:
                    PlayTorso("Run");
                    PlayLegs("LegsRun");
                    AlignLegsToFacing(legFacingY, snapRunToBody: true);
                    break;

                case LocomotionState.Walk:
                    PlayTorso("Idle");
                    PlayLegs(reverseLegs ? "LegsWalkReverse" : "LegsWalk");
                    AlignLegsToFacing(legFacingY, snapRunToBody: false);
                    break;

                default:
                    PlayTorso("Idle");
                    break;
            }
        }

        /// <summary>
        /// Smoothly rotates legs back to align with the body rotation when idling.
        /// </summary>
        public void ResetLegsToStanding()
        {
            if (_legsAnimator == null)
                return;

            Quaternion target = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            _legsAnimator.transform.rotation = Quaternion.Slerp(
                _legsAnimator.transform.rotation,
                target,
                Time.deltaTime * LegBlendSpeed);
        }

        private void AlignLegsToFacing(short legFacingY, bool snapRunToBody)
        {
            if (_legsAnimator == null)
                return;

            if (snapRunToBody)
            {
                _legsAnimator.transform.rotation = transform.rotation;
                return;
            }

            float y = _hasNetworkLegFacing ? legFacingY : transform.eulerAngles.y;
            _legsAnimator.transform.rotation = Quaternion.Euler(90f, y, 0f);
        }

        private void PlayTorso(string clipName)
        {
            if (_torsoAnimator == null || string.IsNullOrEmpty(clipName))
                return;

            if (_torsoAnimator.GetClipByName(clipName) == null)
            {
                // Fall back to the "None" animation library if the clone doesn't have the clip
                if (_noneAnimsLib != null && _noneAnimsLib.GetClipByName(clipName) != null)
                {
                    _torsoAnimator.Library = _noneAnimsLib;
                }
                else
                {
                    return;
                }
            }

            // Only skip if the animator is already actively playing this clip.
            // Allows replay when a non-looping clip finishes and restarts
            // (e.g. double barrel reload loop — same clip plays twice).
            if (_torsoAnimator.Playing && _torsoAnimator.CurrentClip?.name == clipName)
                return;

            _torsoAnimator.Play(clipName);
        }

        private void PlayLegs(string clipName)
        {
            if (_legsAnimator == null || string.IsNullOrEmpty(clipName))
                return;

            if (_legsAnimator.GetClipByName(clipName) == null)
            {
                // Fall back to normal walk animation if reverse walk clip is missing
                if (clipName == "LegsWalkReverse" && _legsAnimator.GetClipByName("LegsWalk") != null)
                    clipName = "LegsWalk";
                else
                    return;
            }

            if (_legsAnimator.Playing && _legsAnimator.CurrentClip != null && _legsAnimator.CurrentClip.name == clipName)
                return;

            _legsAnimator.Play(clipName);
        }

        private void StopTorso()
        {
            if (_torsoAnimator == null)
                return;
            _torsoAnimator.Stop();
        }

        private void SetFlipX(bool flip)
        {
            _flipX = flip;
            if (_torsoSprite != null)
                _torsoSprite.FlipX = flip;
        }
    }
}
