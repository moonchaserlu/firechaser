using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Tests
{
    /// <summary>失火者动画驱动：爪击起手早于出伤害、命中帧与伤害时间对齐；死亡时模型直立贴地并播放倒地动画。</summary>
    public sealed class FirelostAnimationTests
    {
        private const string ScenePath = "Assets/Game/Scenes/VerticalSlice.unity";
        private const string VariantPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";
        private static readonly int AttackTag = Animator.StringToHash("Attack");
        private static readonly int DeadTag = Animator.StringToHash("Dead");

        private MeleeEnemy enemy;
        private Animator animator;
        private PlayerCharacter player;

        [UnitySetUp]
        public IEnumerator Load()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            foreach (var bot in Object.FindObjectsByType<AiBotController>()) bot.gameObject.SetActive(false);
            foreach (var other in Object.FindObjectsByType<MeleeEnemy>()) other.gameObject.SetActive(false);
            player = Object.FindAnyObjectByType<RunSession>().Player;
            if (player.Exposure != null) player.Exposure.enabled = false;

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            var go = Object.Instantiate(prefab, new Vector3(20f, 0f, 44f), Quaternion.LookRotation(Vector3.forward));
            enemy = go.GetComponent<MeleeEnemy>();
            enemy.SetPatrolRoute(new Transform[0]);
            animator = go.transform.Find("FirelostModel").GetComponent<Animator>();
            yield return null;
#else
            Assert.Ignore("只在编辑器中运行。");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Attack_SwingStartsBeforeDamage_AndImpactMatchesHit()
        {
            player.Motor.Teleport(new Vector3(20f, 0f, 45.3f));
            var health = player.Health.Current;
            float swingStart = -1f, damageTime = -1f;
            var timeout = Time.time + 6f;
            while (damageTime < 0f && Time.time < timeout)
            {
                if (swingStart < 0f && animator.GetCurrentAnimatorStateInfo(0).tagHash == AttackTag) swingStart = Time.time;
                if (player.Health.Current < health) damageTime = Time.time;
                yield return null;
            }

            Assert.That(damageTime, Is.GreaterThan(0f), "失火者应在近身后出伤害");
            Assert.That(swingStart, Is.GreaterThan(0f), "出伤害前应已进入爪击动画");
            // 起手经过 0.06s 过渡；命中帧（0.367s）与伤害之间允许一点帧误差。
            Assert.That(damageTime - swingStart, Is.InRange(0.15f, 0.5f), "命中帧应与出伤害时间对齐");
        }

        [UnityTest]
        public IEnumerator Death_PlaysFall_ThenBecomesSearchableBox()
        {
            var ground = enemy.transform.position;
            enemy.Health.TakeDamage(9999f);
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(MeleeEnemy.State.Dead, enemy.CurrentState);
            Assert.AreEqual(DeadTag, animator.GetCurrentAnimatorStateInfo(0).tagHash, "先播放死亡倒地动画");
            var model = animator.transform;
            Assert.That(Vector3.Angle(model.up, Vector3.up), Is.LessThan(1f), "灰盒倒地的整体旋转应被抵消，由动画自己倒下");
            Assert.That(model.position.y, Is.EqualTo(ground.y).Within(0.02f), "模型根应贴地");

            yield return new WaitForSeconds(1.0f);
            var drop = enemy.GetComponent<EnemyLootDrop>();
            var box = drop.Spawned;
            Assert.IsNotNull(box, "倒地后应变成箱子");
            Assert.IsFalse(model.gameObject.activeInHierarchy, "变成箱子后隐藏怪物模型");
            Assert.That(Vector3.Distance(box.transform.position, ground), Is.LessThan(0.05f), "箱子留在怪物倒下的位置、贴地");
            Assert.AreEqual(ContainerType.Corpse, box.Type);
            Assert.IsTrue(box.IsInteractable);
            Assert.AreSame(box, LootContainer.FindNearestInteractable(ground, 1f), "玩家走近可以搜索");

            box.MarkSearched();
            Assert.That(box.Remaining.Count, Is.GreaterThan(0), "搜索后应有掉落");
        }
    }
}
