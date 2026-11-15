using System;
using System.IO;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 玩家设置：写入 persistentDataPath 下的 JSON，与存档槽无关。
    /// 已生效：显示模式、分辨率、画质、垂直同步、主音量、后台静音。
    /// 只保存不生效（等对应系统接入）：亮度、音乐、音效、环境音。
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int CurrentVersion = 1;
        private const string FileName = "firechaser_settings.json";

        public static readonly string[] DisplayModeNames = { "全屏", "无边框窗口", "窗口" };
        private static readonly FullScreenMode[] DisplayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

        public int version = CurrentVersion;
        public int displayMode = 1;
        /// <summary>0 表示沿用当前屏幕分辨率。</summary>
        public int width;
        public int height;
        /// <summary>-1 表示沿用工程默认画质。</summary>
        public int quality = -1;
        public bool vSync = true;
        public int brightness = 70;
        public int masterVolume = 80;
        public int musicVolume = 60;
        public int sfxVolume = 85;
        public int ambienceVolume = 50;
        public bool muteInBackground = true;

        /// <summary>测试用：改写设置文件路径。</summary>
        public static string PathOverride;

        public static string SavePath => !string.IsNullOrEmpty(PathOverride) ? PathOverride : Path.Combine(Application.persistentDataPath, FileName);

        private static GameSettings current;

        /// <summary>当前设置；首次访问时从磁盘读取。</summary>
        public static GameSettings Current
        {
            get => current ??= Load();
            set => current = value;
        }

        public static GameSettings Load()
        {
            var path = SavePath;
            if (!File.Exists(path)) return new GameSettings();
            try
            {
                var settings = JsonUtility.FromJson<GameSettings>(File.ReadAllText(path)) ?? new GameSettings();
                settings.Clamp();
                return settings;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Settings] 设置文件无法读取，改用默认设置：{exception.Message}");
                return new GameSettings();
            }
        }

        public void Save()
        {
            Clamp();
            var path = SavePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public void Clamp()
        {
            displayMode = Mathf.Clamp(displayMode, 0, DisplayModes.Length - 1);
            brightness = Mathf.Clamp(brightness, 0, 100);
            masterVolume = Mathf.Clamp(masterVolume, 0, 100);
            musicVolume = Mathf.Clamp(musicVolume, 0, 100);
            sfxVolume = Mathf.Clamp(sfxVolume, 0, 100);
            ambienceVolume = Mathf.Clamp(ambienceVolume, 0, 100);
        }

        public FullScreenMode FullScreen => DisplayModes[Mathf.Clamp(displayMode, 0, DisplayModes.Length - 1)];

        /// <summary>
        /// 应用到引擎。编辑器里不改显示与画质：那会改动编辑器本身的状态。
        /// </summary>
        public void Apply()
        {
            AudioListener.volume = masterVolume / 100f;
            if (Application.isEditor) return;

            QualitySettings.vSyncCount = vSync ? 1 : 0;
            if (quality >= 0 && quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(quality, true);
            var w = width > 0 ? width : Screen.currentResolution.width;
            var h = height > 0 ? height : Screen.currentResolution.height;
            Screen.SetResolution(w, h, FullScreen);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => current = null;
    }
}
