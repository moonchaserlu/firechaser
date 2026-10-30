using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Shelter
{
    /// <summary>
    /// 靶场：安全屋里唯一能开枪的地方。玩家进入靶场范围时打开射击，离开时关闭。
    /// 靶子被打中会记录命中与伤害，并立即回满，不会被打坏。靶场用练习弹：子弹不消耗背包库存，但弹匣照常会打空、要换弹。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShelterRange : MonoBehaviour
    {
        [Tooltip("靶场范围（局部坐标，底面中心为原点）。")]
        [SerializeField] private Vector3 size = new Vector3(18f, 3.5f, 8f);
        [SerializeField] private RangeTarget[] targets = new RangeTarget[0];

        private PlayerCharacter player;

        public bool PlayerInside { get; private set; }
        public IReadOnlyList<RangeTarget> Targets => targets;
        public int TotalHits
        {
            get
            {
                var hits = 0;
                foreach (var target in targets) if (target != null) hits += target.Hits;
                return hits;
            }
        }

        public void Configure(Vector3 rangeSize, RangeTarget[] rangeTargets)
        {
            size = rangeSize;
            targets = rangeTargets;
        }

        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f && local.y >= -0.5f && local.y <= size.y;
        }

        public void ResetStats()
        {
            foreach (var target in targets) if (target != null) target.ResetStats();
        }

        private void Start()
        {
            player = FindAnyObjectByType<PlayerCharacter>();
            Apply(false);
        }

        private void Update()
        {
            if (player == null) return;
            var inside = Contains(player.transform.position);
            if (inside != PlayerInside) Apply(inside);
        }

        private void Apply(bool inside)
        {
            PlayerInside = inside;
            if (player != null && player.Shooter != null)
            {
                player.Shooter.enabled = inside && player.ControlEnabled;
                player.Shooter.FreeAmmo = inside;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
        }
    }
}
