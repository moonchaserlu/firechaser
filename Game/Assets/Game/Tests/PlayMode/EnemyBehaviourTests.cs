using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>《敌人与 AI Bot 行为策划案》12 节怪物验收：巡逻、背身/隔墙不发现、正面短暂警觉后追击、丢失目标后搜索并返回巡逻。</summary>
    public sealed class EnemyBehaviourTests
    {
        private const string SceneName = "VerticalSlice";

        [UnityTest]
        public IEnumerator Patrols_BetweenPoints()
        {
            yield return LoadSlice();
            var enemy = Enemy();
            Assert.IsTrue(enemy.HasPatrolRoute);
            Assert.AreEqual(MeleeEnemy.State.Patrolling, enemy.CurrentState);

            var startIndex = enemy.PatrolIndex;
            var timeout = Time.time + 15f;
            while (enemy.PatrolIndex == startIndex && Time.time < timeout) yield return null;
            Assert.AreNotEqual(startIndex, enemy.PatrolIndex, "怪物应持续在巡逻点之间移动");
            Assert.AreEqual(MeleeEnemy.State.Patrolling, enemy.CurrentState);
        }

        [UnityTest]
        public IEnumerator PlayerBehind_NotDetected()
        {
            yield return LoadSlice();
            var enemy = Enemy();
            Place(enemy, new Vector3(20f, 0f, 44f), Vector3.forward, clearPatrol: true);
            Player().Motor.Teleport(new Vector3(20f, 0f, 40f));

            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(MeleeEnemy.State.Idle, enemy.CurrentState, "背后且不在近身范围内不应被发现");
        }

        [UnityTest]
        public IEnumerator PlayerBehindWall_NotDetected()
        {
            yield return LoadSlice();
            var enemy = Enemy();
            // 近郊/外围分隔墙（z=32）把两者隔开，距离 5 米、正前方。
            Place(enemy, new Vector3(15f, 0f, 29.5f), Vector3.forward, clearPatrol: true);
            Player().Motor.Teleport(new Vector3(15f, 0f, 34.5f));

            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(MeleeEnemy.State.Idle, enemy.CurrentState, "隔墙不应被视觉锁定");
        }

        [UnityTest]
        public IEnumerator PlayerInFront_BriefAlertThenChase()
        {
            yield return LoadSlice();
            var enemy = Enemy();
            Place(enemy, new Vector3(20f, 0f, 44f), Vector3.forward, clearPatrol: true);
            Player().Motor.Teleport(new Vector3(20f, 0f, 50f));

            yield return new WaitForSeconds(0.2f);
            Assert.AreNotEqual(MeleeEnemy.State.Chasing, enemy.CurrentState, "发现后应先确认并短暂警觉");

            var timeout = Time.time + 3f;
            while (enemy.CurrentState != MeleeEnemy.State.Chasing && enemy.CurrentState != MeleeEnemy.State.Attacking && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(enemy.CurrentState, Is.EqualTo(MeleeEnemy.State.Chasing).Or.EqualTo(MeleeEnemy.State.Attacking));
        }

        [UnityTest]
        public IEnumerator LosingSight_SearchesLastSeen_ThenReturnsToPatrol()
        {
            yield return LoadSlice();
            var enemy = Enemy();
            // 先清空巡逻让怪物保持朝向；开始追击后再给它一条巡逻路线，验证结束后会回去巡逻。
            Place(enemy, new Vector3(20f, 0f, 44f), Vector3.forward, clearPatrol: true);
            var player = Player();
            player.Motor.Teleport(new Vector3(20f, 0f, 50f));

            var timeout = Time.time + 3f;
            while (enemy.CurrentState != MeleeEnemy.State.Chasing && Time.time < timeout) yield return null;
            Assert.AreEqual(MeleeEnemy.State.Chasing, enemy.CurrentState);
            var pointA = new GameObject("TestPatrol_A").transform;
            pointA.position = new Vector3(20f, 0f, 42f);
            var pointB = new GameObject("TestPatrol_B").transform;
            pointB.position = new Vector3(24f, 0f, 42f);
            enemy.SetPatrolRoute(new[] { pointA, pointB });

            // 玩家脱离视野：传送回远处的安全屋。
            player.Motor.Teleport(new Vector3(0f, 0f, -3f));
            var sawSearching = false;
            timeout = Time.time + 15f;
            while (enemy.CurrentState != MeleeEnemy.State.Patrolling && Time.time < timeout)
            {
                sawSearching |= enemy.CurrentState == MeleeEnemy.State.Searching;
                yield return null;
            }

            Assert.IsTrue(sawSearching, "丢失目标后应在最后目击点搜索");
            Assert.AreEqual(MeleeEnemy.State.Patrolling, enemy.CurrentState, "搜索结束后应返回巡逻");
            Assert.AreEqual(player.Health.Max, player.Health.Current, "丢失视野后不应继续造成伤害");
        }

        private static void Place(MeleeEnemy enemy, Vector3 position, Vector3 facing, bool clearPatrol)
        {
            foreach (var other in Object.FindObjectsByType<MeleeEnemy>())
            {
                if (other == enemy) continue;
                other.enabled = false;
                other.GetComponent<NavMeshAgent>().isStopped = true;
            }

            if (clearPatrol) enemy.SetPatrolRoute(new Transform[0]);
            var agent = enemy.GetComponent<NavMeshAgent>();
            agent.ResetPath();
            agent.Warp(position);
            enemy.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        }

        private static MeleeEnemy Enemy()
        {
            var enemy = Object.FindAnyObjectByType<MeleeEnemy>();
            Assert.IsNotNull(enemy);
            return enemy;
        }

        private static PlayerCharacter Player()
        {
            return Object.FindAnyObjectByType<RunSession>().Player;
        }

        private static IEnumerator LoadSlice()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                $"Build Settings 中没有 {SceneName}，请先运行菜单 Wilderness/Build Vertical Slice。");
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
            // AI Bot 有独立测试（AiBotTests），这里停用以免干扰。
            foreach (var bot in Object.FindObjectsByType<AiBotController>()) bot.gameObject.SetActive(false);

            // 只验证怪物行为，不让黑暗伤害干扰。
            var player = Player();
            if (player.Exposure != null) player.Exposure.enabled = false;
            player.Motor.Teleport(new Vector3(0f, 0f, -3f));
        }
    }
}
