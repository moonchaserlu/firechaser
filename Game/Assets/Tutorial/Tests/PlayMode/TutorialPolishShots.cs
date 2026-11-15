#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WildernessExtraction.Map01;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.Tests
{
    /// <summary>
    /// 教学关精修对比截图（不是回归测试，默认不跑）：开场字幕、第 1 段提示、搜箱段、长暗段、引信倒计时。
    /// HUD 是 IMGUI，必须在有窗口的编辑器里跑（不要加 -batchmode），从 Game 视图抓帧。
    /// 环境变量：POLISH_SHOTS_DIR 输出目录，POLISH_SHOTS_PREFIX 文件名前缀，POLISH_SHOTS_LANG 为 en / zh 时切换语言。
    /// </summary>
    [Explicit("录对比截图，手动运行")]
    public sealed class TutorialPolishShots
    {
        private const int Width = 1600;
        private const int Height = 900;

        private TutorialDirector director;
        private PlayerCharacter player;
        private string directory;
        private string prefix;

        [UnityTest]
        public IEnumerator Capture()
        {
            directory = Environment.GetEnvironmentVariable("POLISH_SHOTS_DIR");
            if (string.IsNullOrEmpty(directory)) directory = Path.Combine(Application.temporaryCachePath, "polish_shots");
            Directory.CreateDirectory(directory);
            prefix = Environment.GetEnvironmentVariable("POLISH_SHOTS_PREFIX") ?? "shot";
            ApplyLanguage(Environment.GetEnvironmentVariable("POLISH_SHOTS_LANG"));

            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(Width, Height, "Shots 1600x900");
            TutorialDirector.SuppressSceneLoad = true;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(GameScenes.Tutorial, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            director = Object.FindAnyObjectByType<TutorialDirector>();
            player = Object.FindAnyObjectByType<Map01Authority>().Player;
            foreach (var enemy in Object.FindObjectsByType<TutorialEnemy>().Where(e => !e.Pinned)) enemy.gameObject.SetActive(false);

            yield return Hold(1.2f);
            yield return Shot("1_opening");

            yield return Hold(4f);
            yield return Shot("2_move_prompt");

            director.Report(TutorialFacts.Moved);
            yield return Hold(0.4f);
            yield return Shot("3_lamp_lit");

            player.Motor.Teleport(new Vector3(28f, 0f, 0f));
            yield return Hold(1.2f);
            yield return Shot("4_loot_prompt");

            director.Report("crate_cloth");
            director.Report("crate_chemicals");
            director.Report(TutorialFacts.BackpackOpened);
            director.Report(TutorialFacts.HasFuel);
            player.Motor.Teleport(new Vector3(44f, 0f, 0f));
            yield return Hold(1.2f);
            yield return Shot("5_long_dark");

            foreach (var step in new[] { "campfire_built", "reached_far_lamp" }) director.Report(step);
            var pinned = Object.FindObjectsByType<TutorialEnemy>().FirstOrDefault(e => e.Pinned);
            if (pinned != null) pinned.Health.TakeDamage(9999f, player.gameObject);
            else director.Report("pinned_killed");
            player.Motor.Teleport(new Vector3(66f, 0f, -20f));
            yield return Hold(1.5f);
            yield return Shot("6_bandage");

            director.Report("relic_searched");
            director.Report("bleeding_stopped");
            director.Report("targets_cleared");
            player.Motor.Teleport(new Vector3(40f, 0f, -24f));
            yield return Hold(1.5f);
            yield return Shot("7_sneak");

            director.Report("sneaked_past");
            var seeker = Object.FindAnyObjectByType<TutorialChoicePoint>();
            if (seeker != null)
            {
                player.Motor.Teleport(seeker.transform.position + new Vector3(1.8f, 0f, -1.2f));
                yield return Hold(2.8f);
                yield return Shot("8_talk");
                director.OpenChoice();
                yield return Hold(0.5f);
                yield return Shot("9_choice");
                director.Choose(1);
                yield return Hold(1.2f);
                yield return Shot("10_everbright");
            }

            foreach (var step in new[] { "choice_made", "campfire_built_2", "patrol_b_killed", "at_exit" }) director.Report(step);
            player.Motor.Teleport(TutorialBuilderPositions.ExitDoor);
            yield return Hold(1f);
            var wall = Object.FindAnyObjectByType<TutorialBreachWall>();
            player.Inventory.Inventory.TryAdd(wall.Explosive);
            player.Motor.Teleport(TutorialBuilderPositions.BreachPoint + new Vector3(1.5f, 0f, 0f));
            yield return Hold(0.3f);
            wall.TryArm(player);
            player.Motor.Teleport(TutorialBuilderPositions.ExitDoor + new Vector3(3f, 0f, 0f));
            yield return Hold(1f);
            yield return Shot("11_fuse");

            yield return Hold(2.6f);
            yield return Shot("12_blown");

            TutorialDirector.SuppressSceneLoad = false;
            ApplyLanguage(null);
        }

        /// <summary>按名字反射调用本地化（老代码里没有这个类时什么也不做）。</summary>
        private static void ApplyLanguage(string code)
        {
            var type = Type.GetType("WildernessExtraction.Loc, WildernessExtraction.Runtime");
            var method = type?.GetMethod("SetLanguageForTests");
            method?.Invoke(null, new object[] { code });
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(directory, $"{prefix}_{name}.png"), shot.EncodeToPNG());
            Object.Destroy(shot);
        }

        private static IEnumerator Hold(float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }
    }
}
#endif
