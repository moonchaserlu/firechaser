using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// KayKit 原始素材的统一导入设置：只导网格，不导材质、动画、相机与灯光；1 单位 = 1 米。
    /// 原始素材保持只读，改色、碰撞与预制体全部放在 Assets/Map01/Art 下。
    /// </summary>
    public sealed class KayKitImport : AssetPostprocessor
    {
        public const string Root = "Assets/ThirdPartyAssets/itch.io/KayLousberg";
        public const string ForestModels = Root + "/ForestNaturePack/1.0_SOURCE/Models";
        public const string DungeonModels = Root + "/DungeonPack/1.1_SOURCE/Models";
        public const string FurnitureModels = Root + "/FurnitureBits/1.0_SOURCE/Models";
        public const string ForestTexture = Root + "/ForestNaturePack/1.0_SOURCE/Textures/forest_texture.png";
        public const string DungeonTexture = Root + "/DungeonPack/1.1_SOURCE/Textures/dungeon_texture.png";
        public const string FurnitureTexture = Root + "/FurnitureBits/1.0_SOURCE/Textures/furniturebits_texture_alt_A.png";

        private void OnPreprocessModel()
        {
            if (assetPath.StartsWith(Root))
            {
                Configure((ModelImporter)assetImporter);
            }
        }

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(Root))
            {
                Configure((TextureImporter)assetImporter);
            }
        }

        private static bool Configure(ModelImporter importer)
        {
            var changed = importer.materialImportMode != ModelImporterMaterialImportMode.None
                          || importer.importAnimation || importer.importCameras || importer.importLights
                          || !Mathf.Approximately(importer.globalScale, 1f) || importer.addCollider || importer.isReadable;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.addCollider = false;
            importer.isReadable = false;
            return changed;
        }

        private static bool Configure(TextureImporter importer)
        {
            // 渐变色图集：可读（用于生成项目自有的降饱和版本），不压缩，避免色带。
            var changed = !importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            return changed;
        }

        /// <summary>对已存在的素材补齐导入设置（素材先于本脚本进入工程时使用）。</summary>
        public static void Apply()
        {
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path);
                var changed = importer switch
                {
                    ModelImporter model => Configure(model),
                    TextureImporter texture => Configure(texture),
                    _ => false
                };
                if (changed)
                {
                    importer.SaveAndReimport();
                    count++;
                }
            }

            Debug.Log($"[Map01] KayKit 导入设置已更新 {count} 个文件。");
        }

        /// <summary>输出每个模型的包围盒，用于核对比例（写入 Assets/Map01/Art/KayKitBounds.txt）。</summary>
        public static void ReportBounds()
        {
            Apply();
            var lines = new List<string>();
            foreach (var folder in new[] { ForestModels, DungeonModels, FurnitureModels })
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var bounds = new Bounds();
                    var first = true;
                    foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = filter.sharedMesh;
                        if (mesh == null) continue;
                        var b = TransformBounds(filter.transform, model.transform, mesh.bounds);
                        if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
                    }

                    lines.Add($"{Path.GetFileNameWithoutExtension(path)}\tsize={bounds.size.x:F2},{bounds.size.y:F2},{bounds.size.z:F2}\tmin={bounds.min.x:F2},{bounds.min.y:F2},{bounds.min.z:F2}");
                }
            }

            Directory.CreateDirectory("Assets/Map01/Art");
            File.WriteAllLines("Assets/Map01/Art/KayKitBounds.txt", lines, new UTF8Encoding(false));
            Debug.Log($"[Map01] 已记录 {lines.Count} 个模型包围盒。");
        }

        private static Bounds TransformBounds(Transform child, Transform root, Bounds local)
        {
            var matrix = root.worldToLocalMatrix * child.localToWorldMatrix;
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }

            return result;
        }
    }
}
