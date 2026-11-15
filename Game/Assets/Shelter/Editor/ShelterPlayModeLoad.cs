using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.Shelter.EditorTools
{
    /// <summary>编辑器里让安全屋与地图之间按场景路径切换，不需要把场景登记进 Build Settings。</summary>
    [InitializeOnLoad]
    public static class ShelterPlayModeLoad
    {
        static ShelterPlayModeLoad()
        {
            GameScenes.LoadOverride = path => EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
        }
    }
}
