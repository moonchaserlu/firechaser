using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>固定圆形撤离区：玩家在 XZ 半径内连续停留满时长即完成，离开则重置。</summary>
    public sealed class ExtractionZone : MonoBehaviour
    {
        [SerializeField] private RunRulesConfig rules;
        [SerializeField, Min(0.5f)] private float radius = 2.5f;
        [Tooltip("为空时自动查找场景中的 PlayerCharacter。")]
        [SerializeField] private PlayerCharacter player;

        private ExtractionTimer timer;

        public event Action ExtractionStarted;
        public event Action ExtractionInterrupted;
        public event Action ExtractionCompleted;

        public float Radius => radius;
        public float Duration => Timer.Duration;
        public float Elapsed => Timer.Elapsed;
        public float Progress => Timer.Progress;
        public bool IsExtracting => Timer.Elapsed > 0f && !Timer.IsComplete;

        private ExtractionTimer Timer => timer ??= new ExtractionTimer(rules.extractionDuration);

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerCharacter>();
            }
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            var inside = !player.Health.IsDead && Contains(player.transform.position);
            switch (Timer.Tick(inside, Time.deltaTime))
            {
                case ExtractionTick.Started:
                    ExtractionStarted?.Invoke();
                    break;
                case ExtractionTick.Interrupted:
                    ExtractionInterrupted?.Invoke();
                    break;
                case ExtractionTick.Completed:
                    ExtractionCompleted?.Invoke();
                    break;
            }
        }

        public bool Contains(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
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
