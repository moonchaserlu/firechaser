using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 局外存档：地下安全屋的仓库与远行次数。写入本地 JSON，关掉游戏也保留。
    /// 局内状态不进存档；中途退出视为放弃本局。
    /// </summary>
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;
        public const int DefaultStashCapacity = 24;
        private const string FileName = "firechaser_profile.json";

        [Serializable]
        private sealed class ItemCount
        {
            public string itemId;
            public int count;
        }

        [Serializable]
        private sealed class Data
        {
            public int version = CurrentVersion;
            public int credits;
            public int stashCapacity = DefaultStashCapacity;
            public int completedRaids;
            public List<ItemCount> stash = new List<ItemCount>();
        }

        /// <summary>测试用：改写存档路径，避免碰到玩家真实存档。</summary>
        public static string PathOverride;

        private PlayerProfile(int stashCapacity)
        {
            Stash = new Inventory(Mathf.Max(1, stashCapacity));
        }

        /// <summary>从主菜单进入时按存档槽分开存放；直接打开场景时沿用旧的单文件存档。</summary>
        public static string SavePath => !string.IsNullOrEmpty(PathOverride) ? PathOverride
            : SaveSlots.ActiveSlot > 0 ? SaveSlots.ProfilePath(SaveSlots.ActiveSlot)
            : Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>仓库：按格计容量，与背包同一套规则。</summary>
        public Inventory Stash { get; private set; }
        public int Credits { get; private set; }
        public int CompletedRaids { get; private set; }
        /// <summary>存档里有、但物品库找不到的条目数（例如物品被改名）。</summary>
        public int UnknownItems { get; private set; }

        public static PlayerProfile CreateNew() => new PlayerProfile(DefaultStashCapacity);

        /// <summary>读取存档；没有存档或文件损坏时返回新档。</summary>
        public static PlayerProfile LoadOrCreate(ItemDatabase database)
        {
            var path = SavePath;
            if (!File.Exists(path))
            {
                return CreateNew();
            }

            Data data;
            try
            {
                data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Profile] 存档无法读取，改用新档：{exception.Message}");
                return CreateNew();
            }

            if (data == null)
            {
                return CreateNew();
            }

            var profile = new PlayerProfile(data.stashCapacity) { Credits = Mathf.Max(0, data.credits), CompletedRaids = data.completedRaids };
            foreach (var entry in data.stash)
            {
                var item = database != null ? database.Find(entry.itemId) : null;
                if (item == null)
                {
                    profile.UnknownItems += entry.count;
                    continue;
                }

                for (var i = 0; i < entry.count; i++)
                {
                    profile.Stash.TryAdd(item);
                }
            }

            return profile;
        }

        public void Save()
        {
            var data = new Data { credits = Credits, stashCapacity = Stash.Capacity, completedRaids = CompletedRaids };
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var item in Stash.Items)
            {
                if (!counts.ContainsKey(item.itemId))
                {
                    counts[item.itemId] = 0;
                    order.Add(item.itemId);
                }

                counts[item.itemId]++;
            }

            foreach (var id in order)
            {
                data.stash.Add(new ItemCount { itemId = id, count = counts[id] });
            }

            var path = SavePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // 先写临时文件再替换，避免写一半时断电把存档写坏。
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public void AddCredits(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Credits += amount;
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Credits < amount) return false;
            Credits -= amount;
            return true;
        }

        public void RecordRaid() => CompletedRaids++;

        /// <summary>扩容仓库，已有物品保留。</summary>
        public void ExpandStash(int extraSlots) => Stash.Expand(extraSlots);
    }
}
