using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 任务链 Q01–Q10：从开场到收尾连续完成；庇护所照明与救援隔离；提前探索、重复提交、失败交互不扣资源；
    /// 保底物资；任务点可达；重开后任务状态一致重置。
    /// </summary>
    public sealed class Map01QuestTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const int Seed = 20261023;

        private Map01Authority authority;
        private Map01QuestDirector director;
        private Map01RunFlow flow;
        private PlayerCharacter player;
        private int sequence = 100;

        private Inventory Bag => player.Inventory.Inventory;
        private ItemDefinition Fuel => player.Placer.Config.fuelItem;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SeedOverride = Seed;
            Map01Authority.SuppressMonsters = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            authority = Object.FindAnyObjectByType<Map01Authority>();
            director = authority.QuestDirector;
            flow = Object.FindAnyObjectByType<Map01RunFlow>();
            player = authority.Player;
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SeedOverride = null;
            Map01Authority.SuppressMonsters = false;
            Time.timeScale = 1f;
        }

        // 设定：安全屋室内任何时候都不因黑暗扣血。开场断电只是视觉上暗（补光与电灯关闭、只有炉火亮），
        // 远离炉火的角落看起来是黑的，但站在那里不扣血。
        [UnityTest]
        public IEnumerator Opening_Q01Active_SafehouseDarkExceptHearth()
        {
            Assert.IsTrue(authority.QuestMode, "Map01 场景默认任务模式");
            Assert.IsNotNull(director);
            Assert.AreEqual("Q01", director.Model.CurrentId);
            Assert.IsFalse(authority.SafehouseLit);
            Assert.IsTrue(LightRegistry.Scene.IsLit(player.transform.position), "出生点在炉火的小范围安全光里");
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            var corner = World(safehouse, new Vector2(6f, -5f));
            var indoor = GameObject.Find("Map01_Root/Buildings_Static/map01_safehouse").GetComponent<IndoorLight>();
            var hearth = GameObject.Find("Hearth_Safehouse").GetComponent<SafeLightSource>();
            Assert.IsFalse(indoor.Powered, "开场断电");
            Assert.IsFalse(indoor.Fills.Any(l => l != null && l.enabled), "断电时室内补光关闭");
            Assert.IsFalse(hearth.Contains(corner), "远离炉火的角落在炉火光圈外，视觉上是黑的");
            Assert.IsFalse(Fixtures().Any(l => l.enabled), "开场室内电灯关闭");
            Assert.IsTrue(LightRegistry.Scene.IsLit(corner), "断电时安全屋室内仍算安全");
            Assert.IsFalse(string.IsNullOrEmpty(director.CurrentStep()), "有当前一步目标");

            player.Motor.Teleport(Ground(corner) + Vector3.up * 0.1f);
            var before = player.Health.Current;
            yield return new WaitForSeconds(3.5f);
            Assert.IsTrue(indoor.Contains(player.transform.position), "玩家站在安全屋室内");
            Assert.IsFalse(hearth.Contains(player.transform.position), "玩家在炉火光圈外");
            Assert.AreEqual(before, player.Health.Current, "开场断电时站在安全屋角落不扣血");
        }

        [UnityTest]
        public IEnumerator FullChain_Q01ToQ10()
        {
            player.Exposure.enabled = false;

            // Q01：庇护所旁的保底燃料，带回炉火提交，只扣一次。
            yield return null;
            yield return Collect("Q01", Fuel);
            Bag.TryAdd(Fuel);
            Assert.AreEqual(2, Bag.CountOf(Fuel));
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_hearth"));
            Assert.AreEqual(1, Bag.CountOf(Fuel), "提交只扣一份燃料");
            Assert.AreEqual("Q02", director.Model.CurrentId);
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_hearth"));
            Assert.AreEqual(1, Bag.CountOf(Fuel), "Q01 完成后炉火不再收燃料");

            // Q02：空电池在庇护所保底，零件在厂房保底；修发电机、充电、回庇护所安装。
            yield return Collect("Q02", authority.EmptyBattery);
            yield return Collect("Q02", authority.RepairPart);
            yield return GoTo(authority.FindStation(Map01Station.Generator));
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Generator, InteractionAction.RepairGenerator));
            Time.timeScale = 20f;
            yield return new WaitUntil(() => authority.Generator.StoredCharge >= 1);
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.Generator, InteractionAction.ChargeBattery));
            yield return new WaitUntil(() => Bag.CountOf(authority.ChargedBattery) == 1);
            Time.timeScale = 1f;
            Assert.AreEqual("Q02", director.Model.CurrentId, "厂房亮灯不等于任务完成，必须回庇护所安装");

            yield return GoTo(authority.FindStation(Map01Station.BatterySlot));
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.BatterySlot, InteractionAction.DepositBattery));
            Assert.IsTrue(authority.SafehouseLit);
            Assert.AreEqual(0, authority.Ledger.Power, "任务模式下第一块电池不计入救援电量");
            Assert.AreEqual(ExtractionState.Candidate, authority.Extraction.State, "不提前呼叫救援");
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            Assert.IsTrue(LightRegistry.Scene.IsLit(World(safehouse, new Vector2(6f, -5f))), "照明恢复后整个室内算有光");
            Assert.IsTrue(Fixtures().All(l => l.enabled), "室内电灯点亮");
            Assert.AreEqual("Q03", director.Model.CurrentId);

            // Q03：厂房保底医疗用品；在炉火清点，不收走。
            yield return Collect("Q03", null);
            var medical = Bag.Stacks.First(s => (s.Item.tags & ItemTags.Medical) != 0).Item;
            var before = Bag.CountOf(medical);
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_hearth"));
            Assert.AreEqual(before, Bag.CountOf(medical), "只确认，不吞药品");

            // Q04、Q05、Q07、Q09：阅读记录时暂停。
            yield return ReadRecord("map01_safehouse_log", "Q05");
            yield return ReadRecord("map01_forest_note", "Q06");

            // Q06：过桥抵达对岸区域，不要求击杀。
            player.Motor.Teleport(director.Chain.bridgeArrival.center + Vector3.up * 0.2f);
            yield return null;
            yield return null;
            Assert.AreEqual("Q07", director.Model.CurrentId);

            yield return ReadRecord("map01_house_note", "Q08");

            // Q08：桥头保底燃料，在指定区域建火（沿用原有建火与燃尽规则）。
            Bag.Remove(Fuel, Bag.CountOf(Fuel));
            yield return Collect("Q08", Fuel);
            yield return BuildFireAt(director.Chain.campfireSite.center);
            Assert.AreEqual("Q09", director.Model.CurrentId);
            var fire = Campfire.Burning.First();
            Assert.IsTrue(fire.IsBurning);
            Assert.Greater(fire.Model.Remaining, 0f, "火堆照常计时燃尽");

            yield return ReadRecord("map01_warehouse_manifest", "Q10");

            // Q10：回庇护所对照旧地图，故事阶段收尾，本局不结束。
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_old_map"));
            Assert.IsTrue(flow.IsReading);
            flow.CloseReading();
            yield return null;
            Assert.IsTrue(director.Model.IsFinished);
            Assert.AreEqual(10, director.Model.CompletedCount);
            Assert.AreEqual(Map01RunOutcome.InProgress, authority.Outcome);
        }

        [UnityTest]
        public IEnumerator FailedInteractions_DoNotConsume_AndEarlyDiscoveriesAreRecognized()
        {
            player.Exposure.enabled = false;
            Bag.Remove(Fuel, Bag.CountOf(Fuel));

            // 没燃料：失败，不推进。
            Assert.AreEqual(InteractionResult.MissingItem, Quest("map01_safehouse_hearth"));
            Assert.AreEqual("Q01", director.Model.CurrentId);

            // 提前阅读 Q04、Q05 的记录，提前过桥，并在桥头先建好火。
            yield return ReadRecord("map01_safehouse_log", "Q01");
            yield return ReadRecord("map01_forest_note", "Q01");
            player.Motor.Teleport(director.Chain.bridgeArrival.center + Vector3.up * 0.2f);
            yield return null;
            yield return null;
            Bag.TryAdd(Fuel);
            yield return BuildFireAt(director.Chain.campfireSite.center);
            Assert.AreEqual("Q01", director.Model.CurrentId, "提前完成后面的条件不会跳过当前任务");

            // Q01 提交；Q02 用已经修好的发电机与已充好的电池（提前持有）；Q03 提前持有药品。
            Bag.TryAdd(Fuel);
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_hearth"));
            Bag.TryAdd(authority.ChargedBattery);
            yield return GoTo(authority.FindStation(Map01Station.BatterySlot));
            Assert.AreEqual(InteractionResult.Accepted, Send(Map01Station.BatterySlot, InteractionAction.DepositBattery));
            Assert.AreEqual(InteractionResult.MissingItem, Send(Map01Station.BatterySlot, InteractionAction.DepositBattery), "没有电池时失败");

            Bag.Remove(authority.EmptyBattery, Bag.CountOf(authority.EmptyBattery));
            var medical = authority.Shop[0].item;
            Bag.TryAdd(medical);
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_hearth"));
            yield return null;

            // Q04、Q05、Q06 立即认可；Q07 未做，停在 Q07；Q08 的火若仍在燃烧也会在开启时认可（这里只检查到 Q07）。
            Assert.AreEqual("Q07", director.Model.CurrentId);
            Assert.AreEqual(6, director.Model.CompletedCount);
            Assert.AreEqual(1, Bag.CountOf(medical), "药品留给玩家");

            yield return ReadRecord("map01_house_note", "Q09");
            Assert.AreEqual(QuestStatus.Completed, director.Model.StatusOf("Q08"), "开启时区域内已有燃烧中的火堆，直接认可");

            // 重复阅读同一记录不重复推进。
            var completed = director.Model.CompletedCount;
            yield return ReadRecord("map01_house_note", "Q09");
            Assert.AreEqual(completed, director.Model.CompletedCount);
        }

        [UnityTest]
        public IEnumerator Supplies_AreGuaranteed_AndRespawnOnlyWhenNeeded()
        {
            yield return null;
            var fuelSupply = director.SpawnedSupplies.First(p => p.Key.questId == "Q01");
            Assert.IsNotNull(fuelSupply.Value, "Q01 开启即有保底燃料");
            Assert.IsFalse(director.SpawnedSupplies.Any(p => p.Key.questId == "Q02"), "后续任务的保底物资在任务开启时才出现");

            yield return Collect("Q01", Fuel);
            Bag.Remove(Fuel, Bag.CountOf(Fuel));
            yield return new WaitForSeconds(0.7f);
            var refreshed = director.SpawnedSupplies.First(p => p.Key.questId == "Q01").Value;
            Assert.IsNotNull(refreshed);
            Assert.IsFalse(refreshed.IsEmpty, "燃料丢了会补一份，不会卡任务");
        }

        [Test]
        public void QuestPoints_AreOnNavMesh_AndReachableFromSafehouse()
        {
            var spawn = player.transform.position;
            var chain = director.Chain;
            var points = chain.records.Select(r => (r.recordId, r.position))
                .Concat(chain.supplies.Select(s => ($"supply_{s.questId}_{s.label}", s.position)))
                .Append(("hearth", chain.hearthPosition))
                .Append(("old_map", chain.oldMapPosition))
                .Append(("bridge_arrival", chain.bridgeArrival.center))
                .Append(("campfire_site", chain.campfireSite.center));
            foreach (var (name, position) in points)
            {
                Assert.IsTrue(NavMesh.SamplePosition(position, out var hit, 2f, NavMesh.AllAreas), $"{name} 不在可行走地面附近");
                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(spawn, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete,
                    $"{name} 从庇护所不可达");
            }
        }

        [UnityTest]
        public IEnumerator CampfireSite_AcceptsPlacement()
        {
            Bag.TryAdd(Fuel);
            var site = director.Chain.campfireSite.center;
            player.Motor.Teleport(site + new Vector3(1.5f, 0.2f, 0f));
            yield return null;
            Assert.AreEqual(CampfirePlacementResult.Valid, player.Placer.Evaluate(Ground(site)), "Q08 指定区域中心可以建火");
        }

        [UnityTest]
        public IEnumerator Restart_ResetsQuestState()
        {
            player.Exposure.enabled = false;
            Bag.TryAdd(Fuel);
            Assert.AreEqual(InteractionResult.Accepted, Quest("map01_safehouse_hearth"));
            Assert.AreEqual("Q02", director.Model.CurrentId);

            player.Health.TakeDamage(10000f);
            yield return null;
            flow.Restart();
            yield return null;
            yield return null;
            var next = Object.FindAnyObjectByType<Map01Authority>();
            Assert.AreNotSame(authority, next);
            Assert.AreEqual("Q01", next.QuestDirector.Model.CurrentId, "重开后从 Q01 重新开始");
            Assert.IsFalse(next.SafehouseLit);
            Assert.AreEqual(0, next.QuestDirector.Model.CompletedCount);
        }

        // ---------- 工具 ----------

        private IEnumerator Collect(string questId, ItemDefinition item)
        {
            yield return new WaitUntil(() => director.SpawnedSupplies.Any(p => p.Key.questId == questId && (item == null || p.Key.item == item) && p.Value != null && !p.Value.IsEmpty));
            var container = director.SpawnedSupplies.First(p => p.Key.questId == questId && (item == null || p.Key.item == item) && p.Value != null && !p.Value.IsEmpty).Value;
            player.Motor.Teleport(container.transform.position + Vector3.right * 1f + Vector3.up * 0.1f);
            yield return null;
            Assert.IsTrue(player.Interactor.TryInteract(), $"{questId} 的保底物资可以搜索");
            yield return new WaitUntil(() => container.IsEmpty);
        }

        private IEnumerator ReadRecord(string recordId, string expectedAfter)
        {
            Assert.AreEqual(InteractionResult.Accepted, Quest(recordId));
            Assert.IsTrue(flow.IsReading, "阅读时打开记录面板");
            Assert.AreEqual(0f, Time.timeScale, "阅读时暂停，不受黑暗伤害");
            Assert.IsFalse(player.Input.GameplayEnabled);
            flow.CloseReading();
            yield return null;
            yield return null;
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(player.Input.GameplayEnabled, "合上后恢复操作");
            Assert.AreEqual(expectedAfter, director.Model.CurrentId);
        }

        private IEnumerator BuildFireAt(Vector3 site)
        {
            site = Ground(site);
            player.Motor.Teleport(site + new Vector3(1.5f, 0.2f, 0f));
            yield return null;
            Assert.IsTrue(player.Placer.BeginPreview(), "有燃料可以建火");
            Assert.IsTrue(player.Placer.TryConfirm(site), $"指定区域可以建火：{player.Placer.SiteResult}");
            yield return new WaitUntil(() => player.Placer.State == CampfirePlacerState.Idle);
            yield return null;
        }

        private InteractionResult Quest(string entityId)
        {
            var station = Map01Interactable.Find(entityId);
            Assert.IsNotNull(station, $"缺少任务交互点 {entityId}");
            player.Motor.Teleport(station.transform.position + Vector3.right * 0.8f + Vector3.up * 0.1f);
            return authority.Request(new InteractionRequest(entityId, InteractionAction.Quest, ++sequence));
        }

        private IEnumerator GoTo(Map01Interactable station)
        {
            player.Motor.Teleport(station.transform.position + Vector3.right * 1.2f + Vector3.up * 0.1f);
            yield return null;
        }

        private InteractionResult Send(Map01Station station, InteractionAction action)
        {
            return authority.Request(new InteractionRequest(authority.FindStation(station).EntityId, action, ++sequence));
        }

        private Light[] Fixtures() => Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l.name == "Safehouse_CeilingLight").ToArray();

        private static Vector3 Ground(Vector3 position)
        {
            return NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas) ? hit.position : position;
        }

        private static Vector3 World(BuildingSpec building, Vector2 local)
        {
            return new Vector3(building.Center.x + local.x, 0.5f, building.Center.y + local.y);
        }
    }
}
