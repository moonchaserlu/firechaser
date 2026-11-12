using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WildernessExtraction.Map01;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.Tests
{
    /// <summary>
    /// 开局教学关：开局状态、逐段推进与点灯开路、黑暗生命底线、倒下复活、走完与跳过后存档改记为 Map01。
    /// </summary>
    public sealed class TutorialFlowTests
    {
        private TutorialDirector director;
        private Map01Authority authority;
        private PlayerCharacter player;
        private string saveDirectory;

        private Inventory Bag => player.Inventory.Inventory;

        private IEnumerator Load()
        {
            TutorialDirector.SuppressSceneLoad = true;
            saveDirectory = Path.Combine(Application.temporaryCachePath, "tutorial_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveDirectory);
            SaveSlots.DirectoryOverride = saveDirectory;
            SaveSlots.CreateNew(1, GameScenes.Tutorial, DateTime.UtcNow);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(GameScenes.Tutorial, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            yield return null;
            director = Object.FindAnyObjectByType<TutorialDirector>();
            authority = Object.FindAnyObjectByType<Map01Authority>();
            player = authority.Player;
        }

        [TearDown]
        public void Restore()
        {
            TutorialDirector.SuppressSceneLoad = false;
            SaveSlots.ClearActive();
            SaveSlots.DirectoryOverride = null;
            Time.timeScale = 1f;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator Opening_OnlyStartLampLit_KnifeOnly_GatesClosed()
        {
            yield return Load();
            Assert.IsNotNull(director, "场景里有教学导演");
            Assert.AreEqual("move", director.Model.CurrentId);
            Assert.AreEqual(9, director.Model.Count);
            Assert.AreEqual(1, Bag.Stacks.Count(), "开局只有一把短刀");
            Assert.AreEqual("short_knife", Bag.Stacks.First().Item.itemId);
            Assert.AreEqual(1, player.Shooter.ActiveSlot, "手枪槽是空的，先拿着短刀");
            Assert.IsTrue(LightRegistry.Scene.IsLit(player.transform.position), "出生在亮着的灯下");

            var lit = Beacons().Where(b => b.IsLit).ToArray();
            Assert.AreEqual(1, lit.Length, "开局只有出生点那盏灯亮");
            Assert.IsTrue(Gate("Gate_1_Move").activeSelf);
            Assert.IsFalse(Object.FindObjectsByType<LootContainer>().Any(c => c.name == "Crate_SeekerRelic"), "手枪遗物在近战段之后才出现");

            foreach (var point in new[] { "Zone_Lamp2", "Zone_FarSide", "Zone_LastRoom", "Zone_Stairs", "Crate_Explosive", "Crate_Cloth", "Crate_Chemicals", "Lamp_6_Corner" })
            {
                var go = GameObject.Find(point);
                Assert.IsNotNull(go, point);
                Assert.IsTrue(NavMesh.SamplePosition(go.transform.position, out _, 1.5f, NavMesh.AllAreas), $"{point} 在导航面上");
            }
        }

        [UnityTest]
        public IEnumerator Walkthrough_EachStepLightsTheNextLamp_ThenBlastsIntoShelter()
        {
            yield return Load();
            player.Exposure.enabled = false;
            foreach (var enemy in Object.FindObjectsByType<TutorialEnemy>().Where(e => !e.Pinned))
            {
                enemy.gameObject.SetActive(false);
            }

            // 1 余烬
            director.Report(TutorialFacts.Moved);
            director.Report(TutorialFacts.Sprinted);
            Assert.AreEqual("light", director.Model.CurrentId);
            Assert.IsFalse(Gate("Gate_1_Move").activeSelf, "第一道阻隔移开");
            Assert.IsTrue(Beacon("Lamp_1").IsLit);

            // 2 灯与暗：走到还没点的第二盏灯，它被点亮
            Assert.IsFalse(Beacon("Lamp_2").IsLit);
            yield return Teleport(new Vector3(28f, 0f, 0f));
            Assert.AreEqual("loot", director.Model.CurrentId);
            Assert.IsTrue(Beacon("Lamp_2").IsLit);

            // 3 翻货箱：真实搜索两只箱子 + 打开背包
            yield return Search("Crate_Cloth");
            yield return Search("Crate_Chemicals");
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            flow.OpenBackpack();
            yield return null;
            flow.CloseBackpack();
            yield return null;
            Assert.AreEqual("craft", director.Model.CurrentId);
            Assert.IsFalse(Gate("Gate_3_Loot").activeSelf);

            // 4 合成燃料包（背包合成页，走权威层）
            var interactor = player.GetComponent<Map01Interactor>();
            Assert.AreEqual(1, authority.Recipes.Count, "教学只教燃料包一条配方");
            Assert.AreEqual(InteractionResult.Accepted, interactor.SendFieldCraft(0));
            yield return null;
            Assert.AreEqual("fire", director.Model.CurrentId);
            Assert.IsFalse(Gate("Gate_4_Craft").activeSelf);

            // 5 长暗段：真实建火，再走到对面
            yield return Teleport(new Vector3(44f, 0f, 0f));
            Assert.IsTrue(player.Placer.BeginPreview(), "有燃料可以进入建火预览");
            Assert.IsTrue(player.Placer.TryConfirm(new Vector3(46f, 0f, 0f)), "暗处边缘可以建火");
            var deadline = Time.time + 4f;
            while (Campfire.Burning.Count == 0 && Time.time < deadline) yield return null;
            Assert.AreEqual(1, Campfire.Burning.Count, "火堆点燃");
            Assert.AreEqual("fire", director.Model.CurrentId, "还要走到对面那盏灯");
            Assert.IsTrue(Beacon("Lamp_4_Middle").IsLit, "火堆点起来时引燃长暗段中间那盏灯");
            for (var x = 40f; x <= 64f; x += 0.5f)
            {
                Assert.IsTrue(LightRegistry.Scene.IsLit(new Vector3(x, 0f, 0f)), $"建火后长暗段全程有光（x = {x}）");
            }

            yield return Teleport(new Vector3(66f, 0f, 0f));
            Assert.AreEqual("melee", director.Model.CurrentId);
            Assert.IsTrue(Beacon("Lamp_5_FarSide").IsLit);

            // 6 卡住的失火者
            var pinned = Object.FindObjectsByType<TutorialEnemy>().Single(e => e.Pinned);
            director.Report(TutorialFacts.Dodged);
            pinned.Health.TakeDamage(9999f, player.gameObject);
            yield return null;
            Assert.AreEqual("shoot", director.Model.CurrentId);
            Assert.IsFalse(Gate("Gate_6_Melee").activeSelf);

            // 7 射击场：搜遗物拿手枪，打碎三个油罐，换一次弹
            yield return Search("Crate_SeekerRelic");
            Assert.AreEqual(1, Bag.CountOf(Item("old_pistol")), "拿到手枪");
            Assert.AreEqual(8, Bag.CountOf(Item("ammo_9mm")), "和一匣备弹");
            foreach (var target in Object.FindObjectsByType<TutorialTarget>())
            {
                target.Health.TakeDamage(5f, player.gameObject);
            }

            Assert.IsTrue(director.Model.HasFact(TutorialFacts.TargetsCleared));
            Assert.AreEqual("shoot", director.Model.CurrentId, "还没换过弹");
            director.Report(TutorialFacts.Reloaded);
            Assert.AreEqual("home", director.Model.CurrentId);
            Assert.IsTrue(Beacon("Lamp_9_LastRoom").IsLit, "尽头那间屋子的灯亮起");
            Assert.IsFalse(Gate("Gate_7_Shoot").activeSelf);

            // 8 走进尽头的屋子：身后落石，来路堵死
            var rockfall = Object.FindAnyObjectByType<TutorialRockfall>();
            Assert.IsNotNull(rockfall);
            Assert.IsFalse(rockfall.HasFallen, "进屋之前来路还通着");
            yield return Teleport(TutorialBuilderPositions.ExitDoor);
            Assert.IsTrue(rockfall.HasFallen, "进屋后身后落石");
            Assert.IsTrue(rockfall.GetComponent<Collider>().enabled, "落石挡住来路");
            Assert.AreEqual("blast", director.Model.CurrentId);

            // 9 炸开外墙：搜炸药、到墙边安放、退开、炸开、走下台阶
            var wall = Object.FindAnyObjectByType<TutorialBreachWall>();
            Assert.IsNotNull(wall);
            Assert.IsFalse(wall.CanArm(player), "没有炸药不能安放");
            yield return Search("Crate_Explosive");
            yield return null;
            Assert.IsTrue(director.Model.HasFact(TutorialFacts.ExplosiveTaken));
            yield return Teleport(TutorialBuilderPositions.BreachPoint + new Vector3(1.5f, 0f, 0f));
            Assert.IsTrue(wall.TryArm(player), "墙边可以安放炸药");
            Assert.AreEqual(0, Bag.CountOf(wall.Explosive), "炸药用掉了");
            Assert.IsTrue(director.Model.HasFact(TutorialFacts.ChargePlaced));
            yield return Teleport(TutorialBuilderPositions.ExitDoor + new Vector3(3f, 0f, 0f));
            var health = player.Health.Current;
            deadline = Time.time + 5f;
            while (wall.State != TutorialBreachWall.WallState.Blown && Time.time < deadline) yield return null;
            Assert.AreEqual(TutorialBreachWall.WallState.Blown, wall.State, "引信烧完墙被炸开");
            Assert.AreEqual(health, player.Health.Current, 0.01f, "退到爆炸范围外不受伤");
            Assert.IsFalse(GameObject.Find("OuterWall_Cracked (可炸开)") != null, "裂缝墙体消失");
            Assert.IsTrue(Beacon("Lamp_10_Stairs").IsLit, "墙后台阶透出光");
            Assert.AreEqual("blast", director.Model.CurrentId, "还要走下台阶");

            yield return Teleport(TutorialBuilderPositions.BreachPoint + new Vector3(-1.2f, 0f, 0f));
            Assert.IsTrue(director.InCutscene, "走到墙洞口，下台阶过场接管");
            Assert.IsFalse(player.ControlEnabled, "过场中不能操作");
            Assert.IsFalse(director.IsEnding, "过场还没播完");
            deadline = Time.time + 10f;
            while (!director.IsEnding && Time.time < deadline) yield return null;
            Assert.IsTrue(director.IsEnding, "过场播完，教学结束");
            Assert.Less(player.transform.position.x, TutorialBuilderPositions.StairsEntry.x, "角色自己走下了台阶");
            Assert.Less(player.transform.position.y, -1f, "没入台阶下的黑暗");
            Assert.AreEqual(1f, director.FadeAlpha, 0.01f, "黑幕淡入");
            deadline = Time.realtimeSinceStartup + director.Config.endingSeconds + 1f;
            while (director.LoadedScene == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(GameScenes.Shelter, director.LoadedScene, "教学走完进入地下安全屋");
            Assert.AreEqual(GameScenes.Shelter, SaveSlots.Load(1).scenePath, "存档改记为安全屋，继续游戏不再进教学");
            Assert.AreEqual(Map01RunOutcome.InProgress, authority.Outcome, "教学不走结算");
        }

        [UnityTest]
        public IEnumerator Blast_TooClose_HurtsAndKnocksDownInsideTheRoom()
        {
            yield return Load();
            player.Exposure.enabled = false;
            foreach (var step in new[] { TutorialFacts.Moved, TutorialFacts.Sprinted, "reached_lamp_2", "crate_cloth", "crate_chemicals",
                         TutorialFacts.BackpackOpened, TutorialFacts.HasFuel, TutorialFacts.CampfireBuilt, "reached_far_lamp", TutorialFacts.Dodged,
                         "pinned_killed", TutorialFacts.TargetsCleared, TutorialFacts.Reloaded })
            {
                director.Report(step);
            }

            yield return Teleport(TutorialBuilderPositions.ExitDoor);
            var wall = Object.FindAnyObjectByType<TutorialBreachWall>();
            Bag.TryAdd(wall.Explosive);
            yield return Teleport(TutorialBuilderPositions.BreachPoint + new Vector3(1.5f, 0f, 0f));
            player.Health.TakeDamage(70f, player.gameObject);
            Assert.IsTrue(wall.TryArm(player));
            var deadline = Time.time + 5f;
            while (wall.State != TutorialBreachWall.WallState.Blown && Time.time < deadline) yield return null;
            yield return null;
            Assert.AreEqual(1, director.Knockdowns, "站在墙边被炸倒");
            Assert.IsFalse(player.Health.IsDead);
            Assert.Less(Vector3.Distance(Flat(player.transform.position), Flat(TutorialBuilderPositions.ExitDoor + new Vector3(3f, 0f, 0f))), 1f,
                "落石之后只在屋里醒来，不会被送回石头另一边");
        }

        [UnityTest]
        public IEnumerator Darkness_StopsAtHealthFloor_AndHeavyHitKnocksDownToLastLamp()
        {
            yield return Load();
            director.Report(TutorialFacts.Moved);
            director.Report(TutorialFacts.Sprinted);

            // 黑暗只教“疼”：生命不会被黑暗压到底线以下
            player.Health.TakeDamage(60f, player.gameObject);
            yield return Teleport(new Vector3(20f, 0f, 0f));
            Assert.IsFalse(LightRegistry.Scene.IsLit(player.transform.position), "第二段中间是黑的");
            yield return new WaitForSeconds(4.5f);
            Assert.AreNotEqual(LightExposureState.Safe, player.Exposure.State);
            Assert.GreaterOrEqual(player.Health.Current, director.Config.darknessHealthFloor - 0.01f);
            Assert.IsTrue(director.Model.HasFact(TutorialFacts.FeltWarning));

            // 被打到倒下阈值以下：在最近一盏亮着的灯旁满血醒来，不进结算
            player.Health.TakeDamage(player.Health.Current - 5f, player.gameObject);
            yield return null;
            Assert.IsFalse(player.Health.IsDead);
            Assert.AreEqual(player.Health.Max, player.Health.Current, 0.01f);
            Assert.AreEqual(1, director.Knockdowns);
            Assert.Less(Vector3.Distance(Flat(player.transform.position), Flat(Beacon("Lamp_1").RespawnPosition)), 1f, "回到最近点亮的第二盏灯");
            Assert.AreEqual(Map01RunOutcome.InProgress, authority.Outcome);
        }

        [UnityTest]
        public IEnumerator SkipFromPause_GoesToShelter_AndMarksSave()
        {
            yield return Load();
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            flow.SetPaused(true);
            Assert.IsTrue(flow.IsPaused);
            director.Skip();
            yield return null;
            Assert.IsFalse(flow.IsPaused);
            Assert.AreEqual(GameScenes.Shelter, director.LoadedScene, "跳过不放结尾字幕，直接进地下安全屋");
            Assert.AreEqual(GameScenes.Shelter, SaveSlots.Load(1).scenePath);
        }

        // ---------- 工具 ----------

        private IEnumerator Teleport(Vector3 position)
        {
            player.Motor.Teleport(position);
            yield return null;
            yield return null;
        }

        private IEnumerator Search(string crateName)
        {
            var crate = Object.FindObjectsByType<LootContainer>().Single(c => c.name == crateName);
            yield return Teleport(crate.transform.position + new Vector3(0f, 0f, -1f));
            Assert.IsTrue(player.Interactor.TryInteract(), $"可以搜索 {crateName}");
            var deadline = Time.time + 4f;
            while (!crate.IsEmpty && Time.time < deadline)
            {
                if (!player.Interactor.IsSearching && crate.IsSearched) player.Interactor.TryInteract();
                yield return null;
            }

            Assert.IsTrue(crate.IsEmpty, $"{crateName} 已搜空");
        }

        private static TutorialBeacon[] Beacons() => Object.FindObjectsByType<TutorialBeacon>();
        private static TutorialBeacon Beacon(string name) => Beacons().Single(b => b.name == name);

        private static GameObject Gate(string name)
        {
            return Resources.FindObjectsOfTypeAll<Transform>().Select(t => t.gameObject)
                .Single(g => g.name == name && g.scene.IsValid());
        }

        private ItemDefinition Item(string id) => Bag.Stacks.First(s => s.Item.itemId == id).Item;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    /// <summary>与 TutorialBuilder 中的坐标一致（运行时测试程序集不能引用编辑器程序集）。</summary>
    internal static class TutorialBuilderPositions
    {
        public static readonly Vector3 ExitDoor = new Vector3(1f, 0f, -24f);
        public static readonly Vector3 BreachPoint = new Vector3(-4f, 0f, -24f);
        public static readonly Vector3 StairsEntry = new Vector3(-8.5f, 0f, -24f);
    }
}
