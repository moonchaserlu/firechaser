using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>M1：出生于安全屋光内；离光先预警后受伤；回光立即停止；光源熄灭后不再保护；重开不残留登记。</summary>
    public sealed class DarknessTests
    {
        private const string SceneName = "VerticalSlice";

        [UnityTest]
        public IEnumerator SpawnInSafeHouse_IsSafe()
        {
            yield return LoadSlice();
            var player = Object.FindAnyObjectByType<RunSession>().Player;
            Assert.IsNotNull(player.Exposure, "玩家预制体应挂载 LightExposure");

            yield return new WaitForSeconds(2f);
            Assert.AreEqual(LightExposureState.Safe, player.Exposure.State);
            Assert.AreEqual(player.Health.Max, player.Health.Current);
        }

        [UnityTest]
        public IEnumerator LeavingLight_WarnsThenDamages_ReturningStops()
        {
            yield return LoadSlice();
            var player = Object.FindAnyObjectByType<RunSession>().Player;
            var spawn = player.transform.position;

            player.Motor.Teleport(FindDarkPoint());
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(LightExposureState.Warning, player.Exposure.State);
            Assert.AreEqual(player.Health.Max, player.Health.Current, "预警期不扣血");

            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(LightExposureState.Exposed, player.Exposure.State);
            Assert.Less(player.Health.Current, player.Health.Max);

            player.Motor.Teleport(spawn);
            yield return null;
            yield return null;
            Assert.AreEqual(LightExposureState.Safe, player.Exposure.State);
            var healthInLight = player.Health.Current;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(healthInLight, player.Health.Current, "回到光内后应立即停止扣血");
        }

        [UnityTest]
        public IEnumerator LightGoingOut_StopsProtecting_AndRestartLeavesNoStaleLights()
        {
            yield return LoadSlice();
            var player = Object.FindAnyObjectByType<RunSession>().Player;
            var lightCount = LightRegistry.Scene.Count;
            Assert.Greater(lightCount, 0, "场景中应有有效光源");

            foreach (var source in Object.FindObjectsByType<SafeLightSource>())
            {
                if (source.Contains(player.transform.position))
                {
                    source.SetLit(false);
                }
            }

            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(LightExposureState.Warning, player.Exposure.State, "所在光源熄灭后应立即开始预警");

            Object.FindAnyObjectByType<RunSession>().Restart();
            yield return null;
            yield return null;
            Assert.AreEqual(lightCount, LightRegistry.Scene.Count, "重开后不应残留上一局的光源登记");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(LightExposureState.Safe, Object.FindAnyObjectByType<RunSession>().Player.Exposure.State);
        }

        private static Vector3 FindDarkPoint()
        {
            // 撤离区位于远离两处光源的东北角，M1 地图中处于黑暗。
            var point = Object.FindAnyObjectByType<ExtractionZone>().transform.position + new Vector3(-3.5f, 0f, -3.5f);
            Assert.IsFalse(LightRegistry.Scene.IsLit(point), "测试点应位于黑暗中");
            return point;
        }

        private static IEnumerator LoadSlice()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                $"Build Settings 中没有 {SceneName}，请先运行菜单 Wilderness/Build Vertical Slice。");
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var enemy = Object.FindAnyObjectByType<MeleeEnemy>();
            if (enemy != null)
            {
                enemy.enabled = false;
            }
        }
    }
}
