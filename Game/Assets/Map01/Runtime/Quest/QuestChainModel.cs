using System;
using System.Collections.Generic;

namespace WildernessExtraction.Map01
{
    public enum QuestStatus
    {
        NotStarted,
        Active,
        Completed
    }

    /// <summary>
    /// 顺序任务链的纯规则：任务按顺序开启，每个任务由一个“事实”完成。
    /// 事实可以在任务开启前获得（提前探索、提前阅读），开启时立即认可；同一事实只记一次，不会重复完成或重复发奖。
    /// 状态只在本局内存中，场景重载即重置。
    /// </summary>
    public sealed class QuestChainModel
    {
        private readonly string[] ids;
        private readonly string[] facts;
        private readonly QuestStatus[] status;
        private readonly HashSet<string> known = new HashSet<string>();

        public QuestChainModel(IReadOnlyList<(string id, string fact)> quests)
        {
            if (quests == null || quests.Count == 0) throw new ArgumentException("任务链为空", nameof(quests));
            ids = new string[quests.Count];
            facts = new string[quests.Count];
            status = new QuestStatus[quests.Count];
            for (var i = 0; i < quests.Count; i++)
            {
                ids[i] = quests[i].id;
                facts[i] = quests[i].fact;
            }
        }

        public event Action<int> QuestStarted;
        public event Action<int> QuestCompleted;
        public event Action ChainCompleted;

        public int Count => ids.Length;
        /// <summary>当前进行中的任务序号；尚未开始为 -1，全部完成为 Count。</summary>
        public int CurrentIndex { get; private set; } = -1;
        public bool IsStarted => CurrentIndex >= 0;
        public bool IsFinished => CurrentIndex >= ids.Length;
        public string CurrentId => CurrentIndex >= 0 && CurrentIndex < ids.Length ? ids[CurrentIndex] : null;
        public int CompletedCount { get; private set; }

        public string IdAt(int index) => ids[index];
        public QuestStatus StatusOf(int index) => status[index];
        public QuestStatus StatusOf(string id) => status[Array.IndexOf(ids, id)];
        public bool IsActive(string id) => CurrentId == id;
        public bool HasFact(string fact) => !string.IsNullOrEmpty(fact) && known.Contains(fact);

        /// <summary>开启第一个任务。重复调用无效。</summary>
        public void Begin()
        {
            if (IsStarted) return;
            Activate(0);
        }

        /// <summary>记录事实。首次记录返回 true；若满足当前任务则完成并顺序推进。</summary>
        public bool SetFact(string fact)
        {
            if (string.IsNullOrEmpty(fact) || !known.Add(fact)) return false;
            TryAdvance();
            return true;
        }

        private void Activate(int index)
        {
            CurrentIndex = index;
            if (index >= ids.Length)
            {
                ChainCompleted?.Invoke();
                return;
            }

            status[index] = QuestStatus.Active;
            QuestStarted?.Invoke(index);
            TryAdvance();
        }

        private void TryAdvance()
        {
            if (!IsStarted || IsFinished) return;
            var index = CurrentIndex;
            if (status[index] != QuestStatus.Active || !known.Contains(facts[index])) return;
            status[index] = QuestStatus.Completed;
            CompletedCount++;
            QuestCompleted?.Invoke(index);
            Activate(index + 1);
        }
    }
}
