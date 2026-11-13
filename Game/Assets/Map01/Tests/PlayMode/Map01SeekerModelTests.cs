using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>寻火者模型接入：Map01 玩家使用模型变体，占位胶囊不再显示，移动和翻滚能驱动动画。</summary>
    public sealed class Map01SeekerModelTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

        private PlayerCharacter player;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SuppressMonsters = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            player = Object.FindAnyObjectByType<Map01Authority>().Player;
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SuppressMonsters = false;
        }

        [UnityTest]
        public IEnumerator Player_UsesSeekerModel_PlaceholdersHidden()
        {
            yield return null;
            var model = player.transform.Find("SeekerModel");
            Assert.IsNotNull(model, "Map01 玩家应来自 Player_Seeker 变体");
            Assert.GreaterOrEqual(model.GetComponentsInChildren<SkinnedMeshRenderer>().Length, 4, "身体、斗篷、背包、手枪");
            Assert.IsFalse(player.transform.Find("Body").GetComponent<MeshRenderer>().enabled, "占位胶囊应隐藏");
            Assert.IsNotNull(player.GetComponent<PlayerAnimationDriver>());
            var animator = model.GetComponent<Animator>();
            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.IsNull(player.GetComponent<Animator>(), "动画只挂在模型上，玩家根节点不受根运动影响");
        }

        [UnityTest]
        public IEnumerator Moving_DrivesLocomotionParameters()
        {
            var animator = player.transform.Find("SeekerModel").GetComponent<Animator>();
            var start = player.transform.position;
            // 按时间而不是帧数：批处理下帧间隔很小，参数阻尼需要实际经过的时间
            var end = Time.time + 0.6f;
            while (Time.time < end)
            {
                start += Vector3.right * 5f * Time.deltaTime;
                player.Motor.Teleport(start);
                yield return null;
            }

            var move = new Vector2(animator.GetFloat("MoveX"), animator.GetFloat("MoveZ"));
            Assert.Greater(move.magnitude, 0.5f, "以移动速度平移时跑步混合参数应接近 1");
        }

        [UnityTest]
        public IEnumerator DodgeTrigger_PlaysRoll_ThenReturnsToLocomotion()
        {
            var animator = player.transform.Find("SeekerModel").GetComponent<Animator>();
            animator.SetTrigger("Dodge");
            yield return new WaitForSeconds(0.15f);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Roll") || animator.GetNextAnimatorStateInfo(0).IsName("Roll"));
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "翻滚结束后回到跑步混合");
        }
    }
}
