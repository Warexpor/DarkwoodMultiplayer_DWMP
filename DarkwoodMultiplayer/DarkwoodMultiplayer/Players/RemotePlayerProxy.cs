using System;
using BepInEx.Logging;
using UnityEngine;

namespace DarkwoodMultiplayer.Players
{
    /// <summary>
    /// Network-driven proxy that mimics a remote player's position, animation, and collision.
    /// </summary>
    public sealed class RemotePlayerProxy : MonoBehaviour
    {
        private SecondPlayerAnimController _anim;
        private Transform _shadow;
        private Vector3 _targetPosition;
        private float _targetRotationY;
        // Accumulated push force from local player collisions
        private Vector3 _pushOffset;
        private bool _hasState;
        private Rigidbody _rb;
        // True while the remote player is mid-vault (colliders temporarily disabled)
        private bool _isVaulting;
        private Collider[] _cachedColliders;

        /// <summary>
        /// Singleton instance of the remote player proxy.
        /// </summary>
        public static RemotePlayerProxy Instance { get; private set; }

        /// <summary>Whether the remote player has the Shadow Ward skill active.</summary>
        public bool RemoteHasShadowWard { get; set; }
        /// <summary>Whether the remote player has the Forest Spirit Ward skill active.</summary>
        public bool RemoteHasForestSpiritWard { get; set; }
        /// <summary>Whether the remote player has the Friend of the Forest skill active.</summary>
        public bool RemoteHasFriendOfTheForest { get; set; }
        /// <summary>Whether the remote player has the Enemy of the Forest skill active.</summary>
        public bool RemoteHasEnemyOfTheForest { get; set; }
        /// <summary>Whether the remote player is currently running.</summary>
        public bool RemoteRunning { get; set; }
        /// <summary>The last received locomotion state for the remote player.</summary>
        public SecondPlayerAnimController.LocomotionState RemoteLocomotion { get; set; }

        /// <summary>Fires when a footstep animation event occurs. Parameter is true for running, false for walking.</summary>
        public event Action<bool> OnFootstep;

        /// <summary>
        /// Creates the remote player GameObject, wires components, and returns the proxy component.
        /// </summary>
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
            int wasTrigger = 0;
            foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger)
                    wasTrigger++;
                col.enabled = true;
                col.isTrigger = false;
                enabledCount++;
            }
            log?.LogInfo($"RemoteProxy: enabled {enabledCount} colliders ({wasTrigger} were triggers, set to non-trigger)");
        }

        /// <summary>
        /// Applies a network state snapshot to the proxy (position, animation, vault state).
        /// </summary>
        public void ApplyNetworkState(PlayerStateNet state)
        {
            _targetPosition = state.Position;
            _targetRotationY = state.TorsoFacingY;
            _hasState = true;
            _anim?.ApplyNetworkSnapshot(
                state.Locomotion, state.FlipX, state.LegFacingY,
                state.ReverseLegs, state.TorsoFacingY, state.TorsoClip, state.LegsClip);

            bool nowVaulting = state.TorsoClip == "JumpWindow";
            if (nowVaulting != _isVaulting)
            {
                _isVaulting = nowVaulting;

                // Disable colliders during vault so proxy can pass through window;
                // re-enable when vault ends.
                foreach (var col in _cachedColliders)
                    col.enabled = !nowVaulting;

                if (!nowVaulting)
                {
                    // Vault just ended — teleport to exact final position so we don't
                    // accumulate lag from the lerp.
                    _rb.position = _targetPosition;
                    _pushOffset = Vector3.zero;
                }
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _cachedColliders = GetComponentsInChildren<Collider>(true);
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

        // Safety-net: detect Bullet component collisions in case FastProjectile
        // raycast misses the proxy (short-distance edge cases).
        private void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody == null) return;
            var bullet = collision.gameObject.GetComponent<Bullet>();
            if (bullet == null) return;

            var net = ModRuntime.Network as Networking.LanNetworkManager;
            if (net == null || net.Role == Networking.NetworkRole.Offline) return;
            if (bullet.objectThatSpawnedMe != null) return; // Skip enemy bullets

            int dmg = Mathf.Max(1, bullet.damage);
            Vector3 pos = transform.position;

            if (net.Role == Networking.NetworkRole.Host)
            {
                net.Send(Networking.NetMessageType.DamagePlayer, w =>
                {
                    new Networking.DamagePlayerMessage
                    {
                        Damage = dmg,
                        AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z,
                        ShowRedScreen = true
                    }.Serialize(w);
                }, LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
            else
            {
                net.Send(Networking.NetMessageType.FriendlyFire, w =>
                {
                    new Networking.FriendlyFireMessage
                    {
                        Damage = dmg,
                        AttackerPosX = pos.x, AttackerPosY = pos.y, AttackerPosZ = pos.z
                    }.Serialize(w);
                }, LiteNetLib.DeliveryMethod.ReliableOrdered);
            }

            ModRuntime.Log?.LogInfo("[ProxyCollisionEnter] bullet hit proxy, relayed " + dmg + " damage");

            // Physically destroy the bullet so it doesn't persist
            if (collision.gameObject != null)
                UnityEngine.Object.Destroy(collision.gameObject);
        }

        // Throttled log counter to avoid spamming the log file
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

            if (_isVaulting)
            {
                // During vault: teleport directly to target position.
                // Colliders are disabled so we pass through the window.
                _rb.position = _targetPosition;
                _rb.velocity = Vector3.zero;
                _pushOffset = Vector3.zero;
                return;
            }

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

            // Decay push force gradually
            _pushOffset = Vector3.Lerp(_pushOffset, Vector3.zero, Time.fixedDeltaTime * 10f);

            // Smoothly interpolate Y rotation to avoid cone/view jitter from low-rate network updates
            float rotT = 18f * Time.fixedDeltaTime;
            Vector3 euler = transform.eulerAngles;
            euler.y = Mathf.LerpAngle(euler.y, _targetRotationY, rotT);
            transform.eulerAngles = euler;
        }

        // Forces shadow to always point straight down for correct 2.5D appearance
        private void LateUpdate()
        {
            if (_shadow != null)
                _shadow.eulerAngles = new Vector3(90f, 0f, 0f);
        }
    }

    /// <summary>
    /// Snapshot of a remote player's position and animation state sent over the network.
    /// </summary>
    public struct PlayerStateNet
    {
        /// <summary>World position.</summary>
        public Vector3 Position;
        /// <summary>Locomotion state (Idle/Walk/Run).</summary>
        public SecondPlayerAnimController.LocomotionState Locomotion;
        /// <summary>Whether the sprite is flipped horizontally.</summary>
        public bool FlipX;
        /// <summary>Legs object Y rotation (quantised to short).</summary>
        public short LegFacingY;
        /// <summary>Whether legs are reversed (walking backwards).</summary>
        public bool ReverseLegs;
        /// <summary>Torso object Y rotation (quantised to short).</summary>
        public short TorsoFacingY;
        /// <summary>Name of the currently playing torso clip, or null/empty for idle.</summary>
        public string TorsoClip;
        /// <summary>Name of the currently playing legs clip, or null/empty for idle.</summary>
        public string LegsClip;
    }
}
