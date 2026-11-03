using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>一个存档槽的摘要：主菜单显示用。槽内的局外进度（仓库等）由 PlayerProfile 按槽位单独存放。</summary>
    [Serializable]
    public sealed class SaveSlotInfo
    {
        public int version = SaveSlots.CurrentVersion;
        public int slot;
        public string scenePath;
        /// <summary>第几次进入游戏（新建为 1，每次读档 +1）。</summary>
        public int sessions;
        public double playSeconds;
        /// <summary>UTC，ISO 8601 往返格式。</summary>
        public string createdAt;
        public string lastPlayedAt;

        public DateTime LastPlayed => Parse(lastPlayedAt);
        public DateTime Created => Parse(createdAt);

        private static DateTime Parse(string value) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : DateTime.MinValue;
    }

    /// <summary>
    /// 存档槽：每槽一个摘要 JSON + 一个 PlayerProfile JSON，都在 persistentDataPath 下。
    /// ActiveSlot 为 0 表示没有经过主菜单（编辑器里直接打开场景），此时 PlayerProfile 沿用旧的单文件存档。
    /// </summary>
    public static class SaveSlots
    {
        public const int CurrentVersion = 1;
        public const int SlotCount = 3;

        /// <summary>测试用：改写存档目录，避免碰到玩家真实存档。</summary>
        public static string DirectoryOverride;

        public static int ActiveSlot { get; private set; }

        public static string Directory => !string.IsNullOrEmpty(DirectoryOverride) ? DirectoryOverride : Application.persistentDataPath;

        public static string InfoPath(int slot) => Path.Combine(Directory, $"firechaser_slot{Check(slot)}.json");
        public static string ProfilePath(int slot) => Path.Combine(Directory, $"firechaser_slot{Check(slot)}_profile.json");

        public static bool Exists(int slot) => File.Exists(InfoPath(slot));

        /// <summary>读取槽位摘要；空槽或文件损坏时返回 null。</summary>
        public static SaveSlotInfo Load(int slot)
        {
            var path = InfoPath(slot);
            if (!File.Exists(path)) return null;
            try
            {
                var info = JsonUtility.FromJson<SaveSlotInfo>(File.ReadAllText(path));
                if (info == null) return null;
                info.slot = slot;
                return info;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SaveSlots] 存档槽 {slot} 无法读取：{exception.Message}");
                return null;
            }
        }

        public static SaveSlotInfo[] LoadAll()
        {
            var slots = new SaveSlotInfo[SlotCount];
            for (var i = 0; i < SlotCount; i++) slots[i] = Load(i + 1);
            return slots;
        }

        /// <summary>第一个空槽；全满时返回 0。</summary>
        public static int FirstEmpty()
        {
            for (var slot = 1; slot <= SlotCount; slot++)
            {
                if (!Exists(slot)) return slot;
            }

            return 0;
        }

        /// <summary>最近玩过的槽位；没有存档时返回 0。</summary>
        public static int MostRecent()
        {
            var best = 0;
            var bestTime = DateTime.MinValue;
            foreach (var info in LoadAll())
            {
                if (info == null || (best != 0 && info.LastPlayed <= bestTime)) continue;
                best = info.slot;
                bestTime = info.LastPlayed;
            }

            return best;
        }

        /// <summary>在空槽建立新存档并设为当前槽位。</summary>
        public static SaveSlotInfo CreateNew(int slot, string scenePath, DateTime nowUtc)
        {
            Check(slot);
            if (Exists(slot)) throw new InvalidOperationException($"存档槽 {slot} 已有存档。");
            if (File.Exists(ProfilePath(slot))) File.Delete(ProfilePath(slot));
            var stamp = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            var info = new SaveSlotInfo { slot = slot, scenePath = scenePath, sessions = 1, createdAt = stamp, lastPlayedAt = stamp };
            Write(info);
            ActiveSlot = slot;
            return info;
        }

        /// <summary>读档：记一次进入并设为当前槽位。</summary>
        public static SaveSlotInfo Resume(int slot, DateTime nowUtc)
        {
            var info = Load(slot) ?? throw new InvalidOperationException($"存档槽 {slot} 是空的。");
            info.sessions++;
            info.lastPlayedAt = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            Write(info);
            ActiveSlot = slot;
            return info;
        }

        /// <summary>给当前槽位累计游戏时长。</summary>
        public static void AddPlayTime(double seconds, DateTime nowUtc)
        {
            if (ActiveSlot == 0 || seconds <= 0) return;
            var info = Load(ActiveSlot);
            if (info == null) return;
            info.playSeconds += seconds;
            info.lastPlayedAt = nowUtc.ToString("o", CultureInfo.InvariantCulture);
            Write(info);
        }

        /// <summary>改写当前槽位下次读档进入的场景（例如教学完成后改为 Map01）。没有当前槽位时不做任何事。</summary>
        public static void SetActiveScene(string scenePath)
        {
            if (ActiveSlot == 0) return;
            var info = Load(ActiveSlot);
            if (info == null) return;
            info.scenePath = scenePath;
            Write(info);
        }

        public static void Delete(int slot)
        {
            if (File.Exists(InfoPath(slot))) File.Delete(InfoPath(slot));
            if (File.Exists(ProfilePath(slot))) File.Delete(ProfilePath(slot));
            if (ActiveSlot == slot) ActiveSlot = 0;
        }

        public static void ClearActive() => ActiveSlot = 0;

        public static string FormatPlayTime(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        private static void Write(SaveSlotInfo info)
        {
            var path = InfoPath(info.slot);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            // 先写临时文件再替换，避免写一半时断电把存档写坏。
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(info, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static int Check(int slot)
        {
            if (slot < 1 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            return slot;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ActiveSlot = 0;
    }
}
