using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static WildernessExtraction.UiKit;

namespace WildernessExtraction
{
    /// <summary>
    /// 游戏内设置窗口。版式照 we-ll-be-right-back 的 SettingsPanel（1280×720 参考分辨率、1040×640 窗口）：
    /// 标题栏（设置 SETTINGS · 中文/EN 快切 · ×）、左侧分页（通用 / 音频 / 画面 / 按键）、右侧内容、
    /// 底栏（恢复默认 · 取消 · 应用）。改动先记在副本里，按“应用”才写盘生效；语言切换立即生效。
    /// </summary>
    public sealed class SettingsWindow : MonoBehaviour
    {
        private const float W = 1040f, H = 640f, NavW = 208f, BodyX = 241f, BodyY = 93f, BodyW = 768f;
        private static readonly string[] TabNames = { "通用", "音频", "画面", "按键" };

        public static SettingsWindow Active { get; private set; }
        public static bool IsOpen => Active != null && Active.window != null && Active.window.activeInHierarchy;
        /// <summary>本帧 Esc 已被设置窗口用掉（关闭窗口），别的界面不要再响应。</summary>
        public static int EscFrame { get; private set; } = -1;

        private Canvas canvas;
        private GameObject window;
        private readonly List<Button> tabs = new List<Button>();
        private readonly List<GameObject> pages = new List<GameObject>();
        private int tab;
        private Action onClose;
        private GameSettings editing;
        private List<Vector2Int> resolutions;

        private Button quickZh, quickEn, languageZh, languageEn;
        private Slider master, music, sfx, ambience, brightness;
        private Text masterValue, musicValue, sfxValue, ambienceValue, brightnessValue;
        private Button muteOn, muteOff, vsyncOn, vsyncOff;
        private Text displayValue, resolutionValue, qualityValue;

        public int Tab => tab;
        public GameSettings Editing => editing;

        public static SettingsWindow Ensure()
        {
            if (Active != null) return Active;
            var go = new GameObject("[SettingsWindow]");
            return go.AddComponent<SettingsWindow>();
        }

        private void Awake()
        {
            Active = this;
            Build();
            window.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        public void Open(Action closed)
        {
            onClose = closed;
            editing = GameSettings.Current.Clone();
            BuildResolutions();
            window.SetActive(true);
            SelectTab(tab);
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            window.SetActive(false);
            editing = null;
            var callback = onClose;
            onClose = null;
            callback?.Invoke();
        }

        public void Apply()
        {
            if (editing == null) return;
            editing.Save();
            GameSettings.Current = editing.Clone();
            GameSettings.Current.Apply();
            Loc.NotifyChanged();
            Close();
        }

        public void ResetDefaults()
        {
            if (editing == null) return;
            editing = new GameSettings { language = editing.language };
            Refresh();
        }

        public void SelectTab(int index)
        {
            tab = (index % TabNames.Length + TabNames.Length) % TabNames.Length;
            for (var i = 0; i < pages.Count; i++) pages[i].SetActive(i == tab);
            Refresh();
        }

        public void SetLanguage(GameLanguage language)
        {
            if (editing != null) editing.language = (int)language;
            Loc.SetLanguage(language);
            Refresh();
        }

        private void Update()
        {
            if (!IsOpen) return;
            var k = Keyboard.current;
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame)
            {
                EscFrame = Time.frameCount;
                Close();
                return;
            }

            if (k.qKey.wasPressedThisFrame) SelectTab(tab - 1);
            if (k.eKey.wasPressedThisFrame) SelectTab(tab + 1);
        }

        // ---------- 搭建 ----------

        private void Build()
        {
            canvas = UiKit.Canvas("SettingsCanvas", transform, 1000, 1280, 720);
            var dim = Box("Window", canvas.transform, new Color(0.02f, 0.012f, 0.008f, 0.72f), null, false);
            Stretch(dim.transform);
            window = dim.gameObject;
            var frame = Box("Frame", dim.transform, UiKit.Window, Line).transform;
            Center(frame, W, H);

            // 标题栏
            TitleRow(frame, "设置", "SETTINGS");
            var quick = Box("LanguageQuick", frame, Well, Line).transform;
            Place(quick, 838, 15, 118, 38);
            quickZh = UiKit.Button("Zh", quick, "中文", ButtonKind.Ghost, () => SetLanguage(GameLanguage.Chinese), 3, 3, 54, 32, 14);
            quickEn = UiKit.Button("En", quick, "EN", ButtonKind.Ghost, () => SetLanguage(GameLanguage.English), 61, 3, 54, 32, 14);
            UiKit.Button("Close", frame, "×", ButtonKind.Ghost, Close, 972, 12, 44, 44, 24);
            Rule(frame, 0, 68, W, 1, Line);

            // 左侧分页
            var nav = Box("Nav", frame, Nav, null, false).transform;
            Place(nav, 0, 69, NavW, 500);
            Rule(frame, NavW, 69, 1, 500, Line);
            for (var i = 0; i < TabNames.Length; i++)
            {
                var index = i;
                var t = UiKit.Button("Tab" + i, nav, "", ButtonKind.Ghost, () => SelectTab(index), 14, 20 + i * 54, 180, 48, 15);
                var mark = Box("Mark", t.transform, Amber, null, false);
                Place(mark.transform, 14, 15, 4, 18);
                Label("Label", t.transform, TabNames[i], 15, Text1, 30, 0, 140, 48);
                tabs.Add(t);
            }

            var body = Node("Body", frame);
            Place(body, BodyX, BodyY, BodyW, 452);
            pages.Add(GeneralPage(body));
            pages.Add(AudioPage(body));
            pages.Add(DisplayPage(body));
            pages.Add(KeysPage(body));

            // 底栏
            Rule(frame, 0, 569, W, 1, Line);
            UiKit.Button("Reset", frame, "恢复默认", ButtonKind.Ghost, ResetDefaults, 24, 583, 150, 44, 14);
            UiKit.Button("Cancel", frame, "取消", ButtonKind.Secondary, Close, 768, 583, 108, 44, 15);
            UiKit.Button("Apply", frame, "应用", ButtonKind.Primary, Apply, 888, 583, 128, 44, 15);
        }

        private static RectTransform Page(string name, Transform body)
        {
            var page = Node(name, body);
            Stretch(page);
            return page;
        }

        private GameObject GeneralPage(Transform body)
        {
            var page = Page("GeneralPage", body);
            Label("Head", page, "界面语言", 13, Text2, 0, 0, 560, 20);
            languageZh = LanguageOption(page, "LanguageZh", "简体中文", "界面与提示使用中文", "ZH-CN", 32, () => SetLanguage(GameLanguage.Chinese));
            languageEn = LanguageOption(page, "LanguageEn", "English", "Interface and hints in English", "EN-US", 124, () => SetLanguage(GameLanguage.English));
            Label("Hint", page, "语言切换立即生效，不用按“应用”。", 13, Text2, 0, 216, 560, 40, TextAnchor.UpperLeft);
            return page.gameObject;
        }

        private GameObject AudioPage(Transform body)
        {
            var page = Page("AudioPage", body);
            Label("VolumeHead", page, "音量", 13, Text2, 0, 0, 368, 20);
            master = SliderRow(page, "主音量", 0, 28, 368, out masterValue, v => editing.masterVolume = v);
            music = SliderRow(page, "音乐", 0, 88, 368, out musicValue, v => editing.musicVolume = v);
            sfx = SliderRow(page, "音效", 0, 148, 368, out sfxValue, v => editing.sfxVolume = v);
            ambience = SliderRow(page, "环境音", 0, 208, 368, out ambienceValue, v => editing.ambienceVolume = v);
            const float x = 400, w = 368;
            Label("OtherHead", page, "其他", 13, Text2, x, 0, w, 20);
            Label("MuteLabel", page, "切到后台时静音", 15, Text1, x, 28, w, 24);
            muteOn = UiKit.Button("MuteOn", page, "开", ButtonKind.Secondary, () => { editing.muteInBackground = true; Refresh(); }, x, 56, 180, 40, 14);
            muteOff = UiKit.Button("MuteOff", page, "关", ButtonKind.Secondary, () => { editing.muteInBackground = false; Refresh(); }, x + 188, 56, 180, 40, 14);
            var tip = Box("Tip", page, new Color(0.17f, 0.1f, 0.04f), new Color(0.38f, 0.24f, 0.1f)).transform;
            Place(tip, x, 120, w, 64);
            Label("Text", tip, "黑暗里要靠声音判断怪物的方位，环境音不要调得太低。", 13, new Color(0.96f, 0.85f, 0.64f), 16, 8, w - 32, 48);
            return page.gameObject;
        }

        private GameObject DisplayPage(Transform body)
        {
            var page = Page("DisplayPage", body);
            FormRow(page, "显示模式", 0);
            displayValue = Stepper("DisplayMode", page, 448, 12, 320, 40, () => Step(0, -1), () => Step(0, 1));
            FormRow(page, "分辨率", 64);
            resolutionValue = Stepper("Resolution", page, 448, 76, 320, 40, () => Step(1, -1), () => Step(1, 1));
            FormRow(page, "画质", 128);
            qualityValue = Stepper("Quality", page, 448, 140, 320, 40, () => Step(2, -1), () => Step(2, 1));
            FormRow(page, "垂直同步", 192);
            vsyncOn = UiKit.Button("VsyncOn", page, "开", ButtonKind.Secondary, () => { editing.vSync = true; Refresh(); }, 448, 204, 156, 40, 14);
            vsyncOff = UiKit.Button("VsyncOff", page, "关", ButtonKind.Secondary, () => { editing.vSync = false; Refresh(); }, 612, 204, 156, 40, 14);
            brightness = SliderRow(page, "亮度", 0, 270, 768, out brightnessValue, v => editing.brightness = v);
            Label("Hint", page, "显示模式、分辨率和画质在编辑器里只保存不生效，打包后生效。", 13, Text2, 0, 340, 768, 24);
            return page.gameObject;
        }

        private GameObject KeysPage(Transform body)
        {
            var page = Page("KeysPage", body);
            Label("Hint", page, "目前不能改键，下面是当前按键。", 13, Text2, 0, 0, 768, 20);
            Label("MoveHead", page, "移动与交互", 13, Text2, 0, 28, 368, 20);
            Label("FightHead", page, "战斗与物品", 13, Text2, 400, 28, 368, 20);
            (string action, string key)[] move =
            {
                ("移动", "W A S D"), ("冲刺", "左 Shift"), ("闪避", "空格"), ("交互 / 搜刮", "F"), ("背包", "Tab"), ("建火", "Q"), ("取消", "鼠标右键"), ("暂停菜单", "Esc")
            };
            (string action, string key)[] fight =
            {
                ("攻击", "鼠标左键"), ("瞄准", "鼠标右键"), ("换弹", "R"), ("近战", "V"), ("收起武器", "C"), ("主 / 副武器", "1 / 2"), ("切换武器", "滚轮"), ("使用物品", "U"), ("丢弃物品", "X")
            };
            for (var i = 0; i < move.Length; i++) KeyRow(page, move[i], 0, 52 + i * 39);
            for (var i = 0; i < fight.Length; i++) KeyRow(page, fight[i], 400, 52 + i * 39);
            return page.gameObject;
        }

        // ---------- 小控件（尺寸同 we-ll-be-right-back 设置面板） ----------

        private Slider SliderRow(Transform page, string label, float x, float y, float w, out Text value, Action<int> set)
        {
            Label("Label", page, label, 15, Text1, x, y, w - 70, 24);
            value = Label("Value", page, "100", 15, Amber, x + w - 70, y, 70, 24, TextAnchor.MiddleRight);
            var go = Node("Slider", page);
            Place(go, x, y + 28, w, 24);
            var track = Box("Track", go, Off);
            Place(track.transform, 0, 9, w, 6);
            var fillArea = Node("Fill Area", go);
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(0, 9);
            fillArea.offsetMax = new Vector2(0, -9);
            var fill = Box("Fill", fillArea, Amber);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Node("Handle Area", go);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(8, 0);
            handleArea.offsetMax = new Vector2(-8, 0);
            var handle = Box("Handle", handleArea, Text1);
            handle.sprite = Knob;
            handle.type = Image.Type.Simple;
            handle.rectTransform.sizeDelta = new Vector2(18, 0);
            var slider = go.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0;
            slider.maxValue = 100;
            slider.wholeNumbers = true;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            var shown = value;
            slider.onValueChanged.AddListener(v =>
            {
                if (editing == null) return;
                set(Mathf.RoundToInt(v));
                shown.text = Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
            });
            return slider;
        }

        private static void FormRow(Transform page, string label, float y)
        {
            Label(label, page, label, 15, Text1, 0, y, 400, 64);
            Rule(page, 0, y + 63, BodyW, 1, Row);
        }

        private static void KeyRow(Transform page, (string action, string key) row, float x, float y)
        {
            Label("Label", page, row.action, 15, Text1, x, y, 220, 38);
            Rule(page, x, y + 38, 368, 1, Row);
            var chip = Box("Key", page, Field, new Color(0.35f, 0.24f, 0.14f)).transform;
            Place(chip, x + 248, y + 4, 120, 30);
            Label("Text", chip, row.key, 14, Text1, 0, 0, 120, 30, TextAnchor.MiddleCenter);
        }

        private static Button LanguageOption(Transform page, string name, string title, string sub, string code, float y, UnityEngine.Events.UnityAction click)
        {
            var b = UiKit.Button(name, page, "", ButtonKind.Secondary, click, 0, y, 560, 76, 17);
            var ring = Box("Ring", b.transform, Amber, null, false);
            ring.sprite = Knob;
            Place(ring.transform, 20, 28, 20, 20);
            var hole = Box("Hole", ring.transform, Field, null, false);
            hole.sprite = Knob;
            Place(hole.transform, 2, 2, 16, 16);
            var dot = Box("Dot", ring.transform, Amber, null, false);
            dot.sprite = Knob;
            Place(dot.transform, 5, 5, 10, 10);
            Label("Name", b.transform, title, 17, Text1, 56, 14, 380, 26);
            var subText = Label("Sub", b.transform, "", 13, Text2, 56, 40, 380, 20);
            subText.text = sub; // 两种语言的说明各用本语言写，不翻译
            Label("Code", b.transform, code, 13, Text2, 450, 0, 90, 76, TextAnchor.MiddleRight);
            return b;
        }

        // ---------- 状态 ----------

        private void BuildResolutions()
        {
            resolutions = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!resolutions.Contains(size)) resolutions.Add(size);
            }

            var current = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            if (!resolutions.Contains(current)) resolutions.Add(current);
        }

        private int ResolutionIndex()
        {
            var size = editing.width > 0 ? new Vector2Int(editing.width, editing.height) : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            var index = resolutions.IndexOf(size);
            return index >= 0 ? index : resolutions.Count - 1;
        }

        private int QualityIndex() => editing.quality >= 0 ? editing.quality : QualitySettings.GetQualityLevel();

        private static int Wrap(int value, int count) => count <= 0 ? 0 : (value % count + count) % count;

        /// <summary>画面页的 ‹ › 选择：0 显示模式，1 分辨率，2 画质。</summary>
        public void Step(int row, int step)
        {
            if (editing == null) return;
            switch (row)
            {
                case 0: editing.displayMode = Wrap(editing.displayMode + step, GameSettings.DisplayModeNames.Length); break;
                case 1:
                    var size = resolutions[Wrap(ResolutionIndex() + step, resolutions.Count)];
                    editing.width = size.x;
                    editing.height = size.y;
                    break;
                case 2: editing.quality = Wrap(QualityIndex() + step, Mathf.Max(1, QualitySettings.names.Length)); break;
            }

            Refresh();
        }

        private void Refresh()
        {
            for (var i = 0; i < tabs.Count; i++)
            {
                var active = i == tab;
                tabs[i].transform.Find("Mark").GetComponent<Image>().enabled = active;
                var label = tabs[i].transform.Find("Label").GetComponent<Text>();
                label.color = active ? Amber : Text1;
                Focus(tabs[i], active, new Color(1, 1, 1, 0));
            }

            var english = Loc.IsEnglish;
            SetSelected(quickZh, !english);
            SetSelected(quickEn, english);
            Radio(languageZh, !english);
            Radio(languageEn, english);
            if (editing == null) return;

            Sync(master, masterValue, editing.masterVolume);
            Sync(music, musicValue, editing.musicVolume);
            Sync(sfx, sfxValue, editing.sfxVolume);
            Sync(ambience, ambienceValue, editing.ambienceVolume);
            Sync(brightness, brightnessValue, editing.brightness);
            SetSelected(muteOn, editing.muteInBackground);
            SetSelected(muteOff, !editing.muteInBackground);
            SetSelected(vsyncOn, editing.vSync);
            SetSelected(vsyncOff, !editing.vSync);
            Set(displayValue, GameSettings.DisplayModeNames[Mathf.Clamp(editing.displayMode, 0, GameSettings.DisplayModeNames.Length - 1)]);
            var res = resolutions[ResolutionIndex()];
            Set(resolutionValue, $"{res.x} × {res.y}");
            var names = QualitySettings.names;
            Set(qualityValue, names.Length > 0 ? names[Mathf.Clamp(QualityIndex(), 0, names.Length - 1)] : "默认");
        }

        private static void Radio(Button b, bool on)
        {
            SetSelected(b, on);
            b.transform.Find("Ring").GetComponent<Image>().color = on ? Amber : Dim;
            b.transform.Find("Ring/Dot").GetComponent<Image>().enabled = on;
            b.transform.Find("Name").GetComponent<Text>().color = Text1;
        }

        private static void Sync(Slider slider, Text label, int value)
        {
            slider.SetValueWithoutNotify(value);
            label.text = value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
