using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 射击表现：开枪有枪口火光和飞出去的曳光，打中失火者溅血 + 闪白，打在墙上冒火星和灰尘，播完全部清理干净。
    /// 设置环境变量 MAP01_CAPTURE_DIR 时顺带用游戏镜头把过程拍下来（含改动前整条细线曳光的对照图）。
    /// </summary>
    public sealed class Map01GunFeedbackTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const string FirelostPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";
        private const float TargetDistance = 6f;

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
            // 和近战表现测试同一块开阔地。
            var site = new Vector3(27f, 0f, 118f);
            site.y = Terrain.activeTerrain.SampleHeight(site) + Terrain.activeTerrain.transform.position.y;
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = site;
            controller.enabled = true;
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Map01Daylight>()?.BeginNight();
            Assert.AreNotEqual(WeaponKind.Melee, player.Shooter.CurrentConfig.kind, "Map01 主武器应是枪");
            player.Shooter.FreeAmmo = true;
        }

        [TearDown]
        public void Restore()
        {
            Time.captureDeltaTime = 0f;
            Map01Authority.SuppressMonsters = false;
        }

        [UnityTest]
        public IEnumerator ShotAtFirelost_TracerFlies_ThenBloodAndFlash_ThenCleansUp()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<MeleeEnemy>(FirelostPath);
            var aim = Flat(player.Motor.AimDirection);
            var enemy = Object.Instantiate(prefab, player.transform.position + aim * TargetDistance, Quaternion.LookRotation(-aim));
            Physics.SyncTransforms();
            var model = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
            var materialCount = model.sharedMaterials.Length;
            var healthBefore = enemy.Health.Current;

            Assert.IsTrue(player.Shooter.TryFire(Time.time));
            Assert.Less(enemy.Health.Current, healthBefore, "这一枪应打中（伤害仍在开枪当帧结算）");
            Assert.AreEqual(1, Object.FindObjectsByType<GunMuzzleFlash>().Length, "开枪应有枪口火光");
            Assert.AreEqual(1, Object.FindObjectsByType<BulletTracer>().Length, "开枪应有曳光");
            Assert.AreEqual(0, Object.FindObjectsByType<BulletImpactEffect>().Length, "曳光还没飞到，不该已经出命中特效");
            yield return Capture("Gun_Firelost_0_Muzzle");

            BulletImpactEffect impact = null;
            for (var frame = 0; frame < 30 && impact == null; frame++)
            {
                yield return null;
                impact = Object.FindAnyObjectByType<BulletImpactEffect>();
            }

            Assert.IsNotNull(impact, "曳光飞到后应出命中特效");
            Assert.IsTrue(impact.HitFlesh, "打中失火者应溅血");
            Assert.AreEqual(materialCount + 1, model.sharedMaterials.Length, "打中时模型应闪白");
            yield return Capture("Gun_Firelost_1_Hit");
            for (var frame = 0; frame < 6; frame++) yield return null;
            yield return Capture("Gun_Firelost_2_Blood");

            yield return new WaitForSeconds(BulletImpactEffect.Duration + 0.1f);
            Assert.AreEqual(0, Object.FindObjectsByType<BulletImpactEffect>().Length, "命中特效播完应销毁");
            Assert.AreEqual(0, Object.FindObjectsByType<BulletTracer>().Length, "曳光播完应销毁");
            Assert.AreEqual(0, Object.FindObjectsByType<GunMuzzleFlash>().Length, "枪口火光播完应销毁");
            Assert.AreEqual(materialCount, model.sharedMaterials.Length, "闪白结束应摘掉叠加材质");
            Object.Destroy(enemy.gameObject);
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ShotAtWall_ShowsSparks()
        {
            var aim = Flat(player.Motor.AimDirection);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetPositionAndRotation(player.transform.position + aim * TargetDistance + Vector3.up, Quaternion.LookRotation(aim));
            wall.transform.localScale = new Vector3(4f, 3f, 0.4f);
            Physics.SyncTransforms();

            Assert.IsTrue(player.Shooter.TryFire(Time.time));
            yield return Capture("Gun_Wall_0_Muzzle");
            BulletImpactEffect impact = null;
            for (var frame = 0; frame < 30 && impact == null; frame++)
            {
                yield return null;
                impact = Object.FindAnyObjectByType<BulletImpactEffect>();
            }

            Assert.IsNotNull(impact, "打在墙上也应有命中特效");
            Assert.IsFalse(impact.HitFlesh, "打在墙上是火星不是血");
            yield return Capture("Gun_Wall_1_Hit");
            for (var frame = 0; frame < 6; frame++) yield return null;
            yield return Capture("Gun_Wall_2_Sparks");
            Object.Destroy(wall);
        }

        [UnityTest]
        public IEnumerator Shot_TracerAndFlash_CleanUp()
        {
            // 不管有没有打中东西，曳光和枪口火光都会播完自毁。
            Assert.IsTrue(player.Shooter.TryFire(Time.time));
            Assert.AreEqual(1, Object.FindObjectsByType<BulletTracer>().Length);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0, Object.FindObjectsByType<BulletTracer>().Length, "曳光播完应销毁");
            Assert.AreEqual(0, Object.FindObjectsByType<GunMuzzleFlash>().Length, "枪口火光播完应销毁");
        }

        /// <summary>改动前的样子：玩家身上那条 LineRenderer 从枪口到落点亮 0.05 秒，没有火光和命中。只在拍对照图时跑。</summary>
        [UnityTest]
        public IEnumerator Before_OldLineTracer_ForComparison()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR"))) yield break;
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<MeleeEnemy>(FirelostPath);
            var aim = Flat(player.Motor.AimDirection);
            var enemy = Object.Instantiate(prefab, player.transform.position + aim * TargetDistance, Quaternion.LookRotation(-aim));
            Physics.SyncTransforms();
            yield return null;
            var line = player.GetComponentInChildren<LineRenderer>(true);
            Assert.IsNotNull(line, "玩家身上应有旧的曳光 LineRenderer");
            var from = player.transform.position + Vector3.up * 1.1f + aim * 0.55f;
            var muzzle = player.transform.Find("Muzzle");
            if (muzzle != null) from = muzzle.position;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, from + aim * (TargetDistance - 0.4f));
            line.enabled = true;
            yield return Capture("Gun_Before_Firelost");
            line.enabled = false;
            Object.Destroy(enemy.gameObject);
#endif
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        private static IEnumerator Capture(string name)
        {
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) yield break;
            Directory.CreateDirectory(directory);
            var camera = Camera.main;
            var target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
