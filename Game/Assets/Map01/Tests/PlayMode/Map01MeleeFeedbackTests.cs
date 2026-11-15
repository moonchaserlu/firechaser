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
    /// 近战表现：挥空也有刀光，打中再加火星和闪白，播完全部清理干净。
    /// 设置环境变量 MAP01_CAPTURE_DIR 时顺带用游戏镜头把过程拍下来。
    /// </summary>
    public sealed class Map01MeleeFeedbackTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const string FirelostPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";

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
            // 桥北头的开阔地，镜头里没有屋顶和树冠挡着。
            var site = new Vector3(27f, 0f, 118f);
            site.y = Terrain.activeTerrain.SampleHeight(site) + Terrain.activeTerrain.transform.position.y;
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = site;
            controller.enabled = true;
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            yield return null;
        }

        [TearDown]
        public void Restore()
        {
            Time.captureDeltaTime = 0f;
            Map01Authority.SuppressMonsters = false;
        }

        [UnityTest]
        public IEnumerator MissedSwing_ShowsSlash_ThenCleansUp()
        {
            Object.FindAnyObjectByType<Map01Daylight>()?.BeginNight();
            Assert.IsTrue(player.Shooter.TryQuickMelee(Time.time));
            Assert.AreEqual(1, Object.FindObjectsByType<MeleeSlashEffect>().Length, "挥空也要有刀光");
            Assert.AreEqual(0, Object.FindObjectsByType<MeleeHitEffect>().Length, "挥空不该有命中特效");
            yield return Shots("Melee_Night_Miss");
            yield return new WaitForSeconds(MeleeSlashEffect.Duration + 0.1f);
            Assert.AreEqual(0, Object.FindObjectsByType<MeleeSlashEffect>().Length, "刀光播完应销毁");
        }

        [UnityTest]
        public IEnumerator Hit_ShowsSparksAndFlash_ThenRestoresMaterials([Values(false, true)] bool night)
        {
#if UNITY_EDITOR
            var daylight = Object.FindAnyObjectByType<Map01Daylight>();
            if (night) daylight?.BeginNight();
            else daylight?.BeginDay();
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<MeleeEnemy>(FirelostPath);
            var enemy = Object.Instantiate(prefab, player.transform.position + player.Motor.AimDirection, Quaternion.identity);
            Physics.SyncTransforms();
            var model = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
            var materialCount = model.sharedMaterials.Length;
            var healthBefore = enemy.Health.Current;

            Assert.IsTrue(player.Shooter.TryQuickMelee(Time.time));
            Assert.Less(enemy.Health.Current, healthBefore, "这一刀应打中");
            Assert.AreEqual(1, Object.FindObjectsByType<MeleeHitEffect>().Length);
            Assert.AreEqual(materialCount + 1, model.sharedMaterials.Length, "打中时模型应闪白");
            yield return Shots(night ? "Melee_Night_Hit" : "Melee_Day_Hit");
            yield return new WaitForSeconds(MeleeHitEffect.Duration + 0.1f);
            Assert.AreEqual(0, Object.FindObjectsByType<MeleeHitEffect>().Length, "命中特效播完应销毁");
            Assert.AreEqual(materialCount, model.sharedMaterials.Length, "闪白结束应摘掉叠加材质");
            Object.Destroy(enemy.gameObject);
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator FirelostAttack_ShowsSlash()
        {
#if UNITY_EDITOR
            Object.FindAnyObjectByType<Map01Daylight>()?.BeginNight();
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<MeleeEnemy>(FirelostPath);
            var enemy = Object.Instantiate(prefab, player.transform.position + Vector3.left * 1.2f, Quaternion.LookRotation(Vector3.right));
            Physics.SyncTransforms();
            var attacked = false;
            enemy.Attacked += () => attacked = true;
            for (var frame = 0; frame < 600 && !attacked; frame++) yield return null;
            Assert.IsTrue(attacked, "失火者贴身应出手");
            Assert.AreEqual(1, Object.FindObjectsByType<MeleeSlashEffect>().Length, "爪击命中帧应出爪痕");
            yield return Shots("Melee_Night_Firelost");
            Object.Destroy(enemy.gameObject);
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ConsecutiveSwings_Alternate()
        {
            var signs = new System.Collections.Generic.List<int>();
            player.Shooter.MeleeSwung += signs.Add;
            Assert.IsTrue(player.Shooter.TryQuickMelee(Time.time));
            Assert.IsTrue(player.Shooter.TryQuickMelee(Time.time + 10f));
            player.Shooter.MeleeSwung -= signs.Add;
            Assert.AreEqual(2, signs.Count);
            Assert.AreNotEqual(signs[0], signs[1], "连续两刀方向应相反");
            yield return null;
        }

        /// <summary>刀头扫到一半、扫完、收尾各拍一张。没设置输出目录时直接跳过。</summary>
        private static IEnumerator Shots(string name)
        {
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) yield break;
            Directory.CreateDirectory(directory);
            for (var shot = 0; shot < 3; shot++)
            {
                // 每隔 4 帧（约 0.067 秒）一张，覆盖刀光 0.26 秒的全过程。
                for (var frame = 0; frame < (shot == 0 ? 3 : 4); frame++) yield return null;
                Render(Path.Combine(directory, $"{name}_{shot}.png"));
            }
        }

        private static void Render(string path)
        {
            var camera = Camera.main;
            var target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
