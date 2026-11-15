using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>Map01 刷出的怪物使用失火者变体：有模型、动画驱动和死亡遗物箱，占位胶囊不显示。</summary>
    public sealed class Map01FirelostTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SuppressMonsters = false;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpawnedMonsters_AreFirelost()
        {
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            Assert.IsNotNull(authority);
            Assert.That(authority.Monsters.Count, Is.GreaterThan(0), "Map01 应刷出怪物");
            foreach (var monster in authority.Monsters)
            {
                Assert.IsNotNull(monster.transform.Find("FirelostModel"), $"{monster.name} 应使用失火者模型");
                Assert.IsNotNull(monster.GetComponent<FirelostAnimationDriver>());
                Assert.IsNotNull(monster.GetComponent<EnemyLootDrop>());
                Assert.IsFalse(monster.transform.Find("Body").GetComponent<MeshRenderer>().enabled, "占位胶囊应隐藏");
            }

            yield return null;
        }
    }
}
