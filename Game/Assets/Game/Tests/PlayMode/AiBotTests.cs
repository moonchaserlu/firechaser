using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>敌对人机：对玩家敌对、与怪物互相敌对、遵守视野/遮挡/黑暗/弹药规则、不进玩家安全屋、死后留下可搜尸体。</summary>
    public sealed class AiBotTests
    {
        private const string ScenePath = "Assets/Game/Scenes/VerticalSlice.unity";
        // 外围微光岛 B（-6,52，半径 4）内的站位。
        private static readonly Vector3 LitPost = new Vector3(-6f, 0f, 50f);

        [UnityTest]
        public IEnumerator SeesPlayerInFront_ShootsPlayer()
        {
            yield return LoadSlice();
            var bot = IsolateBot(LitPost, Vector3.forward);
            var player = Player();
            player.Motor.Teleport(LitPost + new Vector3(0f, 0f, 8f));

            var timeout = Time.time + 6f;
            while (player.Health.Current >= player.Health.Max && Time.time < timeout) yield return null;
            Assert.AreSame(player.transform, bot.Target, "人机应把玩家当作敌人");
            Assert.Less(player.Health.Current, player.Health.Max, "人机应向玩家开枪并命中");
        }

        [UnityTest]
        public IEnumerator PlayerBehindWall_NotShot()
        {
            yield return LoadSlice();
            // 近郊/外围分隔墙（z=32）隔开人机与玩家。
            var bot = IsolateBot(new Vector3(15f, 0f, 34.5f), Vector3.back);
            bot.SetAnchor(new Vector3(15f, 0f, 34.5f));
            var player = Player();
            player.Motor.Teleport(new Vector3(15f, 0f, 29f));

            yield return new WaitForSeconds(3f);
            Assert.IsNull(bot.Target, "隔墙不应被发现");
            Assert.AreEqual(player.Health.Max, player.Health.Current);
        }

        [UnityTest]
        public IEnumerator Bot_InDarkness_SameWarningAndDamage()
        {
            yield return LoadSlice();
            var bot = IsolateBot(new Vector3(20f, 0f, 12f), Vector3.forward);
            var exposure = bot.GetComponent<LightExposure>();
            var health = bot.GetComponent<Health>();

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(LightExposureState.Warning, exposure.State);
            Assert.AreEqual(health.Max, health.Current, "预警期不扣血");
            yield return new WaitForSeconds(2f);
            Assert.Less(health.Current, health.Max, "人机不免疫黑暗");
        }

        [UnityTest]
        public IEnumerator MonsterAndBot_FightEachOther()
        {
            yield return LoadSlice();
            var bot = IsolateBot(LitPost, Vector3.forward);
            var monster = Object.FindAnyObjectByType<MeleeEnemy>();
            monster.enabled = true;
            var monsterAgent = monster.GetComponent<NavMeshAgent>();
            monsterAgent.isStopped = false;
            monster.SetPatrolRoute(new Transform[0]);
            monsterAgent.Warp(LitPost + new Vector3(0f, 0f, 9f));
            monster.transform.rotation = Quaternion.LookRotation(Vector3.back);

            var timeout = Time.time + 8f;
            while (monster.Health.Current >= monster.Health.Max && Time.time < timeout) yield return null;
            Assert.Less(monster.Health.Current, monster.Health.Max, "人机应向怪物开枪");
            Assert.AreSame(bot.GetComponent<CombatTarget>(), monster.CurrentTarget, "怪物应把人机当作目标");
        }

        [UnityTest]
        public IEnumerator Shooting_ConsumesAmmo()
        {
            yield return LoadSlice();
            var bot = IsolateBot(LitPost, Vector3.forward);
            var total = bot.AmmoInMagazine + bot.ReserveAmmo;
            var shots = 0;
            bot.Fired += _ => shots++;
            var player = Player();
            player.Health.Configure(10000f);
            player.Motor.Teleport(LitPost + new Vector3(0f, 0f, 8f));

            yield return new WaitForSeconds(6f);
            Assert.Greater(shots, 0);
            Assert.AreEqual(total - shots, bot.AmmoInMagazine + bot.ReserveAmmo, "弹药有限，每发都真实消耗");
        }

        [UnityTest]
        public IEnumerator NeverEntersPlayerSafeHouse()
        {
            yield return LoadSlice();
            var zone = Object.FindAnyObjectByType<RunSession>().ExtractionZone;
            var bots = Object.FindObjectsByType<AiBotController>();
            Assert.GreaterOrEqual(bots.Length, 1);
            var start = Time.time;
            while (Time.time - start < 20f)
            {
                foreach (var bot in bots)
                {
                    Assert.IsFalse(zone.Contains(bot.transform.position), "人机不使用玩家的安全屋/撤离点");
                    Assert.LessOrEqual(Vector3.Distance(bot.transform.position, bot.Anchor), 30f, "人机只在自己的地盘活动");
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        [UnityTest]
        public IEnumerator Death_LeavesLootableCorpse()
        {
            yield return LoadSlice();
            var bot = IsolateBot(LitPost, Vector3.forward);
            yield return null;
            bot.GetComponent<Health>().TakeDamage(1000f);
            yield return null;

            Assert.AreEqual(AiBotController.State.Dead, bot.CurrentState);
            Assert.IsFalse(bot.GetComponent<CombatTarget>().IsValid, "怪物不再把死亡的人机当作目标");
            var corpse = bot.GetComponent<LootContainer>();
            Assert.IsNotNull(corpse, "死亡后应留下可搜索的尸体");

            var player = Player();
            var before = player.Inventory.Inventory.ItemCount;
            player.Motor.Teleport(bot.transform.position + Vector3.back * 1.2f);
            yield return null;
            Assert.IsTrue(player.Interactor.TryInteract(), "玩家应能搜索人机尸体");
            var deadline = Time.time + 10f;
            while (!corpse.IsSearched && Time.time < deadline) yield return null;
            player.Interactor.TakeAll();
            Assert.Greater(player.Inventory.Inventory.ItemCount, before, "尸体里有枪和弹药");
        }

        /// <summary>只保留一个人机并放到指定位置（其余人机与所有怪物停用），地盘中心设为该位置。</summary>
        private static AiBotController IsolateBot(Vector3 position, Vector3 facing)
        {
            AiBotController chosen = null;
            foreach (var bot in Object.FindObjectsByType<AiBotController>())
            {
                if (chosen == null) chosen = bot;
                else bot.gameObject.SetActive(false);
            }

            Assert.IsNotNull(chosen, "场景中应有敌对人机");
            chosen.GetComponent<NavMeshAgent>().Warp(position);
            chosen.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            chosen.SetAnchor(position);
            return chosen;
        }

        private static PlayerCharacter Player()
        {
            return Object.FindAnyObjectByType<RunSession>().Player;
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

            var player = Player();
            if (player.Exposure != null) player.Exposure.enabled = false;
            foreach (var monster in Object.FindObjectsByType<MeleeEnemy>())
            {
                monster.enabled = false;
                monster.GetComponent<NavMeshAgent>().isStopped = true;
            }
        }
    }
}
