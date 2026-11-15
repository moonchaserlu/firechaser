using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WildernessExtraction
{
    public enum GameLanguage
    {
        Chinese = 0,
        English = 1,
    }

    /// <summary>
    /// 界面文字本地化。中文原文就是键：代码和资产里照旧写中文，显示前过一遍 <see cref="T"/>；
    /// 切到英文时查 Resources/Localization_en.txt 里的对照表，查不到就原样显示中文。
    /// 当前语言存在 <see cref="GameSettings.language"/>（-1 = 跟随系统：系统是中文用中文，否则英文）。
    /// </summary>
    public static class Loc
    {
        public const string TableResource = "Localization_en";

        private static Dictionary<string, string> english;
        private static GameLanguage? overrideLanguage;
        private static readonly HashSet<string> missing = new HashSet<string>();

        /// <summary>语言切换后触发（设置里切换、测试覆盖）。</summary>
        public static event Action Changed;

        public static GameLanguage Language
        {
            get
            {
                if (overrideLanguage.HasValue) return overrideLanguage.Value;
                return Resolve(GameSettings.Current.language);
            }
        }

        public static bool IsEnglish => Language == GameLanguage.English;

        /// <summary>设置值 → 实际语言：-1 跟随系统。</summary>
        public static GameLanguage Resolve(int setting)
        {
            if (setting == (int)GameLanguage.Chinese) return GameLanguage.Chinese;
            if (setting == (int)GameLanguage.English) return GameLanguage.English;
            return IsChineseSystem(Application.systemLanguage) ? GameLanguage.Chinese : GameLanguage.English;
        }

        public static bool IsChineseSystem(SystemLanguage language) =>
            language == SystemLanguage.Chinese || language == SystemLanguage.ChineseSimplified || language == SystemLanguage.ChineseTraditional;

        /// <summary>切换语言并写入设置文件。</summary>
        public static void SetLanguage(GameLanguage language)
        {
            var settings = GameSettings.Current;
            settings.language = (int)language;
            settings.Save();
            Changed?.Invoke();
        }

        /// <summary>通知界面重新取文字（设置面板整体保存后调用）。</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>测试用：临时指定语言（"en" / "zh"），null 恢复为按设置。不写设置文件。</summary>
        public static void SetLanguageForTests(string code)
        {
            overrideLanguage = code switch
            {
                "en" => GameLanguage.English,
                "zh" => GameLanguage.Chinese,
                _ => (GameLanguage?)null,
            };
            Changed?.Invoke();
        }

        /// <summary>中文原文 → 当前语言。</summary>
        public static string T(string chinese)
        {
            if (string.IsNullOrEmpty(chinese) || !IsEnglish || !HasCjk(chinese)) return chinese;
            var table = English;
            if (table.TryGetValue(chinese, out var text)) return text;
            if (Application.isEditor && missing.Add(chinese)) Debug.LogWarning($"[Loc] 缺少英文：{Escape(chinese)}");
            return chinese;
        }

        /// <summary>先翻译格式串再填参数：Loc.F("带回 {0} 件", n)。</summary>
        public static string F(string chineseFormat, params object[] args) => string.Format(T(chineseFormat), args);

        /// <summary>是否有英文对照（测试用）。</summary>
        public static bool HasEnglish(string chinese) => string.IsNullOrEmpty(chinese) || English.ContainsKey(chinese);

        /// <summary>把整段文字里所有能匹配的行逐行翻译（多行提示由多句拼成时用）。</summary>
        public static string Lines(string chinese)
        {
            if (string.IsNullOrEmpty(chinese) || !IsEnglish) return chinese;
            if (English.ContainsKey(chinese) || chinese.IndexOf('\n') < 0) return T(chinese);
            var parts = chinese.Split('\n');
            for (var i = 0; i < parts.Length; i++) parts[i] = T(parts[i]);
            return string.Join("\n", parts);
        }

        private static Dictionary<string, string> English
        {
            get
            {
                if (english != null) return english;
                var asset = Resources.Load<TextAsset>(TableResource);
                english = asset != null ? Parse(asset.text) : new Dictionary<string, string>();
                return english;
            }
        }

        /// <summary>
        /// 对照表格式：一行 zh=原文，紧接一行 en=译文；# 开头是注释；\n 表示换行。
        /// </summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var table = new Dictionary<string, string>();
            string pending = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("zh=", StringComparison.Ordinal))
                {
                    pending = Unescape(line.Substring(3));
                }
                else if (line.StartsWith("en=", StringComparison.Ordinal) && pending != null)
                {
                    table[pending] = Unescape(line.Substring(3));
                    pending = null;
                }
            }

            return table;
        }

        /// <summary>含中日韩文字或全角标点。不含的字符串（数字、按键名、已经是英文）直接原样显示。</summary>
        public static bool HasCjk(string text)
        {
            foreach (var c in text)
            {
                if ((c >= '一' && c <= '鿿') || (c >= '　' && c <= '〿') || (c >= '＀' && c <= '￯')) return true;
            }

            return false;
        }

        public static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\n", "\\n");

        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0) return text;
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    var next = text[++i];
                    builder.Append(next == 'n' ? '\n' : next);
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            english = null;
            missing.Clear();
        }
    }
}
