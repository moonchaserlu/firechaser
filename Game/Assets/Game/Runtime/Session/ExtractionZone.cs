using System;
using UnityEngine;

namespace WildernessExtraction
{
    public enum ReturnInterruptReason
    {
        LeftZone,
        Damaged,
    }

    /// <summary>
    /// 安全屋入口返程区：玩家离家足够远后才启用；之后在 XZ 半径内连续停留满时长即结算，
    /// 离开区域或受到伤害会中断并重置。
    /// </summary>
    public sealed class ExtractionZone : MonoBehaviour
    {
        [SerializeField] private RunRulesConfig rules;
        [SerializeField, Min(0.5f)] private float radius = 2.5f;
        [Tooltip("为空时自动查找场景中的 PlayerCharacter。")]
        [SerializeField] private PlayerCharacter player;
        [Tooltip("是否要求玩家先离开安全屋（返程启用距离见 RunRulesConfig）。")]
        [SerializeField] private bool requireLeaveFirst = true;

        private ExtractionTimer timer;
        private ReturnGate gate;
        private Health subscribedHealth;

        public event Action ExtractionStarted;
        public event Action ExtractionInterrupted;
        public event Action ExtractionCompleted;
        /// <summary>玩家离家足够远，返程区启用。</summary>
        public event Action Armed;

        public float Radius => radius;
        public float Duration => Timer.Duration;
        public float Elapsed => Timer.Elapsed;
        public float Progress => Timer.Progress;
        public bool IsExtracting => Timer.Elapsed > 0f && !Timer.IsComplete;
        public bool IsArmed => !requireLeaveFirst || Gate.IsArmed;
        public ReturnInterruptReason LastInterruptReason { get; private set; }

        private ExtractionTimer Timer => timer ??= new ExtractionTimer(rules.extractionDuration);
        private ReturnGate Gate => gate ??= new ReturnGate(rules.returnArmDistance);

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerCharacter>();
            }
        }

        private void OnEnable()
        {
            if (player != null)
            {
                subscribedHealth = player.Health;
                subscribedHealth.Damaged += OnPlayerDamaged;
            }
        }

        private void OnDisable()
        {
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged -= OnPlayerDamaged;
                subscribedHealth = null;
            }
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            if (requireLeaveFirst && Gate.Update(HorizontalDistance(player.transform.position)))
            {
                Armed?.Invoke();
            }

            var inside = IsArmed && !player.Health.IsDead && Contains(player.transform.position);
            switch (Timer.Tick(inside, Time.deltaTime))
            {
                case ExtractionTick.Started:
                    ExtractionStarted?.Invoke();
                    break;
                case ExtractionTick.Interrupted:
                    LastInterruptReason = ReturnInterruptReason.LeftZone;
                    ExtractionInterrupted?.Invoke();
                    break;
                case ExtractionTick.Completed:
                    ExtractionCompleted?.Invoke();
                    break;
            }
        }

        private void OnPlayerDamaged(float amount)
        {
            if (Timer.Interrupt())
            {
                LastInterruptReason = ReturnInterruptReason.Damaged;
                ExtractionInterrupted?.Invoke();
            }
        }

        public bool Contains(Vector3 position)
        {
            return HorizontalDistance(position) <= radius;
        }

        private float HorizontalDistance(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            const int segments = 32;
            var previous = transform.position + new Vector3(radius, 0.05f, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var next = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
