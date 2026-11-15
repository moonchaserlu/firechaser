using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction
{
    public enum MainMenuPage
    {
        Main,
        Saves,
        Settings,
        Loading
    }

    public enum MainMenuDialog
    {
        None,
        Quit,
        StartNew,
        DeleteSlot,
        SlotsFull
    }

    /// <summary>
    /// 主菜单（IMGUI，按 1920×1080 设计稿等比缩放）：继续、开始、存档、设置、退出。
    /// 视觉与跳转见 Figma《向火而生 主菜单原型》。界面状态都走公开方法，PlayMode 测试直接调用。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        private const float DesignWidth = 1920f;
        private const float DesignHeight = 1080f;

        private static readonly Color Bg = new Color(0.039f, 0.027f, 0.02f);
        private static readonly Color Panel = new Color(0.075f, 0.047f, 0.03f);
        private static readonly Color Amber = new Color(0.95f, 0.78f, 0.3f);
        private static readonly Color Fire = new Color(1f, 0.55f, 0.2f);
        private static readonly Color TextColor = new Color(1f, 0.95f, 0.86f);
        private static readonly Color Muted = new Color(0.74f, 0.66f, 0.56f);
        private static readonly Color Dim = new Color(0.42f, 0.37f, 0.32f);
        private static readonly Color Danger = new Color(1f, 0.42f, 0.36f);
        private static readonly Color Dark = new Color(0.12f, 0.07f, 0.03f);

        private static readonly string[] MainItems = { "继续游戏", "开始游戏", "存档", "设置", "退出游戏" };
        private static readonly string[] Tabs = { "画面", "音频", "操作" };
        private static readonly (string action, string key)[] KeyBindings =
        {
            ("移动", "W A S D"), ("冲刺", "左 Shift"), ("闪避 / 跳跃", "空格"), ("交互", "E"), ("攻击", "鼠标左键"),
            ("瞄准", "鼠标右键"), ("换弹", "R"), ("背包", "Tab"), ("暂停", "Esc")
        };

        [SerializeField] private string gameScenePath = GameScenes.Map01;
        [Tooltip("新存档进入的场景：先走开局教学关，走完或跳过后存档改记为 Map01。")]
        [SerializeField] private string newGameScenePath = GameScenes.Tutorial;
        [SerializeField, Min(0f)] private float loadingSeconds = 1.2f;
        [SerializeField] private string versionLabel = "v0.1";

        private int mainIndex = 1;
        private int slotIndex;
        private int settingsTab;
        private int settingsRow;
        private int dialogChoice = 1;
        private int pendingDeleteSlot;
        private float loadingStarted;
        private bool drawingDialog;
        private SaveSlotInfo[] slots = new SaveSlotInfo[SaveSlots.SlotCount];
        private GameSettings editing;
        private List<Vector2Int> resolutions;
        private string[] qualityNames;

        private Texture2D white;
        private Texture2D glow;
        private Texture2D fade;
        private Texture2D vignette;
        private Texture2D horizon;
        private Font serif;
        private Font sans;
        private readonly Dictionary<(Font, int, TextAnchor, bool, bool), GUIStyle> styles = new Dictionary<(Font, int, TextAnchor, bool, bool), GUIStyle>();
        private float scale = 1f;
        private Vector2 origin;
        private Ember[] embers;

        private struct Ember
        {
            public float x;
            public float y;
            public float speed;
            public float size;
            public float phase;
            public bool amber;
        }

        public MainMenuPage Page { get; private set; } = MainMenuPage.Main;
        public MainMenuDialog Dialog { get; private set; } = MainMenuDialog.None;
        public int SettingsTab => settingsTab;
        public string GameScenePath => gameScenePath;
        public bool CanContinue => SaveSlots.MostRecent() != 0;
        public IReadOnlyList<SaveSlotInfo> Slots => slots;
        /// <summary>设置页里尚未应用的修改。</summary>
        public GameSettings EditingSettings => editing;

        private void OnEnable()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SaveSlots.ClearActive();
            RefreshSlots();
            mainIndex = CanContinue ? 0 : 1;
        }

        private void OnDestroy()
        {
            foreach (var texture in new[] { glow, fade, vignette, horizon })
            {
                if (texture != null) Destroy(texture);
            }
        }

        public void RefreshSlots() => slots = SaveSlots.LoadAll();

        // ---------- 导航（公开给测试） ----------

        public void Continue()
        {
            var slot = SaveSlots.MostRecent();
            if (slot != 0) LoadSlot(slot);
        }

        public void RequestStart()
        {
            Dialog = SaveSlots.FirstEmpty() == 0 ? MainMenuDialog.SlotsFull : MainMenuDialog.StartNew;
            dialogChoice = 1;
        }

        /// <summary>在第一个空槽开始新游戏。</summary>
        public void ConfirmStart() => StartNewIn(SaveSlots.FirstEmpty());

        public void StartNewIn(int slot)
        {
            if (slot == 0 || SaveSlots.Exists(slot)) return;
            gameScenePath = newGameScenePath;
            SaveSlots.CreateNew(slot, gameScenePath, DateTime.UtcNow);
            BeginLoading();
        }

        public void LoadSlot(int slot)
        {
            if (!SaveSlots.Exists(slot)) return;
            var info = SaveSlots.Resume(slot, DateTime.UtcNow);
            if (!string.IsNullOrEmpty(info.scenePath)) gameScenePath = info.scenePath;
            BeginLoading();
        }

        public void OpenSaves()
        {
            RefreshSlots();
            Page = MainMenuPage.Saves;
            Dialog = MainMenuDialog.None;
            slotIndex = Mathf.Max(0, SaveSlots.MostRecent() - 1);
        }

        public void RequestDelete(int slot)
        {
            if (!SaveSlots.Exists(slot)) return;
            pendingDeleteSlot = slot;
            Dialog = MainMenuDialog.DeleteSlot;
            dialogChoice = 0;
        }

        public void ConfirmDelete()
        {
            if (pendingDeleteSlot != 0) SaveSlots.Delete(pendingDeleteSlot);
            pendingDeleteSlot = 0;
            Dialog = MainMenuDialog.None;
            RefreshSlots();
        }

        public void OpenSettings()
        {
            editing = GameSettings.Current.Clone();
            BuildOptionLists();
            Page = MainMenuPage.Settings;
            Dialog = MainMenuDialog.None;
            settingsTab = 0;
            settingsRow = 0;
        }

        public void SelectTab(int tab)
        {
            settingsTab = (tab % Tabs.Length + Tabs.Length) % Tabs.Length;
            settingsRow = 0;
        }

        public void ResetSettings()
        {
            editing = new GameSettings();
        }

        public void ApplySettings()
        {
            if (editing == null) return;
            editing.Save();
            GameSettings.Current = editing.Clone();
            GameSettings.Current.Apply();
            Back();
        }

        public void RequestQuit()
        {
            Dialog = MainMenuDialog.Quit;
            dialogChoice = 0;
        }

        public void ConfirmQuit()
        {
            Dialog = MainMenuDialog.None;
#if UNITY_EDITOR
            Debug.Log("[MainMenu] 退出游戏（编辑器里不关闭）。");
#else
            Application.Quit();
#endif
        }

        /// <summary>Esc：先关弹窗，再回主菜单。</summary>
        public void Back()
        {
            if (Dialog != MainMenuDialog.None)
            {
                Dialog = MainMenuDialog.None;
                return;
            }

            if (Page == MainMenuPage.Loading) return;
            Page = MainMenuPage.Main;
            editing = null;
            RefreshSlots();
        }

        private void BeginLoading()
        {
            Dialog = MainMenuDialog.None;
            Page = MainMenuPage.Loading;
            loadingStarted = Time.unscaledTime;
        }

        private void ActivateMain(int index)
        {
            switch (index)
            {
                case 0: Continue(); break;
                case 1: RequestStart(); break;
                case 2: OpenSaves(); break;
                case 3: OpenSettings(); break;
                case 4: RequestQuit(); break;
            }
        }

        private void ConfirmDialog()
        {
            switch (Dialog)
            {
                case MainMenuDialog.Quit: ConfirmQuit(); break;
                case MainMenuDialog.StartNew: ConfirmStart(); break;
                case MainMenuDialog.DeleteSlot: ConfirmDelete(); break;
                case MainMenuDialog.SlotsFull: OpenSaves(); break;
            }
        }

        // ---------- 键盘 ----------

        private void Update()
        {
            if (Page == MainMenuPage.Loading)
            {
                if (Time.unscaledTime - loadingStarted >= loadingSeconds)
                {
                    Page = MainMenuPage.Main;
                    GameScenes.Load(gameScenePath);
                }

                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var up = keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame;
            var down = keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame;
            var left = keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame;
            var right = keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame;
            var confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Back();
                return;
            }

            if (Dialog != MainMenuDialog.None)
            {
                if (left) dialogChoice = 0;
                if (right) dialogChoice = 1;
                if (confirm)
                {
                    if (dialogChoice == 0) Back();
                    else ConfirmDialog();
                }

                return;
            }

            switch (Page)
            {
                case MainMenuPage.Main:
                    if (up) mainIndex = NextMainIndex(-1);
                    if (down) mainIndex = NextMainIndex(1);
                    if (confirm) ActivateMain(mainIndex);
                    break;
                case MainMenuPage.Saves:
                    if (up) slotIndex = Mathf.Max(0, slotIndex - 1);
                    if (down) slotIndex = Mathf.Min(SaveSlots.SlotCount - 1, slotIndex + 1);
                    if (confirm)
                    {
                        if (slots[slotIndex] != null) LoadSlot(slotIndex + 1);
                        else StartNewIn(slotIndex + 1);
                    }

                    if (keyboard.deleteKey.wasPressedThisFrame) RequestDelete(slotIndex + 1);
                    break;
                case MainMenuPage.Settings:
                    if (keyboard.qKey.wasPressedThisFrame) SelectTab(settingsTab - 1);
                    if (keyboard.eKey.wasPressedThisFrame) SelectTab(settingsTab + 1);
                    var rows = RowCount(settingsTab);
                    if (up) settingsRow = Mathf.Max(0, settingsRow - 1);
                    if (down) settingsRow = Mathf.Min(rows - 1, settingsRow + 1);
                    if (left) AdjustRow(settingsTab, settingsRow, -1);
                    if (right) AdjustRow(settingsTab, settingsRow, 1);
                    if (confirm) ApplySettings();
                    break;
            }
        }

        private int NextMainIndex(int step)
        {
            var index = mainIndex;
            for (var i = 0; i < MainItems.Length; i++)
            {
                index = (index + step + MainItems.Length) % MainItems.Length;
                if (index != 0 || CanContinue) return index;
            }

            return mainIndex;
        }

        // ---------- 设置项 ----------

        private void BuildOptionLists()
        {
            resolutions = new List<Vector2Int>();
            foreach (var resolution in UnityEngine.Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (!resolutions.Contains(size)) resolutions.Add(size);
            }

            var currentSize = new Vector2Int(UnityEngine.Screen.currentResolution.width, UnityEngine.Screen.currentResolution.height);
            if (!resolutions.Contains(currentSize)) resolutions.Add(currentSize);
            qualityNames = QualitySettings.names;
        }

        private static int RowCount(int tab) => tab switch { 0 => 5, 1 => 5, _ => KeyBindings.Length };

        private int ResolutionIndex()
        {
            var size = editing.width > 0
                ? new Vector2Int(editing.width, editing.height)
                : new Vector2Int(UnityEngine.Screen.currentResolution.width, UnityEngine.Screen.currentResolution.height);
            var index = resolutions.IndexOf(size);
            return index >= 0 ? index : resolutions.Count - 1;
        }

        private int QualityIndex() => editing.quality >= 0 ? editing.quality : QualitySettings.GetQualityLevel();

        /// <summary>A/D 或点击箭头调整一行。滑条每次 5。</summary>
        public void AdjustRow(int tab, int row, int step)
        {
            if (editing == null) return;
            if (tab == 0)
            {
                switch (row)
                {
                    case 0: editing.displayMode = Wrap(editing.displayMode + step, GameSettings.DisplayModeNames.Length); break;
                    case 1:
                        var size = resolutions[Wrap(ResolutionIndex() + step, resolutions.Count)];
                        editing.width = size.x;
                        editing.height = size.y;
                        break;
                    case 2: editing.quality = Wrap(QualityIndex() + step, Mathf.Max(1, qualityNames.Length)); break;
                    case 3: editing.vSync = !editing.vSync; break;
                    case 4: editing.brightness = Mathf.Clamp(editing.brightness + step * 5, 0, 100); break;
                }
            }
            else if (tab == 1)
            {
                switch (row)
                {
                    case 0: editing.masterVolume = Mathf.Clamp(editing.masterVolume + step * 5, 0, 100); break;
                    case 1: editing.musicVolume = Mathf.Clamp(editing.musicVolume + step * 5, 0, 100); break;
                    case 2: editing.sfxVolume = Mathf.Clamp(editing.sfxVolume + step * 5, 0, 100); break;
                    case 3: editing.ambienceVolume = Mathf.Clamp(editing.ambienceVolume + step * 5, 0, 100); break;
                    case 4: editing.muteInBackground = !editing.muteInBackground; break;
                }
            }
        }

        private static int Wrap(int value, int count) => count <= 0 ? 0 : (value % count + count) % count;

        // ---------- 绘制 ----------

        private void OnGUI()
        {
            EnsureAssets();
            scale = Mathf.Min(UnityEngine.Screen.width / DesignWidth, UnityEngine.Screen.height / DesignHeight);
            origin = new Vector2((UnityEngine.Screen.width - DesignWidth * scale) * 0.5f, (UnityEngine.Screen.height - DesignHeight * scale) * 0.5f);

            if (Page == MainMenuPage.Loading)
            {
                DrawLoading();
                return;
            }

            DrawBackground(Page == MainMenuPage.Main ? 0f : Page == MainMenuPage.Saves ? 0.55f : 0.62f);
            switch (Page)
            {
                case MainMenuPage.Main: DrawMain(); break;
                case MainMenuPage.Saves: DrawSaves(); break;
                case MainMenuPage.Settings: DrawSettings(); break;
            }

            if (Dialog != MainMenuDialog.None)
            {
                drawingDialog = true;
                DrawDialog();
                drawingDialog = false;
            }
        }

        private void DrawBackground(float dim)
        {
            var full = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            Fill(full, Bg);

            var t = Time.unscaledTime;
            var flicker = 0.9f + 0.06f * Mathf.Sin(t * 7.3f) + 0.04f * Mathf.Sin(t * 13.1f);
            Tex(V(1382 - 900, 907 - 900, 1800, 1800), glow, Fire, 0.5f * flicker);
            Tex(V(1382 - 300, 907 - 220, 600, 440), glow, Fire, 0.35f * flicker);

            // 地平线：底部压暗的轮廓，顶边做柔和过渡。
            Tex(V(0, 840, DesignWidth, 60), horizon, new Color(0.016f, 0.011f, 0.008f), 1f);
            Fill(V(0, 899, DesignWidth, 200), new Color(0.016f, 0.011f, 0.008f));

            Tex(V(1270, 840, 220, 90), glow, Fire, 0.75f * flicker);
            Tex(V(1352, 846, 60, 60), glow, new Color(1f, 0.85f, 0.5f), flicker);

            foreach (var ember in embers)
            {
                var life = Mathf.Repeat(ember.phase + t * ember.speed, 1f);
                var x = ember.x + Mathf.Sin((t + ember.phase * 10f) * 0.8f) * 18f;
                var y = 860f - life * 700f;
                var alpha = Mathf.Sin(life * Mathf.PI) * (0.5f + 0.4f * Mathf.Sin(t * 5f + ember.phase * 20f));
                var size = ember.size * 3f;
                Tex(V(x - size * 0.5f, y - size * 0.5f, size, size), glow, ember.amber ? Amber : Fire, alpha);
            }

            Tex(full, vignette, Color.black, 0.85f);
            if (dim > 0f) Fill(full, new Color(0, 0, 0, dim));
        }

        private void DrawMain()
        {
            Text(V(160, 196, 900, 30), "T O W A R D     T H E     F I R E", 20, Amber, sans, TextAnchor.UpperLeft, 0.7f);
            for (var i = 1; i <= 3; i++)
            {
                Text(V(160 - i, 228 - i, 900, 200), "向火而生", 148, Fire, serif, TextAnchor.UpperLeft, 0.08f, true);
                Text(V(160 + i, 228 + i, 900, 200), "向火而生", 148, Fire, serif, TextAnchor.UpperLeft, 0.08f, true);
            }

            Text(V(160, 228, 900, 200), "向火而生", 148, Amber, serif, TextAnchor.UpperLeft, 1f, true);
            Text(V(160, 430, 1200, 36), "黑暗永不退去。带上火种出发，在熄灭之前回到安全屋。", 22, Muted, serif);

            var canContinue = CanContinue;
            var y = 560f;
            for (var i = 0; i < MainItems.Length; i++)
            {
                var rect = V(160, y, 400, 60);
                var enabled = i != 0 || canContinue;
                if (enabled && Hover(rect)) mainIndex = i;
                if (MenuButton(rect, MainItems[i], enabled, enabled && mainIndex == i)) ActivateMain(i);
                y += 66f;
                if (i == 0)
                {
                    var recent = canContinue ? SaveSlots.Load(SaveSlots.MostRecent()) : null;
                    var info = recent != null ? $"存档 {recent.slot:00}　·　{SaveSlots.FormatPlayTime(recent.playSeconds)}" : "没有可继续的存档";
                    Text(V(184, y - 6, 400, 24), info, 16, recent != null ? Muted : Dim);
                    y += 22f;
                }
            }

            Text(V(160, 1006, 900, 24), "W / S  选择　　Enter  确认　　Esc  返回", 16, Dim);
            Text(V(1160, 1006, 600, 24), versionLabel, 16, Dim, null, TextAnchor.UpperRight);
        }

        private void DrawHeader(string title, string subtitle)
        {
            var back = V(152, 72, 140, 48);
            var hover = Hover(back);
            Text(V(160, 74, 40, 44), "‹", 30, Amber, sans, TextAnchor.UpperLeft, 1f, true);
            Text(V(190, 82, 120, 32), "返回", 22, hover ? Amber : Muted);
            if (Click(back)) Back();
            Text(V(160, 140, 900, 80), title, 60, TextColor, serif, TextAnchor.UpperLeft, 1f, true);
            if (subtitle != null) Text(V(160, 238, 1400, 30), subtitle, 20, Muted);
        }

        private void DrawSaves()
        {
            DrawHeader("存档", "选择存档槽继续进度，或在空槽开始新的旅程。");
            var y = 330f;
            for (var i = 0; i < SaveSlots.SlotCount; i++)
            {
                var rect = V(400, y, 1120, 140);
                if (Dialog == MainMenuDialog.None && Hover(rect)) slotIndex = i;
                DrawSlot(rect, i + 1, slots[i], slotIndex == i);
                y += 160f;
            }

            Text(V(400, y + 8, 1120, 28), "◆ 进入游戏后，游戏时长会自动记录到所选存档槽。安全屋的仓库等局外进度按存档槽分开保存。", 18, Amber, null, TextAnchor.UpperLeft, 0.7f);
            Text(V(160, 1006, 1200, 24), "W / S  选择存档　　Enter  读取　　Del  删除　　Esc  返回", 16, Dim);
        }

        private void DrawSlot(Rect rect, int slot, SaveSlotInfo info, bool selected)
        {
            var empty = info == null;
            Fill(rect, new Color(Panel.r, Panel.g, Panel.b, empty ? 0.5f : 0.85f));
            if (selected) Tex(Inflate(rect, 24f * scale), glow, Fire, 0.12f);
            Border(rect, Amber, selected ? 0.9f : 0.18f, selected ? 2f : 1f);

            var x = rect.x + 28 * scale;
            Text(new Rect(x, rect.y, 80 * scale, rect.height), slot.ToString("00"), 44, empty ? Dim : Amber, serif, TextAnchor.MiddleLeft, 1f, true);
            x += 88 * scale;
            var thumb = new Rect(x, rect.y + 20 * scale, 176 * scale, 99 * scale);
            Fill(thumb, empty ? new Color(1, 1, 1, 0.03f) : Bg);
            if (!empty) Tex(new Rect(thumb.x + thumb.width * 0.2f, thumb.y + thumb.height * 0.25f, thumb.width * 0.6f, thumb.height * 0.9f), glow, Fire, 0.8f);
            Border(thumb, Amber, 0.15f, 1f);
            x += 204 * scale;

            if (empty)
            {
                Text(new Rect(x, rect.y + 34 * scale, 500 * scale, 36 * scale), "空存档槽", 26, Muted, sans, TextAnchor.UpperLeft, 1f);
                Text(new Rect(x, rect.y + 76 * scale, 500 * scale, 28 * scale), "在此开始新的旅程", 18, Dim);
                if (ActionButton(new Rect(rect.xMax - (28 + 160) * scale, rect.center.y - 26 * scale, 160 * scale, 52 * scale), "新游戏", ButtonKind.Secondary)) StartNewIn(slot);
                return;
            }

            Text(new Rect(x, rect.y + 22 * scale, 520 * scale, 36 * scale), $"安全屋 · 第 {info.sessions} 次出发", 26, TextColor, sans, TextAnchor.UpperLeft, 1f, true);
            Text(new Rect(x, rect.y + 62 * scale, 520 * scale, 28 * scale), $"游戏时长 {SaveSlots.FormatPlayTime(info.playSeconds)}", 18, Muted);
            var local = info.LastPlayed == DateTime.MinValue ? "" : info.LastPlayed.ToLocalTime().ToString("yyyy-MM-dd　HH:mm", CultureInfo.InvariantCulture);
            Text(new Rect(x, rect.y + 94 * scale, 520 * scale, 24 * scale), local, 16, Dim);

            var right = rect.xMax - 28 * scale;
            var deleteRect = new Rect(right - 140 * scale, rect.center.y - 26 * scale, 140 * scale, 52 * scale);
            var loadRect = new Rect(deleteRect.x - 12 * scale - 140 * scale, deleteRect.y, 140 * scale, 52 * scale);
            if (ActionButton(loadRect, "读取", ButtonKind.Primary)) LoadSlot(slot);
            if (ActionButton(deleteRect, "删除", ButtonKind.Danger)) RequestDelete(slot);
        }

        private void DrawSettings()
        {
            DrawHeader("设置", null);
            var tx = 440f;
            for (var i = 0; i < Tabs.Length; i++)
            {
                var rect = V(tx, 250, 104, 56);
                var active = i == settingsTab;
                var hover = Dialog == MainMenuDialog.None && Hover(rect);
                Text(V(tx, 258, 104, 34), Tabs[i], 24, active ? Amber : hover ? TextColor : Muted, sans, TextAnchor.UpperCenter, 1f, active);
                if (active) Fill(V(tx + 28, 298, 48, 3), Amber);
                if (Click(rect)) SelectTab(i);
                tx += 112f;
            }

            var rows = RowCount(settingsTab);
            // 按键表行数多，行高压低以免压到底部提示。
            var rowHeight = settingsTab == 2 ? 52f : 68f;
            var panel = V(440, 320, 1040, 16 + rows * rowHeight + 24);
            Fill(panel, new Color(Panel.r, Panel.g, Panel.b, 0.82f));
            Border(panel, Amber, 0.15f, 1f);

            var y = 336f;
            for (var row = 0; row < rows; row++)
            {
                var rect = V(480, y, 960, rowHeight);
                if (Dialog == MainMenuDialog.None && Hover(rect)) settingsRow = row;
                if (row == settingsRow) Tex(rect, fade, Fire, 0.12f);
                Fill(V(480, y + rowHeight - 1, 960, 1), new Color(Amber.r, Amber.g, Amber.b, 0.12f));
                DrawSettingRow(rect, settingsTab, row);
                y += rowHeight;
            }

            var footY = 320 + 16 + rows * rowHeight + 24 + 28;
            if (settingsTab == 2)
            {
                Text(V(440, footY + 12, 600, 28), "改键将在后续版本提供。", 18, Dim);
            }

            if (ActionButton(V(1480 - 140, footY, 140, 52), "应用", ButtonKind.Primary)) ApplySettings();
            if (ActionButton(V(1480 - 140 - 16 - 168, footY, 168, 52), "恢复默认", ButtonKind.Secondary)) ResetSettings();
            Text(V(160, 1006, 1400, 24), "Q / E  切换分页　　W / S  选择　　A / D  调整　　Enter  应用　　Esc  返回", 16, Dim);
        }

        private void DrawSettingRow(Rect rect, int tab, int row)
        {
            var labelRect = new Rect(rect.x + 8 * scale, rect.y, 500 * scale, rect.height);
            if (tab == 2)
            {
                var (action, key) = KeyBindings[row];
                Text(labelRect, action, 22, TextColor, sans, TextAnchor.MiddleLeft);
                var keyRect = new Rect(rect.xMax - 8 * scale - 180 * scale, rect.center.y - 18 * scale, 180 * scale, 36 * scale);
                Fill(keyRect, new Color(1, 1, 1, 0.05f));
                Border(keyRect, Amber, 0.35f, 1f);
                Text(keyRect, key, 20, Amber, sans, TextAnchor.MiddleCenter);
                return;
            }

            switch (tab * 10 + row)
            {
                case 0: Select(rect, labelRect, "显示模式", GameSettings.DisplayModeNames[editing.displayMode], tab, row); break;
                case 1:
                    var size = resolutions[ResolutionIndex()];
                    Select(rect, labelRect, "分辨率", $"{size.x} × {size.y}", tab, row);
                    break;
                case 2:
                    var names = qualityNames;
                    var quality = QualityIndex();
                    Select(rect, labelRect, "画质", names.Length > 0 ? names[Mathf.Clamp(quality, 0, names.Length - 1)] : "默认", tab, row);
                    break;
                case 3: Toggle(rect, labelRect, "垂直同步", editing.vSync, tab, row); break;
                case 4: editing.brightness = Slider(rect, labelRect, "亮度", editing.brightness); break;
                case 10: editing.masterVolume = Slider(rect, labelRect, "主音量", editing.masterVolume); break;
                case 11: editing.musicVolume = Slider(rect, labelRect, "音乐", editing.musicVolume); break;
                case 12: editing.sfxVolume = Slider(rect, labelRect, "音效", editing.sfxVolume); break;
                case 13: editing.ambienceVolume = Slider(rect, labelRect, "环境音", editing.ambienceVolume); break;
                case 14: Toggle(rect, labelRect, "切到后台时静音", editing.muteInBackground, tab, row); break;
            }
        }

        private void Select(Rect rect, Rect labelRect, string name, string value, int tab, int row)
        {
            Text(labelRect, name, 22, TextColor, sans, TextAnchor.MiddleLeft);
            var right = rect.xMax - 8 * scale;
            var next = new Rect(right - 32 * scale, rect.y, 32 * scale, rect.height);
            var valueRect = new Rect(next.x - 240 * scale, rect.y, 240 * scale, rect.height);
            var prev = new Rect(valueRect.x - 32 * scale, rect.y, 32 * scale, rect.height);
            Text(prev, "‹", 28, Hover(prev) ? TextColor : Amber, sans, TextAnchor.MiddleCenter, 1f, true);
            Text(valueRect, value, 22, TextColor, sans, TextAnchor.MiddleCenter);
            Text(next, "›", 28, Hover(next) ? TextColor : Amber, sans, TextAnchor.MiddleCenter, 1f, true);
            if (Click(prev)) AdjustRow(tab, row, -1);
            if (Click(next)) AdjustRow(tab, row, 1);
        }

        private void Toggle(Rect rect, Rect labelRect, string name, bool on, int tab, int row)
        {
            Text(labelRect, name, 22, TextColor, sans, TextAnchor.MiddleLeft);
            var pill = new Rect(rect.xMax - 8 * scale - 64 * scale, rect.center.y - 16 * scale, 64 * scale, 32 * scale);
            Fill(pill, on ? new Color(Amber.r, Amber.g, Amber.b, 0.85f) : new Color(1, 1, 1, 0.12f));
            var knob = new Rect(on ? pill.xMax - 28 * scale : pill.x + 4 * scale, pill.y + 4 * scale, 24 * scale, 24 * scale);
            Fill(knob, on ? Dark : Muted);
            if (Click(new Rect(rect.xMax - 200 * scale, rect.y, 200 * scale, rect.height))) AdjustRow(tab, row, 1);
        }

        private int Slider(Rect rect, Rect labelRect, string name, int value)
        {
            Text(labelRect, name, 22, TextColor, sans, TextAnchor.MiddleLeft);
            var valueRect = new Rect(rect.xMax - 8 * scale - 48 * scale, rect.y, 48 * scale, rect.height);
            Text(valueRect, value.ToString(CultureInfo.InvariantCulture), 20, Amber, sans, TextAnchor.MiddleRight);
            var track = new Rect(valueRect.x - 20 * scale - 360 * scale, rect.center.y - 3 * scale, 360 * scale, 6 * scale);
            Fill(track, new Color(1, 1, 1, 0.12f));
            var fill = new Rect(track.x, track.y, track.width * value / 100f, track.height);
            Fill(fill, Fire);
            Fill(new Rect(fill.x + fill.width * 0.5f, fill.y, fill.width * 0.5f, fill.height), Amber);
            var knob = new Rect(fill.xMax - 10 * scale, track.center.y - 10 * scale, 20 * scale, 20 * scale);
            Tex(Inflate(knob, 10 * scale), glow, Fire, 0.7f);
            Fill(knob, Amber);

            var hit = new Rect(track.x - 10 * scale, rect.y, track.width + 20 * scale, rect.height);
            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && Dialog == MainMenuDialog.None
                && (hit.Contains(e.mousePosition) || (e.type == EventType.MouseDrag && GUIUtility.hotControl == hit.GetHashCode())))
            {
                GUIUtility.hotControl = hit.GetHashCode();
                value = Mathf.Clamp(Mathf.RoundToInt((e.mousePosition.x - track.x) / track.width * 100f), 0, 100);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == hit.GetHashCode())
            {
                GUIUtility.hotControl = 0;
            }

            return value;
        }

        private void DrawDialog()
        {
            var full = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            Fill(full, new Color(0, 0, 0, 0.72f));

            string title, body, cancel, ok;
            var okKind = ButtonKind.Primary;
            switch (Dialog)
            {
                case MainMenuDialog.Quit:
                    (title, body, cancel, ok) = ("离开火光？", "确定要退出游戏吗？\n上次回到安全屋之后的进度不会被保存。", "取消", "退出游戏");
                    okKind = ButtonKind.Danger;
                    break;
                case MainMenuDialog.StartNew:
                    (title, body, cancel, ok) = ("开始新的旅程", $"你将从安全屋重新出发。\n新进度会写入存档槽 {SaveSlots.FirstEmpty():00}，已有存档不受影响。", "返回", "出发");
                    break;
                case MainMenuDialog.DeleteSlot:
                    (title, body, cancel, ok) = ("删除存档？", $"存档槽 {pendingDeleteSlot:00} 的全部进度将被永久删除，无法恢复。", "取消", "删除");
                    okKind = ButtonKind.Danger;
                    break;
                default:
                    (title, body, cancel, ok) = ("存档槽已满", "三个存档槽都有进度。\n请先在存档页删除一个，再开始新的旅程。", "返回", "前往存档");
                    break;
            }

            var box = V(640, 370, 640, 340);
            Fill(box, new Color(Panel.r, Panel.g, Panel.b, 0.96f));
            Tex(Inflate(box, 60 * scale), glow, Fire, 0.1f);
            Border(box, Amber, 0.4f, 1.5f);
            Text(V(640, 418, 640, 56), title, 40, Amber, serif, TextAnchor.UpperCenter, 1f, true);
            Text(V(700, 490, 520, 110), body, 20, Muted, sans, TextAnchor.UpperCenter, 1f, false, true);

            var cancelRect = V(960 - 8 - 168, 620, 168, 52);
            var okRect = V(960 + 8, 620, 168, 52);
            if (Hover(cancelRect)) dialogChoice = 0;
            if (Hover(okRect)) dialogChoice = 1;
            if (ActionButton(cancelRect, cancel, ButtonKind.Secondary, dialogChoice == 0)) Back();
            else if (ActionButton(okRect, ok, okKind, dialogChoice == 1)) ConfirmDialog();
        }

        private void DrawLoading()
        {
            var full = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            Fill(full, new Color(0.02f, 0.014f, 0.01f));
            var t = Time.unscaledTime;
            var pulse = 0.85f + 0.15f * Mathf.Sin(t * 6f);
            Tex(V(960 - 300, 480 - 300, 600, 600), glow, Fire, 0.45f * pulse);
            Tex(V(960 - 40, 480 - 50, 80, 100), glow, new Color(1f, 0.85f, 0.5f), pulse);
            Text(V(460, 640, 1000, 44), "正在点燃火种……", 28, TextColor, serif, TextAnchor.UpperCenter);
            var progress = loadingSeconds <= 0f ? 1f : Mathf.Clamp01((t - loadingStarted) / loadingSeconds);
            Fill(V(680, 700, 560, 4), new Color(1, 1, 1, 0.1f));
            Fill(V(680, 700, 560 * progress, 4), Amber);
            Text(V(360, 724, 1200, 30), "提示：火堆熄灭前回到光里，黑暗会持续伤害你。", 18, Dim, sans, TextAnchor.UpperCenter);
        }

        // ---------- 控件 ----------

        private enum ButtonKind
        {
            Primary,
            Secondary,
            Danger
        }

        private bool MenuButton(Rect rect, string text, bool enabled, bool selected)
        {
            if (selected)
            {
                Tex(rect, fade, Fire, 0.28f);
                Fill(new Rect(rect.x, rect.center.y - 16 * scale, 4 * scale, 32 * scale), Amber);
            }

            Text(new Rect(rect.x + 24 * scale, rect.y, rect.width, rect.height), text, 28, !enabled ? Dim : selected ? Amber : TextColor, sans, TextAnchor.MiddleLeft, 1f, selected);
            return enabled && Click(rect);
        }

        private bool ActionButton(Rect rect, string text, ButtonKind kind, bool focused = false)
        {
            var hover = Hover(rect) || focused;
            var tint = kind == ButtonKind.Danger ? Danger : Amber;
            if (kind == ButtonKind.Primary)
            {
                Fill(rect, hover ? Color.Lerp(Amber, Color.white, 0.2f) : Amber);
                Text(rect, text, 20, Dark, sans, TextAnchor.MiddleCenter, 1f, true);
            }
            else
            {
                Fill(rect, new Color(tint.r, tint.g, tint.b, hover ? 0.16f : 0.06f));
                Border(rect, tint, hover ? 0.9f : kind == ButtonKind.Danger ? 0.7f : 0.45f, 1.5f);
                Text(rect, text, 20, kind == ButtonKind.Danger ? Danger : TextColor, sans, TextAnchor.MiddleCenter, 1f, true);
            }

            if (focused) Tex(Inflate(rect, 14 * scale), glow, Fire, 0.12f);
            return Click(rect);
        }

        // ---------- 基础绘制 ----------

        /// <summary>设计稿坐标（1920×1080）转屏幕坐标。</summary>
        private Rect V(float x, float y, float w, float h) => new Rect(origin.x + x * scale, origin.y + y * scale, w * scale, h * scale);

        private static Rect Inflate(Rect rect, float amount) => new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);

        private static bool Hover(Rect rect) => rect.Contains(Event.current.mousePosition);

        /// <summary>弹窗打开时只响应弹窗内的点击。</summary>
        private bool Click(Rect rect)
        {
            if (Dialog != MainMenuDialog.None && !drawingDialog) return false;
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        private void Fill(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        private void Tex(Rect rect, Texture2D texture, Color color, float alpha)
        {
            if (Event.current.type != EventType.Repaint || alpha <= 0f) return;
            var previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * alpha);
            GUI.DrawTexture(rect, texture);
            GUI.color = previous;
        }

        private void Border(Rect rect, Color color, float alpha, float width)
        {
            var c = new Color(color.r, color.g, color.b, alpha);
            var w = Mathf.Max(1f, width * scale);
            Fill(new Rect(rect.x, rect.y, rect.width, w), c);
            Fill(new Rect(rect.x, rect.yMax - w, rect.width, w), c);
            Fill(new Rect(rect.x, rect.y, w, rect.height), c);
            Fill(new Rect(rect.xMax - w, rect.y, w, rect.height), c);
        }

        private void Text(Rect rect, string text, int size, Color color, Font font = null, TextAnchor anchor = TextAnchor.UpperLeft, float alpha = 1f, bool bold = false, bool wrap = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            var previous = GUI.contentColor;
            GUI.contentColor = new Color(color.r, color.g, color.b, color.a * alpha);
            GUI.Label(rect, text, Style(font ?? sans, Mathf.Max(1, Mathf.RoundToInt(size * scale)), anchor, bold, wrap));
            GUI.contentColor = previous;
        }

        /// <summary>每种字号/对齐一份样式。IMGUI 的文字排版按样式缓存，反复改同一个样式会让字号和对齐失效。</summary>
        private GUIStyle Style(Font font, int size, TextAnchor anchor, bool bold, bool wrap)
        {
            var key = (font, size, anchor, bold, wrap);
            if (styles.TryGetValue(key, out var style)) return style;
            style = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor,
                wordWrap = wrap,
                richText = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = Color.white;
            styles[key] = style;
            return style;
        }

        private void EnsureAssets()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            glow = Radial(128, d => Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f));
            vignette = Radial(128, d => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1.45f, d)));
            fade = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (var x = 0; x < 64; x++) fade.SetPixel(x, 0, new Color(1, 1, 1, 1f - x / 63f));
            fade.Apply();
            // 地平线顶边的竖向过渡：上透明、下不透明（纹理 y=0 在底部）。
            horizon = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (var y = 0; y < 64; y++) horizon.SetPixel(0, y, new Color(1, 1, 1, 1f - y / 63f));
            horizon.Apply();

            // 先用默认字体：Unity 6 的 IMGUI 对 CreateDynamicFontFromOSFont 字体会忽略字号、颜色和对齐。
            // 以后导入思源宋体/黑体字体资源后，在这里赋给 serif / sans 即可。
            serif = null;
            sans = null;

            var random = new System.Random(7);
            embers = new Ember[34];
            for (var i = 0; i < embers.Length; i++)
            {
                embers[i] = new Ember
                {
                    x = 1180f + (float)random.NextDouble() * 420f,
                    phase = (float)random.NextDouble(),
                    speed = 0.04f + (float)random.NextDouble() * 0.06f,
                    size = 2f + (float)random.NextDouble() * 4f,
                    amber = random.NextDouble() > 0.5
                };
            }
        }

        private static Texture2D Radial(int size, Func<float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var half = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                    texture.SetPixel(x, y, new Color(1, 1, 1, alpha(d)));
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
