using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 19 件物品模型的 Unity 交付：FBX 导入设置、共享 URP 材质、展示预制体、独立展示场景和导入检查。
    /// 模型由 ArtSource/Items 的 Blender 脚本生成；本工具可重复运行，预制体与材质原地更新（GUID 不变）。
    /// 不改 ItemDefinition、不加游戏逻辑、不把展示场景加入 Build Settings。
    /// 批处理：-executeMethod WildernessExtraction.EditorTools.ItemModelSetup.Setup
    ///         -executeMethod WildernessExtraction.EditorTools.ItemModelSetup.Capture -itemShots &lt;目录&gt;（截图单独跑）
    /// </summary>
    public static class ItemModelSetup
    {
        private const string Root = "Assets/Game/Art/Items";
        private const string ModelFolder = Root + "/Models";
        private const string MaterialFolder = Root + "/Materials";
        private const string TextureFolder = Root + "/Textures";
        private const string PrefabFolder = Root + "/Prefabs";
        private const string PreviewFolder = Root + "/Preview";
        private const string ScenePath = PreviewFolder + "/ItemShowcase.unity";
        private const string PalettePath = TextureFolder + "/T_Item_Palette.png";
        private const string ReportPath = "../Docs/Art/ItemModels_QA/unity_validation.json";

        /// <summary>展示分组：组名、物品。</summary>
        private static readonly (string group, string[] items)[] Groups =
        {
            ("电池", new[] { "old_battery", "empty_battery", "charged_battery" }),
            ("弹药", new[] { "ammo_9mm", "ammo_12g", "ammo_556" }),
            ("材料", new[] { "scrap_metal", "mechanical_parts", "cloth", "chemicals" }),
            ("消耗品", new[] { "fuel_pack", "dirty_bandage", "first_aid_spray", "canned_food" }),
            ("贵重品", new[] { "encrypted_drive", "intact_watch", "identity_tag" }),
            ("武器", new[] { "old_pistol", "short_knife" }),
        };

        private static IEnumerable<string> AllItems => Groups.SelectMany(g => g.items);

        [MenuItem("Wilderness/Items/Setup Item Models")]
        public static void Setup()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(PreviewFolder);
            ConfigurePalette();
            var materials = CreateMaterials();
            foreach (var item in AllItems)
            {
                if (File.Exists($"{ModelFolder}/Item_{item}.fbx")) ConfigureModel(item, materials);
            }

            foreach (var item in AllItems)
            {
                if (File.Exists($"{ModelFolder}/Item_{item}.fbx")) BuildPrefab(item);
            }

            // 重建展示场景要切换场景：先记下当前打开的场景，建完再还原；有未保存的场景就跳过，不替主创做保存决定。
            var dirty = Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
            if (dirty)
            {
                Debug.LogWarning("[ItemModelSetup] 有未保存的场景，跳过重建 ItemShowcase.unity（预制体已更新，场景里的实例会跟着变）。");
            }
            else
            {
                var previous = EditorSceneManager.GetSceneManagerSetup();
                BuildShowcase();
                if (previous.Any(setup => !string.IsNullOrEmpty(setup.path))) EditorSceneManager.RestoreSceneManagerSetup(previous);
            }

            AssetDatabase.SaveAssets();
            Validate();
        }

        // ---------- 贴图与材质 ----------

        private static void ConfigurePalette()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(PalettePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            var result = new Dictionary<string, Material>
            {
                ["M_Item_Palette"] = Lit("M_Item_Palette", palette, Color.white, 0f, 0.15f),
                ["M_Item_Metal"] = Lit("M_Item_Metal", palette, Color.white, 0.5f, 0.4f),
                ["M_Item_GlowGreen"] = Glow("M_Item_GlowGreen", new Color(0.36f, 1f, 0.42f), 1.0f),
                ["M_Item_GlowCyan"] = Glow("M_Item_GlowCyan", new Color(0.31f, 0.89f, 0.88f), 0.8f),
            };
            return result;
        }

        private static Material Lit(string name, Texture2D baseMap, Color color, float metallic, float smoothness)
        {
            var mat = LoadOrCreate(name);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material Glow(string name, Color color, float intensity)
        {
            var mat = LoadOrCreate(name);
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * intensity);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material LoadOrCreate(string name)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ---------- 模型与预制体 ----------

        private static void ConfigureModel(string item, Dictionary<string, Material> materials)
        {
            var path = $"{ModelFolder}/Item_{item}.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var pair in materials)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            }

            importer.SaveAndReimport();
        }

        private static void BuildPrefab(string item)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/Item_{item}.fbx");
            var root = new GameObject($"Item_{item}");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Item_{item}.prefab");
            Object.DestroyImmediate(root);
        }

        // ---------- 展示场景 ----------

        private static void BuildShowcase()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.29f, 0.26f);
            PopulateShowcase(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>按组摆放 19 个预制体、灯光、地面和相机；也用于截图时的临时预览场景。</summary>
        private static Camera PopulateShowcase(Scene scene)
        {
            var sun = new GameObject("Key Light").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.7f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 215f, 0f); // 从正面（+Z）左上方打过来

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.name = "Ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(0.22f, 1f, 0.24f);
            ground.transform.position = new Vector3(0.45f, 0f, 0.75f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();

            for (var row = 0; row < Groups.Length; row++)
            {
                var (name, items) = Groups[row];
                var group = new GameObject($"Group_{name}");
                SceneManager.MoveGameObjectToScene(group, scene);
                group.transform.position = new Vector3(0f, 0f, row * 0.3f);
                for (var i = 0; i < items.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Item_{items[i]}.prefab");
                    if (prefab == null) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    instance.transform.localPosition = new Vector3(i * 0.3f, 0f, 0f);
                    // 武器侧躺在地上（只改场景里实例的摆放，不改预制体）。
                    if (name == "武器") instance.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
                    SitOnGround(instance);
                }
            }

            var camera = new GameObject("Showcase Camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0x16 / 255f, 0x12 / 255f, 0x0F / 255f);
            // 物品正面朝 +Z，相机放在 +Z 一侧往回看。
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.05f;
            camera.transform.position = new Vector3(0.45f, 2.1f, 2.45f);
            camera.transform.rotation = Quaternion.Euler(55f, 180f, 0f);
            return camera;
        }

        private static void SitOnGround(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            instance.transform.position += Vector3.up * -bounds.min.y;
        }

        private static Material GroundMaterial()
        {
            var mat = LoadOrCreate("M_Item_ShowcaseGround");
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", new Color(0.2f, 0.18f, 0.16f));
            mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- 检查 ----------

        [MenuItem("Wilderness/Items/Validate Item Models")]
        public static void Validate()
        {
            var json = new StringBuilder("[\n");
            var first = true;
            // 在不保存的预览场景里实例化，不碰当前打开的场景。
            var preview = EditorSceneManager.NewPreviewScene();
            foreach (var item in AllItems)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Item_{item}.prefab");
                var problems = new List<string>();
                var triangles = 0;
                var slots = new HashSet<string>();
                var size = Vector3.zero;
                if (prefab == null)
                {
                    problems.Add("缺少预制体");
                }
                else
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    var root = instance.transform;
                    if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity) problems.Add("根节点有缩放或旋转");
                    var visual = root.Find("Visual");
                    if (visual == null) problems.Add("缺少 Visual 子节点");
                    else if (visual.localScale != Vector3.one || Quaternion.Angle(visual.localRotation, Quaternion.identity) > 0.01f) problems.Add($"Visual 有补救性缩放或旋转（{visual.localEulerAngles}, {visual.localScale}）");
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(instance) > 0) problems.Add("Missing Script");
                    var renderers = instance.GetComponentsInChildren<MeshRenderer>();
                    Bounds? bounds = null;
                    foreach (var renderer in renderers)
                    {
                        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        if (mesh == null) { problems.Add($"{renderer.name} 缺网格"); continue; }
                        triangles += mesh.triangles.Length / 3;
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader") problems.Add($"{renderer.name} 材质丢失或粉色");
                            else slots.Add(mat.name);
                        }

                        if (renderer.sharedMaterials.Length != mesh.subMeshCount) problems.Add($"{renderer.name} 材质槽数与子网格不符");
                        if (bounds == null) bounds = renderer.bounds;
                        else { var b = bounds.Value; b.Encapsulate(renderer.bounds); bounds = b; }
                    }

                    if (bounds != null)
                    {
                        size = bounds.Value.size;
                        if (Mathf.Abs(bounds.Value.min.y) > 0.002f) problems.Add($"底部不在原点（min.y={bounds.Value.min.y:F4}）");
                    }

                    if ((item == "old_pistol" || item == "short_knife") && instance.GetComponentsInChildren<Transform>().All(t => t.name != "GripAnchor")) problems.Add("缺少 GripAnchor");
                    if (item == "intact_watch" && instance.GetComponentsInChildren<Transform>().All(t => t.name != "Lid")) problems.Add("缺少 Lid 子节点");
                    if (item == "charged_battery") CheckFront(instance, "M_Item_GlowGreen", problems);
                    if (item == "encrypted_drive") CheckFront(instance, "M_Item_GlowCyan", problems);
                    Object.DestroyImmediate(instance);
                }

                if (!first) json.Append(",\n");
                first = false;
                json.Append($"  {{\"itemId\": \"{item}\", \"prefab\": \"{PrefabFolder}/Item_{item}.prefab\", \"triangles\": {triangles}, " +
                            $"\"materials\": [{string.Join(", ", slots.OrderBy(s => s).Select(s => $"\"{s}\""))}], " +
                            $"\"size_xyz_m\": [{size.x:F4}, {size.y:F4}, {size.z:F4}], \"ok\": {(problems.Count == 0 ? "true" : "false")}, " +
                            $"\"problems\": [{string.Join(", ", problems.Select(p => $"\"{p}\""))}]}}");
                Debug.Log($"[ItemModelSetup] {item}: {(problems.Count == 0 ? "OK" : string.Join("; ", problems))} tris={triangles}");
            }

            EditorSceneManager.ClosePreviewScene(preview);
            json.Append("\n]\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)));
            File.WriteAllText(Path.GetFullPath(ReportPath), json.ToString(), new UTF8Encoding(false));
        }

        /// <summary>发光部件应在正面（Unity +Z）：用来确认导出轴向没有转反。</summary>
        private static void CheckFront(GameObject instance, string material, List<string> problems)
        {
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    if (renderer.sharedMaterials[i] == null || renderer.sharedMaterials[i].name != material) continue;
                    var bounds = mesh.GetSubMesh(i).bounds;
                    var front = renderer.transform.TransformPoint(bounds.center);
                    if (front.z <= 0f) problems.Add($"{material} 不在 +Z 正面（z={front.z:F3}）");
                    return;
                }
            }

            problems.Add($"没找到 {material}");
        }

        // ---------- 截图 ----------

        [MenuItem("Wilderness/Items/Capture Item Showcase")]
        public static void Capture()
        {
            var args = System.Environment.GetCommandLineArgs();
            var folder = "../Docs/Art/ItemModels_QA";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-itemShots") folder = args[i + 1];
            }

            Directory.CreateDirectory(folder);
            // 在预览场景里重新摆一遍展示内容来拍，不切换编辑器里打开的场景。
            var preview = EditorSceneManager.NewPreviewScene();
            // 批处理截图时 SRP Batcher 会让所有物体套同一份材质常量；临时用属性关掉（不写回 URP 资产）。
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var batcher = pipeline != null ? pipeline.GetType().GetProperty("useSRPBatcher") : null;
            var batching = batcher != null && (bool)batcher.GetValue(pipeline);
            batcher?.SetValue(pipeline, false);
            try
            {
                var camera = PopulateShowcase(preview);
                camera.scene = preview;
                Shot(camera, Path.Combine(folder, "Unity_ItemShowcase.png"));
                var home = camera.transform.position;
                var rotation = camera.transform.rotation;
                for (var row = 0; row < Groups.Length; row++)
                {
                    var width = (Groups[row].items.Length - 1) * 0.3f;
                    camera.transform.SetPositionAndRotation(new Vector3(width / 2f, 0.55f, row * 0.3f + 0.62f), Quaternion.Euler(40f, 180f, 0f));
                    camera.fieldOfView = 40f;
                    Shot(camera, Path.Combine(folder, $"Unity_Group_{row + 1}.png"));
                }

                camera.transform.SetPositionAndRotation(home, rotation);
            }
            finally
            {
                batcher?.SetValue(pipeline, batching);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void Shot(Camera camera, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = rt;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
