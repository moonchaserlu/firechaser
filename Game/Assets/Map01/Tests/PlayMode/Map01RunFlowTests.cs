using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>单局流程：暂停冻结时间与操作；结算后可重开新一局，仓库保留。</summary>
    public sealed class Map01RunFlowTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

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
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SuppressMonsters = false;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Pause_FreezesTimeAndInput_ThenResumes()
        {
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            Assert.IsNotNull(flow, "场景里应有单局流程组件");

            flow.SetPaused(true);
            Assert.IsTrue(flow.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(authority.Player.Input.GameplayEnabled, "暂停时不接受操作");

            flow.SetPaused(false);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(authority.Player.Input.GameplayEnabled);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AfterDeath_RestartStartsFreshRun_StashKept()
        {
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            var stashBefore = SessionStash.Current.TotalValue;

            authority.Player.Health.TakeDamage(10000f);
            yield return null;
            Assert.AreEqual(Map01RunOutcome.Died, authority.Outcome);
            Assert.IsNotNull(authority.Result, "死亡也要有结算结果");
            flow.SetPaused(true);
            Assert.IsFalse(flow.IsPaused, "结算后不能暂停");

            flow.Restart();
            yield return null;
            yield return null;
            var next = Object.FindAnyObjectByType<Map01Authority>();
            Assert.AreNotSame(authority, next, "应加载新的一局");
            Assert.AreEqual(Map01RunOutcome.InProgress, next.Outcome);
            Assert.IsFalse(next.Player.Health.IsDead);
            Assert.AreEqual(stashBefore, SessionStash.Current.TotalValue, "死亡不入库，仓库保持不变");
        }
    }
}
