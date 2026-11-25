using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WildernessExtraction.Map01;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>
    /// 演示与截图：新建空白 Map02（已存在则跳过），另建一张 MapBuilderDemo，用搭建器的同一套放置接口摆一座小屋、
    /// 一片树林和几个点位，并拍斜俯视 / 影子预览截图到 Docs/MapBuilder。
    /// 命令行：-executeMethod WildernessExtraction.MapBuilder.EditorTools.MapBuilderShowcase.Run
    /// </summary>
    public static class MapBuilderShowcase
    {
        private const string DemoName = "MapBuilderDemo";
        private const string KayKit = "Assets/ThirdPartyAssets/itch.io/KayLousberg";

        [MenuItem("Wilderness/地图搭建器工具/生成演示地图并截图")]
        public static void Run()
        {
            var library = MapBuilderLibrary.LoadOrCreate();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MapBuilderMaps.ScenePath("Map02")) == null)
            {
                MapBuilderMaps.CreateMap("Map02", new Vector2(160f, 160f));
            }

            var demoPath = MapBuilderMaps.ScenePath(DemoName);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(demoPath) != null)
            {
                AssetDatabase.DeleteAsset(Path.GetDirectoryName(demoPath).Replace('\\', '/'));
            }

            var root = MapBuilderMaps.CreateMap(DemoName, new Vector2(60f, 60f));
            var dungeon = library.groups.First(g => g.name.StartsWith("地牢"));
            var furniture = library.groups.First(g => g.name.StartsWith("家具"));
            var forest = library.groups.First(g => g.name.StartsWith("森林"));

            // 小屋：0.8 倍后墙段 3.2 米，按墙段长度围一圈。
            const float s = 3.2f;
            for (var i = -1; i <= 1; i++)
            {
                if (i != 0) Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/wall", new Vector3(i * s, 0f, -1.5f * s), 0f); // 正面中间留门洞
                Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/" + (i == 1 ? "wall_window_open" : "wall"), new Vector3(i * s, 0f, 1.5f * s), 180f);
                Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/" + (i == -1 ? "wall_broken" : "wall"), new Vector3(-1.5f * s, 0f, i * s), 90f);
                Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/wall", new Vector3(1.5f * s, 0f, i * s), -90f);
                for (var j = -1; j <= 1; j++)
                {
                    Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/floor_tile_large", new Vector3(i * s, 0f, j * s), 0f, collider: false);
                }
            }

            Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/barrel_large", new Vector3(3f, 0.1f, 3f), 0f);
            Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/box_large", new Vector3(-3f, 0.1f, 3f), 20f);
            Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/table_medium", new Vector3(0f, 0.1f, 1f), 0f);
            Put(root, dungeon, "DungeonPack/1.1_SOURCE/Models/rubble_large", new Vector3(-12f, 0f, -9f), 30f);
            Put(root, furniture, "FurnitureBits/1.0_SOURCE/Models/armchair", new Vector3(-2.5f, 0.1f, -1.5f), 45f);

            var random = new System.Random(7);
            var trees = forest.assets.Where(a => a.name.StartsWith("Tree_") || a.name.StartsWith("Rock_") || a.name.StartsWith("Bush_")).ToList();
            for (var n = 0; n < 40; n++)
            {
                var angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
                var distance = 11f + (float)random.NextDouble() * 16f;
                var position = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                if (Mathf.Abs(position.x) < 4f && position.z < 0f) continue; // 门前留一条路
                var asset = trees[random.Next(trees.Count)];
                MapBuilderPlacement.Place(root, new MapBuilderPlacement.Request
                {
                    asset = asset, group = forest, position = position, rotation = Quaternion.Euler(0f, random.Next(24) * 15f, 0f),
                    scaleMultiplier = forest.scale * (1f + ((float)random.NextDouble() * 2f - 1f) * forest.randomScale), addColliders = true
                });
            }

            MapBuilderPoints.Place(root, SpawnPointType.PlayerSpawn, new Vector3(0f, 0f, -22f), 1.5f, null);
            MapBuilderPoints.Place(root, SpawnPointType.Loot, new Vector3(3f, 0.1f, 1.5f), 1.5f, new[] { "indoor" });
            MapBuilderPoints.Place(root, SpawnPointType.Loot, new Vector3(-3f, 0.1f, 1.5f), 1.5f, new[] { "indoor" });
            MapBuilderPoints.Place(root, SpawnPointType.Loot, new Vector3(-12f, 0f, -5f), 1.5f, null);
            MapBuilderPoints.Place(root, SpawnPointType.Monster, new Vector3(14f, 0f, 6f), 4f, null);
            MapBuilderPoints.Place(root, SpawnPointType.ExtractionCandidate, new Vector3(0f, 0f, 24f), 3f, null);

            MapBuilderPoints.BakeNavMesh(root);
            MapBuilderPoints.Export(root, MapBuilderPoints.DefinitionPath(root), out var report);
            Debug.Log(report);
            var issues = MapBuilderChecks.Run(root);
            Debug.Log($"[地图搭建器] 演示地图检查：{issues.Count} 个问题\n" + string.Join("\n", issues.Select(i => i.Message)));
            EditorSceneManager.SaveScene(root.gameObject.scene);

            Capture(root);
            AssetDatabase.SaveAssets();
        }

        private static void Put(MapAuthoringRoot root, MapBuilderLibrary.Group group, string model, Vector3 position, float yaw, bool collider = true)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{KayKit}/{model}.fbx");
            if (asset == null)
            {
                Debug.LogWarning("[地图搭建器] 缺少演示素材 " + model);
                return;
            }

            MapBuilderPlacement.Place(root, new MapBuilderPlacement.Request
            {
                asset = asset, group = group, position = position, rotation = Quaternion.Euler(0f, yaw, 0f),
                scaleMultiplier = group.scale, addColliders = collider
            });
        }

        private static void Capture(MapAuthoringRoot root)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/MapBuilder"));
            Directory.CreateDirectory(folder);
            var cameraObject = new GameObject("截图相机") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            try
            {
                camera.orthographic = false;
                camera.fieldOfView = 40f;
                var rotation = Quaternion.Euler(55f, 0f, 0f);
                cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -2f) - rotation * Vector3.forward * 34f, rotation);
                Save(camera, Path.Combine(folder, "MapBuilder_Demo_Oblique.png"));

                // 影子预览：右边绿色（可放），门口里红色（和桌子重叠）。
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{KayKit}/DungeonPack/1.1_SOURCE/Models/barrel_large.fbx");
                var ok = Ghost(asset, new Vector3(7.5f, 0f, -6.5f), MapBuilderWindow.GhostMaterial(new Color(0.2f, 0.85f, 0.4f, 0.45f)));
                var bad = Ghost(asset, new Vector3(0.4f, 0.1f, 1.2f), MapBuilderWindow.GhostMaterial(new Color(0.95f, 0.25f, 0.2f, 0.45f)));
                rotation = Quaternion.Euler(50f, -20f, 0f);
                cameraObject.transform.SetPositionAndRotation(new Vector3(3f, 0f, -1f) - rotation * Vector3.forward * 20f, rotation);
                Save(camera, Path.Combine(folder, "MapBuilder_Demo_Ghost.png"));
                Object.DestroyImmediate(ok);
                Object.DestroyImmediate(bad);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }

            Debug.Log("[地图搭建器] 截图已写入 " + folder);
        }

        private static GameObject Ghost(GameObject asset, Vector3 position, Material material)
        {
            var go = Object.Instantiate(asset);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale *= 0.8f;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();
            }

            return go;
        }

        private static void Save(Camera camera, string path)
        {
            const int width = 1600, height = 1000;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 4;
            camera.targetTexture = target;
            camera.aspect = (float)width / height;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
