using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    [Serializable]
    public sealed class QuestDefinition
    {
        [Tooltip("任务 ID（Q01–Q10）。任务导演按 ID 识别每个任务的判定方式，不要改。")]
        public string questId;
        public string title;
        [Tooltip("任务说明（玩家目标）。")]
        [TextArea(1, 3)] public string goal;
        [Tooltip("完成该任务需要的事实。事实可以在任务开启前就已获得（提前探索），开启时直接认可。")]
        public string completionFact;
        [Tooltip("当前一步目标。任务导演按进度选用其中一条，每个任务的含义见 Docs/Map01/Map01_任务链_Q01-Q10.md。")]
        [TextArea(1, 2)] public string[] steps = Array.Empty<string>();
        [Tooltip("完成提示。")]
        [TextArea(1, 3)] public string completionText;

        public string Step(int index)
        {
            if (steps == null || steps.Length == 0) return goal;
            return steps[Mathf.Clamp(index, 0, steps.Length - 1)];
        }
    }

    [Serializable]
    public sealed class QuestRecordEntry
    {
        [Tooltip("记录 ID，同时是场景里交互点的实体 ID。")]
        public string recordId;
        public string title;
        [TextArea(3, 12)] public string body;
        [Tooltip("记录在世界中的位置（场景生成时摆放）。")]
        public Vector3 position;
        [Tooltip("阅读后获得的事实。")]
        public string fact;
    }

    [Serializable]
    public sealed class QuestArea
    {
        public string label;
        public Vector3 center;
        [Min(0.5f)] public float radius = 6f;

        public bool Contains(Vector3 position)
        {
            var delta = position - center;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }
    }

    [Serializable]
    public sealed class QuestSupply
    {
        [Tooltip("该保底物资服务的任务。只在任务进行中、玩家身上没有且场上没有剩余时刷出。")]
        public string questId;
        public ItemDefinition item;
        public string label = "物资";
        public Vector3 position;
    }

    /// <summary>
    /// 《向火而生》Map01 前 10 个任务的可配置数据：任务名、目标文案、剧情记录、区域与保底物资位置。
    /// 只在缺失时由编辑器生成默认值，之后由策划直接改；交互点位置改动后需要重建 Map01 场景。
    /// </summary>
    [CreateAssetMenu(menuName = "Wilderness/Map01/Quest Chain", fileName = "Map01_QuestChain")]
    public sealed class Map01QuestChain : ScriptableObject
    {
        public List<QuestDefinition> quests = new List<QuestDefinition>();
        public List<QuestRecordEntry> records = new List<QuestRecordEntry>();
        public List<QuestSupply> supplies = new List<QuestSupply>();

        [Header("开局")]
        [Tooltip("任务模式开局收走玩家初始携带的燃料（燃料即将耗尽），让 Q01 需要去物资点取燃料。关闭则玩家开局即可提交。")]
        public bool stripStartingFuel = true;

        [Header("庇护所")]
        public Vector3 hearthPosition;
        [Tooltip("开场炉火的有效安全光半径（米）。室内电灯恢复前，庇护所只有这一圈是安全的。")]
        [Min(1f)] public float hearthRadius = 5f;
        public Vector3 oldMapPosition;

        [Header("区域")]
        [Tooltip("Q06：过桥后到达的对岸区域。")]
        public QuestArea bridgeArrival = new QuestArea();
        [Tooltip("Q08：桥头可建火区域。")]
        public QuestArea campfireSite = new QuestArea();

        [Header("文案")]
        [TextArea(1, 3)] public string chainCompleteText = "本地图第一段故事完成。";
        [TextArea(1, 2)] public string hearthIdleText = "炉火还在烧。";
        [TextArea(1, 2)] public string oldMapIdleText = "一张旧地图，标着附近几处地名。现在还看不出什么。";

        public QuestDefinition Find(string questId) => quests.Find(q => q.questId == questId);
        public QuestRecordEntry FindRecord(string recordId) => records.Find(r => r.recordId == recordId);
    }
}
