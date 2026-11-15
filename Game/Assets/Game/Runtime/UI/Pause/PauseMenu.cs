using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static WildernessExtraction.UiKit;

namespace WildernessExtraction
{
    /// <summary>
    /// 游戏内 Esc 菜单。版式照 we-ll-be-right-back 的 PauseMenu（1280×720 参考分辨率）：
    /// 中间是菜单窗口（标题、提示、一列按钮：继续游戏 / 设置 / [教学关：跳过教学] / 返回主菜单 / 退出游戏），
    /// 返回主菜单和退出要再点一次确认；两侧是按键提示牌（移动与交互、战斗与物品），底下一行常用键。
    /// 跟随场景里实现 <see cref="IPauseHost"/> 的那一局（Map01RunFlow、RunSession）的暂停状态显示，
    /// 界面由代码生成，常驻不随场景销毁，不需要放进场景或 Prefab。
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        public const string MainMenuScene = "Assets/Game/Scenes/MainMenu.unity";

        private enum Confirm
        {
            None,
            MainMenu,
            Quit
        }

        private sealed class Hint
        {
            public string key, label;
            public RectTransform chip, labelRect;
            public Text keyText;
        }

        public static PauseMenu Active { get; private set; }
        public static bool IsOpen => Active != null && Active.overlay != null && Active.overlay.activeInHierarchy;

        private static string extraLabel;
        private static Action extraAction;

        private IPauseHost host;
        private Canvas canvas;
        private GameObject overlay, menu;
        private RectTransform frame, buttonRoot, hintRoot;
        private Button resume, settings, extra, mainMenu, quit;
        private Text note;
        private Confirm confirm;
        private bool repause;
        private readonly List<Hint> hints = new List<Hint>();

        public IPauseHost Host => host;
        public bool ExtraVisible => extra != null && extra.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            extraLabel = null;
            extraAction = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || Active != null) return;
            var go = new GameObject("[PauseMenu]");
            DontDestroyOnLoad(go);
            go.AddComponent<PauseMenu>();
        }

        /// <summary>
        /// 当前场景额外加一个按钮（教学关的“跳过教学”）。切场景时自动清掉。
        /// </summary>
        public static void SetExtra(string label, Action action)
        {
            extraLabel = label;
            extraAction = action;
            if (Active != null) Active.Layout();
        }

        private void Awake()
        {
            Active = this;
            Build();
            overlay.SetActive(false);
            SceneManager.sceneLoaded += OnSceneLoaded;
            Bind();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unbind();
            if (Active == this) Active = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            extraLabel = null;
            extraAction = null;
            Bind();
        }

        /// <summary>找到当前场景的那一局并跟随它的暂停状态（测试或场景切换后也可手动调用）。</summary>
        public void Bind()
        {
            Unbind();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>())
            {
                if (behaviour is IPauseHost candidate)
                {
                    host = candidate;
                    break;
                }
            }

            if (host != null) host.PauseChanged += OnPauseChanged;
            Show(host != null && host.IsPaused);
        }

        private void Unbind()
        {
            if (host != null) host.PauseChanged -= OnPauseChanged;
            host = null;
            repause = false;
        }

        private void OnPauseChanged(bool paused)
        {
            // 设置窗口开着时按 Esc：那一局会先把暂停切掉，这里关掉设置、回到菜单并重新暂停（下一帧）。
            if (!paused && (SettingsWindow.IsOpen || SettingsWindow.EscFrame == Time.frameCount))
            {
                if (SettingsWindow.IsOpen) SettingsWindow.Active.Close();
                repause = true;
                return;
            }

            Show(paused);
        }

        private void LateUpdate()
        {
            if (repause)
            {
                repause = false;
                if (host != null && !host.IsPaused) host.SetPaused(true);
                Show(host != null && host.IsPaused);
            }

            // 那一局被销毁（切场景前）时收起菜单。
            if (IsOpen && (host == null || (host is MonoBehaviour b && b == null))) Show(false);
        }

        private void Show(bool open)
        {
            if (!open && SettingsWindow.IsOpen) SettingsWindow.Active.Close();
            confirm = Confirm.None;
            overlay.SetActive(open);
            menu.SetActive(true);
            if (open) Layout();
        }

        // ---------- 按钮 ----------

        public void Resume()
        {
            if (host != null) host.SetPaused(false);
            else Show(false);
        }

        public void OpenSettings()
        {
            confirm = Confirm.None;
            menu.SetActive(false);
            SettingsWindow.Ensure().Open(() =>
            {
                if (!IsOpen) return;
                menu.SetActive(true);
                Layout();
            });
        }

        public void ReturnToMainMenu()
        {
            if (confirm != Confirm.MainMenu)
            {
                confirm = Confirm.MainMenu;
                Layout();
                return;
            }

            GameRuntime.FlushActive();
            // 中途离开：这一局从安全屋带出来的物资不再带回。
            if (RaidTransfer.Active) RaidTransfer.Reset();
            Show(false);
            GameScenes.Load(MainMenuScene);
        }

        public void Quit()
        {
            if (confirm != Confirm.Quit)
            {
                confirm = Confirm.Quit;
                Layout();
                return;
            }

            GameRuntime.FlushActive();
#if UNITY_EDITOR
            Debug.Log("[PauseMenu] 退出游戏（编辑器里不关闭）。");
            confirm = Confirm.None;
            Layout();
#else
            Application.Quit();
#endif
        }

        private void Extra()
        {
            var action = extraAction;
            if (action == null) return;
            if (host != null) host.SetPaused(false);
            action();
        }

        // ---------- 搭建 ----------

        private void Build()
        {
            canvas = UiKit.Canvas("PauseCanvas", transform, 900, 1280, 720);
            var dim = Box("Overlay", canvas.transform, new Color(0.02f, 0.012f, 0.008f, 0.62f), null, false);
            Stretch(dim.transform);
            overlay = dim.gameObject;
            menu = Stretch(Node("Menu", overlay.transform)).gameObject;

            frame = Box("Window", menu.transform, UiKit.Window, Line).rectTransform;
            Label("Title", frame, "游戏菜单", 24, Text1, 30, 14, 360, 44, TextAnchor.MiddleCenter, true);
            Label("Hint", frame, "游戏已暂停 · Esc 返回游戏", 13, Text2, 30, 56, 360, 24, TextAnchor.MiddleCenter);
            buttonRoot = Node("Buttons", frame);
            resume = MenuButton("Resume", "继续游戏", ButtonKind.Primary, Resume);
            settings = MenuButton("Settings", "设置", ButtonKind.Secondary, OpenSettings);
            extra = MenuButton("Extra", "", ButtonKind.Secondary, Extra);
            mainMenu = MenuButton("MainMenu", "返回主菜单", ButtonKind.Secondary, ReturnToMainMenu);
            quit = MenuButton("Quit", "退出游戏", ButtonKind.Secondary, Quit);
            note = Label("Note", frame, "", 13, new Color(1f, 0.65f, 0.38f), 20, 0, 380, 40, TextAnchor.MiddleCenter);

            hintRoot = Node("KeyHints", menu.transform);
            hintRoot.anchorMin = hintRoot.anchorMax = hintRoot.pivot = new Vector2(0.5f, 0.5f);
            hintRoot.sizeDelta = Vector2.zero;
            Column("移动与交互", -585, ("W A S D", "移动"), ("Shift", "冲刺"), ("空格", "闪避"), ("F", "交互 / 搜刮"), ("Tab", "背包"), ("Q", "建火"));
            Column("战斗与物品", 235, ("左键", "攻击"), ("右键", "瞄准"), ("R", "换弹"), ("V", "近战"), ("1 / 2", "切换武器"), ("U", "使用物品"));
            Plate(new Vector2(0, -238), new Vector2(1170, 40));
            (string, string)[] bottom = { ("C", "收起武器"), ("X", "丢弃物品"), ("滚轮", "切换武器"), ("Esc", "返回游戏") };
            for (var i = 0; i < bottom.Length; i++) Row(bottom[i], new Vector2(-560 + i * 285, -238));
        }

        private Button MenuButton(string name, string caption, ButtonKind kind, UnityEngine.Events.UnityAction click)
        {
            var b = UiKit.Button(name, buttonRoot, caption, kind, click, 16);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            return b;
        }

        /// <summary>按当前有几个按钮排版窗口（教学关多一个“跳过教学”），刷新确认文字。</summary>
        private void Layout()
        {
            var showExtra = extraAction != null && !string.IsNullOrEmpty(extraLabel);
            extra.gameObject.SetActive(showExtra);
            if (showExtra) Set(Caption(extra), extraLabel);
            var list = new List<Button> { resume, settings };
            if (showExtra) list.Add(extra);
            list.Add(mainMenu);
            list.Add(quit);

            const float top = 96f, step = 56f;
            var noteH = confirm == Confirm.None ? 16f : 48f;
            var height = top + list.Count * step + noteH;
            Center(frame, 420, height);
            Place(buttonRoot, 40, 0, 340, height);
            for (var i = 0; i < list.Count; i++) Place(list[i].transform, 0, top + i * step, 340, 46);
            Place(note.transform, 20, top + list.Count * step - 8, 380, 40);

            SetConfirm(mainMenu, "返回主菜单", "再点一次返回主菜单", confirm == Confirm.MainMenu);
            SetConfirm(quit, "退出游戏", "再点一次退出游戏", confirm == Confirm.Quit);
            Set(note, confirm == Confirm.None ? "" : RaidTransfer.Active ? "这一局还没撤离，带出来的物资会丢失。" : "上次存档之后的进度不会保留。");
        }

        private static void SetConfirm(Button b, string normal, string confirmText, bool confirming)
        {
            var t = Caption(b);
            Set(t, confirming ? confirmText : normal);
            t.color = confirming ? new Color(1f, 0.65f, 0.38f) : Text1;
        }

        // ---------- 按键提示牌 ----------

        private void Column(string title, float x, params (string key, string label)[] rows)
        {
            Plate(new Vector2(x + 175, 0), new Vector2(350, 300));
            var heading = Label("Heading", hintRoot, title, 16, Amber);
            Pin(heading.rectTransform, new Vector2(x + 16, 126), new Vector2(320, 26));
            for (var i = 0; i < rows.Length; i++) Row(rows[i], new Vector2(x + 16, 90 - i * 38));
        }

        private void Plate(Vector2 center, Vector2 size)
        {
            var plate = Box("Plate", hintRoot, new Color(0.075f, 0.047f, 0.03f, 0.95f));
            plate.raycastTarget = false;
            var r = plate.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = center;
            r.sizeDelta = size;
        }

        private void Row((string key, string label) row, Vector2 at)
        {
            var hint = new Hint { key = row.key, label = row.label };
            var chip = Box("Key", hintRoot, Amber);
            chip.raycastTarget = false;
            hint.chip = chip.rectTransform;
            Pin(hint.chip, at, new Vector2(40, 28));
            hint.keyText = Label("KeyText", chip.transform, row.key, 14, Dark, TextAnchor.MiddleCenter, true);
            hint.keyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(hint.keyText.transform);
            var label = Label("Label", hintRoot, row.label, 15, Text1);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.labelRect = label.rectTransform;
            Pin(hint.labelRect, at, new Vector2(220, 28));
            hints.Add(hint);
        }

        private static void Pin(RectTransform r, Vector2 position, Vector2 size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0, 0.5f);
            r.anchoredPosition = position;
            r.sizeDelta = size;
        }

        private void Update()
        {
            if (!IsOpen) return;
            // 键名长短不一（含中文“左键”“空格”），按文字宽度撑开键帽，说明文字跟在后面。
            foreach (var hint in hints)
            {
                var width = Mathf.Max(34f, hint.keyText.preferredWidth + 16f);
                hint.chip.sizeDelta = new Vector2(width, 28);
                hint.labelRect.anchoredPosition = hint.chip.anchoredPosition + Vector2.right * (width + 12);
            }
        }
    }
}
