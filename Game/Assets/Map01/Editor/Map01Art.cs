using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 阶段 2 素材层：KayKit 模型、项目自有的降饱和色板材质，以及按名称取模型的工具。
    /// 只生成视觉对象；碰撞、导航和点位仍由白盒几何负责，便于回退。
    /// </summary>
    public sealed class Map01Art
    {
        public const string ArtFolder = "Assets/Map01/Art";
        private const string TexturesFolder = ArtFolder + "/Textures";
        private const string MaterialsFolder = ArtFolder + "/Materials";

        /// <summary>森林健康色（Color2 深绿）、枯黄（Color5）、地牢、家具。</summary>
        public Material Forest { get; private set; }
        public Material Dungeon { get; private set; }
        public Material Furniture { get; private set; }
        public Material Contamination { get; private set; }
        /// <summary>自制工业建筑的混凝土地面。</summary>
        public Material Concrete { get; private set; }

        private readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

        public static Map01Art Load()
        {
            KayKitImport.Apply();
            EnsureFolder(ArtFolder);
            EnsureFolder(TexturesFolder);
            EnsureFolder(MaterialsFolder);
            var art = new Map01Art
            {
                // 指南 3.2：低饱和但仍有颜色。森林压得最狠，家具保留更多木色。
                Forest = GradedMaterial("Forest", KayKitImport.ForestTexture, 0.42f, 0.86f),
                Dungeon = GradedMaterial("Dungeon", KayKitImport.DungeonTexture, 0.7f, 0.95f),
                // alt_A 里的玫红布面会与污染异常色撞色，压到 0.45 只留旧木褐、灰绿、奶油白的倾向。
                Furniture = GradedMaterial("Furniture", KayKitImport.FurnitureTexture, 0.45f, 0.92f),
                // 怪物污染唯一的异常色：褪紫红，只出现在 MonsterContamination 层。
                Contamination = SolidMaterial("M_Map01_Contamination", new Color(0.62f, 0.24f, 0.46f)),
                Concrete = SolidMaterial("M_Map01_Concrete", new Color(0.5f, 0.5f, 0.48f))
            };
            return art;
        }

        public static string ForestModel(string name, bool withered)
        {
            var color = withered ? "Color5" : "Color2";
            return $"{KayKitImport.ForestModels}/{color}/{name}_{color}.fbx";
        }

        public static string DungeonModel(string name) => $"{KayKitImport.DungeonModels}/{name}.fbx";
        public static string FurnitureModel(string name) => $"{KayKitImport.FurnitureModels}/{name}.fbx";

        /// <summary>实例化模型（只有渲染，没有碰撞），底面落在 localPosition 上方 baseY 处。</summary>
        public GameObject Place(string modelPath, Material material, Transform parent, Vector3 localPosition, float yaw,
            float scale = 1f, float baseY = 0f)
        {
            var model = Model(modelPath);
            if (model == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * scale * PackScale(modelPath);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (var i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }

            foreach (var collider in instance.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(collider);
            }

            // 让模型底面贴地：按世界包围盒修正高度。
            var bounds = WorldBounds(instance);
            var targetBase = parent.TransformPoint(localPosition + Vector3.up * baseY).y;
            instance.transform.position += Vector3.up * (targetBase - bounds.min.y);
            GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic);
            return instance;
        }

        /// <summary>沿矩形长边平铺模型（货架、桌子一类），返回放置数量。</summary>
        public int FillRow(string modelPath, Material material, Transform parent, LocalRect rect, float scale = 1f)
        {
            var probe = Place(modelPath, material, parent, Vector3.zero, 0f, scale);
            if (probe == null)
            {
                return 0;
            }

            var size = WorldBounds(probe).size;
            Object.DestroyImmediate(probe);
            var alongX = rect.Size.x >= rect.Size.y;
            var length = alongX ? rect.Size.x : rect.Size.y;
            // 模型长边对齐矩形长边。
            var modelLong = Mathf.Max(size.x, size.z);
            var yaw = (size.x >= size.z) == alongX ? 0f : 90f;
            var count = Mathf.Max(1, Mathf.FloorToInt(length / Mathf.Max(modelLong, 0.5f)));
            var step = length / count;
            for (var i = 0; i < count; i++)
            {
                var offset = -length * 0.5f + step * (i + 0.5f);
                var local = alongX ? new Vector3(rect.Center.x + offset, 0f, rect.Center.y) : new Vector3(rect.Center.x, 0f, rect.Center.y + offset);
                Place(modelPath, material, parent, local, yaw, scale);
            }

            return count;
        }

        /// <summary>用地板模块铺满矩形，模块顶面对齐 topY。</summary>
        public void TileFloor(string modelPath, Material material, Transform parent, Vector2 center, Vector2 size, float topY)
        {
            var probe = Place(modelPath, material, parent, Vector3.zero, 0f);
            if (probe == null)
            {
                return;
            }

            var tile = WorldBounds(probe).size;
            Object.DestroyImmediate(probe);
            var nx = Mathf.Max(1, Mathf.CeilToInt(size.x / tile.x));
            var nz = Mathf.Max(1, Mathf.CeilToInt(size.y / tile.z));
            // 轻微缩放让瓦片正好铺满房间，不超出墙外。
            var sx = size.x / (nx * tile.x);
            var sz = size.y / (nz * tile.z);
            for (var x = 0; x < nx; x++)
            {
                for (var z = 0; z < nz; z++)
                {
                    var local = new Vector3(center.x - size.x * 0.5f + (x + 0.5f) * tile.x * sx, 0f, center.y - size.y * 0.5f + (z + 0.5f) * tile.z * sz);
                    var piece = Place(modelPath, material, parent, local, 0f, 1f, topY - tile.y);
                    if (piece != null)
                    {
                        piece.transform.localScale = Vector3.Scale(piece.transform.localScale, new Vector3(sx, 1f, sz));
                    }
                }
            }
        }

        /// <summary>
        /// 包级比例校准（见 KayKitBounds.txt）：Dungeon / Furniture 以约 1.5 倍真实尺寸建模（单人床 3m、桌高 1m），
        /// 统一乘 0.8 后约为真实尺寸的 1.2 倍，落在指南允许的 10%–20% 放大范围内；Forest 灌木与草偏小，乘 1.8。
        /// 树另按目标高度适配，不走这里。
        /// </summary>
        public static float PackScale(string modelPath)
        {
            if (modelPath.StartsWith(KayKitImport.DungeonModels) || modelPath.StartsWith(KayKitImport.FurnitureModels))
            {
                return 0.8f;
            }

            if (modelPath.StartsWith(KayKitImport.ForestModels) && !modelPath.Contains("/Tree_"))
            {
                return 1.8f;
            }

            return 1f;
        }

        public static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.zero);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private GameObject Model(string path)
        {
            if (!models.TryGetValue(path, out var model))
            {
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    Debug.LogWarning($"[Map01] 缺少模型 {path}");
                }

                models[path] = model;
            }

            return model;
        }

        // ---------- 色板 ----------

        private static Material GradedMaterial(string name, string sourcePath, float saturation, float value)
        {
            var texture = GradedTexture(name, sourcePath, saturation, value);
            var path = $"{MaterialsFolder}/M_KK_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D GradedTexture(string name, string sourcePath, float saturation, float value)
        {
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException($"缺少 KayKit 贴图 {sourcePath}");
            }

            var pixels = source.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out var h, out var s, out var v);
                var graded = Color.HSVToRGB(h, s * saturation, v * value);
                graded.a = pixels[i].a;
                pixels[i] = graded;
            }

            var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            output.SetPixels(pixels);
            output.Apply();
            var path = $"{TexturesFolder}/T_KK_{name}_Graded.png";
            File.WriteAllBytes(path, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material SolidMaterial(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Smoothness", 0.35f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
