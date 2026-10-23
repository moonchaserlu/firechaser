using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 阶段 4 最小可玩循环（单机）：搜刮 → 回安全屋回收 → 购买医疗、背包、电池 → 修发电机、充电、
    /// 带电池回安全屋 → 救援激活撤离点 → 登机撤离；以及权威层对非法请求的拒绝。
    /// </summary>
    public sealed class Map01LoopTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const int Seed = 20261023;

        private Map01Authority authority;
        private PlayerCharacter player;
        private int sequence = 100;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SeedOverride = Seed;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            authority = Object.FindAnyObjectByType<Map01Authority>();
            player = authority.Player;
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SeedOverride = null;
            Map01Authority.SuppressMonsters = false;
            Time.timeScale = 1f;
        }

        [Test]
        public void Spawns_LootAndBothMonsterTypes_FromSeed()
        {
            Assert.Greater(authority.Containers.Count, 5, "按种子生成物资容器");
            Assert.IsTrue(authority.Content.Monsters.Any(m => m.Behavior == MonsterBehavior.Patrol), "至少一个巡逻怪");
            Assert.IsTrue(authority.Content.Monsters.Any(m => m.Behavior == MonsterBehavior.RoomGuard), "至少一个房间驻守怪");
            Assert.AreEqual(authority.Content.Monsters.Count, authority.Monsters.Count);
            var patroller = authority.Monsters.First(m => m.name.StartsWith("Patrol"));
            Assert.IsTrue(patroller.HasPatrolRoute, "巡逻怪有巡逻路线");
            var guard = authority.Monsters.First(m => m.name.StartsWith("RoomGuard"));
            Assert.IsFalse(guard.HasPatrolRoute, "驻守怪原地待命");
            Assert.AreEqual(ExtractionState.Candidate, authority.Extraction.State, "开局撤离不可用");
        }

        [UnityTest]
        public IEnumerator FullLoop_ScavengeSellBuyPowerAndExtract()
        {
            DisableMonsters();
            var bag = player.Inventory.Inventory;

            // 1. 搜刮：走到一个物资点完成搜索，带回回收物。
            var container = authority.Containers.First(c => c.Remaining.Count == 0);
            player.Motor.Teleport(container.transform.position + Vector3.right * 1f);
            yield return null;
            Assert.IsTrue(player.Interactor.TryInteract());
            yield return new WaitForSeconds(container.SearchDuration + 0.3f);
            Assert.IsTrue(container.IsSearched);
            // 保证有足够价值用于购买（等同于多跑几趟搜刮）。
            var scrap = authority.Tables.loot.Select(e => e.item).First(authority.IsRecyclable);
            for (var i = 0; i < 40 && scrap != null; i++) bag.TryAdd(scrap);
            Assert.Greater(bag.Items.Count(authority.IsRecyclable), 0, "背包里应有回收物");

            // 2. 回安全屋回收换价值。
            yield return GoTo(Map01Station.RecycleTerminal);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.RecycleTerminal, InteractionAction.Sell));
            Assert.Greater(authority.Ledger.Credits, 0);
            Assert.AreEqual(0, bag.Items.Count(authority.IsRecyclable), "回收物全部换成价值");

            // 3. 商店：医疗、背包、空电池各一次。
            authority.Ledger.AddCredits(200);
            yield return GoTo(Map01Station.ShopTerminal);
            var capacity = bag.Capacity;
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.ShopTerminal, InteractionAction.Buy, 0));
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.ShopTerminal, InteractionAction.Buy, 1));
            Assert.AreEqual(capacity + 4, bag.Capacity, "背包升级生效");
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.ShopTerminal, InteractionAction.Buy, 2));
            Assert.AreEqual(1, bag.CountOf(authority.EmptyBattery));

            // 4. 厂房：修发电机、产电、充电。
            bag.TryAdd(authority.RepairPart);
            yield return GoTo(Map01Station.Generator);
            yield return null;
            Assert.AreNotEqual(GeneratorState.Unknown, authority.Generator.State, "靠近即发现发电机");
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Generator, InteractionAction.RepairGenerator));
            Time.timeScale = 20f;
            yield return new WaitUntil(() => authority.Generator.StoredCharge >= 2);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Generator, InteractionAction.ChargeBattery));
            yield return new WaitUntil(() => bag.CountOf(authority.ChargedBattery) == 1);
            Time.timeScale = 1f;

            // 5. 带电池回安全屋供电；第二块电池同理（直接给，等同第二趟）。
            yield return GoTo(Map01Station.BatterySlot);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.BatterySlot, InteractionAction.DepositBattery));
            Assert.AreEqual(1, authority.Ledger.Power);
            Assert.AreEqual(ExtractionState.Candidate, authority.Extraction.State, "电量不足时救援未呼叫");
            bag.TryAdd(authority.ChargedBattery);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.BatterySlot, InteractionAction.DepositBattery));
            Assert.AreEqual(ExtractionState.Active, authority.Extraction.State, "电量够了，撤离点激活");

            // 6. 前往本局撤离点登机。
            var point = Object.FindObjectsByType<MapSpawnPoint>(FindObjectsSortMode.None).First(p => p.PointId == authority.Extraction.ChosenPointId);
            player.Motor.Teleport(point.transform.position + Vector3.up * 0.2f);
            Time.timeScale = 4f;
            yield return new WaitUntil(() => authority.Outcome != Map01RunOutcome.InProgress);
            Time.timeScale = 1f;
            Assert.AreEqual(Map01RunOutcome.Rescued, authority.Outcome);
        }

        [UnityTest]
        public IEnumerator Authority_RejectsInvalidRequests()
        {
            DisableMonsters();
            var shop = authority.FindStation(Map01Station.ShopTerminal);

            // 超距离：玩家在别处发起请求。
            player.Motor.Teleport(shop.transform.position + new Vector3(30f, 0.1f, 0f));
            yield return null;
            Assert.AreEqual(InteractionResult.OutOfRange, Send(Map01Station.ShopTerminal, InteractionAction.Buy, 0));

            yield return GoTo(Map01Station.ShopTerminal);
            // 伪造实体 ID。
            Assert.AreEqual(InteractionResult.UnknownEntity, authority.Request(new InteractionRequest("map01_fake_station", InteractionAction.Buy, ++sequence)));
            // 价值不足。
            Assert.AreEqual(InteractionResult.NotEnoughCredits, Send(Map01Station.ShopTerminal, InteractionAction.Buy, 0));
            // 重复序号：同一请求只结算一次。
            authority.Ledger.AddCredits(25);
            var request = new InteractionRequest(shop.EntityId, InteractionAction.Buy, ++sequence, 2);
            Assert.AreEqual(InteractionResult.Accepted, authority.Request(request));
            Assert.AreEqual(InteractionResult.DuplicateSequence, authority.Request(request));
            Assert.AreEqual(5, authority.Ledger.Credits, "重复请求不应再次扣费");
            // 背包超量。
            var bag = player.Inventory.Inventory;
            while (bag.FreeSlots >= 2) bag.TryAdd(authority.EmptyBattery);
            authority.Ledger.AddCredits(100);
            Assert.AreEqual(InteractionResult.InventoryFull, Send(Map01Station.ShopTerminal, InteractionAction.Buy, 2));
            // 动作与设施不匹配。
            Assert.AreEqual(InteractionResult.WrongStation, Send(Map01Station.ShopTerminal, InteractionAction.Sell));
        }

        private void DisableMonsters()
        {
            foreach (var monster in authority.Monsters) monster.gameObject.SetActive(false);
        }

        private IEnumerator GoTo(Map01Station station)
        {
            var target = authority.FindStation(station);
            var away = (player.transform.position - target.transform.position);
            away.y = 0f;
            var offset = away.sqrMagnitude > 0.01f ? away.normalized * 1.2f : Vector3.forward * 1.2f;
            player.Motor.Teleport(target.transform.position + offset + Vector3.up * 0.1f);
            yield return null;
        }

        private InteractionResult Send(Map01Station station, InteractionAction action, int argument = 0)
        {
            return authority.Request(new InteractionRequest(authority.FindStation(station).EntityId, action, ++sequence, argument));
        }
    }
}
