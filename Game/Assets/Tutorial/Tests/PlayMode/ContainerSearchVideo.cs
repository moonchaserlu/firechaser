#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WildernessExtraction.Map01;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.Tests
{
    /// <summary>
    /// 录一段箱子搜索的演示视频（不是回归测试，默认不跑）：打开箱子 → 逐件搜出 → 拖进背包 → 右键快速拿 → R 全部拿取。
    /// 容器面板是 IMGUI，相机画面里没有，所以必须在有窗口的编辑器里跑（不要加 -batchmode），从 Game 视图抓帧。
    /// 运行：Unity.exe -projectPath Game -runTests -testPlatform PlayMode -testFilter WildernessExtraction.Tutorial.Tests.ContainerSearchVideo，
    /// 输出 Docs/Loot/Container_Search.mp4。
    /// </summary>
    [Explicit("录演示视频，手动运行")]
    public sealed class ContainerSearchVideo
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int Fps = 30;

        private PlayerCharacter player;
        private ContainerLootPanel panel;
        private Caption caption;

        [UnityTest]
        public IEnumerator RecordSearch()
        {
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(Width, Height, "Video 1280x720");
            TutorialDirector.SuppressSceneLoad = true;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(GameScenes.Tutorial, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            player = Object.FindAnyObjectByType<Map01Authority>().Player;
            panel = player.GetComponent<ContainerLootPanel>();
            foreach (var enemy in Object.FindObjectsByType<TutorialEnemy>()) enemy.gameObject.SetActive(false);
            if (player.Exposure != null) player.Exposure.enabled = false;
            caption = new GameObject("VideoCaption").AddComponent<Caption>();

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Loot", "Container_Search.mp4"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var stills = Environment.GetEnvironmentVariable("LOOT_VIDEO_STILLS");
            if (!string.IsNullOrEmpty(stills)) Directory.CreateDirectory(stills);
            var attributes = new VideoTrackAttributes { frameRate = new MediaRational(Fps), width = Width, height = Height, includeAlpha = false };
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            var frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);

            Time.captureFramerate = Fps;
            var frames = 0;
            var recording = true;
            using (var encoder = new MediaEncoder(path, attributes))
            {
                var grabber = new GameObject("FrameGrabber").AddComponent<EndOfFrameGrabber>();
                grabber.Run(() => recording, () =>
                {
                    var shot = ScreenCapture.CaptureScreenshotAsTexture();
                    Graphics.Blit(shot, target);
                    Object.Destroy(shot);
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    frame.Apply();
                    RenderTexture.active = null;
                    encoder.AddFrame(frame);
                    if (!string.IsNullOrEmpty(stills) && frames % Fps == 0) File.WriteAllBytes(Path.Combine(stills, $"frame_{frames:0000}.png"), frame.EncodeToPNG());
                    frames++;
                });

                yield return Play();
                recording = false;
                yield return null;
                yield return null;
            }

            Time.captureFramerate = 0;
            TutorialDirector.SuppressSceneLoad = false;
            Debug.Log($"[ContainerSearchVideo] 视频已写入 {path}（{frames} 帧，{frames / (float)Fps:F1} 秒）");
            Assert.Greater(frames, Fps * 5);
        }

        private IEnumerator Play()
        {
            var interactor = player.Interactor;
            var cloth = Array.Find(Object.FindObjectsByType<LootContainer>(), c => c.name == "Crate_Cloth");
            var chemicals = Array.Find(Object.FindObjectsByType<LootContainer>(), c => c.name == "Crate_Chemicals");

            // 1 靠近箱子：提示 [F] 搜索
            caption.Text = "靠近箱子，按 F 搜索";
            player.Motor.Teleport(cloth.transform.position + new Vector3(0f, 0f, -1.2f));
            yield return Hold(1.5f);

            // 2 打开：物品一件一件搜出来，留在箱子里
            caption.Text = "物品一件一件搜出来（越稀有越久），搜出来的先留在箱子里";
            Assert.IsTrue(interactor.TryInteract());
            while (!cloth.IsSearched) yield return null;
            yield return Hold(1.2f);

            // 3 拖进背包
            caption.Text = "把物品拖进右边的背包才算拿走";
            panel.ScriptedPointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.75f);
            yield return Move(panel.ContainerSlotCenter(0), 0.6f);
            yield return Hold(0.3f);
            Assert.IsTrue(panel.BeginDrag(0));
            yield return Move(panel.BackpackCenter, 0.8f);
            yield return Hold(0.2f);
            Assert.IsTrue(panel.Drop(), "拖进背包");
            yield return Hold(0.8f);

            // 4 右键快速拿
            caption.Text = "右键（或 Shift+左键、双击）直接放进背包";
            yield return Move(panel.ContainerSlotCenter(0), 0.6f);
            yield return Hold(0.4f);
            Assert.IsTrue(interactor.TakeAt(0), "右键快速拿取");
            yield return Hold(1f);

            // 5 关掉，换一只箱子，R 全部拿取
            caption.Text = "F / Tab / Esc 关闭面板";
            panel.ScriptedPointer = null;
            yield return Hold(0.6f);
            interactor.CloseLoot();
            yield return Hold(0.6f);
            caption.Text = "另一只箱子：搜完按 R 全部拿取";
            player.Motor.Teleport(chemicals.transform.position + new Vector3(0f, 0f, -1.2f));
            yield return Hold(0.8f);
            Assert.IsTrue(interactor.TryInteract());
            while (!chemicals.IsSearched) yield return null;
            yield return Hold(0.8f);
            interactor.TakeAll();
            Assert.IsTrue(chemicals.IsEmpty);
            yield return Hold(1.5f);
            interactor.CloseLoot();
            caption.Text = string.Empty;
            yield return Hold(0.8f);
        }

        private IEnumerator Move(Vector2 to, float seconds)
        {
            var from = panel.ScriptedPointer ?? to;
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / seconds);
                panel.ScriptedPointer = Vector2.Lerp(from, to, k);
                yield return null;
            }

            panel.ScriptedPointer = to;
        }

        private static IEnumerator Hold(float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        /// <summary>画面顶部的说明文字（IMGUI，和面板一起被抓进视频）。</summary>
        private sealed class Caption : MonoBehaviour
        {
            public string Text = string.Empty;
            private GUIStyle style;

            private void OnGUI()
            {
                if (string.IsNullOrEmpty(Text)) return;
                GUI.depth = -100;
                style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = new Color(1f, 0.85f, 0.55f);
                var rect = new Rect(0, 8, Screen.width, 40);
                var previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(Screen.width * 0.15f, 8, Screen.width * 0.7f, 40), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(rect, Text, style);
            }
        }

        /// <summary>每帧画完（含 IMGUI）之后抓一帧。</summary>
        private sealed class EndOfFrameGrabber : MonoBehaviour
        {
            private Func<bool> active;
            private Action grab;

            public void Run(Func<bool> isActive, Action onFrame)
            {
                active = isActive;
                grab = onFrame;
                StartCoroutine(Loop());
            }

            private IEnumerator Loop()
            {
                var wait = new WaitForEndOfFrame();
                while (true)
                {
                    yield return wait;
                    if (active != null && active()) grab();
                }
            }
        }
    }
}
#endif
