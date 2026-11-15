using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>流血状态：命中刷新时长不叠层；流血、移动且不在有效光内时按间隔产生血迹。</summary>
    public sealed class BleedingModel
    {
        private float dropTimer;

        public BleedingModel(float duration, float dropInterval)
        {
            Duration = Mathf.Max(0.01f, duration);
            DropInterval = Mathf.Max(0.01f, dropInterval);
        }

        public float Duration { get; }
        public float DropInterval { get; }
        public float Remaining { get; private set; }
        public bool IsBleeding => Remaining > 0f;

        /// <summary>开始或刷新流血。</summary>
        public void Bleed()
        {
            Remaining = Duration;
        }

        public void Stop()
        {
            Remaining = 0f;
            dropTimer = 0f;
        }

        /// <summary>推进流血；返回本帧是否应留下一处血迹。</summary>
        public bool Tick(float deltaTime, bool moving, bool inLight)
        {
            if (!IsBleeding || deltaTime <= 0f)
            {
                return false;
            }

            Remaining = Mathf.Max(0f, Remaining - deltaTime);
            if (!moving || inLight)
            {
                return false;
            }

            dropTimer += deltaTime;
            if (dropTimer < DropInterval)
            {
                return false;
            }

            dropTimer -= DropInterval;
            return true;
        }
    }

    public readonly struct BloodMark
    {
        public BloodMark(int id, Vector3 position, float createdAt)
        {
            Id = id;
            Position = position;
            CreatedAt = createdAt;
        }

        public int Id { get; }
        public Vector3 Position { get; }
        public float CreatedAt { get; }
    }

    /// <summary>全场血迹：数量有上限、会过期；怪物按旧到新查询可察觉的血迹。不会生成敌人。</summary>
    public sealed class BloodTrailRegistry
    {
        private readonly List<BloodMark> marks = new List<BloodMark>();
        private int nextId = 1;

        public static BloodTrailRegistry Scene { get; } = new BloodTrailRegistry();

        public int MaxMarks { get; set; } = 12;
        public float Lifetime { get; set; } = 15f;
        public IReadOnlyList<BloodMark> Marks => marks;
        public int Count => marks.Count;

        public BloodMark Add(Vector3 position, float now)
        {
            Prune(now);
            while (marks.Count >= Mathf.Max(1, MaxMarks))
            {
                marks.RemoveAt(0);
            }

            var mark = new BloodMark(nextId++, position, now);
            marks.Add(mark);
            return mark;
        }

        public void Prune(float now)
        {
            marks.RemoveAll(mark => now - mark.CreatedAt >= Lifetime);
        }

        /// <summary>
        /// 找出 from 周围 range 内、晚于 after 产生、未被遮蔽的最旧血迹。
        /// </summary>
        public bool TryFindOldestAfter(Vector3 from, float range, float after, float now, Func<Vector3, bool> isMasked, out BloodMark found)
        {
            Prune(now);
            foreach (var mark in marks)
            {
                if (mark.CreatedAt <= after) continue;
                var delta = mark.Position - from;
                delta.y = 0f;
                if (delta.sqrMagnitude > range * range) continue;
                if (isMasked != null && isMasked(mark.Position)) continue;
                found = mark;
                return true;
            }

            found = default;
            return false;
        }

        public void Clear()
        {
            marks.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Scene.Clear();
        }
    }
}
