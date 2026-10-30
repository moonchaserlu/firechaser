using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>M2：燃料扣除只发生在建造成功时；火堆提供黑暗安全并在燃尽后失效；阻挡与移动取消；重开不残留。</summary>
    public sealed class CampfireTests
    {
        private const string ScenePath = "Assets/Game/Scenes/VerticalSlice.unity";
        // 近郊东侧黑暗空地，远离安全屋核心与固定灯，四周无墙体和容器。
        private static readonly Vector3 BuildStand = new Vector3(18f, 0f, 10f);
        private static readonly Vector3 BuildSite = new Vector3(20f, 0f, 10f);

        [UnityTest]
        public IEnumerator StartsWithTwoFuel()
        {
            yield return LoadSlice();
            Assert.AreEqual(2, Player().Placer.FuelCount);
        }

        [UnityTest]
        public IEnumerator Build_ConsumesOneFuel_AndProtectsFromDarkness()
        {
            yield return LoadSlice();
            var player = Player();
            var placer = player.Placer;
            player.Motor.Teleport(BuildStand);
            yield return null;

            Assert.IsTrue(placer.BeginPreview());
            Assert.IsTrue(placer.TryConfirm(BuildSite), $"应能在空地建火：{placer.SiteResult}");
            Assert.AreEqual(CampfirePlacerState.Building, placer.State);
            Assert.AreEqual(2, placer.FuelCount, "建造完成前不扣燃料");

            yield return new WaitForSeconds(placer.Config.buildDuration + 0.3f);
            Assert.AreEqual(CampfirePlacerState.Idle, placer.State);
            Assert.AreEqual(1, placer.FuelCount);
            Assert.AreEqual(1, Campfire.Burning.Count);

            yield return null;
            Assert.AreEqual(LightExposureState.Safe, player.Exposure.State, "站在火堆范围内应安全");
            var health = player.Health.Current;
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(health, player.Health.Current, "火堆内不受黑暗伤害");
        }

        [UnityTest]
        public IEnumerator Moving_CancelsBuild_WithoutConsumingFuel()
        {
            yield return LoadSlice();
            var player = Player();
            var placer = player.Placer;
            player.Motor.Teleport(BuildStand);
            yield return null;

            CampfireBuildCancelReason? reason = null;
            placer.BuildCancelled += r => reason = r;
            Assert.IsTrue(placer.BeginPreview());
            Assert.IsTrue(placer.TryConfirm(BuildSite));
            player.Motor.Teleport(BuildStand + Vector3.back);
            yield return null;
            yield return null;

            Assert.AreEqual(CampfireBuildCancelReason.Moved, reason);
            Assert.AreEqual(2, placer.FuelCount);
            Assert.AreEqual(0, Campfire.Burning.Count);
        }

        [UnityTest]
        public IEnumerator Obstacle_RejectsPlacement()
        {
            yield return LoadSlice();
            var player = Player();
            var placer = player.Placer;
            // 出生点最近的近郊民用箱，南侧 2 米是空地。
            LootContainer crate = null;
            foreach (var container in LootContainer.Active)
            {
                if (crate == null || container.HorizontalDistanceTo(player.transform.position) < crate.HorizontalDistanceTo(player.transform.position)) crate = container;
            }
            Assert.IsNotNull(crate);
            player.Motor.Teleport(crate.transform.position + new Vector3(0f, 0f, -2f));
            yield return null;

            Assert.IsTrue(placer.BeginPreview());
            Assert.IsFalse(placer.TryConfirm(crate.transform.position));
            Assert.AreEqual(CampfirePlacementResult.Blocked, placer.SiteResult);
            Assert.AreEqual(2, placer.FuelCount);
        }

        [UnityTest]
        public IEnumerator BurnOut_StopsProtecting_AndRestartClearsFires()
        {
            yield return LoadSlice();
            var player = Player();
            var placer = player.Placer;
            player.Motor.Teleport(BuildStand);
            yield return null;
            Assert.IsTrue(placer.BeginPreview());
            Assert.IsTrue(placer.TryConfirm(BuildSite));
            yield return new WaitForSeconds(placer.Config.buildDuration + 0.3f);
            Assert.AreEqual(1, Campfire.Burning.Count);

            var fire = Campfire.Burning[0];
            var fireLight = fire.GetComponentInChildren<Light>();
            var particles = fire.GetComponentsInChildren<ParticleSystem>();
            Assert.IsTrue(fireLight.enabled);
            Assert.GreaterOrEqual(particles.Length, 2, "火焰和火星");
            Assert.IsTrue(particles[0].isPlaying, "燃烧时火焰在播放");
            fire.Model.Tick(fire.Model.Remaining - 0.05f);
            Assert.IsTrue(fire.Model.IsWarning);
            yield return new WaitForSeconds(0.3f);

            Assert.IsFalse(fire.IsBurning);
            Assert.IsFalse(fireLight.enabled, "燃尽后点光源关闭");
            foreach (var particle in particles) Assert.IsFalse(particle.gameObject.activeInHierarchy, "燃尽后火焰隐藏");
            Assert.AreEqual(0, Campfire.Burning.Count);
            Assert.AreNotEqual(LightExposureState.Safe, player.Exposure.State, "火堆熄灭后应立即失去保护");

            Assert.IsTrue(placer.BeginPreview());
            Assert.IsTrue(placer.TryConfirm(BuildSite), "熄灭的火堆不再阻挡新火堆");
            yield return new WaitForSeconds(placer.Config.buildDuration + 0.3f);
            Assert.AreEqual(1, Campfire.Burning.Count);

            Object.FindAnyObjectByType<RunSession>().Restart();
            yield return null;
            yield return null;
            Assert.AreEqual(0, Campfire.Burning.Count, "重开后不残留上一局火堆");
            Assert.AreEqual(0, Object.FindObjectsByType<Campfire>().Length);
        }

        private static PlayerCharacter Player()
        {
            var player = Object.FindAnyObjectByType<RunSession>().Player;
            Assert.IsNotNull(player.Placer, "玩家预制体应挂载 CampfirePlacer");
            return player;
        }

        private static IEnumerator LoadSlice()
        {
#if UNITY_EDITOR
            // VerticalSlice 只供测试，不进 Build Settings，按路径加载。
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("VerticalSlice 场景测试只在编辑器中运行。");
#endif
            yield return null;
            yield return null;
            // AI Bot 有独立测试（AiBotTests），这里停用以免干扰。
            foreach (var bot in Object.FindObjectsByType<AiBotController>()) bot.gameObject.SetActive(false);

            foreach (var enemy in Object.FindObjectsByType<MeleeEnemy>())
            {
                enemy.enabled = false;
            }
        }
    }
}
