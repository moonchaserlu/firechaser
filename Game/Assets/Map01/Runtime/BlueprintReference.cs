using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>概念图参考平面：编辑器中可见，进入运行时隐藏；对象标记为 EditorOnly，构建时剔除。</summary>
    public sealed class BlueprintReference : MonoBehaviour
    {
        private void Awake()
        {
            gameObject.SetActive(false);
        }
    }
}
