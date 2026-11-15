using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.EditorTools
{
    /// <summary>生成主菜单场景并放到 Build Settings 第一位。以附加方式新建再关闭，不影响当前打开的场景。</summary>
    public static class MainMenuBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/MainMenu.unity";

        [MenuItem("Wilderness/Build Main Menu")]
        public static void Build()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (open.path == ScenePath)
                {
                    Debug.LogError("[MainMenuBuilder] MainMenu 场景正打开着，请先关闭再重建。");
                    return;
                }

                if (string.IsNullOrEmpty(open.path))
                {
                    Debug.LogError("[MainMenuBuilder] 当前有未保存的新场景，无法附加新建场景。请先保存或关闭它。");
                    return;
                }
            }

            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.039f, 0.027f, 0.02f);
            camera.cullingMask = 0;
            cameraObject.AddComponent<AudioListener>();
            SceneManager.MoveGameObjectToScene(cameraObject, scene);

            var menu = new GameObject("MainMenu");
            menu.AddComponent<MainMenuController>();
            SceneManager.MoveGameObjectToScene(menu, scene);

            var saved = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid()) SceneManager.SetActiveScene(active);
            if (!saved)
            {
                Debug.LogError($"[MainMenuBuilder] 保存 {ScenePath} 失败。");
                return;
            }

            RegisterFirst(ScenePath);
            Debug.Log($"[MainMenuBuilder] 已生成 {ScenePath}，并放在 Build Settings 第一位。");
        }

        private static void RegisterFirst(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            if (!scenes.Exists(s => s.path == GameScenes.Map01)) scenes.Add(new EditorBuildSettingsScene(GameScenes.Map01, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }
    }
}
