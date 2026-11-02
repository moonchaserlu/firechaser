using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WildernessExtraction.Map01;

namespace WildernessExtraction.Shelter.Tests
{
    /// <summary>
    /// 地下安全屋第一阶段：新档初始物资、仓库与背包搬运并存档；出发去 Map01 带上物资、
    /// 经前哨返回带回物资；死亡丢失带出去的物资；连续两局后仓库正确，重新读档一致。
    /// </summary>
    public sealed class ShelterFlowTests
    {
        private string savePath;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"firechaser_shelter_test_{System.Guid.NewGuid():N}.json");
            PlayerProfile.PathOverride = savePath;
            RaidTransfer.Reset();
            Map01Authority.SuppressMonsters = true;
            yield return LoadScene(GameScenes.Shelter);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerProfile.PathOverride = null;
            Map01Authority.SuppressMonsters = false;
            RaidTransfer.Reset();
            Time.timeScale = 1f;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        [UnityTest]
        public IEnumerator NewProfile_StarterKitInStash_MovesPersist()
        {
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            Assert.IsNotNull(shelter);
            yield return null;
            Assert.Greater(shelter.Profile.Stash.ItemCount, 0, "新档仓库里有初始物资");
            Assert.AreEqual(0, shelter.Bag.ItemCount, "安全屋里背包从空开始");
            Assert.IsTrue(LightRegistry.Scene.IsLit(shelter.Player.transform.position), "出生在亮着的大厅里");

            var pistol = shelter.Profile.Stash.Items.First(i => i.itemId == "old_pistol");
            Assert.AreEqual(ShelterResult.Done, shelter.MoveToBackpack(pistol));
            Assert.AreEqual(1, shelter.Bag.CountOf(pistol));
            Assert.AreEqual(0, shelter.Profile.Stash.CountOf(pistol));

            var reloaded = PlayerProfile.LoadOrCreate(Database());
            Assert.AreEqual(0, reloaded.Stash.CountOf(pistol), "每次搬动都立即存档");
        }

        [UnityTest]
        public IEnumerator TwoRaids_ReturnBringsLoot_DeathLosesLoadout()
        {
            // ---- 第一趟：带手枪和燃料出发，经前哨返回，带回捡到的废金属 ----
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            var pistol = shelter.Profile.Stash.Items.First(i => i.itemId == "old_pistol");
            var fuel = shelter.Profile.Stash.Items.First(i => i.itemId == "fuel_pack");
            shelter.MoveToBackpack(pistol);
            shelter.MoveToBackpack(fuel);
            shelter.Depart();
            yield return WaitFor<Map01Authority>();

            var authority = Object.FindAnyObjectByType<Map01Authority>();
            yield return null;
            var bag = authority.Player.Inventory.Inventory;
            Assert.AreEqual(1, bag.CountOf(pistol), "带上了出发时选的手枪");
            Assert.AreEqual(1, bag.CountOf(fuel));
            Assert.AreEqual(2, bag.ItemCount, "背包只有出发时带的东西");
            Assert.IsNull(authority.FindStation(Map01Station.ShopTerminal), "前哨不再有商店");
            Assert.IsNull(authority.FindStation(Map01Station.RecycleTerminal), "前哨不再有回收台");

            var scrap = authority.Tables.loot.Select(e => e.item).First(i => i != null && i.itemId == "scrap_metal");
            bag.TryAdd(scrap);
            bag.TryAdd(scrap);

            var outpost = Object.FindAnyObjectByType<OutpostReturn>();
            Assert.IsNotNull(outpost, "前哨北门外有返程点");
            authority.Player.Motor.Teleport(outpost.transform.position + new Vector3(0f, 0.3f, 40f));
            yield return null;
            yield return null;
            Assert.IsTrue(outpost.IsArmed, "离开前哨足够远后返程点启用");
            authority.Player.Motor.Teleport(outpost.transform.position + new Vector3(0f, 0.3f, 0f));
            yield return new WaitForSeconds(outpost.Duration + 0.5f);
            Assert.AreEqual(Map01RunOutcome.Rescued, authority.Outcome, "在前哨停留满时长即返回");

            Object.FindAnyObjectByType<Map01RunFlow>().Restart();
            yield return WaitFor<ShelterController>();
            shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            Assert.AreEqual(RaidOutcome.Returned, shelter.ArrivalOutcome);
            Assert.AreEqual(4, shelter.ArrivalItems);
            Assert.AreEqual(1, shelter.Profile.Stash.CountOf(pistol), "手枪带回了仓库");
            Assert.AreEqual(2, shelter.Profile.Stash.CountOf(scrap), "捡到的废金属进了仓库");
            Assert.AreEqual(1, shelter.Profile.CompletedRaids);

            // ---- 第二趟：带手枪出发，死在外面 ----
            shelter.MoveToBackpack(pistol);
            shelter.Depart();
            yield return WaitFor<Map01Authority>();
            authority = Object.FindAnyObjectByType<Map01Authority>();
            yield return null;
            authority.Player.Health.TakeDamage(100000f);
            yield return null;
            Assert.AreEqual(Map01RunOutcome.Died, authority.Outcome);

            Object.FindAnyObjectByType<Map01RunFlow>().Restart();
            yield return WaitFor<ShelterController>();
            shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            Assert.AreEqual(RaidOutcome.Died, shelter.ArrivalOutcome);
            Assert.AreEqual(0, shelter.Profile.Stash.CountOf(pistol), "死亡丢掉了带出去的手枪");
            Assert.AreEqual(2, shelter.Profile.Stash.CountOf(scrap), "留在仓库的东西还在");
            Assert.AreEqual(2, shelter.Profile.CompletedRaids);

            // ---- 相当于重启游戏：从磁盘读档，内容一致 ----
            var reloaded = PlayerProfile.LoadOrCreate(Database());
            Assert.AreEqual(2, reloaded.Stash.CountOf(scrap));
            Assert.AreEqual(0, reloaded.Stash.CountOf(pistol));
            Assert.AreEqual(2, reloaded.CompletedRaids);
        }

        [UnityTest]
        public IEnumerator FirstVersion_HasOnlyStashWorkbenchExitAndRange()
        {
            yield return null;
            var kinds = ShelterStation.Active.Select(s => s.Kind).OrderBy(k => k).ToArray();
            CollectionAssert.AreEqual(new[] { ShelterStationKind.Stash, ShelterStationKind.Workbench, ShelterStationKind.Exit }, kinds);
            Assert.IsNotNull(Object.FindAnyObjectByType<ShelterRange>(), "有靶场");
        }

        [UnityTest]
        public IEnumerator Range_OnlyPlaceToShoot_TargetsCountHitsAndRecover()
        {
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            var range = Object.FindAnyObjectByType<ShelterRange>();
            yield return null;
            Assert.IsFalse(shelter.Player.Shooter.enabled, "靶场外不能开枪");

            shelter.Player.Motor.Teleport(range.transform.position + new Vector3(6f, 0.3f, 0f));
            yield return null;
            yield return null;
            Assert.IsTrue(range.PlayerInside);
            Assert.IsTrue(shelter.Player.Shooter.enabled, "进入靶场可以开枪");
            Assert.IsTrue(LightRegistry.Scene.IsLit(shelter.Player.transform.position), "靶场有灯");

            var target = range.Targets[0];
            var health = target.GetComponent<Health>();
            health.TakeDamage(25f, shelter.Player.gameObject);
            yield return null;
            Assert.AreEqual(1, target.Hits);
            Assert.AreEqual(25f, target.LastDamage, 0.01f);
            Assert.AreEqual(health.Max, health.Current, 0.01f, "靶子打不坏");

            range.ResetStats();
            Assert.AreEqual(0, range.TotalHits);

            // 练习弹：弹匣会打空，换弹不需要也不消耗背包里的子弹。
            var shooter = shelter.Player.Shooter;
            Assert.IsTrue(shooter.FreeAmmo, "靶场里用练习弹");
            var pistol = shelter.Profile.Stash.Items.First(i => i.itemId == "old_pistol");
            shelter.MoveToBackpack(pistol);
            Assert.AreEqual(0, shelter.Bag.CountOf(shooter.CurrentConfig.ammoItem), "背包里没有子弹");
            var weapon = shooter.Weapon;
            for (var shot = 0; shot < 200 && weapon.AmmoInMagazine > 0; shot++) weapon.TryFire(Time.time + shot);
            Assert.AreEqual(0, weapon.AmmoInMagazine, "弹匣会打空");
            typeof(PlayerShooter).GetMethod("TryReload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(shooter, new object[] { Time.time, false });
            yield return new WaitForSeconds(shooter.CurrentConfig.reloadDuration + 0.3f);
            Assert.AreEqual(weapon.MagazineSize, weapon.AmmoInMagazine, "没有子弹也能换满练习弹");
            Assert.AreEqual(0, shelter.Bag.CountOf(shooter.CurrentConfig.ammoItem), "背包子弹没有被扣");

            shelter.Player.Motor.Teleport(new Vector3(0f, 0.3f, 0f));
            yield return null;
            yield return null;
            Assert.IsFalse(shelter.Player.Shooter.enabled, "离开靶场后不能开枪");
            Assert.IsFalse(shelter.Player.Shooter.FreeAmmo, "离开靶场后不再是练习弹");
        }

        private static ItemDatabase Database()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/Shelter/Data/ItemDatabase.asset");
#else
            return null;
#endif
        }

        private static IEnumerator LoadScene(string path)
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
        }

        private static IEnumerator WaitFor<T>() where T : Object
        {
            for (var frame = 0; frame < 600; frame++)
            {
                yield return null;
                if (Object.FindAnyObjectByType<T>() != null) yield break;
            }

            Assert.Fail($"等待 {typeof(T).Name} 所在场景加载超时");
        }
    }
}
