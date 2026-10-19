using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>可取消的计时动作，例如搜索容器。</summary>
    public sealed class TimedProgress
    {
        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsRunning { get; private set; }
        public float Progress => !IsRunning ? 0f : Duration <= 0f ? 1f : Mathf.Clamp01(Elapsed / Duration);

        public void Start(float duration)
        {
            Duration = Mathf.Max(0f, duration);
            Elapsed = 0f;
            IsRunning = true;
        }

        public void Cancel()
        {
            IsRunning = false;
            Elapsed = 0f;
        }

        /// <summary>推进计时；完成的那一次调用返回 true 并停止运行。</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsRunning)
            {
                return false;
            }

            Elapsed += Mathf.Max(0f, deltaTime);
            if (Elapsed < Duration)
            {
                return false;
            }

            IsRunning = false;
            Elapsed = Duration;
            return true;
        }
    }
}
