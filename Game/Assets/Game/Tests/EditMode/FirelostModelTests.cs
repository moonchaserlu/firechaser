using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction.Tests
{
    /// <summary>失火者：模型变体沿用原近战怪物的 AI、碰撞和导航，原预制体保持不变，旧灯不发光；动画控制器与驱动脚本接好。</summary>
    public sealed class FirelostModelTests
    {
        private const string VariantPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";
        private const string EnemyPrefabPath = "Assets/Game/Prefabs/MeleeEnemy.prefab";

        [Test]
        public void Variant_KeepsEnemyLogic_AndShowsModel()
        {
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            Assert.IsNotNull(variant, "缺少失火者变体，先运行 Wilderness/Firelost/Setup Model And Variant");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath), PrefabUtility.GetCorrespondingObjectFromSource(variant),
                "应是 MeleeEnemy.prefab 的变体");
            Assert.IsNotNull(variant.GetComponent<MeleeEnemy>());
            Assert.IsNotNull(variant.GetComponent<NavMeshAgent>());
            Assert.IsNotNull(variant.GetComponent<CapsuleCollider>());

            Assert.IsFalse(variant.transform.Find("Body").GetComponent<MeshRenderer>().enabled, "占位胶囊应隐藏");
            var model = variant.transform.Find("FirelostModel");
            Assert.IsNotNull(model);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            CollectionAssert.AreEquivalent(new[] { "Firelost_Body", "Firelost_Clothes", "Firelost_Lantern" },
                System.Array.ConvertAll(renderers, r => r.name));
            Assert.IsEmpty(variant.GetComponentsInChildren<Light>(), "旧灯是熄灭的，不能带光源");

            var instance = Object.Instantiate(variant);
            try
            {
                // 蒙皮网格的 bounds 是带余量的估算，这里烘出实际顶点再量
                var placed = instance.transform.Find("FirelostModel").GetComponentsInChildren<SkinnedMeshRenderer>();
                var bounds = new Bounds(instance.transform.position, Vector3.zero);
                var baked = new Mesh();
                foreach (var r in placed)
                {
                    r.BakeMesh(baked, true);
                    foreach (var v in baked.vertices) bounds.Encapsulate(r.transform.TransformPoint(v));
                }

                Object.DestroyImmediate(baked);
                Assert.That(bounds.size.y, Is.InRange(1.5f, 2.1f), "驼背姿态高度约 1.7m");
                Assert.That(bounds.min.y, Is.InRange(-0.1f, 0.1f), "脚底应在原点");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Variant_HasAnimatorAndDriver_WithAttackClips()
        {
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            var animator = variant.transform.Find("FirelostModel").GetComponent<Animator>();
            Assert.IsNotNull(animator);
            Assert.IsFalse(animator.applyRootMotion, "移动由 NavMeshAgent 负责");
            var controller = animator.runtimeAnimatorController as AnimatorController;
            Assert.IsNotNull(controller);
            var states = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "Locomotion", "Attack_R", "Attack_L", "Hit", "Death" }, states);

            var driver = variant.GetComponent<FirelostAnimationDriver>();
            Assert.IsNotNull(driver);
            var so = new SerializedObject(driver);
            Assert.AreEqual(animator, so.FindProperty("animator").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("config").objectReferenceValue, "攻击节奏应读取怪物的 EnemyConfig");
            var impact = so.FindProperty("impactTime").floatValue;
            var config = (EnemyConfig)so.FindProperty("config").objectReferenceValue;
            Assert.That(impact, Is.LessThanOrEqualTo(config.attackInterval), "命中前摇不能长于攻击间隔");

            foreach (var name in new[] { "Attack_R", "Attack_L" })
            {
                var clip = controller.animationClips.First(c => c.name == name);
                Assert.That(clip.length, Is.GreaterThan(impact), $"{name} 片段应在命中帧之后还有收势");
                Assert.IsFalse(clip.isLooping);
            }

            Assert.IsTrue(controller.animationClips.First(c => c.name == "Walk").isLooping);
        }

        [Test]
        public void OriginalPrefab_IsUntouched()
        {
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            Assert.IsNull(original.transform.Find("FirelostModel"));
            Assert.IsTrue(original.transform.Find("Body").GetComponent<MeshRenderer>().enabled);
        }
    }
}
