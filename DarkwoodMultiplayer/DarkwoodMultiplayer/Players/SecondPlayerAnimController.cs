using BepInEx.Logging;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Drives Darkwood's split torso + PlayerLegs animators (same clips as Player.ProcessAnims).
    /// </summary>
    public sealed class SecondPlayerAnimController : MonoBehaviour
    {
        public enum LocomotionState : byte
        {
            Idle = 0,
            Walk = 1,
            Run = 2
        }

        private const float LegBlendSpeed = 15f;

        private tk2dSpriteAnimator _torsoAnimator;
        private tk2dSpriteAnimator _legsAnimator;
        private tk2dBaseSprite _torsoSprite;

        private string _currentTorsoClip;
        private string _currentLegsClip;
        private LocomotionState _state = LocomotionState.Idle;
        private bool _flipX;
        private short _networkLegFacingY;
        private bool _networkReverseLegs;
        private bool _hasNetworkLegFacing;

        public LocomotionState State => _state;
        public bool FlipX => _flipX;

        private void Awake()
        {
            _torsoAnimator = GetComponent<tk2dSpriteAnimator>();
            _torsoSprite = GetComponent<tk2dBaseSprite>();

            Transform legsTransform = transform.Find("PlayerLegs");
            if (legsTransform != null)
            {
                _legsAnimator = legsTransform.GetComponent<tk2dSpriteAnimator>();

                Renderer legsRenderer = legsTransform.GetComponent<Renderer>();
                if (legsRenderer != null)
                    legsRenderer.enabled = true;
            }

            if (_torsoAnimator == null)
                ModRuntime.Log?.LogWarning("SecondPlayerAnimController: no torso tk2dSpriteAnimator on root.");

            if (_legsAnimator == null)
                ModRuntime.Log?.LogWarning("SecondPlayerAnimController: no PlayerLegs / legs animator found.");
            else
                ModRuntime.Log?.LogInfo("SecondPlayerAnimController: torso + legs animators ready.");
        }

        private float _idleTime;

        private void LateUpdate()
        {
            if (_state == LocomotionState.Idle)
            {
                ResetLegsToStanding();
                _idleTime += Time.deltaTime;
                if (_idleTime > 0.15f && _currentLegsClip != null)
                {
                    if (_legsAnimator != null)
                    {
                        _legsAnimator.Stop();
                        if (_legsAnimator.CurrentClip != null)
                            _legsAnimator.SetFrame(0, false);
                    }
                    _currentLegsClip = null;
                }
            }
            else
            {
                _idleTime = 0f;
            }
        }

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

            _state = state;

            if (!string.IsNullOrEmpty(torsoClip))
                PlayTorso(torsoClip);
            else if (state == LocomotionState.Idle)
                StopTorso();
            else
                ApplyLocomotion(state, Vector3.zero, legFacingY, reverseLegs);

            if (!string.IsNullOrEmpty(legsClip))
                PlayLegs(legsClip);
            else if (state == LocomotionState.Idle)
            {
                // Let LateUpdate handle the stop after a delay
            }
            else if (state == LocomotionState.Walk)
                PlayLegs(reverseLegs ? "LegsWalkReverse" : "LegsWalk");
            else if (state == LocomotionState.Run)
                PlayLegs("LegsRun");

            if (state == LocomotionState.Walk)
                AlignLegsToFacing(legFacingY, snapRunToBody: false);
            else if (state == LocomotionState.Run)
                AlignLegsToFacing(legFacingY, snapRunToBody: true);
        }

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
                    StopLegs();
                    break;
            }
        }

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
                return;

            if (clipName == _currentTorsoClip)
                return;

            _torsoAnimator.Play(clipName);
            _currentTorsoClip = clipName;
        }

        private void PlayLegs(string clipName)
        {
            if (_legsAnimator == null || string.IsNullOrEmpty(clipName))
                return;

            if (_legsAnimator.GetClipByName(clipName) == null)
            {
                if (clipName == "LegsWalkReverse" && _legsAnimator.GetClipByName("LegsWalk") != null)
                    clipName = "LegsWalk";
                else
                    return;
            }

            if (clipName == _currentLegsClip)
                return;

            _legsAnimator.Play(clipName);
            _currentLegsClip = clipName;
        }

        private void StopTorso()
        {
            if (_torsoAnimator == null)
                return;
            _torsoAnimator.Stop();
            _currentTorsoClip = null;
        }

        private void StopLegs()
        {
            if (_legsAnimator == null)
                return;

            _legsAnimator.Stop();
            _currentLegsClip = null;
        }

        private void SetFlipX(bool flip)
        {
            _flipX = flip;
            if (_torsoSprite != null)
                _torsoSprite.FlipX = flip;
        }
    }
}
