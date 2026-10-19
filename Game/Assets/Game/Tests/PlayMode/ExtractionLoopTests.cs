using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>
    /// 在真实灰盒场景中验证闭环：搜索容器 → 撤离 → 仓库增加 → 第二局仍保留；以及死亡不入库。
    /// 场景由菜单 Wilderness/Build Vertical Slice 生成并登记到 Build Settings。
    /// </summary>
    public sealed class ExtractionLoopTests
    {
        private const string SceneName = "VerticalSlice";
        private const float SettleMargin = 0.5f;

        [UnityTest]
        public IEnumerator LootThenExtract_AddsToStash_AndPersistsIntoNextRun()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            DisableEnemy();

            var container = NearestContainer(player.transform.position);
            yield return SearchContainer(player, container);

            var carried = player.Inventory.Inventory.ItemCount;
            Assert.Greater(carried, 0, "权重掉落表应至少生成并拾取一件物品");
            Assert.IsTrue(container.IsEmpty, "背包空间充足时应拾取该容器全部物品");
            var stashBefore = SessionStash.Current.TotalItems;
            var valueBefore = SessionStash.Current.TotalValue;
            var carriedValue = 0;
            foreach (var item in player.Inventory.Inventory.Items)
            {
                carriedValue += item.stashValue;
            }

            var zone = session.ExtractionZone;
            player.Motor.Teleport(zone.transform.position);
            yield return new WaitForSeconds(zone.Duration + SettleMargin);

            Assert.IsTrue(session.HasEnded, "停留满撤离时长后应结算");
            Assert.AreEqual(RunOutcome.Extracted, session.Result.Outcome);
            Assert.AreEqual(stashBefore + carried, SessionStash.Current.TotalItems);
            Assert.AreEqual(valueBefore + carriedValue, SessionStash.Current.TotalValue);
            Assert.AreEqual(0, player.Inventory.Inventory.Count);

            // 第二局：重新加载场景后仓库仍保留第一局所得。
            session.Restart();
            yield return null;
            yield return null;
            var nextSession = Object.FindAnyObjectByType<RunSession>();
            Assert.AreNotSame(session, nextSession);
            Assert.IsFalse(nextSession.HasEnded);
            Assert.AreEqual(3, nextSession.Player.Inventory.Inventory.UsedSlots, "新一局的手枪占 2 格、短刀占 1 格");
            Assert.AreEqual(2, nextSession.Player.Inventory.Inventory.ItemCount);
            Assert.AreEqual(stashBefore + carried, SessionStash.Current.TotalItems);
        }

        [UnityTest]
        public IEnumerator LeavingZone_InterruptsExtraction()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            var zone = session.ExtractionZone;
            DisableEnemy();

            var interrupted = false;
            zone.ExtractionInterrupted += () => interrupted = true;
            var spawn = player.transform.position;

            player.Motor.Teleport(zone.transform.position);
            yield return new WaitForSeconds(zone.Duration * 0.5f);
            Assert.IsTrue(zone.IsExtracting);

            player.Motor.Teleport(spawn);
            yield return null;
            yield return null;
            Assert.IsTrue(interrupted);
            Assert.AreEqual(0f, zone.Elapsed);
            Assert.IsFalse(session.HasEnded);
        }

        [UnityTest]
        public IEnumerator Death_LosesInventory_StashUnchanged()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            DisableEnemy();

            yield return SearchContainer(player, NearestContainer(player.transform.position));
            Assert.Greater(player.Inventory.Inventory.Count, 0);
            var stashBefore = SessionStash.Current.TotalItems;

            player.Health.TakeDamage(player.Health.Max * 10f);
            yield return null;

            Assert.IsTrue(session.HasEnded);
            Assert.AreEqual(RunOutcome.Died, session.Result.Outcome);
            Assert.AreEqual(0, player.Inventory.Inventory.Count);
            Assert.AreEqual(stashBefore, SessionStash.Current.TotalItems);
        }

        [UnityTest]
        public IEnumerator SearchCancelledWhenDamaged()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            DisableEnemy();

            var container = NearestContainer(player.transform.position);
            MoveNextTo(player, container);
            yield return null;

            SearchCancelReason? reason = null;
            player.Interactor.SearchCancelled += (_, r) => reason = r;
            Assert.IsTrue(player.Interactor.TryInteract());
            player.Health.TakeDamage(1f);
            yield return null;

            Assert.AreEqual(SearchCancelReason.Damaged, reason);
            Assert.IsFalse(player.Interactor.IsSearching);
            Assert.IsFalse(container.IsSearched);
            Assert.AreEqual(3, player.Inventory.Inventory.UsedSlots, "搜索取消后仍保留玩家自带装备");
        }

        private static IEnumerator LoadSlice()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                $"Build Settings 中没有 {SceneName}，请先运行菜单 Wilderness/Build Vertical Slice。");
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
        }

        private static void DisableEnemy()
        {
            var enemy = Object.FindAnyObjectByType<MeleeEnemy>();
            if (enemy != null)
            {
                enemy.enabled = false;
            }
        }

        private static LootContainer NearestContainer(Vector3 position)
        {
            LootContainer nearest = null;
            foreach (var container in LootContainer.Active)
            {
                if (nearest == null || container.HorizontalDistanceTo(position) < nearest.HorizontalDistanceTo(position))
                {
                    nearest = container;
                }
            }

            Assert.IsNotNull(nearest, "场景中没有容器");
            return nearest;
        }

        private static void MoveNextTo(PlayerCharacter player, LootContainer container)
        {
            var away = player.transform.position - container.transform.position;
            away.y = 0f;
            var offset = away.sqrMagnitude > 0.01f ? away.normalized * 1.3f : Vector3.back * 1.3f;
            player.Motor.Teleport(container.transform.position + offset);
        }

        private static IEnumerator SearchContainer(PlayerCharacter player, LootContainer container)
        {
            MoveNextTo(player, container);
            yield return null;
            Assert.IsTrue(player.Interactor.TryInteract(), "应能与相邻容器交互");
            Assert.IsTrue(player.Interactor.IsSearching);
            yield return new WaitForSeconds(player.Interactor.SearchDuration + SettleMargin);
            Assert.IsFalse(player.Interactor.IsSearching);
            Assert.IsTrue(container.IsSearched);
        }
    }
}
