using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>
    /// 素材面板的数据：每个分组对应 Project 里的一个文件夹，收录其中的预制体和模型（fbx 等）。
    /// 分组可以带材质覆盖和缩放，用来处理 KayKit 这种只导网格、需要统一贴图和比例的素材。
    /// </summary>
    public sealed class MapBuilderLibrary : ScriptableObject
    {
        public const string AssetPath = "Assets/MapBuilder/Config/MapBuilderLibrary.asset";

        [Serializable]
        public sealed class Group
        {
            public string name;
            public string folderGuid;
            public string lastPath;
            [Tooltip("放置时统一换成这个材质；留空保留素材自带材质。")]
            public Material materialOverride;
            [Min(0.01f)] public float scale = 1f;
            [Tooltip("每次放置随机朝向（适合树、石头、草）。")]
            public bool randomYaw;
            [Range(0f, 0.5f), Tooltip("每次放置随机大小的幅度，0.2 = ±20%。")]
            public float randomScale;
            public List<GameObject> assets = new List<GameObject>();
        }

        public List<Group> groups = new List<Group>();

        public static MapBuilderLibrary LoadOrCreate()
        {
            var library = AssetDatabase.LoadAssetAtPath<MapBuilderLibrary>(AssetPath);
            if (library != null)
            {
                return library;
            }

            library = CreateInstance<MapBuilderLibrary>();
            EnsureFolder("Assets/MapBuilder/Config");
            AssetDatabase.CreateAsset(library, AssetPath);
            SeedDefaults(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>首次创建时预置项目里已有的 KayKit 素材和玩法物件。</summary>
        private static void SeedDefaults(MapBuilderLibrary library)
        {
            const string kayKit = "Assets/ThirdPartyAssets/itch.io/KayLousberg";
            library.ImportFolder(kayKit + "/ForestNaturePack/1.0_SOURCE/Models/Color2", "森林（KayKit）",
                "Assets/Map01/Art/Materials/M_KK_Forest.mat", 1.5f, true, 0.15f);
            library.ImportFolder(kayKit + "/DungeonPack/1.1_SOURCE/Models", "地牢建筑（KayKit）",
                "Assets/Map01/Art/Materials/M_KK_Dungeon.mat", 0.8f, false, 0f);
            library.ImportFolder(kayKit + "/FurnitureBits/1.0_SOURCE/Models", "家具（KayKit）",
                "Assets/Map01/Art/Materials/M_KK_Furniture.mat", 0.8f, false, 0f);
            library.ImportFolder("Assets/Game/Prefabs", "玩法物件", null, 1f, false, 0f);
        }

        public Group ImportFolder(string path, string displayName = null, string materialPath = null,
            float scale = 1f, bool randomYaw = false, float randomScale = 0f)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                return null;
            }

            var guid = AssetDatabase.AssetPathToGUID(path);
            var group = groups.Find(g => g.folderGuid == guid);
            if (group == null)
            {
                group = new Group
                {
                    folderGuid = guid,
                    name = string.IsNullOrEmpty(displayName) ? System.IO.Path.GetFileName(path) : displayName,
                    scale = scale,
                    randomYaw = randomYaw,
                    randomScale = randomScale,
                    materialOverride = string.IsNullOrEmpty(materialPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(materialPath)
                };
                groups.Add(group);
            }

            group.lastPath = path;
            group.assets = FindPlaceable(path);
            EditorUtility.SetDirty(this);
            return group;
        }

        public void Refresh(Group group)
        {
            var path = AssetDatabase.GUIDToAssetPath(group.folderGuid);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
            {
                group.lastPath = path;
                group.assets = FindPlaceable(path);
                EditorUtility.SetDirty(this);
            }
        }

        /// <summary>文件夹（含子文件夹）中的预制体和模型，按路径排序。</summary>
        public static List<GameObject> FindPlaceable(string folder)
        {
            return AssetDatabase.FindAssets("t:Prefab t:Model", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(go => go != null && go.GetComponentsInChildren<Renderer>(true).Length > 0)
                .ToList();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
