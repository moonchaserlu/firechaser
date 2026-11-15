using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 玩家弹药占位 HUD（IMGUI）：右下角显示当前武器、弹匣/备弹，换弹时显示进度条，
    /// 准星下方也画一条细的换弹进度；想换弹却没有子弹时提示“没有备弹”。
    /// 已有 SliceHud（自带弹药文字）的场景不绘制，避免重复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAmmoHud : MonoBehaviour
    {
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private Vector2 panelSize = new Vector2(220f, 52f);

        private static Texture2D white;
        private GUIStyle title;
        private GUIStyle detail;
        private bool suppressed;
        private float outOfAmmoTime = -10f;

        /// <summary>最近一次“没有备弹”提示仍在显示。</summary>
        public bool OutOfAmmoNoticeVisible => Time.unscaledTime - outOfAmmoTime < 1.5f;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerCharacter>();
        }

        private void Start()
        {
            suppressed = FindAnyObjectByType<SliceHud>() != null;
        }

        private void OnEnable()
        {
            if (player != null && player.Shooter != null) player.Shooter.OutOfAmmo += OnOutOfAmmo;
        }

        private void OnDisable()
        {
            if (player != null && player.Shooter != null) player.Shooter.OutOfAmmo -= OnOutOfAmmo;
        }

        private void OnOutOfAmmo() => outOfAmmoTime = Time.unscaledTime;

        private void OnGUI()
        {
            if (suppressed || player == null) return;
            var shooter = player.Shooter;
            if (shooter == null || shooter.Weapon == null || shooter.CurrentConfig == null || shooter.IsHolstered) return;
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }

            title ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
            detail ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };

            var weapon = shooter.Weapon;
            var definition = shooter.CurrentConfig;
            var rect = new Rect(Screen.width - 16f - panelSize.x, Screen.height - 16f - panelSize.y, panelSize.x, panelSize.y);
            Fill(rect, new Color(0f, 0f, 0f, 0.55f));
            var top = new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 26f);
            var bottom = new Rect(rect.x + 8f, rect.y + 28f, rect.width - 16f, 18f);

            if (definition.kind == WeaponKind.Melee)
            {
                GUI.Label(top, definition.displayName, title);
                GUI.Label(bottom, $"近战  体力 {definition.staminaCost:0}", detail);
                return;
            }

            var reserve = shooter.FreeAmmo || definition.ammoItem == null ? -1 : player.Inventory.Inventory.CountOf(definition.ammoItem);
            var reserveText = reserve < 0 ? "练习弹" : $"备弹 {reserve}";
            var previous = title.normal.textColor;
            if (weapon.AmmoInMagazine == 0 && !weapon.IsReloading) title.normal.textColor = new Color(1f, 0.4f, 0.3f);
            GUI.Label(top, $"{weapon.AmmoInMagazine} / {weapon.MagazineSize}", title);
            title.normal.textColor = previous;

            if (weapon.IsReloading)
            {
                var progress = weapon.ReloadProgress(Time.time);
                var bar = new Rect(bottom.x, bottom.y + 4f, bottom.width, 10f);
                Fill(bar, new Color(1f, 1f, 1f, 0.15f));
                Fill(new Rect(bar.x, bar.y, bar.width * progress, bar.height), new Color(0.95f, 0.78f, 0.3f));
                GUI.Label(new Rect(rect.x + 8f, top.y, rect.width - 16f, top.height), $"换弹中", new GUIStyle(detail) { alignment = TextAnchor.MiddleLeft });
                DrawCrosshairBar(progress);
            }
            else if (OutOfAmmoNoticeVisible)
            {
                var style = new GUIStyle(detail) { normal = { textColor = new Color(1f, 0.45f, 0.35f) } };
                GUI.Label(bottom, "没有备弹", style);
            }
            else
            {
                var hint = weapon.AmmoInMagazine == 0 ? "[R] 换弹  " : string.Empty;
                GUI.Label(bottom, $"{hint}{definition.displayName}  {reserveText}", detail);
            }
        }

        // 换弹时玩家视线在准星上，准星正下方再画一条细进度条。
        private void DrawCrosshairBar(float progress)
        {
            if (player.Input == null || !player.Input.GameplayEnabled) return;
            var screen = player.Input.AimScreenPosition;
            var center = new Vector2(screen.x, Screen.height - screen.y);
            var bar = new Rect(center.x - 22f, center.y + 22f, 44f, 4f);
            Fill(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), new Color(0f, 0f, 0f, 0.7f));
            Fill(new Rect(bar.x, bar.y, bar.width * progress, bar.height), new Color(0.95f, 0.78f, 0.3f));
        }

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }
    }
}
