#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WildernessExtraction.Map01;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.Tests
{
    /// <summary>
    /// 录一段教学关结尾的演示视频（不是回归测试，默认不跑）：建火引燃长暗段的灯 → 进屋落石 → 炸开外墙 → 下台阶过场。
    /// 角色由脚本驱动走路；提示文字用一层 uGUI 画在画面上（IMGUI 的 HUD 录不进相机画面）。
    /// 运行：-testFilter WildernessExtraction.Tutorial.Tests.TutorialEndingVideo，输出 Docs/Tutorial/Tutorial_Ending.mp4。
    /// </summary>
    [Explicit("录演示视频，手动运行")]
    public sealed class TutorialEndingVideo
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int Fps = 30;

        private TutorialDirector director;
        private PlayerCharacter player;
        private Text prompt;
        private Text label;
        private Image fade;
        private Image barTop;
        private Image barBottom;

        [UnityTest]
        public IEnumerator RecordEnding()
        {
            TutorialDirector.SuppressSceneLoad = true;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(GameScenes.Tutorial, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            director = Object.FindAnyObjectByType<TutorialDirector>();
            player = Object.FindAnyObjectByType<Map01Authority>().Player;
            foreach (var enemy in Object.FindObjectsByType<TutorialEnemy>())
            {
                if (!enemy.Pinned) enemy.gameObject.SetActive(false);
            }

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Tutorial", "Tutorial_Ending.mp4"));
            var view = Camera.main;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            view.targetTexture = target;
            view.aspect = (float)Width / Height;
            BuildOverlay(view);
            var frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var attributes = new VideoTrackAttributes
            {
                frameRate = new MediaRational(Fps),
                width = Width,
                height = Height,
                includeAlpha = false
            };

            Time.captureFramerate = Fps;
            player.Motor.enabled = false;
            var recording = true;
            var frames = 0;
            using (var encoder = new MediaEncoder(path, attributes))
            {
                var grabber = new GameObject("FrameGrabber").AddComponent<FrameGrabber>();
                grabber.Run(() => recording, () =>
                {
                    RefreshOverlay();
                    Canvas.ForceUpdateCanvases();
                    view.Render();
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    frame.Apply();
                    RenderTexture.active = null;
                    encoder.AddFrame(frame);
                    // 抽帧给人检查：设了 TUTORIAL_VIDEO_STILLS 时每 2 秒存一张 PNG。
                    var stills = Environment.GetEnvironmentVariable("TUTORIAL_VIDEO_STILLS");
                    if (!string.IsNullOrEmpty(stills) && frames % (Fps * 2) == 0) File.WriteAllBytes(Path.Combine(stills, $"frame_{frames:0000}.png"), frame.EncodeToPNG());
                    frames++;
                });

                yield return Play();
                recording = false;
                yield return null;
            }

            Time.captureFramerate = 0;
            view.targetTexture = null;
            TutorialDirector.SuppressSceneLoad = false;
            Debug.Log($"[Tutorial] 视频已写入 {path}（{frames} 帧，{frames / (float)Fps:F1} 秒）");
            Assert.Greater(frames, Fps * 10);
        }

        private IEnumerator Play()
        {
            // 前四段直接认可，从长暗段开始录。
            foreach (var fact in new[] { TutorialFacts.Moved, TutorialFacts.Sprinted, "reached_lamp_2", "crate_cloth", "crate_chemicals", TutorialFacts.BackpackOpened })
            {
                director.Report(fact);
            }

            player.Inventory.Inventory.TryAdd(player.Placer.Config.fuelItem);

            // 1 长暗段：建火，火光引燃中间和对岸的灯，走过去全程有光
            label.text = "长暗段：火堆点起来，引燃中间和对岸的灯";
            player.Motor.Teleport(new Vector3(39f, 0f, 0f));
            yield return Hold(1f);
            yield return Walk(new Vector3(44f, 0f, 0f), 3.5f);
            yield return Hold(0.6f);
            Assert.IsTrue(player.Placer.BeginPreview());
            yield return Hold(0.8f);
            Assert.IsTrue(player.Placer.TryConfirm(new Vector3(46f, 0f, 0f)));
            yield return Hold(3f);
            yield return Walk(new Vector3(66f, 0f, 0f), 4.5f);
            yield return Hold(1f);

            // 中间几段直接认可，跳到最后一排
            var pinned = Array.Find(Object.FindObjectsByType<TutorialEnemy>(), e => e.Pinned);
            director.Report(TutorialFacts.Dodged);
            pinned.Health.TakeDamage(9999f, player.gameObject);
            foreach (var target in Object.FindObjectsByType<TutorialTarget>()) target.Health.TakeDamage(5f, player.gameObject);
            director.Report(TutorialFacts.Reloaded);
            yield return null;

            // 2 走进尽头的屋子：身后落石
            label.text = "走进尽头的屋子：身后落石，来路被堵死";
            player.Motor.Teleport(new Vector3(16f, 0f, -24f));
            yield return Hold(0.8f);
            yield return Walk(new Vector3(4f, 0f, -24f), 4f);
            yield return Hold(2.2f);

            // 3 搜炸药，墙边安放，退开，炸开
            label.text = "搜出炸药，在外墙裂缝处安放，退开";
            var crate = Array.Find(Object.FindObjectsByType<LootContainer>(), c => c.name == "Crate_Explosive");
            yield return Walk(crate.transform.position + new Vector3(0f, 0f, 1f), 3f);
            player.Interactor.TryInteract();
            while (!crate.IsEmpty) yield return null;
            yield return Hold(0.6f);
            yield return Walk(new Vector3(-2.5f, 0f, -24f), 3.5f);
            yield return Hold(0.4f);
            Assert.IsTrue(director.BreachWall.TryArm(player));
            yield return Hold(0.5f);
            yield return Walk(new Vector3(4.5f, 0f, -24f), 5f);
            while (director.BreachWall.State != TutorialBreachWall.WallState.Blown) yield return null;
            label.text = "墙炸开了";
            yield return Hold(2f);

            // 4 走到墙洞口：下台阶过场
            label.text = "走到洞口：下台阶过场";
            yield return Walk(new Vector3(-5.2f, 0f, -24f), 3.5f);
            label.text = string.Empty;
            while (!director.IsEnding) yield return null;
            yield return Hold(director.Config.endingSeconds + 0.5f);
        }

        private IEnumerator Walk(Vector3 to, float speed)
        {
            var from = player.transform.position;
            to.y = from.y;
            var flat = to - from;
            if (flat.sqrMagnitude > 0.0001f) player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            var duration = flat.magnitude / speed;
            for (var t = 0f; t < duration && !director.InCutscene; t += Time.deltaTime)
            {
                player.Motor.Teleport(Vector3.Lerp(from, to, t / duration));
                player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                yield return null;
            }

            if (!director.InCutscene) player.Motor.Teleport(to);
        }

        private static IEnumerator Hold(float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        private void BuildOverlay(Camera view)
        {
            var canvasObject = new GameObject("VideoOverlay");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = view;
            canvas.planeDistance = view.nearClipPlane + 0.5f;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 28);

            fade = Panel(canvas.transform, "Fade", Vector2.zero, Vector2.one, Color.clear);
            barTop = Panel(canvas.transform, "BarTop", new Vector2(0f, 0.89f), Vector2.one, Color.clear);
            barBottom = Panel(canvas.transform, "BarBottom", Vector2.zero, new Vector2(1f, 0.11f), Color.clear);
            prompt = Label(canvas.transform, font, new Vector2(0.1f, 0.03f), new Vector2(0.9f, 0.16f), 26, TextAnchor.MiddleCenter);
            label = Label(canvas.transform, font, new Vector2(0.02f, 0.9f), new Vector2(0.98f, 0.98f), 22, TextAnchor.UpperLeft);
            label.color = new Color(1f, 0.85f, 0.55f);
        }

        private void RefreshOverlay()
        {
            var bars = director.InCutscene && !director.IsEnding ? Color.black : Color.clear;
            barTop.color = bars;
            barBottom.color = bars;
            fade.color = new Color(0f, 0f, 0f, director.FadeAlpha);
            if (director.IsEnding)
            {
                prompt.text = director.Config.endingCaption;
                prompt.rectTransform.anchorMin = new Vector2(0.1f, 0.4f);
                prompt.rectTransform.anchorMax = new Vector2(0.9f, 0.6f);
                prompt.color = new Color(1f, 0.86f, 0.62f);
                label.text = string.Empty;
                return;
            }

            var wall = director.BreachWall;
            prompt.text = director.InCutscene ? string.Empty
                : wall != null && wall.State == TutorialBreachWall.WallState.Armed ? $"引信 {wall.FuseRemaining:F1} 秒，快退开！"
                : director.CurrentLine();
        }

        private static Image Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text Label(Transform parent, Font font, Vector2 min, Vector2 max, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return text;
        }

        /// <summary>每帧在所有逻辑之后手动渲染相机并抓一帧（批处理模式下相机不会自动出图）。</summary>
        [DefaultExecutionOrder(10000)]
        private sealed class FrameGrabber : MonoBehaviour
        {
            private Func<bool> active;
            private Action grab;

            public void Run(Func<bool> isActive, Action onFrame)
            {
                active = isActive;
                grab = onFrame;
            }

            private void LateUpdate()
            {
                if (active == null || !active()) return;
                grab();
            }
        }
    }
}
#endif
