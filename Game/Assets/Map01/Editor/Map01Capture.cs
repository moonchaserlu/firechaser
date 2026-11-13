using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 生成 Map01 截图：全图俯视、厂房屋顶显示/隐藏对比、森林污染渐变、住宅室内。
    /// 输出目录取环境变量 MAP01_CAPTURE_DIR，未设置时写到工程上一级的 Docs/Map01。
    /// </summary>
    public static class Map01Capture
    {
        [MenuItem("Wilderness/Map01/Capture Screenshots")]
        public static void Capture()
        {
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Map01"));
            }

            Directory.CreateDirectory(directory);
            var srpBatching = UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            // 批处理模式下首帧着色器仍在异步编译，会用占位着色器出图；截图时改为同步编译。
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                CaptureMap(Map01Builder.ScenePath, directory, "Map01_Art");
                Debug.Log($"[Map01] 截图已写入 {directory}");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = srpBatching;
                EditorSceneManager.OpenScene(Map01Builder.ScenePath);
            }
        }

        private static void CaptureMap(string scenePath, string directory, string prefix)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return;
            }

            EditorSceneManager.OpenScene(scenePath);
            var blueprint = GameObject.Find("Map01_Root/BlueprintReference");
            if (blueprint != null)
            {
                blueprint.SetActive(false);
            }

            var daylight = Object.FindAnyObjectByType<Map01Daylight>();
            WithCamera(camera =>
            {
                // 黑夜实况：安全屋与沿路的灯。
                if (daylight != null) daylight.BeginNight();
                var safehouse = Map01Layout.FindBuilding("map01_safehouse").Center;
                CameraPose(camera, new Vector3(safehouse.x - 20f, 45f, safehouse.y - 20f), 55f);
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
                Render(camera, 1600, 900, Path.Combine(directory, $"{prefix}_Night_Safehouse.png"));

                // 以下用白天光检查布局与素材（等同献祭身份牌后的状态）。
                if (daylight != null) daylight.BeginDay();
                camera.backgroundColor = new Color(0.62f, 0.7f, 0.76f);
                const float height = 700f;
                camera.fieldOfView = 2f * Mathf.Atan(Map01Layout.WorldDepth * 0.5f / height) * Mathf.Rad2Deg;
                camera.nearClipPlane = height - 60f;
                CameraPose(camera, new Vector3(0f, height, 0f), 90f);
                Render(camera, 1536, 1024, Path.Combine(directory, $"{prefix}_TopDown.png"));

                camera.fieldOfView = 50f;
                camera.nearClipPlane = 0.3f;
                var factory = Map01Layout.FindBuilding("map01_factory");
                var target = new Vector3(factory.Center.x, 0f, factory.Center.y);
                CameraPose(camera, target + new Vector3(0f, 34f, -24f), 55f);
                var roof = GameObject.Find($"Map01_Root/Buildings_Static/{factory.Id}/RoofRevealGroup");
                Render(camera, 1280, 720, Path.Combine(directory, $"{prefix}_Factory_RoofVisible.png"));
                SetRendering(roof, false);
                Render(camera, 1280, 720, Path.Combine(directory, $"{prefix}_Factory_RoofHidden.png"));
                SetRendering(roof, true);

                var house = Map01Layout.FindBuilding("map01_house_a");
                var houseRoof = GameObject.Find($"Map01_Root/Buildings_Static/{house.Id}/RoofRevealGroup");
                SetRendering(houseRoof, false);
                CameraPose(camera, new Vector3(house.Center.x, 16f, house.Center.y - 11f), 55f);
                Render(camera, 1280, 720, Path.Combine(directory, $"{prefix}_House_Interior.png"));
                SetRendering(houseRoof, true);

                // 森林：从东侧正常林相看向西侧污染边缘。
                CameraPose(camera, new Vector3(-90f, 45f, -30f), 50f);
                camera.transform.rotation = Quaternion.Euler(50f, -60f, 0f);
                Render(camera, 1600, 900, Path.Combine(directory, $"{prefix}_ForestGradient.png"));

                var bridgeZ = (Map01Layout.NorthBankZ(Map01Layout.BridgeX) + Map01Layout.SouthBankZ(Map01Layout.BridgeX)) * 0.5f;
                CameraPose(camera, new Vector3(Map01Layout.BridgeX, 40f, bridgeZ - 40f), 50f);
                Render(camera, 1280, 720, Path.Combine(directory, $"{prefix}_Bridge.png"));
            });
        }

        private static void CameraPose(Camera camera, Vector3 position, float pitch)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, 0f, 0f));
        }

        private static void SetRendering(GameObject group, bool visible)
        {
            if (group == null) return;
            foreach (var renderer in group.GetComponentsInChildren<Renderer>())
            {
                renderer.forceRenderingOff = !visible;
            }
        }

        private static void WithCamera(Action<Camera> action)
        {
            var cameraObject = new GameObject("CaptureCamera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.62f, 0.7f, 0.76f);
                camera.farClipPlane = 1000f;
                camera.fieldOfView = 50f;
                action(camera);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
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
