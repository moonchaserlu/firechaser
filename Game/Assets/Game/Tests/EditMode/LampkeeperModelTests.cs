using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.Tests
{
    /// <summary>教学关 NPC 守灯人：预制体单根节点、自带 Idle 循环；坐姿高度、原点在地面、面朝 +Z（伸出的伤腿和挂灯在前方）。</summary>
    public sealed class LampkeeperModelTests
    {
        private const string PrefabPath = "Assets/Tutorial/Art/NPC/Tutorial_NPC.prefab";
        private const string ModelPath = "Assets/Tutorial/Art/NPC/Lampkeeper.fbx";

        [Test]
        public void Prefab_HasModelAndLoopingIdle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "缺少守灯人预制体，先运行 Wilderness/Tutorial/Setup Lampkeeper NPC Model");
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>();
            CollectionAssert.AreEquivalent(new[] { "Keeper_Body", "Keeper_Clothes", "Keeper_Props", "Keeper_Lamp" }, renderers.Select(r => r.name));
            Assert.IsTrue(renderers.All(r => r.sharedMaterial != null && r.sharedMaterial.GetTexture("_BaseMap") != null), "每个部件都应有烘焙贴图");
            Assert.IsTrue(renderers.First(r => r.name == "Keeper_Lamp").sharedMaterial.IsKeywordEnabled("_EMISSION"), "提灯是点着的");
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(), "碰撞由教学关的父节点负责");

            var animator = prefab.GetComponent<Animator>();
            Assert.IsNotNull(animator, "Animator 应在根节点上");
            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.IsFalse(animator.applyRootMotion);
            var idle = animator.runtimeAnimatorController.animationClips.Single();
            Assert.AreEqual("Idle", idle.name);
            Assert.IsTrue(idle.isLooping, "待机要循环");
            Assert.That(idle.length, Is.InRange(3.5f, 4.5f));
        }

        [Test]
        public void Prefab_SitsOnGround_FacingForward()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().First(c => c.name == "Idle").SampleAnimation(instance, 0f);
                Bounds Measure(string part)
                {
                    var r = instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(x => x.name == part);
                    var mesh = new Mesh();
                    r.BakeMesh(mesh, true);
                    var b = new Bounds(r.transform.TransformPoint(mesh.vertices[0]), Vector3.zero);
                    foreach (var v in mesh.vertices) b.Encapsulate(r.transform.TransformPoint(v));
                    Object.DestroyImmediate(mesh);
                    return b;
                }

                var body = Measure("Keeper_Body");
                var clothes = Measure("Keeper_Clothes");
                var props = Measure("Keeper_Props");
                var lamp = Measure("Keeper_Lamp");
                Assert.That(props.min.y, Is.InRange(-0.05f, 0.05f), "木箱和灯杆底应在地面");
                Assert.That(clothes.min.y, Is.InRange(-0.05f, 0.08f), "靴底应在地面附近");
                Assert.That(clothes.max.y, Is.InRange(1.3f, 1.5f), "坐姿帽顶约 1.4m");
                Assert.That(props.max.y, Is.InRange(1.85f, 2.1f), "灯杆约 2m 高");
                Assert.Greater(clothes.max.z, 0.6f, "伸直的伤腿应在前方（+Z）");
                Assert.Greater(lamp.center.z, 0.2f, "挂灯垂在前方");
                Assert.That(body.center.x, Is.InRange(-0.2f, 0.2f));
                Assert.Less(lamp.center.x, -0.25f, "灯杆在角色左手边（面朝 +Z 时为 -X）");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
