#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Shelter.Tests
{
    /// <summary>
    /// 安全屋守灯人 buff 截图（不是回归测试，默认不跑）。IMGUI 要从 Game 视图抓，必须在有窗口的编辑器里跑（不加 -batchmode）。
    /// 环境变量 LAMPKEEPER_SHOTS_DIR 指定输出目录。
    /// </summary>
    [Explicit("录截图，手动运行")]
    public sealed class LampkeeperShots
    {
        private string directory;
        private string savePath;

        [UnityTest]
        public IEnumerator Capture()
        {
            directory = Environment.GetEnvironmentVariable("LAMPKEEPER_SHOTS_DIR");
            if (string.IsNullOrEmpty(directory)) directory = Path.Combine(Application.temporaryCachePath, "lampkeeper_shots");
            Directory.CreateDirectory(directory);
            savePath = Path.Combine(Path.GetTempPath(), $"firechaser_lampkeeper_shots_{Guid.NewGuid():N}.json");
            PlayerProfile.PathOverride = savePath;
            RaidTransfer.Reset();
            RaidBuffs.Clear();
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(1600, 900, "Shots 1600x900");
            Loc.SetLanguageForTests("zh");
            try
            {
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(GameScenes.Shelter, new LoadSceneParameters(LoadSceneMode.Single));
                yield return Hold(1f);
                var shelter = Object.FindAnyObjectByType<ShelterController>();
                var station = ShelterStation.Find(ShelterStationKind.Lampkeeper);
                shelter.Player.Motor.Teleport(station.transform.position + new Vector3(1f, 0.3f, -1f));
                yield return Hold(1.2f);
                yield return Shot("01_lampkeeper_hall");

                shelter.OpenStation(station);
                yield return Hold(0.3f);
                yield return Shot("02_lampkeeper_panel");
                shelter.SelectBuff(RaidBuffKind.Everbright);
                yield return Hold(0.3f);
                yield return Shot("03_lampkeeper_everbright");
                shelter.SelectBuff(RaidBuffKind.None);
                shelter.ClosePanel();

                shelter.OpenStation(ShelterStation.Find(ShelterStationKind.Exit));
                shelter.RequestDepart();
                yield return Hold(0.3f);
                Assert.IsTrue(shelter.ConfirmingDepartWithoutBuff);
                yield return Shot("04_exit_confirm_no_buff");

                Loc.SetLanguageForTests("en");
                yield return Hold(0.3f);
                yield return Shot("05_exit_confirm_no_buff_en");
                shelter.OpenLampkeeper();
                yield return Hold(0.3f);
                yield return Shot("06_lampkeeper_panel_en");
                shelter.ClosePanel();
            }
            finally
            {
                Loc.SetLanguageForTests(null);
                PlayerProfile.PathOverride = null;
                RaidTransfer.Reset();
                RaidBuffs.Clear();
                if (File.Exists(savePath)) File.Delete(savePath);
            }
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), shot.EncodeToPNG());
            Object.Destroy(shot);
        }

        private static IEnumerator Hold(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
