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

        public static RemotePlayerProxy Instance { get; private set; }

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
            CharBase cb = go.GetComponent<CharBase>();
            if (cb == null)
                cb = go.AddComponent<CharBase>();
            cb.alive = true;
            cb.isActive = true;
            cb.Health = 1f;
            cb.maxHealth = 1f;
            cb.faction = Faction.player;
            log?.LogInfo("RemoteProxy: added CharBase with Faction.player.");
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
                Object.DestroyImmediate(existing);

            Rigidbody rb = clone.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.mass = 5f;
            rb.drag = 3f;
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

            log?.LogInfo($"RemoteProxy: enabled {enabledCount} colliders ({triggerCount} triggers) — non-kinematic, mass=5, drag=3.");
        }

        public void ApplyNetworkState(PlayerStateNet state)
        {
            _targetPosition = state.Position;
            _hasState = true;

            _anim?.ApplyNetworkSnapshot(
                state.Locomotion,
                state.FlipX,
                state.LegFacingY,
                state.ReverseLegs,
                state.TorsoFacingY,
                state.TorsoClip,
                state.LegsClip);
        }

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private static int _pushCollideCount;

        private void OnCollisionStay(Collision collision)
        {
            Player local = Player.Instance;
            if (local == null || collision.rigidbody == null)
                return;

            bool isLocal = collision.rigidbody == local.Rigidbody;
            if (isLocal && ++_pushCollideCount % 60 == 0)
                ModRuntime.Log?.LogInfo("[ProxyCollide] pushed by host player");

            if (!isLocal)
                return;

            Vector3 pushDir = transform.position - collision.transform.position;
            pushDir.y = 0f;
            if (pushDir.magnitude > 0.01f)
                pushDir.Normalize();
            float speed = Mathf.Clamp(collision.relativeVelocity.magnitude * 1.5f, 0f, 8f);
            _pushOffset = pushDir * speed;
        }

        private void FixedUpdate()
        {
            if (!_hasState || _rb == null)
                return;

            Vector3 target = _targetPosition + _pushOffset;
            float t = 18f * Time.fixedDeltaTime;
            Vector3 desiredPos = Vector3.Lerp(_rb.position, target, t);
            desiredPos.y = _rb.position.y;
            _rb.MovePosition(desiredPos);

            _pushOffset = Vector3.Lerp(_pushOffset, Vector3.zero, Time.fixedDeltaTime * 3f);
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
