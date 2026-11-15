using UnityEngine;
using UnityEngine.UI;

namespace WildernessExtraction
{
    /// <summary>挂在 UGUI Text 上：记住中文原文，语言切换（Loc.Changed）时重新取文字。</summary>
    [RequireComponent(typeof(Text))]
    public sealed class LocText : MonoBehaviour
    {
        private string source;
        private Text text;

        public string Source
        {
            get => source;
            set
            {
                source = value;
                Apply();
            }
        }

        private void OnEnable()
        {
            Loc.Changed += Apply;
            Apply();
        }

        private void OnDisable() => Loc.Changed -= Apply;

        private void Apply()
        {
            if (text == null) text = GetComponent<Text>();
            if (text != null) text.text = Loc.Lines(source);
        }
    }
}
