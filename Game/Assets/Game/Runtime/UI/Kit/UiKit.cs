using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WildernessExtraction
{
    /// <summary>
    /// UGUI 代码生成工具（做法沿用 we-ll-be-right-back 的 TitleScreen / SettingsPanel）：
    /// 每个界面自己建一张 ScreenSpaceOverlay Canvas，按 1920×1080 设计坐标、左上角为原点摆放控件，
    /// 不需要放进场景或 Prefab。配色仍是《向火而生》自己的暖色。
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Back = new Color(0.039f, 0.027f, 0.02f, 1f);
        public static readonly Color Window = new Color(0.075f, 0.047f, 0.03f, 0.97f);
        public static readonly Color Nav = new Color(0.058f, 0.037f, 0.024f, 1f);
        public static readonly Color Well = new Color(0.045f, 0.03f, 0.02f, 1f);
        public static readonly Color Field = new Color(0.11f, 0.072f, 0.045f, 1f);
        public static readonly Color Line = new Color(0.28f, 0.19f, 0.11f, 1f);
        public static readonly Color Row = new Color(0.17f, 0.115f, 0.07f, 1f);
        public static readonly Color Amber = new Color(0.95f, 0.78f, 0.3f);
        public static readonly Color Fire = new Color(1f, 0.55f, 0.2f);
        public static readonly Color Text1 = new Color(1f, 0.95f, 0.86f);
        public static readonly Color Text2 = new Color(0.74f, 0.66f, 0.56f);
        public static readonly Color Dim = new Color(0.42f, 0.37f, 0.32f);
        public static readonly Color Danger = new Color(1f, 0.42f, 0.36f);
        public static readonly Color Dark = new Color(0.12f, 0.07f, 0.03f);
        public static readonly Color Off = new Color(0.16f, 0.11f, 0.07f);

        private static Font font;
        private static Sprite rounded;
        private static Sprite knob;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>("Fonts/ChineseUI");
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
                return font;
            }
        }

        /// <summary>运行时没有内置 UISprite，自己画一张 9 宫格圆角图。</summary>
        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(32, 6);

        public static Sprite Knob => knob != null ? knob : knob = MakeRounded(32, 16);

        private static Sprite MakeRounded(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                var dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d + 0.5f)));
            }

            texture.Apply();
            var border = radius < size / 2 ? radius : 0;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        public static Canvas Canvas(string name, Transform parent, int order, float referenceWidth = 1920, float referenceHeight = 1080)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(referenceWidth, referenceHeight);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        // ---------- 摆放：左上角原点，y 向下 ----------

        public static RectTransform Place(Transform t, float x, float y, float w, float h)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        /// <summary>以父节点中心为原点摆放（窗口居中用）。</summary>
        public static RectTransform Center(Transform t, float w, float h, float dx = 0, float dy = 0)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(dx, -dy);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static RectTransform Stretch(Transform t, float inset = 0)
        {
            var r = (RectTransform)t;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // ---------- 控件 ----------

        public static Image Box(string name, Transform parent, Color color, Color? outline = null, bool round = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            if (round)
            {
                image.sprite = Rounded;
                image.type = Image.Type.Sliced;
            }

            if (outline.HasValue && outline.Value.a > 0)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = outline.Value;
                o.effectDistance = new Vector2(1, -1);
            }

            image.raycastTarget = color.a > 0 || outline.HasValue;
            return image;
        }

        public static Image Rule(Transform parent, float x, float y, float w, float h, Color color)
        {
            var image = Box("Rule", parent, color, null, false);
            image.raycastTarget = false;
            Place(image.transform, x, y, w, h);
            return image;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (ContainsCjk(text)) t.gameObject.AddComponent<LocText>().Source = text;
            else t.text = text;
            return t;
        }

        public static bool ContainsCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (c >= 0x3000 && c <= 0x9FFF || c >= 0xFF00 && c <= 0xFFEF) return true;
            return false;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, float x, float y, float w, float h, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false)
        {
            var t = Label(name, parent, text, size, color, anchor, bold);
            Place(t.transform, x, y, w, h);
            return t;
        }

        public enum ButtonKind
        {
            Primary,
            Secondary,
            Ghost,
            Danger
        }

        public static Button Button(string name, Transform parent, string caption, ButtonKind kind, UnityAction click, int fontSize = 18)
        {
            Color face, hover, pressed, textColor, line;
            switch (kind)
            {
                case ButtonKind.Primary:
                    face = Amber; hover = new Color(1f, 0.86f, 0.42f); pressed = new Color(0.85f, 0.66f, 0.22f); textColor = Dark; line = Color.clear; break;
                case ButtonKind.Danger:
                    face = new Color(0.32f, 0.09f, 0.07f); hover = new Color(0.42f, 0.12f, 0.09f); pressed = new Color(0.25f, 0.07f, 0.05f); textColor = Danger; line = new Color(0.55f, 0.2f, 0.16f); break;
                case ButtonKind.Ghost:
                    face = new Color(1, 1, 1, 0); hover = new Color(1f, 0.78f, 0.3f, 0.08f); pressed = new Color(1f, 0.78f, 0.3f, 0.14f); textColor = Text2; line = Color.clear; break;
                default:
                    face = Field; hover = new Color(0.17f, 0.11f, 0.065f); pressed = Well; textColor = Text1; line = Line; break;
            }

            var image = Box(name, parent, Color.white, line);
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            var c = b.colors;
            c.normalColor = face;
            c.highlightedColor = hover;
            c.selectedColor = face;
            c.pressedColor = pressed;
            c.disabledColor = new Color(face.r, face.g, face.b, face.a * 0.35f);
            c.fadeDuration = 0.08f;
            b.colors = c;
            if (click != null) b.onClick.AddListener(click);
            var t = Label("Text", image.transform, caption, fontSize, textColor, TextAnchor.MiddleCenter, kind == ButtonKind.Primary);
            Stretch(t.transform);
            return b;
        }

        public static Button Button(string name, Transform parent, string caption, ButtonKind kind, UnityAction click, float x, float y, float w, float h, int fontSize = 18)
        {
            var b = Button(name, parent, caption, kind, click, fontSize);
            Place(b.transform, x, y, w, h);
            return b;
        }

        public static Text Caption(Button b) => b.GetComponentInChildren<Text>(true);

        /// <summary>窗口标题栏左侧：标题 + 灰色英文副标题，用布局组让副标题跟在标题后面（英文标题更长）。</summary>
        public static void TitleRow(Transform frame, string title, string subtitle, float x = 28, float height = 68, int size = 24)
        {
            var row = Node("TitleRow", frame);
            Place(row, x, 0, 600, height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var t = Label("Title", row, title, size, Text1, TextAnchor.MiddleLeft, true);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var s = Label("Subtitle", row, subtitle, 13, Text2, TextAnchor.MiddleLeft);
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>界面文字统一走 Loc：中文原文进，当前语言的文字出。</summary>
        public static string T(string zh) => Loc.T(zh);

        /// <summary>设置一段会随语言切换的文字（记住中文原文，切换语言时自动重取）。</summary>
        public static void Set(Text text, string zh)
        {
            var loc = text.GetComponent<LocText>();
            if (loc == null) loc = text.gameObject.AddComponent<LocText>();
            loc.Source = zh;
        }

        /// <summary>鼠标移入时回调（键盘焦点跟着鼠标走）。</summary>
        public static void OnHover(Component target, UnityAction enter)
        {
            var trigger = target.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = target.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => enter());
            trigger.triggers.Add(entry);
        }

        /// <summary>一行里的横向滑条（不带名称），数值文字放在右侧。</summary>
        public static Slider InlineSlider(Transform parent, float x, float y, float w, float h, out Text value, float min = 0, float max = 100)
        {
            var go = Node("Slider", parent);
            Place(go, x, y, w - 64, h);
            var mid = h * 0.5f;
            var track = Box("Track", go, Off);
            Place(track.transform, 0, mid - 3, w - 64, 6);
            var fillArea = Node("Fill Area", go);
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(0, mid - 3);
            fillArea.offsetMax = new Vector2(0, -(mid - 3));
            var fill = Box("Fill", fillArea, Fire);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Node("Handle Area", go);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10, 0);
            handleArea.offsetMax = new Vector2(-10, 0);
            var handle = Box("Handle", handleArea, Amber);
            handle.sprite = Knob;
            handle.type = Image.Type.Simple;
            handle.rectTransform.sizeDelta = new Vector2(20, -(h - 20));
            var slider = go.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            value = Label("Value", parent, "0", 20, Amber, x + w - 56, y, 56, h, TextAnchor.MiddleRight);
            return slider;
        }

        /// <summary>径向柔光贴图（火光、余烬用）。</summary>
        public static Sprite Glow => glow != null ? glow : glow = MakeGlow(128);

        private static Sprite glow;

        private static Sprite MakeGlow(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var half = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f)));
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>滑条一行：上面是名称和数值，下面是轨道（版式同 we-ll-be-right-back 设置面板）。</summary>
        public static Slider SliderRow(Transform parent, string label, float x, float y, float w, out Text value, out Text name, float min = 0, float max = 100)
        {
            name = Label("Label", parent, label, 18, Text1, x, y, w - 80, 28);
            value = Label("Value", parent, "100", 18, Amber, x + w - 80, y, 80, 28, TextAnchor.MiddleRight);
            var go = Node("Slider", parent);
            Place(go, x, y + 32, w, 24);
            var track = Box("Track", go, Off);
            Place(track.transform, 0, 9, w, 6);
            var fillArea = Node("Fill Area", go);
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(0, 9);
            fillArea.offsetMax = new Vector2(0, -9);
            var fill = Box("Fill", fillArea, Amber);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Node("Handle Area", go);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(9, 0);
            handleArea.offsetMax = new Vector2(-9, 0);
            var handle = Box("Handle", handleArea, Text1);
            handle.sprite = Knob;
            handle.type = Image.Type.Simple;
            handle.rectTransform.sizeDelta = new Vector2(18, 0);
            var slider = go.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        /// <summary>‹ 值 › 选择框。</summary>
        public static Text Stepper(string name, Transform parent, float x, float y, float w, float h, UnityAction prev, UnityAction next)
        {
            var box = Box(name, parent, Well, Line);
            Place(box.transform, x, y, w, h);
            Button("Prev", box.transform, "‹", ButtonKind.Ghost, prev, 0, 0, 52, h, 26);
            Button("Next", box.transform, "›", ButtonKind.Ghost, next, w - 52, 0, 52, h, 26);
            return Label("Value", box.transform, "", 18, Text1, 52, 0, w - 104, h, TextAnchor.MiddleCenter);
        }

        /// <summary>二选一分段按钮；选中项用琥珀色描边和文字。</summary>
        public static void SetSelected(Button b, bool selected)
        {
            var image = (Image)b.targetGraphic;
            var outline = b.GetComponent<Outline>();
            if (outline != null) outline.effectColor = selected ? Amber : Line;
            var c = b.colors;
            c.normalColor = c.selectedColor = selected ? new Color(0.2f, 0.13f, 0.06f) : Field;
            b.colors = c;
            image.color = Color.white;
            var t = Caption(b);
            if (t != null) t.color = selected ? Amber : Text1;
        }

        /// <summary>键盘导航时给当前项一个高亮（鼠标悬停由 Button 自己处理）。</summary>
        public static void Focus(Button b, bool focused, Color normal)
        {
            var c = b.colors;
            c.normalColor = c.selectedColor = focused ? c.highlightedColor : normal;
            b.colors = c;
        }
    }
}
