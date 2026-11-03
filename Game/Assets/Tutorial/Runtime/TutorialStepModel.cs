using System;
using System.Collections.Generic;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 教学关的纯规则：各段按顺序进行，每段需要的“事实”全部到齐才完成。
    /// 事实可以提前获得（例如先打开过背包），轮到那一段时直接认可；同一事实只记一次。
    /// 状态只在本局内存中，场景重载即重置。
    /// </summary>
    public sealed class TutorialStepModel
    {
        private readonly string[] ids;
        private readonly string[][] required;
        private readonly HashSet<string> known = new HashSet<string>();

        public TutorialStepModel(IReadOnlyList<(string id, string[] facts)> steps)
        {
            if (steps == null || steps.Count == 0) throw new ArgumentException("教学步骤为空", nameof(steps));
            ids = new string[steps.Count];
            required = new string[steps.Count][];
            for (var i = 0; i < steps.Count; i++)
            {
                ids[i] = steps[i].id;
                required[i] = steps[i].facts ?? Array.Empty<string>();
            }
        }

        /// <summary>参数为刚完成的那一段的序号。</summary>
        public event Action<int> StepCompleted;
        public event Action Finished;

        public int Count => ids.Length;
        /// <summary>当前进行中的段落序号；全部完成为 Count。</summary>
        public int CurrentIndex { get; private set; }
        public bool IsFinished => CurrentIndex >= ids.Length;
        public string CurrentId => IsFinished ? null : ids[CurrentIndex];
        public string IdAt(int index) => ids[index];
        public bool HasFact(string fact) => !string.IsNullOrEmpty(fact) && known.Contains(fact);

        /// <summary>当前段落已经到齐的事实数 / 需要的事实数。</summary>
        public (int done, int total) Progress()
        {
            if (IsFinished) return (0, 0);
            var done = 0;
            foreach (var fact in required[CurrentIndex])
            {
                if (known.Contains(fact)) done++;
            }

            return (done, required[CurrentIndex].Length);
        }

        /// <summary>记下一个事实；可能连续完成多段。返回这次是否让任何一段完成。</summary>
        public bool Report(string fact)
        {
            if (string.IsNullOrEmpty(fact) || !known.Add(fact)) return false;
            return Advance();
        }

        /// <summary>直接跳到全部完成（跳过教学）。</summary>
        public void Skip()
        {
            if (IsFinished) return;
            CurrentIndex = ids.Length;
            Finished?.Invoke();
        }

        private bool Advance()
        {
            var advanced = false;
            while (!IsFinished && IsSatisfied(CurrentIndex))
            {
                var done = CurrentIndex++;
                advanced = true;
                StepCompleted?.Invoke(done);
                if (IsFinished) Finished?.Invoke();
            }

            return advanced;
        }

        private bool IsSatisfied(int index)
        {
            foreach (var fact in required[index])
            {
                if (!known.Contains(fact)) return false;
            }

            return true;
        }
    }
}
