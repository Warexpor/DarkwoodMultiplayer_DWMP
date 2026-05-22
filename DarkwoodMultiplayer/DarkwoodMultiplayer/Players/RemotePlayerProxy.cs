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

            RemotePlayerProxy proxy = clone.AddComponent<RemotePlayerProxy>();
            proxy._anim = clone.GetComponent<SecondPlayerAnimController>();
            proxy._shadow = clone.transform.Find("Shadow");
            return proxy;
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
            Rigidbody rb = clone.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = clone.AddComponent<Rigidbody>();
                log?.LogInfo("RemoteProxy: added kinematic Rigidbody for collision.");
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
            {
                if (!col.isTrigger)
                {
                    col.enabled = true;
                    log?.LogInfo("RemoteProxy: enabled collider " + col.name);
                }
            }

            Collider[] allCols = clone.GetComponentsInChildren<Collider>(true);
            log?.LogInfo("RemoteProxy: total colliders found = " + allCols.Length);
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

        private static int _proxyCollideCount;

        private void OnCollisionStay(Collision collision)
        {
            Player local = Player.Instance;
            if (local == null || collision.rigidbody == null)
                return;

            bool isLocal = collision.rigidbody == local.Rigidbody;
            if (isLocal && ++_proxyCollideCount % 60 == 0)
                ModRuntime.Log?.LogInfo("[ProxyCollide] player push active isLocal=True");

            if (!isLocal)
                return;

            Vector3 pushDir = transform.position - collision.transform.position;
            pushDir.y = 0f;
            if (pushDir.magnitude > 0.01f)
                pushDir.Normalize();
            else
                pushDir = Vector3.zero;
            pushDir.y = 0f;

            float speed = Mathf.Clamp(collision.relativeVelocity.magnitude * 1.2f, 0f, 6f);
            _pushOffset = pushDir * speed;
        }

        private void LateUpdate()
        {
            if (!_hasState)
                return;

            Vector3 target = _targetPosition + _pushOffset;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 18f);
            _pushOffset = Vector3.Lerp(_pushOffset, Vector3.zero, Time.deltaTime * 3f);

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
