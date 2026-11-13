using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 安全屋院灯：断电时是默认半径（14），通电后放大到罩住整栋安全屋（约 20.9），再断电回到 14。
    /// </summary>
    public sealed class Map01SafehouseLampTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const float DefaultRadius = 14f;
        private const float CoverRadius = 20.92f;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SeedOverride = 20261023;
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
            Map01Authority.SeedOverride = null;
            Map01Authority.SuppressMonsters = false;
        }

        [Test]
        public void YardLamp_CoversSafehouseOnlyWhenPowered()
        {
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            Assert.IsTrue(authority.QuestMode);
            Assert.IsFalse(authority.SafehouseLit, "开场断电");

            var lamp = GameObject.Find("Lamp_Safehouse").GetComponent<SafeLightSource>();
            var spec = Map01Layout.FindBuilding("map01_safehouse");
            var indoor = GameObject.Find("map01_safehouse").GetComponent<IndoorLight>();
            Assert.AreEqual(DefaultRadius, lamp.Radius, 0.01f, "断电时院灯是默认半径");

            indoor.SetAlwaysLit(true);
            Assert.AreEqual(CoverRadius, lamp.Radius, 0.05f, "通电后院灯光圈放大");
            for (var sx = -1; sx <= 1; sx += 2)
            {
                for (var sy = -1; sy <= 1; sy += 2)
                {
                    var corner = new Vector3(spec.Center.x + spec.Size.x * 0.5f * sx, 0.5f, spec.Center.y + spec.Size.y * 0.5f * sy);
                    Assert.IsTrue(lamp.Contains(corner), $"通电后院灯罩住安全屋角 ({sx},{sy})");
                    Assert.IsTrue(LightRegistry.Scene.IsLit(corner), $"通电后安全屋角 ({sx},{sy}) 有光");
                }
            }

            indoor.SetAlwaysLit(false);
            Assert.AreEqual(DefaultRadius, lamp.Radius, 0.01f, "断电后院灯回到默认半径");
        }
    }
}
