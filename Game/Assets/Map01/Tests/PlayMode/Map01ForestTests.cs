using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>树林：成簇分布、留有空地；挡在镜头和玩家之间的树冠会淡出，走开后恢复。</summary>
    public sealed class Map01ForestTests
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
        }

        [Test]
        public void Trees_GrowInGroves_WithClearingsBetween()
        {
            var trees = GameObject.Find("Map01_Root/Props_Static/Trees").transform.Cast<Transform>()
                .Select(t => new Vector2(t.position.x, t.position.z)).ToList();
            Assert.Greater(trees.Count, 150, "林地仍要有足够的树");
            Assert.Less(trees.Count, 330, "树比原来的均匀撒法少");

            // 成簇：大多数树 8 米内有同伴；有空地：林区里能找到 10 米内没有树的空地。
            var clustered = trees.Count(a => trees.Any(b => b != a && (a - b).sqrMagnitude < 8f * 8f));
            Assert.Greater(clustered, trees.Count * 0.8f, "树应成簇");
            var forestCenter = new Vector2(-91f, 19f);
            var clearings = 0;
            for (var x = -60f; x <= 60f; x += 6f)
            {
                for (var z = -30f; z <= 30f; z += 6f)
                {
                    var p = forestCenter + new Vector2(x, z);
                    if (Map01Layout.IsInRiver(p, 2f)) continue;
                    if (!trees.Any(t => (t - p).sqrMagnitude < 10f * 10f)) clearings++;
                }
            }

            Assert.Greater(clearings, 5, "树林中间应有空地和通道");
        }

        [UnityTest]
        public IEnumerator CanopyBetweenCameraAndPlayer_FadesOut_ThenRestores()
        {
            var fade = Object.FindAnyObjectByType<CanopyOcclusionFade>();
            Assert.IsNotNull(fade, "主相机上应有遮挡淡出");
            var player = Object.FindAnyObjectByType<PlayerCharacter>();
            var tree = GameObject.Find("Map01_Root/Props_Static/Trees").transform.Cast<Transform>()
                .First(t => t.GetComponentInChildren<MeshRenderer>() != null);
            var canopy = tree.GetComponentInChildren<MeshRenderer>();

            // 镜头在玩家南侧上方：玩家站到树的北边，树就挡在中间。
            player.Motor.Teleport(tree.position + new Vector3(0f, 0.3f, 3f));
            yield return null;
            yield return null;
            Assert.IsTrue(fade.IsFaded(canopy), "挡住玩家的树冠应淡出");
            Assert.IsNotNull(tree.GetComponentInChildren<Collider>(), "淡出不影响树干碰撞");

            player.Motor.Teleport(tree.position + new Vector3(0f, 0.3f, -40f));
            yield return null;
            yield return null;
            Assert.IsFalse(fade.IsFaded(canopy), "走开后树冠恢复");
        }
    }
}
