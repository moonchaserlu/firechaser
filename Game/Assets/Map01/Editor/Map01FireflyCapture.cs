using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 萤火虫效果截图：黑夜下用游戏镜头拍几团萤火虫（玩家站在光圈边上作比例尺），再拍一张树林远景。
    /// 输出目录取环境变量 MAP01_CAPTURE_DIR，未设置时写到工程上一级的 Docs/Map01。
    /// </summary>
    public static class Map01FireflyCapture
    {
        [MenuItem("Wilderness/Map01/Capture Firefly Screenshots")]
        public static void Capture()
        {
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Map01"));
            }

            Directory.CreateDirectory(directory);
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            GameObject cameraObject = null;
            try
            {
                // 先开场景再建相机：开场景会销毁当前场景里的对象。
                EditorSceneManager.OpenScene(Map01Builder.ScenePath);
                cameraObject = new GameObject("FireflyCaptureCamera");
                var blueprint = GameObject.Find("Map01_Root/BlueprintReference");
                if (blueprint != null) blueprint.SetActive(false);
                Object.FindAnyObjectByType<Map01Daylight>()?.BeginNight();

                var swarms = Object.FindObjectsByType<FireflySwarm>(FindObjectsSortMode.None);
                Array.Sort(swarms, (a, b) => string.CompareOrdinal(a.name, b.name));
                foreach (var swarm in swarms)
                {
                    swarm.SetVisible(true);
                    swarm.Particles.Simulate(6f, true, true);
                }

                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
                camera.farClipPlane = 600f;

                // 游戏镜头：玩家站在光圈边上。
                var player = Object.FindAnyObjectByType<PlayerCharacter>();
                var picks = new[] { "Fireflies_Forest_1", "Fireflies_SouthWestWoods_2", "Fireflies_EastWoods_1" };
                var index = 0;
                foreach (var pick in picks)
                {
                    var swarm = Array.Find(swarms, s => s.name == pick) ?? swarms[Mathf.Min(index, swarms.Length - 1)];
                    var target = swarm.transform.position + new Vector3(swarm.Radius - 1f, 0f, -1f);
                    if (player != null) player.transform.position = target;
                    camera.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
                    camera.nearClipPlane = FixedObliqueCamera.DefaultNearClip;
                    camera.transform.SetPositionAndRotation(target + Vector3.up * FixedObliqueCamera.DefaultFollowHeight + FixedObliqueCamera.DefaultOffset,
                        Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
                    Render(camera, 1600, 900, Path.Combine(directory, $"Map01_Fireflies_{++index}.png"));
                }

                // 树林远景：能看到几团萤火虫和沿路的灯。
                camera.fieldOfView = 40f;
                camera.nearClipPlane = 0.5f;
                camera.transform.SetPositionAndRotation(new Vector3(-95f, 70f, -35f), Quaternion.Euler(55f, 0f, 0f));
                Render(camera, 1600, 900, Path.Combine(directory, "Map01_Fireflies_Forest_Wide.png"));
                Debug.Log($"[Map01] 萤火虫截图已写入 {directory}");
            }
            finally
            {
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                EditorSceneManager.OpenScene(Map01Builder.ScenePath);
            }
        }

        private static void Render(Camera camera, int width, int height, string path)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                // 新打开场景后的第一帧偶发全部使用同一材质出图，先预渲染一帧再截取。
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
