using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.EditorTools
{
    /// <summary>编辑器里让 RunSession 按场景路径重开，VerticalSlice 不需要登记进 Build Settings。</summary>
    [InitializeOnLoad]
    public static class RunSessionPlayModeReload
    {
        static RunSessionPlayModeReload()
        {
            RunSession.ReloadOverride = scene =>
                EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
        }
    }
}
