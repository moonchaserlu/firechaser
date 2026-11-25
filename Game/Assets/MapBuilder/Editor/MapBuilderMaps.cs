using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>新建 / 列出 / 打开用搭建器做的地图。每张地图一个文件夹：Assets/Maps/&lt;名字&gt;/。</summary>
    public static class MapBuilderMaps
    {
        public const string MapsFolder = "Assets/Maps";

        public static string[] ListScenes()
        {
            if (!AssetDatabase.IsValidFolder(MapsFolder))
            {
                return Array.Empty<string>();
            }

            return AssetDatabase.FindAssets("t:Scene", new[] { MapsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        public static MapAuthoringRoot FindRoot()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var go in scene.GetRootGameObjects())
                {
                    var root = go.GetComponentInChildren<MapAuthoringRoot>(true);
                    if (root != null)
                    {
                        return root;
                    }
                }
            }

            return null;
        }

        public static string ScenePath(string name) => $"{MapsFolder}/{name}/{name}.unity";

        /// <summary>
        /// 新建一张空白地图：地面（带碰撞，可烘焙导航）、一盏看得清的编辑用平行光、地图根与“素材 / 点位”分组。
        /// 已存在同名场景时抛异常，避免覆盖。
        /// </summary>
        public static MapAuthoringRoot CreateMap(string name, Vector2 size, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("地图名不能为空，也不能包含文件名不允许的字符。");
            }

            var scenePath = ScenePath(name);
            if (save && AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
            {
                throw new InvalidOperationException("已经有同名地图：" + scenePath);
            }

            var folder = $"{MapsFolder}/{name}";
            if (save)
            {
                MapBuilderLibrary.EnsureFolder(folder);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.33f, 0.38f);

            var light = new GameObject("编辑用平行光（试玩前换成黑暗光照）").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var rootObject = new GameObject("地图根");
            var root = rootObject.AddComponent<MapAuthoringRoot>();
            root.mapId = name.ToLowerInvariant().Replace(' ', '_');
            root.worldSize = size;
            root.Group(MapAuthoringRoot.PropsGroup);
            root.Group(MapAuthoringRoot.PointsGroup);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "地面";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(size.x, 0.5f, size.y);
            ground.GetComponent<Renderer>().sharedMaterial = GroundMaterial(folder, name, save);
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);

            if (save)
            {
                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.SaveAssets();
            }

            Selection.activeGameObject = rootObject;
            return root;
        }

        private static Material GroundMaterial(string folder, string name, bool save)
        {
            var path = $"{folder}/M_{name}_Ground.mat";
            var existing = save ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
            if (existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = $"M_{name}_Ground" };
            material.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.2f));
            material.SetFloat("_Smoothness", 0.1f);
            if (save)
            {
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        public static bool Open(string scenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            EditorSceneManager.OpenScene(scenePath);
            return true;
        }
    }
}
