using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 生成 Map01 白盒截图（全图俯视、厂房屋顶显示/隐藏对比），用于和概念图对照。
    /// 输出目录取环境变量 MAP01_CAPTURE_DIR，未设置时写到工程上一级的 Docs/Map01。
    /// </summary>
    public static class Map01Capture
    {
        [MenuItem("Wilderness/Map01/Capture Whitebox Screenshots")]
        public static void Capture()
        {
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Map01"));
            }

            Directory.CreateDirectory(directory);
            EditorSceneManager.OpenScene(Map01Builder.ScenePath);
            var blueprint = GameObject.Find("Map01_Root/BlueprintReference");
            if (blueprint != null)
            {
                blueprint.SetActive(false);
            }

            var cameraObject = new GameObject("CaptureCamera");
            // 批处理模式下 SRP Batcher 偶发串用材质属性，截图时关闭。
            var srpBatching = UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.62f, 0.7f, 0.76f);
                camera.farClipPlane = 1000f;

                // 远距离窄视角近似正交俯视，图像范围与概念图一致（480m × 320m）。
                const float height = 700f;
                camera.fieldOfView = 2f * Mathf.Atan(Map01Layout.WorldDepth * 0.5f / height) * Mathf.Rad2Deg;
                // 拉远近裁剪面保证深度精度，否则贴地的河面、桥面会与地形深度冲突。
                camera.nearClipPlane = height - 60f;
                cameraObject.transform.SetPositionAndRotation(new Vector3(0f, height, 0f), Quaternion.Euler(90f, 0f, 0f));
                // 远距离阴影级联会把平顶面整体压暗，俯视图关闭阴影以保持色板可读。
                var sun = Object.FindAnyObjectByType<Light>();
                var shadows = sun != null ? sun.shadows : LightShadows.None;
                if (sun != null) sun.shadows = LightShadows.None;
                var reflection = RenderSettings.reflectionIntensity;
                RenderSettings.reflectionIntensity = 0f;
                Render(camera, 1536, 1024, Path.Combine(directory, "Map01_Whitebox_TopDown.png"));
                RenderSettings.reflectionIntensity = reflection;
                if (sun != null) sun.shadows = shadows;

                var factory = Map01Layout.FindBuilding("map01_factory");
                var target = new Vector3(factory.Center.x, 0f, factory.Center.y);
                camera.fieldOfView = 50f;
                camera.nearClipPlane = 0.3f;
                cameraObject.transform.SetPositionAndRotation(target + new Vector3(0f, 34f, -24f), Quaternion.Euler(55f, 0f, 0f));
                var roof = GameObject.Find($"Map01_Root/Buildings_Static/{factory.Id}/RoofRevealGroup");
                Render(camera, 1280, 720, Path.Combine(directory, "Map01_Factory_RoofVisible.png"));
                foreach (var renderer in roof.GetComponentsInChildren<Renderer>())
                {
                    renderer.forceRenderingOff = true;
                }

                Render(camera, 1280, 720, Path.Combine(directory, "Map01_Factory_RoofHidden.png"));
                Debug.Log($"[Map01] 截图已写入 {directory}");
            }
            finally
            {
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = srpBatching;
                Object.DestroyImmediate(cameraObject);
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
