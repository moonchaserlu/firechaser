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
        private const string ScenePath = "Assets/Game/Scenes/VerticalSlice.unity";
        private const float SettleMargin = 0.5f;

        [UnityTest]
        public IEnumerator LootThenExtract_AddsToStash_AndPersistsIntoNextRun()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            DisableEnemy();
            DisableDarkness(player);

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
            yield return LeaveHome(player, zone);
            player.Motor.Teleport(zone.transform.position);
            yield return new WaitForSeconds(zone.Duration + SettleMargin);

            Assert.IsTrue(session.HasEnded, "离家后回到安全屋入口停留满时长应结算");
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
            Assert.AreEqual(4, nextSession.Player.Inventory.Inventory.UsedSlots, "新一局：手枪 2 格、短刀 1 格、2 个燃料包堆叠占 1 格");
            Assert.AreEqual(4, nextSession.Player.Inventory.Inventory.ItemCount);
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
            DisableDarkness(player);

            var interrupted = false;
            zone.ExtractionInterrupted += () => interrupted = true;
            var spawn = player.transform.position;

            yield return LeaveHome(player, zone);
            player.Motor.Teleport(zone.transform.position);
            yield return new WaitForSeconds(zone.Duration * 0.5f);
            Assert.IsTrue(zone.IsExtracting);

            player.Motor.Teleport(spawn);
            yield return null;
            yield return null;
            Assert.IsTrue(interrupted);
            Assert.AreEqual(ReturnInterruptReason.LeftZone, zone.LastInterruptReason);
            Assert.AreEqual(0f, zone.Elapsed);
            Assert.IsFalse(session.HasEnded);
        }

        [UnityTest]
        public IEnumerator BeforeLeavingHome_CannotSettle()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            var zone = session.ExtractionZone;
            DisableEnemy();
            DisableDarkness(player);

            player.Motor.Teleport(zone.transform.position);
            yield return new WaitForSeconds(zone.Duration + 1f);
            Assert.IsFalse(zone.IsArmed);
            Assert.IsFalse(zone.IsExtracting);
            Assert.IsFalse(session.HasEnded, "出生后未离开安全屋不能结算");
        }

        [UnityTest]
        public IEnumerator Damage_InterruptsReturn()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            var zone = session.ExtractionZone;
            DisableEnemy();
            DisableDarkness(player);

            yield return LeaveHome(player, zone);
            player.Motor.Teleport(zone.transform.position);
            yield return new WaitForSeconds(zone.Duration * 0.5f);
            Assert.IsTrue(zone.IsExtracting);

            player.Health.TakeDamage(1f);
            Assert.IsFalse(zone.IsExtracting);
            Assert.AreEqual(ReturnInterruptReason.Damaged, zone.LastInterruptReason);
            yield return new WaitForSeconds(zone.Duration * 0.6f);
            Assert.IsFalse(session.HasEnded, "受伤后需重新停留完整时长");
            yield return new WaitForSeconds(zone.Duration * 0.6f);
            Assert.IsTrue(session.HasEnded);
        }

        /// <summary>离开安全屋足够远以启用返程区。</summary>
        private static IEnumerator LeaveHome(PlayerCharacter player, ExtractionZone zone)
        {
            player.Motor.Teleport(zone.transform.position + Vector3.forward * 14f);
            yield return null;
            yield return null;
            Assert.IsTrue(zone.IsArmed, "离家足够远后返程区应启用");
        }

        [UnityTest]
        public IEnumerator Death_LosesInventory_StashUnchanged()
        {
            yield return LoadSlice();
            var session = Object.FindAnyObjectByType<RunSession>();
            var player = session.Player;
            DisableEnemy();
            DisableDarkness(player);

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
            DisableDarkness(player);

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
            Assert.AreEqual(4, player.Inventory.Inventory.UsedSlots, "搜索取消后仍保留玩家自带装备与燃料");
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
        }

        private static void DisableEnemy()
        {
            foreach (var enemy in Object.FindObjectsByType<MeleeEnemy>())
            {
                enemy.enabled = false;
            }
        }

        // 这些用例验证搜刮/撤离/结算，不让黑暗伤害干扰；黑暗规则由 DarknessTests 覆盖。
        private static void DisableDarkness(PlayerCharacter player)
        {
            if (player.Exposure != null)
            {
                player.Exposure.enabled = false;
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
            Assert.IsTrue(player.Interactor.IsLooting);
            Assert.IsTrue(player.Interactor.IsSearching);
            Assert.IsFalse(player.Input.GameplayEnabled, "翻容器时操作关闭");
            var before = player.Inventory.Inventory.ItemCount;
            var deadline = Time.time + 10f;
            while (!container.IsSearched && Time.time < deadline) yield return null;
            yield return new WaitForSeconds(SettleMargin);
            Assert.IsFalse(player.Interactor.IsSearching);
            Assert.IsTrue(container.IsSearched);
            Assert.AreEqual(before, player.Inventory.Inventory.ItemCount, "搜出的物品留在容器里，不会自动进背包");
            player.Interactor.TakeAll();
            player.Interactor.CloseLoot();
            yield return null;
            Assert.IsTrue(player.Input.GameplayEnabled, "关上面板恢复操作");
        }
    }
}
