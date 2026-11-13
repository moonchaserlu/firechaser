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

        /// <summary>外部中断（例如受伤）：清空进度。返回是否确实打断了进行中的计时。</summary>
        public bool Interrupt()
        {
            if (IsComplete || Elapsed <= 0f)
            {
                return false;
            }

            Elapsed = 0f;
            return true;
        }
    }

    /// <summary>返程开关：玩家离开安全屋足够远之后返程区才启用，防止出生即结算。一旦启用本局保持启用。</summary>
    public sealed class ReturnGate
    {
        public ReturnGate(float armDistance)
        {
            ArmDistance = Mathf.Max(0f, armDistance);
        }

        public float ArmDistance { get; }
        public bool IsArmed { get; private set; }

        /// <summary>传入玩家到安全屋入口的水平距离；首次超过启用距离时返回 true。</summary>
        public bool Update(float distanceFromHome)
        {
            if (IsArmed || distanceFromHome <= ArmDistance)
            {
                return false;
            }

            IsArmed = true;
            return true;
        }
    }
}
