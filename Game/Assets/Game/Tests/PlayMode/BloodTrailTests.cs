using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>M3：近战命中触发流血；流血移动留下血迹；未警戒的怪物沿血迹调查并在血迹变冷后放弃；光内不留血迹；不刷怪。</summary>
    public sealed class BloodTrailTests
    {
        private const string SceneName = "VerticalSlice";

        [UnityTest]
        public IEnumerator MeleeHit_StartsBleeding()
        {
            yield return LoadSlice();
            var player = Player();
            var enemy = Object.FindAnyObjectByType<MeleeEnemy>();
            player.Motor.Teleport(enemy.transform.position + Vector3.back * 1.2f);

            var timeout = Time.time + 4f;
            while (!player.Bleeding.IsBleeding && Time.time < timeout) yield return null;
            Assert.IsTrue(player.Bleeding.IsBleeding, "近战命中应触发流血");
            Assert.Less(player.Health.Current, player.Health.Max);
        }

        [UnityTest]
        public IEnumerator BleedingInDark_LeavesTrail_UnalertedEnemyFollowsThenGivesUp()
        {
            yield return LoadSlice();
            var player = Player();
            var enemyCount = Object.FindObjectsByType<MeleeEnemy>().Length;
            var enemy = IsolateEnemy(new Vector3(20f, 0f, 44f), Vector3.right);
            enemy.enabled = false;

            // 在怪物背后的外围黑暗中走一段：距怪物 > 视野 9 米，但血迹在 12 米察觉范围内。
            player.Bleeding.Bleed();
            yield return Walk(player, new Vector3(10f, 0f, 38f), new Vector3(10f, 0f, 48f), 2f);
            Assert.GreaterOrEqual(BloodTrailRegistry.Scene.Count, 2, "流血移动应留下血迹");

            // 玩家回到安全屋，怪物开始嗅探。
            player.Motor.Teleport(new Vector3(0f, 0f, -3f));
            enemy.enabled = true;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(MeleeEnemy.State.Investigating, enemy.CurrentState);
            Assert.IsTrue(enemy.IsFollowingTrail, "怪物应沿血迹调查");

            var start = enemy.transform.position;
            var timeout = Time.time + 20f;
            while (enemy.CurrentState == MeleeEnemy.State.Investigating && Time.time < timeout) yield return null;
            Assert.IsFalse(enemy.IsFollowingTrail, "血迹变冷后应放弃调查");
            Assert.That(enemy.CurrentState, Is.EqualTo(MeleeEnemy.State.Patrolling).Or.EqualTo(MeleeEnemy.State.Idle), "放弃后回到巡逻");
            Assert.Greater(Vector3.Distance(start, enemy.transform.position), 2f, "怪物应实际沿血迹移动");
            Assert.AreEqual(enemyCount, Object.FindObjectsByType<MeleeEnemy>().Length, "血迹不生成新怪物");
        }

        [UnityTest]
        public IEnumerator BleedingInLight_LeavesNoTrail()
        {
            yield return LoadSlice();
            var player = Player();
            player.Bleeding.Bleed();
            // 安全屋光（半径 6）内来回走动。
            yield return Walk(player, new Vector3(-3f, 0f, -2f), new Vector3(3f, 0f, -2f), 1.5f);
            yield return Walk(player, new Vector3(3f, 0f, -2f), new Vector3(-3f, 0f, -2f), 1.5f);
            Assert.IsTrue(player.Bleeding.IsBleeding);
            Assert.AreEqual(0, BloodTrailRegistry.Scene.Count);
        }

        private static IEnumerator Walk(PlayerCharacter player, Vector3 from, Vector3 to, float speed)
        {
            player.Motor.Teleport(from);
            var position = from;
            while (Vector3.Distance(position, to) > 0.05f)
            {
                yield return null;
                position = Vector3.MoveTowards(position, to, speed * Time.deltaTime);
                player.Motor.Teleport(position);
            }

            yield return null;
        }

        /// <summary>只保留一只怪物并放到指定位置与朝向（清空巡逻），其余怪物停用。</summary>
        private static MeleeEnemy IsolateEnemy(Vector3 position, Vector3 facing)
        {
            MeleeEnemy chosen = null;
            foreach (var enemy in Object.FindObjectsByType<MeleeEnemy>())
            {
                if (chosen == null) { chosen = enemy; continue; }
                enemy.enabled = false;
                enemy.GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped = true;
            }

            chosen.SetPatrolRoute(new Transform[0]);
            var agent = chosen.GetComponent<UnityEngine.AI.NavMeshAgent>();
            agent.ResetPath();
            agent.Warp(position);
            chosen.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            return chosen;
        }

        private static PlayerCharacter Player()
        {
            var player = Object.FindAnyObjectByType<RunSession>().Player;
            Assert.IsNotNull(player.Bleeding, "玩家预制体应挂载 BleedingStatus");
            // 本组用例只验证流血与循迹，不让黑暗伤害干扰。
            if (player.Exposure != null) player.Exposure.enabled = false;
            return player;
        }

        private static IEnumerator LoadSlice()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                $"Build Settings 中没有 {SceneName}，请先运行菜单 Wilderness/Build Vertical Slice。");
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
        }
    }
}
