using DarkwoodMultiplayer.Sync;
using LiteNetLib;
using UnityEngine;

namespace DarkwoodMultiplayer.Networking
{
    public static class EntityStateBroadcastService
    {
        private static NetPeer _peer;
        private static float _sendTimer;
        private const float SendInterval = 0.1f;

        private static EntitySnapshotNet[] _buffer = new EntitySnapshotNet[128];

        public static void SetPeer(NetPeer peer) => _peer = peer;

        public static void Tick()
        {
            if (_peer == null || _peer.ConnectionState != ConnectionState.Connected)
                return;

            _sendTimer += Time.deltaTime;
            if (_sendTimer < SendInterval)
                return;

            _sendTimer = 0f;
            SendSnapshot();
        }

        private static void SendSnapshot()
        {
            Character[] all = CharacterTracker.GetAll();
            if (all == null || all.Length == 0)
                return;

            int maxEntities = Mathf.Min(all.Length, 128);
            if (_buffer.Length < maxEntities)
                _buffer = new EntitySnapshotNet[maxEntities];

            int count = 0;

            Vector3 hostPos = Player.Instance != null ? Player.Instance.transform.position : Vector3.zero;
            float maxDistSq = 700f * 700f;

            for (int i = 0; i < all.Length && count < maxEntities; i++)
            {
                Character c = all[i];
                if (c == null) continue;

                Vector3 cPos = c.transform.position;
                float d1 = Vector3.SqrMagnitude(cPos - hostPos);
                if (d1 > maxDistSq && (!PlayerPositionManager.HasRemotePlayer || Vector3.SqrMagnitude(cPos - PlayerPositionManager.RemotePlayerPosition) > maxDistSq))
                    continue;

                tk2dSpriteAnimator anim = c.GetComponent<tk2dSpriteAnimator>();
                string clip = anim != null && anim.CurrentClip != null ? anim.CurrentClip.name : "";
                short clipFrame = anim != null && anim.CurrentClip != null ? (short)anim.CurrentFrame : (short)-1;
                Vector3 rot = c.transform.eulerAngles;

                string entityName = c.name;
                if (entityName.EndsWith("(Clone)"))
                    entityName = entityName.Substring(0, entityName.Length - 7);

                _buffer[count] = new EntitySnapshotNet
                {
                    Index = CharacterTracker.GetStableId(c),
                    PosX = cPos.x,
                    PosY = cPos.y,
                    PosZ = cPos.z,
                    RotY = rot.y,
                    Clip = clip,
                    ClipFrame = clipFrame,
                    Alive = c.alive,
                    HealthPct = (byte)Mathf.Clamp((c.Health / Mathf.Max(c.maxHealth, 1f)) * 100f, 0, 100),
                    EntityName = entityName
                };
                count++;
            }

            if (count == 0)
                return;

            var writer = new NetWriter();
            writer.Put((byte)NetMessageType.EntityState);

            int entityCount = count;
            writer.Put(entityCount);
            for (int i = 0; i < entityCount; i++)
                _buffer[i].Serialize(writer);

            _peer.Send(writer.CopyData(), DeliveryMethod.Unreliable);

            _sendCount++;
            if (_sendCount % 10 == 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append($"[HostEntitySync] sending {entityCount} entities: ");
                for (int i = 0; i < entityCount; i++)
                {
                    Character c = CharacterTracker.FindByStableId(_buffer[i].Index);
                    if (c != null)
                    {
                        sb.Append(c.name);
                        sb.Append("(id=");
                        sb.Append(_buffer[i].Index);
                        sb.Append(") ");
                    }
                }
                ModRuntime.Log?.LogInfo(sb.ToString());
            }
        }

        private static int _sendCount;

        public static void Stop()
        {
            _peer = null;
            _sendTimer = 0f;
        }
    }
}
