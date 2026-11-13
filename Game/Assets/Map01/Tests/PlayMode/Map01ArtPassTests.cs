using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 阶段 2 素材替换的约束：素材只是视觉层，碰撞只在 Physics/Collision 分组里，
    /// 污染层独立存在，主要视觉来自 KayKit。
    /// </summary>
    public sealed class Map01ArtPassTests
    {
        private const string ArtScene = "Assets/Map01/Scenes/Map01.unity";

        [UnityTest]
        public IEnumerator ArtLayers_AreVisualOnly()
        {
            yield return Load(ArtScene);
            var root = GameObject.Find("Map01_Root").transform;
            foreach (var groupName in new[] { "MonsterContamination", "Props_Storytelling" })
            {
                var group = root.Find(groupName);
                Assert.IsNotNull(group, $"缺少 {groupName} 分组");
                Assert.AreEqual(0, group.GetComponentsInChildren<Collider>(true).Length, $"{groupName} 只能放视觉对象");
            }

            Assert.AreEqual(0, root.Find("Props_Static/Undergrowth").GetComponentsInChildren<Collider>(true).Length, "灌木草丛不应有碰撞");
            foreach (var reveal in Object.FindObjectsByType<BuildingReveal>(FindObjectsSortMode.None))
            {
                var building = reveal.transform;
                Assert.AreEqual(0, building.Find("InteriorVisuals").GetComponentsInChildren<Collider>(true).Length, $"{reveal.BuildingId} 室内视觉不应带碰撞");
                Assert.AreEqual(0, building.Find("RoofRevealGroup").GetComponentsInChildren<Collider>(true).Length, $"{reveal.BuildingId} 屋顶不应带碰撞");
                Assert.AreEqual(0, building.Find("Physics").GetComponentsInChildren<Renderer>(true).Length, $"{reveal.BuildingId} 碰撞分组不应带渲染");
            }
        }

        [UnityTest]
        public IEnumerator ArtScene_UsesKayKitAndHasContaminationGradient()
        {
            yield return Load(ArtScene);
            var kayKit = 0;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var material = renderer.sharedMaterial;
                Assert.IsNotNull(material, $"{renderer.name} 缺少材质");
                if (material.name.StartsWith("M_KK_"))
                {
                    kayKit++;
                }
            }

            Assert.Greater(kayKit, 500, "素材版应以 KayKit 模型为主要视觉");

            var abnormal = GameObject.Find("Map01_Root/MonsterContamination/AbnormalTrees");
            Assert.IsNotNull(abnormal, "缺少污染树层");
            Assert.Greater(abnormal.transform.childCount, 10);
            // 渐变：污染树都在污染源附近，主安全屋一侧没有。
            foreach (Transform tree in abnormal.transform)
            {
                Assert.Greater(Map01Layout.ContaminationAt(new Vector2(tree.position.x, tree.position.z)), 0.29f, $"{tree.name} 出现在正常区域");
            }

            var healthyTrees = GameObject.Find("Map01_Root/Props_Static/Trees").GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(healthyTrees.Length, abnormal.transform.childCount, "正常林相应占主体");
        }

        private static IEnumerator Load(string path)
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Map01 场景测试只在编辑器中运行。");
#endif
            yield return null;
        }
    }
}
