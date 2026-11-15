using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using WildernessExtraction.Map01;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 教学关 HUD（IMGUI）：开场黑幕淡入与字幕、快捷栏上方的一句提示（按键高亮）、右上角段落进度、
    /// 每段完成时的“✓ 段名”、引信倒计时、下台阶过场黑边，以及暂停时的“跳过教学”。
    /// 同一时刻只显示一句提示；长时间没进展时提示条的火光描边闪烁。所有文字按当前语言显示。
    /// </summary>
    public sealed class TutorialHud : MonoBehaviour
    {
        private const float FlashSeconds = 2f;
        private const float OpeningFadeSeconds = 1.6f;
        private const float StepToastSeconds = 2.4f;
        /// <summary>提示条底边离屏幕底部的距离：让开快捷栏（约 78 像素高）。</summary>
        private const float PromptBottomMargin = 104f;

        private static readonly Color Ember = new Color(1f, 0.62f, 0.25f);
        private static readonly Color PanelColor = new Color(0.035f, 0.025f, 0.02f, 0.82f);
        private static readonly Regex KeyPattern = new Regex(
            @"\[?(?<![A-Za-z0-9.])(WASD|Shift|Tab|Esc|Space|LMB|RMB|[FEQRVK1-8])(?![A-Za-z0-9.])\]?|空格|鼠标左键|鼠标右键|左键|右键",
            RegexOptions.Compiled);

        [SerializeField] private TutorialDirector director;
        [SerializeField] private Map01RunFlow flow;

        private GUIStyle prompt;
        private GUIStyle caption;
        private GUIStyle stepIndex;
        private GUIStyle stepTitle;
        private GUIStyle toast;
        private GUIStyle button;
        private int shownStep = -1;
        private string completedTitle;
        private float completedAt = -100f;
        private string cachedLine;
        private string cachedMarkup;

        private void Start()
        {
            if (director == null) director = FindAnyObjectByType<TutorialDirector>();
            if (flow == null) flow = FindAnyObjectByType<Map01RunFlow>();
            // Esc 菜单里多一个“跳过教学”按钮；没有 Esc 菜单时（批处理测试）仍画旧按钮。
            if (director != null) PauseMenu.SetExtra("跳过教学，直接前往庇护所", director.Skip);
        }

        private void Update()
        {
            if (flow != null && flow.IsPaused && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                director.Skip();
            }

            if (director == null || director.Model == null) return;
            var index = director.Model.CurrentIndex;
            if (shownStep >= 0 && index > shownStep && shownStep < director.Config.steps.Length)
            {
                completedTitle = director.Config.steps[Mathf.Min(index, director.Config.steps.Length) - 1].title;
                completedAt = Time.realtimeSinceStartup;
            }

            shownStep = index;
        }

        private void OnGUI()
        {
            // Esc 菜单是 UGUI，IMGUI 总是画在它上面：菜单开着时 HUD 先不画。
            if (PauseMenu.IsOpen) return;
            if (director == null || director.Model == null) return;
            EnsureStyles();

            var config = director.Config;
            if (director.FadeAlpha > 0f) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, director.FadeAlpha));

            if (director.InCutscene && !director.IsEnding)
            {
                // 过场：上下黑边，不显示任何提示。
                var bar = Screen.height * 0.11f;
                Fill(new Rect(0, 0, Screen.width, bar), Color.black);
                Fill(new Rect(0, Screen.height - bar, Screen.width, bar), Color.black);
                return;
            }

            if (director.IsEnding)
            {
                DrawCaption(config.endingCaption, 1f);
                return;
            }

            var sinceStart = Time.realtimeSinceStartup - director.RealtimeStarted;
            if (sinceStart < OpeningFadeSeconds)
            {
                // 开场从黑里亮起来：火把只剩一点余烬。
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 1f - Mathf.SmoothStep(0f, 1f, sinceStart / OpeningFadeSeconds)));
            }

            if (sinceStart < config.openingSeconds)
            {
                DrawCaption(config.openingCaption, Mathf.Clamp01((config.openingSeconds - sinceStart) / 0.8f));
            }

            DrawStepToast();

            var step = director.CurrentStep;
            if (step == null) return;
            DrawProgress(step);

            if (flow != null && flow.IsPaused)
            {
                if (!PauseMenu.IsOpen) DrawSkip();
                return;
            }

            if (flow != null && (flow.IsBackpackOpen || flow.IsCrafting || flow.IsReading)) return;

            var line = Loc.T(director.CurrentLine());
            var urgent = false;
            var highlight = true;
            if (director.ExitHeld > 0f)
            {
                line = Loc.F("在门前站稳　{0:F1} / {1:F0} 秒", director.ExitHeld, director.ExitHoldSeconds);
                highlight = false;
            }

            var wall = director.BreachWall;
            if (wall != null && wall.State == TutorialBreachWall.WallState.Armed)
            {
                line = Loc.F("引信 {0:F1} 秒，快退开！", wall.FuseRemaining);
                urgent = true;
                highlight = false;
            }

            if (string.IsNullOrEmpty(line) || sinceStart < config.openingSeconds * 0.5f) return;
            var flash = Time.realtimeSinceStartup - director.IdleHintStartedRealtime;
            DrawPrompt(line, highlight, urgent, flash < FlashSeconds ? Mathf.PingPong(flash * 3f, 1f) : 0f);
        }

        // ---------- 各部分 ----------

        private void DrawPrompt(string line, bool highlight, bool urgent, float flash)
        {
            if (line != cachedLine)
            {
                cachedLine = line;
                cachedMarkup = highlight ? HighlightKeys(line) : line;
            }

            var width = Mathf.Min(720f, Screen.width - 40f);
            var height = Mathf.Max(52f, prompt.CalcHeight(new GUIContent(cachedMarkup), width - 44f) + 20f);
            var rect = new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height - PromptBottomMargin - height, width, height);
            var accent = urgent ? new Color(1f, 0.38f, 0.22f) : Ember;
            Fill(rect, PanelColor);
            Fill(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            var edge = Mathf.Lerp(0.22f, 0.95f, Mathf.Max(flash, urgent ? 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 10f) : 0f));
            Border(rect, new Color(accent.r, accent.g, accent.b, edge));
            GUI.Label(new Rect(rect.x + 24f, rect.y, rect.width - 40f, rect.height), cachedMarkup, prompt);
        }

        private void DrawProgress(TutorialStep step)
        {
            var count = director.Model.Count;
            var index = director.Model.CurrentIndex;
            var rect = new Rect(Screen.width - 292f, 12f, 280f, 62f);
            Fill(rect, PanelColor);
            Fill(new Rect(rect.x, rect.y, 3f, rect.height), Ember);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 6f, rect.width - 20f, 18f), Loc.F("教学 {0}/{1}", index + 1, count), stepIndex);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 22f, rect.width - 20f, 24f), Loc.T(step.title), stepTitle);
            var pip = (rect.width - 28f - (count - 1) * 4f) / count;
            for (var i = 0; i < count; i++)
            {
                var color = i < index ? Ember : i == index ? new Color(1f, 0.85f, 0.6f, 0.55f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 4f)) : new Color(1f, 1f, 1f, 0.12f);
                Fill(new Rect(rect.x + 14f + i * (pip + 4f), rect.yMax - 10f, pip, 3f), color);
            }
        }

        private void DrawStepToast()
        {
            var age = Time.realtimeSinceStartup - completedAt;
            if (string.IsNullOrEmpty(completedTitle) || age > StepToastSeconds) return;
            var alpha = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((StepToastSeconds - age) / 0.6f);
            var rise = (1f - Mathf.Clamp01(age / 0.35f)) * 12f;
            var rect = new Rect(0, Screen.height * 0.2f + rise, Screen.width, 40f);
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.55f);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 210f, rect.y - 4f, 420f, 48f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, Loc.F("✓  {0}", Loc.T(completedTitle)), toast);
            GUI.color = previous;
        }

        private void DrawCaption(string text, float alpha)
        {
            var previous = GUI.color;
            var rect = new Rect(40, Screen.height * 0.3f, Screen.width - 80, 120);
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.45f);
            GUI.DrawTexture(new Rect(0, rect.y - 10f, Screen.width, rect.height + 20f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, Loc.T(text), caption);
            GUI.color = previous;
        }

        private void DrawSkip()
        {
            var rect = new Rect(Screen.width * 0.5f - 190, Screen.height * 0.5f + 52, 380, 44);
            if (GUI.Button(rect, Loc.T("跳过教学，直接前往庇护所  [K]"), button)) director.Skip();
        }

        /// <summary>把提示里的按键（F、Tab、Shift、空格……）标成火光色的 [键]，已经带方括号的不重复加。</summary>
        public static string HighlightKeys(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            return KeyPattern.Replace(line, m =>
            {
                var key = m.Value.Trim('[', ']');
                return $"<b><color=#ffb15c>[{key}]</color></b>";
            });
        }

        // ---------- 工具 ----------

        private void EnsureStyles()
        {
            prompt ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 19, richText = true, wordWrap = true, normal = { textColor = new Color(0.96f, 0.92f, 0.86f) } };
            caption ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, wordWrap = true, normal = { textColor = new Color(1f, 0.86f, 0.62f) } };
            stepIndex ??= new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(1f, 0.75f, 0.45f, 0.85f) } };
            stepTitle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.97f, 0.93f, 0.86f) } };
            toast ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.8f, 0.45f) } };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 17 };
        }

        private static void Fill(Rect rect, Color color)
        {
            var tint = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = tint;
        }

        private static void Border(Rect rect, Color color)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1f), color);
            Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            Fill(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }
    }
}
