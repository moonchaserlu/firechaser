using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 火的效果截图：黑夜下用游戏镜头拍庇护所炉火、玩家建的火堆和几处火把，玩家站在旁边作比例尺。
    /// 输出目录取环境变量 MAP01_CAPTURE_DIR，未设置时写到工程上一级的 Docs/Screenshots。
    /// </summary>
    public static class Map01FireCapture
    {
        [MenuItem("Wilderness/Map01/Capture Fire Screenshots")]
        public static void Capture()
        {
            var directory = Environment.GetEnvironmentVariable("MAP01_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Screenshots"));
            }

            Directory.CreateDirectory(directory);
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            GameObject cameraObject = null;
            try
            {
                // 先开场景再建相机：开场景会销毁当前场景里的对象。
                EditorSceneManager.OpenScene(Map01Builder.ScenePath);
                cameraObject = new GameObject("FireCaptureCamera");
                var blueprint = GameObject.Find("Map01_Root/BlueprintReference");
                if (blueprint != null) blueprint.SetActive(false);
                Object.FindAnyObjectByType<Map01Daylight>()?.BeginNight();

                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
                camera.farClipPlane = 400f;
                camera.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
                camera.nearClipPlane = FixedObliqueCamera.DefaultNearClip;
                var player = Object.FindAnyObjectByType<PlayerCharacter>();

                // 庇护所炉火：屋顶按进屋时的样子隐藏。
                var roof = GameObject.Find("Map01_Root/Buildings_Static/map01_safehouse/RoofRevealGroup");
                foreach (var renderer in roof.GetComponentsInChildren<Renderer>()) renderer.forceRenderingOff = true;
                Shot(camera, player, GameObject.Find("Hearth_Safehouse").transform.position, new Vector3(1.6f, 0f, -0.6f), directory, "Fire_Hearth");
                foreach (var renderer in roof.GetComponentsInChildren<Renderer>()) renderer.forceRenderingOff = false;

                // 玩家建的火堆：Q08 的桥北头空地。
                var site = new Vector3(27f, 0f, 118f);
                site.y = Terrain.activeTerrain.SampleHeight(site) + Terrain.activeTerrain.transform.position.y;
                var fire = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Campfire.prefab"));
                fire.transform.position = site;
                Shot(camera, player, site, new Vector3(1.6f, 0f, -0.6f), directory, "Fire_PlayerCampfire");
                Object.DestroyImmediate(fire);

                foreach (var torch in new[] { "Torch_BridgeSouth_W", "Torch_ForestEdge", "Torch_SafehouseDoor_W" })
                {
                    Shot(camera, player, GameObject.Find(torch).transform.position, new Vector3(1.4f, 0f, -0.8f), directory, $"Fire_{torch}");
                }

                Debug.Log($"[Map01] 火的截图已写入 {directory}");
            }
            finally
            {
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                EditorSceneManager.OpenScene(Map01Builder.ScenePath);
            }
        }

        private static void Shot(Camera camera, PlayerCharacter player, Vector3 target, Vector3 playerOffset, string directory, string name)
        {
            if (player != null) player.transform.position = target + playerOffset;
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if ((system.transform.position - target).sqrMagnitude < 900f) system.Simulate(2f, true, true);
            }

            camera.transform.SetPositionAndRotation(target + Vector3.up * FixedObliqueCamera.DefaultFollowHeight + FixedObliqueCamera.DefaultOffset,
                Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
            Render(camera, 1600, 900, Path.Combine(directory, $"{name}.png"));
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
