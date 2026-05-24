using System;
using BepInEx.Logging;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    public sealed class RemotePlayerProxy : MonoBehaviour
    {
        private SecondPlayerAnimController _anim;
        private Transform _shadow;
        private Vector3 _targetPosition;
        private Vector3 _pushOffset;
        private bool _hasState;
        private Rigidbody _rb;

        public static RemotePlayerProxy Instance { get; private set; }

        public bool RemoteHasShadowWard { get; set; }
        public bool RemoteHasForestSpiritWard { get; set; }
        public bool RemoteHasFriendOfTheForest { get; set; }
        public bool RemoteHasEnemyOfTheForest { get; set; }
        public bool RemoteRunning { get; set; }
        public SecondPlayerAnimController.LocomotionState RemoteLocomotion { get; set; }

        /// <summary>Fires when a footstep animation event occurs. Parameter is true for running, false for walking.</summary>
        public event Action<bool> OnFootstep;

        public static RemotePlayerProxy Spawn(ManualLogSource log)
        {
            Player source = PlayerControlRouter.MainPlayer ?? Player.Instance;
            if (source == null)
            {
                log?.LogWarning("Cannot spawn remote proxy: no local Player.");
                return null;
            }

            GameObject clone = PlayerProxyBuilder.CreatePlayerClone(
                source,
                "RemotePlayer",
                Vector3.zero,
                PlayerCloneKind.Remote,
                log);
            if (clone == null)
                return null;

            EnableCollision(clone, log);
            EnableGroundLight(clone.transform, log);
            AddCharBase(clone, log);

            RemotePlayerProxy proxy = clone.AddComponent<RemotePlayerProxy>();
            Instance = proxy;
            proxy._anim = clone.GetComponent<SecondPlayerAnimController>();
            proxy._shadow = clone.transform.Find("Shadow");
            return proxy;
        }

        private static void AddCharBase(GameObject go, ManualLogSource log)
        {
            CharBase cb = go.AddComponent<CharBase>();
            cb.alive = true;
            cb.isActive = true;
            cb.faction = Faction.player;

            Player hostPlayer = Player.Instance;
            if (hostPlayer != null)
            {
                cb.maxHealth = hostPlayer.maxHealth;
                cb.Health = hostPlayer.health;
                log?.LogInfo($"RemoteProxy: HP pool = {cb.Health}/{cb.maxHealth} (matching host)");
            }
            else
            {
                cb.Health = 100f;
                cb.maxHealth = 100f;
                log?.LogInfo("RemoteProxy: HP pool = 100/100 (fallback, no host)");
            }
            log?.LogInfo("RemoteProxy: added standalone CharBase with Faction.player.");
        }

        private static void EnableGroundLight(Transform root, ManualLogSource log)
        {
            Transform shadow = root.Find("Shadow");
            if (shadow != null)
            {
                shadow.gameObject.SetActive(true);
                log?.LogInfo("RemoteProxy: enabled Shadow.");
            }
        }

        private static void EnableCollision(GameObject clone, ManualLogSource log)
        {
            Rigidbody existing = clone.GetComponent<Rigidbody>();
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);

            Rigidbody rb = clone.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.mass = 2.5f;
            rb.drag = 0f;
            rb.angularDrag = 10f;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            int enabledCount = 0;
            int triggerCount = 0;
            foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = true;
                if (col.isTrigger)
                    triggerCount++;
                enabledCount++;
            }
            log?.LogInfo($"RemoteProxy: enabled {enabledCount} colliders ({triggerCount} triggers) — non-kinematic mass=2.5 drag=0");
        }

        public void ApplyNetworkState(PlayerStateNet state)
        {
            _targetPosition = state.Position;
            _hasState = true;
            _anim?.ApplyNetworkSnapshot(
                state.Locomotion, state.FlipX, state.LegFacingY,
                state.ReverseLegs, state.TorsoFacingY, state.TorsoClip, state.LegsClip);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            foreach (tk2dSpriteAnimator anim in GetComponentsInChildren<tk2dSpriteAnimator>(true))
            {
                if (anim.name.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    anim.AnimationEventTriggered += OnLegAnimationEvent;
                    break;
                }
            }
        }

        private void OnLegAnimationEvent(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip, int frameNum)
        {
            string eventInfo = clip.GetFrame(frameNum).eventInfo;
            if (eventInfo == "FootHitGround")
                OnFootstep?.Invoke(false);
            else if (eventInfo == "FootHitGroundRun")
                OnFootstep?.Invoke(true);
        }

        private void OnDestroy()
        {
            foreach (tk2dSpriteAnimator anim in GetComponentsInChildren<tk2dSpriteAnimator>(true))
            {
                if (anim.name.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    anim.AnimationEventTriggered -= OnLegAnimationEvent;
                    break;
                }
            }
        }

        private static int _pushCollideCount;

        private void OnCollisionStay(Collision collision)
        {
            if (collision.rigidbody == null)
                return;

            if (collision.rigidbody == Player.Instance?.Rigidbody)
            {
                if (++_pushCollideCount % 60 == 0)
                    ModRuntime.Log?.LogInfo("[ProxyCollide] pushed by local player");

                Vector3 pushDir = transform.position - collision.transform.position;
                pushDir.y = 0f;
                if (pushDir.magnitude > 0.01f)
                    pushDir.Normalize();
                float speed = Mathf.Clamp(collision.relativeVelocity.magnitude * 0.5f, 0f, 4f);
                _pushOffset = pushDir * speed;
            }
        }

        private void FixedUpdate()
        {
            if (!_hasState || _rb == null)
                return;

            Vector3 target = _targetPosition + _pushOffset;
            target.y = _rb.position.y;

            // Clamp drift so proxy can't be launched away by chain-reaction pushes
            Vector3 drift = target - _targetPosition;
            float maxDrift = 50f;
            if (drift.magnitude > maxDrift)
                target = _targetPosition + drift.normalized * maxDrift;

            float t = 18f * Time.fixedDeltaTime;
            Vector3 delta = Vector3.Lerp(_rb.position, target, t) - _rb.position;
            delta.y = 0f;

            // Use velocity so Unity physics naturally handles entity pushing
            _rb.velocity = delta / Time.fixedDeltaTime;

            _pushOffset = Vector3.Lerp(_pushOffset, Vector3.zero, Time.fixedDeltaTime * 10f);
        }

        private void LateUpdate()
        {
            if (_shadow != null)
                _shadow.eulerAngles = new Vector3(90f, 0f, 0f);
        }
    }

    public struct PlayerStateNet
    {
        public Vector3 Position;
        public SecondPlayerAnimController.LocomotionState Locomotion;
        public bool FlipX;
        public short LegFacingY;
        public bool ReverseLegs;
        public short TorsoFacingY;
        public string TorsoClip;
        public string LegsClip;
    }
}
