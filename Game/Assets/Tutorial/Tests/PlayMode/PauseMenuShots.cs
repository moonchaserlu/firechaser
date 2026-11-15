#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WildernessExtraction.Map01;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.Tests
{
    /// <summary>
    /// Esc 菜单改前 / 改后对比截图（不是回归测试，默认不跑）。UGUI Overlay 和旧的 IMGUI 都要从 Game 视图抓，
    /// 必须在有窗口的编辑器里跑（不要加 -batchmode）。环境变量 PAUSE_SHOTS_DIR 指定输出目录。
    /// “改前”是把新菜单整个关掉后，旧 HUD 画出来的那块“已暂停”提示框。
    /// </summary>
    [Explicit("录对比截图，手动运行")]
    public sealed class PauseMenuShots
    {
        private string directory;

        [UnityTest]
        public IEnumerator Capture()
        {
            directory = Environment.GetEnvironmentVariable("PAUSE_SHOTS_DIR");
            if (string.IsNullOrEmpty(directory)) directory = Path.Combine(Application.temporaryCachePath, "pause_shots");
            Directory.CreateDirectory(directory);
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1600, 900, "Shots 1600x900");
            Loc.SetLanguageForTests("zh");

            // ---- Map01 ----
            yield return Load(GameScenes.Map01);
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            var menu = PauseMenu.Active;
            Assert.NotNull(menu, "Esc 菜单没有自动创建");
            menu.Bind();
            yield return Hold(1.5f);

            menu.gameObject.SetActive(false);
            flow.SetPaused(true);
            yield return Hold(0.3f);
            yield return Shot("map01_before");

            menu.gameObject.SetActive(true);
            menu.Bind();
            yield return Hold(0.3f);
            Assert.IsTrue(PauseMenu.IsOpen, "暂停后 Esc 菜单没有打开");
            yield return Shot("map01_after_menu");

            menu.ReturnToMainMenu(); // 第一次只进入确认状态
            yield return Hold(0.2f);
            yield return Shot("map01_after_confirm");

            menu.OpenSettings();
            var settings = SettingsWindow.Active;
            for (var tab = 0; tab < 4; tab++)
            {
                settings.SelectTab(tab);
                yield return Hold(0.2f);
                yield return Shot($"map01_after_settings_{tab}");
            }

            Loc.SetLanguageForTests("en");
            settings.SelectTab(0);
            yield return Hold(0.2f);
            yield return Shot("map01_after_settings_en");
            settings.Close();
            yield return Hold(0.2f);
            yield return Shot("map01_after_menu_en");
            Loc.SetLanguageForTests("zh");
            flow.SetPaused(false);
            yield return Hold(0.3f);
            Assert.IsFalse(PauseMenu.IsOpen, "继续游戏后 Esc 菜单没有关上");

            // ---- 教学关：多一个“跳过教学” ----
            TutorialDirector.SuppressSceneLoad = true;
            yield return Load(GameScenes.Tutorial);
            yield return Hold(1.5f);
            flow = Object.FindAnyObjectByType<Map01RunFlow>();
            menu = PauseMenu.Active;
            menu.gameObject.SetActive(false);
            flow.SetPaused(true);
            yield return Hold(0.3f);
            yield return Shot("tutorial_before");
            menu.gameObject.SetActive(true);
            menu.Bind();
            yield return Hold(0.3f);
            Assert.IsTrue(menu.ExtraVisible, "教学关的 Esc 菜单缺少“跳过教学”");
            yield return Shot("tutorial_after");
            flow.SetPaused(false);
            TutorialDirector.SuppressSceneLoad = false;
            Loc.SetLanguageForTests(null);
        }

        private static IEnumerator Load(string path)
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), shot.EncodeToPNG());
            Object.Destroy(shot);
        }

        /// <summary>暂停时 timeScale 为 0，按真实时间等。</summary>
        private static IEnumerator Hold(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
