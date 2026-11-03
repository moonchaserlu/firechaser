using UnityEngine;
using UnityEngine.InputSystem;
using WildernessExtraction.Map01;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 教学关占位 HUD（IMGUI）：开场/结尾字幕、屏幕下方的一句提示、右上角段落进度、门前停留进度，
    /// 以及暂停时的“跳过教学”。同一时刻只显示一句提示；长时间没进展时提示条闪烁。
    /// </summary>
    public sealed class TutorialHud : MonoBehaviour
    {
        private const float FlashSeconds = 2f;

        [SerializeField] private TutorialDirector director;
        [SerializeField] private Map01RunFlow flow;

        private GUIStyle prompt;
        private GUIStyle caption;
        private GUIStyle panel;
        private GUIStyle button;

        private void Start()
        {
            if (director == null) director = FindAnyObjectByType<TutorialDirector>();
            if (flow == null) flow = FindAnyObjectByType<Map01RunFlow>();
        }

        private void Update()
        {
            if (flow != null && flow.IsPaused && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                director.Skip();
            }
        }

        private void OnGUI()
        {
            if (director == null || director.Model == null) return;
            prompt ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 19, richText = true, wordWrap = true, padding = new RectOffset(16, 16, 10, 10) };
            caption ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, wordWrap = true, normal = { textColor = new Color(1f, 0.86f, 0.62f) } };
            panel ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true, padding = new RectOffset(10, 10, 8, 8) };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 17 };

            var config = director.Config;
            if (director.IsEnding)
            {
                DrawCaption(config.endingCaption, 1f);
                return;
            }

            var sinceStart = Time.realtimeSinceStartup - director.RealtimeStarted;
            if (sinceStart < config.openingSeconds)
            {
                DrawCaption(config.openingCaption, Mathf.Clamp01((config.openingSeconds - sinceStart) / 0.8f));
            }

            var step = director.CurrentStep;
            if (step == null) return;

            GUI.Box(new Rect(Screen.width - 252, 12, 240, 34), $"教学 {director.Model.CurrentIndex + 1}/{director.Model.Count}　{step.title}", panel);

            if (flow != null && flow.IsPaused)
            {
                DrawSkip();
                return;
            }

            if (flow != null && (flow.IsBackpackOpen || flow.IsCrafting || flow.IsReading)) return;

            var line = director.CurrentLine();
            if (director.ExitHeld > 0f)
            {
                line = $"在门前站稳　{director.ExitHeld:F1} / {director.ExitHoldSeconds:F0} 秒";
            }

            if (string.IsNullOrEmpty(line) || sinceStart < config.openingSeconds * 0.5f) return;
            var flash = Time.realtimeSinceStartup - director.IdleHintStartedRealtime;
            var previous = GUI.color;
            if (flash < FlashSeconds)
            {
                GUI.color = Color.Lerp(new Color(1f, 0.8f, 0.4f), Color.white, Mathf.PingPong(flash * 3f, 1f));
            }

            var width = Mathf.Min(640f, Screen.width - 40f);
            GUI.Box(new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height - 92, width, 56), line, prompt);
            GUI.color = previous;
        }

        private void DrawCaption(string text, float alpha)
        {
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(40, Screen.height * 0.3f, Screen.width - 80, 120), text, caption);
            GUI.color = previous;
        }

        private void DrawSkip()
        {
            var rect = new Rect(Screen.width * 0.5f - 160, Screen.height * 0.5f + 52, 320, 44);
            if (GUI.Button(rect, "跳过教学，直接前往庇护所  [K]", button)) director.Skip();
        }
    }
}
