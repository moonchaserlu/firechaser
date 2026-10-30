using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Shelter.EditorTools
{
    /// <summary>安全屋截图：整体斜俯视 + 游戏镜头。输出目录取环境变量 SHELTER_CAPTURE_DIR，默认工程上一级 Docs/Shelter。</summary>
    public static class ShelterCapture
    {
        [MenuItem("Wilderness/Shelter/Capture Screenshots")]
        public static void Capture()
        {
            var directory = Environment.GetEnvironmentVariable("SHELTER_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Shelter"));
            Directory.CreateDirectory(directory);

            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            GameObject cameraObject = null;
            try
            {
                EditorSceneManager.OpenScene(ShelterBuilder.ScenePath);
                cameraObject = new GameObject("ShelterCaptureCamera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.farClipPlane = 300f;

                camera.fieldOfView = 30f;
                camera.nearClipPlane = 1f;
                camera.transform.SetPositionAndRotation(new Vector3(1f, 52f, -36f), Quaternion.Euler(55f, 0f, 0f));
                Render(camera, 1600, 900, Path.Combine(directory, "Shelter_Overview.png"));

                var player = Object.FindAnyObjectByType<PlayerCharacter>();
                var target = player != null ? player.transform.position : Vector3.zero;
                camera.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
                camera.nearClipPlane = FixedObliqueCamera.DefaultNearClip;
                camera.transform.SetPositionAndRotation(target + Vector3.up * FixedObliqueCamera.DefaultFollowHeight + FixedObliqueCamera.DefaultOffset,
                    Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
                Render(camera, 1600, 900, Path.Combine(directory, "Shelter_Gameplay.png"));
                Debug.Log($"[Shelter] 截图已写入 {directory}");
            }
            finally
            {
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
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
