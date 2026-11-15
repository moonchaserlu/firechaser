using System.IO;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 把 Assets/Game/Art/Icons/Icon_&lt;itemId&gt;.png 绑定到对应物品，并统一贴图导入设置。
    /// 新图标按文件名放进目录后再跑一次即可；没有对应文件的物品保持原样（界面退回颜色块加文字）。
    /// </summary>
    public static class ItemIconSetup
    {
        private const string IconsFolder = "Assets/Game/Art/Icons";

        [MenuItem("Wilderness/Bind Item Icons")]
        public static void Bind()
        {
            var bound = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                var iconPath = $"{IconsFolder}/Icon_{item.itemId}.png";
                if (!File.Exists(iconPath)) continue;

                ConfigureImporter(iconPath);
                item.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                EditorUtility.SetDirty(item);
                bound++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ItemIconSetup] 绑定 {bound} 个物品图标");
        }

        private static void ConfigureImporter(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
