using System;
using UnityEngine;

namespace WildernessExtraction
{
    public enum ExtractionTick
    {
        Idle,
        Started,
        Progressing,
        Interrupted,
        Completed
    }

    /// <summary>撤离计时：区域内连续停留达到时长即完成，中途离开则重置。</summary>
    public sealed class ExtractionTimer
    {
        public ExtractionTimer(float duration)
        {
            if (duration <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            Duration = duration;
        }

        public float Duration { get; }
        public float Elapsed { get; private set; }
        public bool IsComplete { get; private set; }
        public float Progress => Mathf.Clamp01(Elapsed / Duration);

        public ExtractionTick Tick(bool inside, float deltaTime)
        {
            if (IsComplete)
            {
                return ExtractionTick.Idle;
            }

            if (!inside)
            {
                if (Elapsed <= 0f)
                {
                    return ExtractionTick.Idle;
                }

                Elapsed = 0f;
                return ExtractionTick.Interrupted;
            }

            var wasIdle = Elapsed <= 0f;
            Elapsed += Mathf.Max(0f, deltaTime);
            if (Elapsed >= Duration)
            {
                Elapsed = Duration;
                IsComplete = true;
                return ExtractionTick.Completed;
            }

            return wasIdle ? ExtractionTick.Started : ExtractionTick.Progressing;
        }
    }
}
