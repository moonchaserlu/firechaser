using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildernessExtraction
{
    public enum RaidOutcome
    {
        None,
        /// <summary>回到前哨或被救援撤离：背包带回安全屋。</summary>
        Returned,
        /// <summary>死亡：带出去的物资全部丢失。</summary>
        Died
    }

    /// <summary>
    /// 安全屋与地图之间的交接：出发时带上的物资、回来时带回的物资与结果。
    /// 只在一次进程内有效；地图场景直接打开（没有从安全屋出发）时不生效，保持原有单局玩法。
    /// </summary>
    public static class RaidTransfer
    {
        private static readonly List<ItemDefinition> loadout = new List<ItemDefinition>();
        private static readonly List<ItemDefinition> returned = new List<ItemDefinition>();

        /// <summary>本局是从安全屋出发的。</summary>
        public static bool Active { get; private set; }
        public static IReadOnlyList<ItemDefinition> Loadout => loadout;
        public static RaidOutcome PendingOutcome { get; private set; }
        public static IReadOnlyList<ItemDefinition> Returned => returned;
        public static bool HasPendingReturn => PendingOutcome != RaidOutcome.None;

        public static void Begin(IEnumerable<ItemDefinition> items)
        {
            loadout.Clear();
            loadout.AddRange(items);
            returned.Clear();
            PendingOutcome = RaidOutcome.None;
            Active = true;
        }

        /// <summary>地图一侧在本局结束时调用。</summary>
        public static void Finish(RaidOutcome outcome, IEnumerable<ItemDefinition> items)
        {
            if (!Active) return;
            returned.Clear();
            if (outcome == RaidOutcome.Returned && items != null) returned.AddRange(items);
            PendingOutcome = outcome;
            Active = false;
            loadout.Clear();
        }

        /// <summary>安全屋一侧取走结果；之后状态清空。</summary>
        public static (RaidOutcome outcome, List<ItemDefinition> items) Collect()
        {
            var result = (PendingOutcome, new List<ItemDefinition>(returned));
            PendingOutcome = RaidOutcome.None;
            returned.Clear();
            return result;
        }

        public static void Reset()
        {
            Active = false;
            PendingOutcome = RaidOutcome.None;
            loadout.Clear();
            returned.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Reset();
    }

    /// <summary>场景切换。编辑器里由编辑器脚本注入按路径加载（场景未登记 Build Settings 也能切换）。</summary>
    public static class GameScenes
    {
        public const string Shelter = "Assets/Shelter/Scenes/Shelter.unity";
        public const string Map01 = "Assets/Map01/Scenes/Map01.unity";
        /// <summary>开局教学关《最后一段路》：新存档先进这里，走完或跳过后进入 Map01。</summary>
        public const string Tutorial = "Assets/Tutorial/Scenes/Tutorial.unity";

        public static Action<string> LoadOverride;

        public static void Load(string scenePath)
        {
            Time.timeScale = 1f;
            if (LoadOverride != null)
            {
                LoadOverride(scenePath);
                return;
            }

            SceneManager.LoadScene(System.IO.Path.GetFileNameWithoutExtension(scenePath));
        }
    }
}
