using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>编辑器里让 Map01 按场景路径重开，不需要把场景登记进 Build Settings。</summary>
    [InitializeOnLoad]
    public static class Map01PlayModeReload
    {
        static Map01PlayModeReload()
        {
            Map01RunFlow.ReloadOverride = scene =>
                EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
        }
    }
}
