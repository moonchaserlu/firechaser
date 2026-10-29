using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 玩家自身的占位 HUD（IMGUI）：左下角血条、体力条，鼠标位置的十字准星。
    /// 挂在玩家预制体上，只读 Health / StaminaModel / 输入状态。
    /// 可操作时隐藏系统光标、显示准星；暂停、阅读、结算时恢复系统光标。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStatusHud : MonoBehaviour
    {
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private Vector2 barSize = new Vector2(280f, 18f);
        [SerializeField] private float crosshairLength = 9f;
        [SerializeField] private float crosshairGap = 5f;
        [Tooltip("移动时准星张开的额外像素，提示移动射击散布更大。")]
        [SerializeField] private float movingGap = 6f;
        [Tooltip("开枪瞬间准星张开的额外像素。")]
        [SerializeField] private float fireKick = 7f;

        private static Texture2D white;
        private GUIStyle label;
        private float kick;
        private float moveBlend;
        private Vector3 lastPosition;
        private bool cursorHidden;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerCharacter>();
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (player != null && player.Shooter != null) player.Shooter.Fired += OnFired;
        }

        private void OnDisable()
        {
            if (player != null && player.Shooter != null) player.Shooter.Fired -= OnFired;
            SetCursorHidden(false);
        }

        private void OnFired()
        {
            kick = 1f;
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            var moved = Time.deltaTime > 0f && (transform.position - lastPosition).magnitude / Time.deltaTime > 1f;
            lastPosition = transform.position;
            moveBlend = Mathf.MoveTowards(moveBlend, moved ? 1f : 0f, deltaTime * 6f);
            kick = Mathf.MoveTowards(kick, 0f, deltaTime * 8f);
            SetCursorHidden(CrosshairActive);
        }

        private bool CrosshairActive => player != null && player.Input.GameplayEnabled && Application.isFocused;

        private void OnGUI()
        {
            if (player == null) return;
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }

            label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };

            var x = 16f;
            var y = Screen.height - 16f - barSize.y * 2f - 8f;
            var health = player.Health;
            DrawBar(new Rect(x, y, barSize.x, barSize.y), health.Normalized, new Color(0.78f, 0.16f, 0.14f),
                $"生命 {Mathf.CeilToInt(health.Current)}/{Mathf.CeilToInt(health.Max)}");
            var stamina = player.Motor.Stamina;
            if (stamina != null)
            {
                DrawBar(new Rect(x, y + barSize.y + 8f, barSize.x, barSize.y), stamina.Normalized, new Color(0.86f, 0.66f, 0.2f),
                    $"体力 {Mathf.FloorToInt(stamina.Current)}/{Mathf.FloorToInt(stamina.Max)}");
            }

            if (CrosshairActive) DrawCrosshair(player.Input.AimScreenPosition);
        }

        private void DrawBar(Rect rect, float fill, Color color, string text)
        {
            Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), color);
            GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 8f, rect.height), text, label);
        }

        private void DrawCrosshair(Vector2 screen)
        {
            // Input System 的屏幕坐标原点在左下，IMGUI 在左上
            var c = new Vector2(screen.x, Screen.height - screen.y);
            var gap = crosshairGap + movingGap * moveBlend + fireKick * kick;
            var len = crosshairLength;
            const float thick = 2f;
            for (var pass = 0; pass < 2; pass++)
            {
                // 先画一圈暗色描边，在亮地面上也看得清
                var o = pass == 0 ? 1f : 0f;
                var color = pass == 0 ? new Color(0f, 0f, 0f, 0.7f) : new Color(1f, 0.95f, 0.85f, 0.95f);
                Fill(new Rect(c.x - thick * 0.5f - o, c.y - gap - len - o, thick + o * 2f, len + o * 2f), color);
                Fill(new Rect(c.x - thick * 0.5f - o, c.y + gap - o, thick + o * 2f, len + o * 2f), color);
                Fill(new Rect(c.x - gap - len - o, c.y - thick * 0.5f - o, len + o * 2f, thick + o * 2f), color);
                Fill(new Rect(c.x + gap - o, c.y - thick * 0.5f - o, len + o * 2f, thick + o * 2f), color);
                Fill(new Rect(c.x - 1f - o, c.y - 1f - o, 2f + o * 2f, 2f + o * 2f), color);
            }
        }

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        private void SetCursorHidden(bool hidden)
        {
            if (cursorHidden == hidden) return;
            cursorHidden = hidden;
            Cursor.visible = !hidden;
        }
    }
}
