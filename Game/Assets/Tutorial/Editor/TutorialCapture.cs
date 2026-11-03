using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.EditorTools
{
    /// <summary>
    /// 教学关截图：全关俯视（布局检查，环境光调亮）与出生点的游戏视角。编辑模式下所有引路灯都按“点亮”显示。
    /// 输出目录取环境变量 TUTORIAL_CAPTURE_DIR，未设置时写到工程上一级的 Docs/Tutorial。
    /// </summary>
    public static class TutorialCapture
    {
        [MenuItem("Wilderness/Tutorial/Capture Screenshots")]
        public static void Capture()
        {
            var directory = Environment.GetEnvironmentVariable("TUTORIAL_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Tutorial"));
            }

            Directory.CreateDirectory(directory);
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                EditorSceneManager.OpenScene(TutorialBuilder.ScenePath);
                var cameraObject = new GameObject("CaptureCamera");
                try
                {
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
                    camera.farClipPlane = 400f;

                    // 游戏视角：出生点，夜里。
                    camera.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
                    camera.transform.SetPositionAndRotation(TutorialBuilder.Spawn + Vector3.up * FixedObliqueCamera.DefaultFollowHeight + FixedObliqueCamera.DefaultOffset,
                        Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
                    Render(camera, 1600, 900, Path.Combine(directory, "Tutorial_Start.png"));

                    // 全关俯视：调亮环境光看清车道、阻隔和点位。
                    var ambient = RenderSettings.ambientLight;
                    RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
                    camera.fieldOfView = 40f;
                    camera.transform.SetPositionAndRotation(new Vector3(33f, 120f, -12f), Quaternion.Euler(90f, 0f, 0f));
                    Render(camera, 1800, 1000, Path.Combine(directory, "Tutorial_TopDown.png"));
                    RenderSettings.ambientLight = ambient;
                }
                finally
                {
                    Object.DestroyImmediate(cameraObject);
                }

                Debug.Log($"[Tutorial] 截图已写入 {directory}");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                EditorSceneManager.OpenScene(TutorialBuilder.ScenePath);
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
