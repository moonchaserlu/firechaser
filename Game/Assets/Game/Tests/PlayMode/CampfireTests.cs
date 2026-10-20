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
        private const string SceneName = "VerticalSlice";
        // 出生安全屋光（-12,-11，半径 6）外的空地，四周无墙体和容器。
        private static readonly Vector3 BuildStand = new Vector3(-4f, 0f, -11f);
        private static readonly Vector3 BuildSite = new Vector3(-2f, 0f, -11f);

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
            // 出生点最近的民用箱，南侧 2 米是固定灯下的空地。
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
            fire.Model.Tick(fire.Model.Remaining - 0.05f);
            Assert.IsTrue(fire.Model.IsWarning);
            yield return new WaitForSeconds(0.3f);

            Assert.IsFalse(fire.IsBurning);
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
