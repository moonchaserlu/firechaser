using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 准星对准失火者胸口开枪，子弹应打中它（修复前朝向按脚底平面求交，斜俯视下弹道整体落在准星上方，侧面的目标会打偏）。
    /// 设置 MAP01_CAPTURE_DIR 时拍修复前后各一张，图上用青色十字标出准星位置。
    /// </summary>
    public sealed class Map01AimTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        private const string FirelostPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";
        private const int Width = 1600;
        private const int Height = 900;

        private PlayerCharacter player;
        private Mouse mouse;
        private InputSettings originalSettings;
        private InputSettings testSettings;
        private RenderTexture view;

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
            var interactor = player.GetComponent<Map01Interactor>();
            if (interactor != null) interactor.enabled = false;
            var site = new Vector3(27f, 0f, 118f);
            site.y = Terrain.activeTerrain.SampleHeight(site) + Terrain.activeTerrain.transform.position.y;
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = site;
            controller.enabled = true;

            // 同 Map01ReloadTests：忽略焦点的输入设置副本，测试鼠标的输入才送得到。
            var current = InputSystem.settings;
            originalSettings = current;
            if (current.hideFlags == HideFlags.HideAndDontSave)
            {
                originalSettings = Object.Instantiate(current);
                originalSettings.hideFlags = HideFlags.HideAndDontSave;
            }

            testSettings = Object.Instantiate(current);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = testSettings;
            mouse = InputSystem.AddDevice<Mouse>();

            // 镜头固定画到一张 1600×900 的图上：屏幕坐标、准星换算和截图用同一套尺寸。
            view = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            Camera.main.targetTexture = view;
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Map01Daylight>()?.BeginNight();
            player.Shooter.FreeAmmo = true;
        }

        [TearDown]
        public void Restore()
        {
            Time.captureDeltaTime = 0f;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (view != null) Object.Destroy(view);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            mouse = null;
            if (originalSettings != null) InputSystem.settings = originalSettings;
            if (testSettings != null) Object.Destroy(testSettings);
            Map01Authority.SuppressMonsters = false;
        }

        [UnityTest]
        public IEnumerator CrosshairOnSideTarget_ShotHits([Values(false, true)] bool legacyGroundAim)
        {
            var capture = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR"));
            // 修复前的行为只在拍对照图时跑一次，不作为断言。
            if (legacyGroundAim && !capture) yield break;
#if UNITY_EDITOR
            var motor = player.Motor;
            if (legacyGroundAim) motor.AimHeight = 0f;
            var camera = Camera.main;
            // 目标放在镜头右侧、斜前方，斜俯视下这里准星和弹道的高度差最容易让子弹打偏。
            var right = Vector3.Cross(Vector3.up, Flat(camera.transform.forward));
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<MeleeEnemy>(FirelostPath);
            var spot = player.transform.position + right * 6f + Flat(camera.transform.forward) * 2.5f;
            var enemy = Object.Instantiate(prefab, spot, Quaternion.LookRotation(player.transform.position - spot));
            enemy.enabled = false;
            Physics.SyncTransforms();
            // 准星指在它胸口（枪口高度）。
            var chest = enemy.transform.position + Vector3.up * 1.1f;
            var screen = (Vector2)camera.WorldToScreenPoint(chest);
            InputState.Change(mouse.position, screen);
            for (var frame = 0; frame < 4; frame++) yield return null;
            var before = enemy.Health.Current;

            Assert.IsTrue(player.Shooter.TryFire(Time.time));
            var hit = enemy.Health.Current < before;
            if (capture) yield return Capture(legacyGroundAim ? "Aim_Before" : "Aim_After", screen);
            if (!legacyGroundAim) Assert.IsTrue(hit, "准星对准的目标应被打中");
            Object.Destroy(enemy.gameObject);
#else
            yield break;
#endif
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        /// <summary>曳光飞到一半时拍一张，再在准星位置画一个青色十字。</summary>
        private IEnumerator Capture(string name, Vector2 crosshair)
        {
            yield return null;
            yield return null;
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            Directory.CreateDirectory(directory);
            var camera = Camera.main;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = view;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            var cx = Mathf.RoundToInt(crosshair.x);
            var cy = Mathf.RoundToInt(crosshair.y);
            for (var d = -18; d <= 18; d++)
            {
                if (Mathf.Abs(d) < 5) continue;
                for (var t = -1; t <= 1; t++)
                {
                    image.SetPixel(cx + d, cy + t, Color.cyan);
                    image.SetPixel(cx + t, cy + d, Color.cyan);
                }
            }

            image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            RenderTexture.active = previous;
        }
    }
}
