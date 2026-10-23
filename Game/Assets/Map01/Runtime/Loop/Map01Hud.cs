using System.Text;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>Map01 占位 HUD（IMGUI）：价值、电量、背包、发电机、撤离、交互提示与商店。只读权威层的状态。</summary>
    public sealed class Map01Hud : MonoBehaviour
    {
        [SerializeField] private Map01Authority authority;
        [SerializeField] private Map01Interactor interactor;

        private GUIStyle panel;
        private GUIStyle center;
        private float messageTime;
        private string message;

        private void Start()
        {
            if (authority == null) authority = FindAnyObjectByType<Map01Authority>();
            if (interactor == null) interactor = FindAnyObjectByType<Map01Interactor>();
            authority.Message += text =>
            {
                message = text;
                messageTime = Time.unscaledTime;
            };
        }

        private void OnGUI()
        {
            if (authority == null || authority.Ledger == null) return;
            panel ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true, padding = new RectOffset(10, 10, 8, 8) };
            center ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };

            var ledger = authority.Ledger;
            var bag = authority.Player.Inventory.Inventory;
            var gen = authority.Generator;
            var status = new StringBuilder();
            status.AppendLine($"价值：{ledger.Credits}");
            status.AppendLine($"安全屋电量：{ledger.Power}/{ledger.PowerRequiredForRescue}");
            status.AppendLine($"背包：{bag.UsedSlots}/{bag.Capacity}");
            status.AppendLine($"发电机：{GeneratorText(gen)}");
            status.AppendLine($"撤离：{ExtractionText(authority.Extraction)}");
            status.Append(LightText());
            GUI.Box(new Rect(12, 12, 300, 154), status.ToString(), panel);

            var focus = interactor != null ? interactor.Focus : null;
            if (focus != null)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 230, Screen.height - 150, 460, focus.Station == Map01Station.ShopTerminal ? 120 : 34), Prompt(focus), panel);
            }

            if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 4f)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 230, 16, 460, 34), message, panel);
            }

            if (authority.Outcome != Map01RunOutcome.InProgress)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 200, Screen.height * 0.5f - 40, 400, 80),
                    authority.Outcome == Map01RunOutcome.Rescued ? "救援成功" : "行动失败", center);
            }
        }

        private string Prompt(Map01Interactable focus)
        {
            switch (focus.Station)
            {
                case Map01Station.RecycleTerminal:
                    return "[E] 回收背包里的回收物，换成价值";
                case Map01Station.BatterySlot:
                    return "[E] 放入充好的电池，为安全屋供电";
                case Map01Station.Altar:
                    return authority.Daylight != null && authority.Daylight.IsDay
                        ? "祭坛已熄灭，天已经亮了"
                        : "[E] 献祭一块别人的身份牌，让天亮起来";
                case Map01Station.Generator:
                    var gen = authority.Generator;
                    if (gen.IsRepairing) return $"修理中 {gen.RepairProgress:P0}";
                    if (authority.IsCharging) return $"充电中 {authority.ChargeProgress:P0}";
                    return gen.State == GeneratorState.Producing || gen.State == GeneratorState.Operational
                        ? $"[E] 用空电池取电（存电 {gen.StoredCharge}/{gen.MaxCharge}）"
                        : "[E] 用 1 个机械零件修理发电机";
                default:
                    var text = new StringBuilder("商店（按数字键购买）\n");
                    for (var i = 0; i < authority.Shop.Count; i++)
                    {
                        text.AppendLine($"[{i + 1}] {authority.Shop[i].displayName}  {authority.Shop[i].price}");
                    }

                    return text.ToString();
            }
        }

        private string LightText()
        {
            if (authority.Daylight != null && authority.Daylight.IsDay) return "光照：白天";
            var exposure = authority.Player.Exposure;
            if (exposure == null || !exposure.enabled) return "光照：黑暗（未生效）";
            return exposure.State switch
            {
                LightExposureState.Safe => "光照：在光里",
                LightExposureState.Warning => $"<color=#ffcc55>光照：离光预警 {exposure.WarningRemaining:F1} 秒</color>",
                _ => "<color=#ff6655>光照：黑暗正在伤害你</color>"
            };
        }

        private static string GeneratorText(GeneratorModel gen) => gen.State switch
        {
            GeneratorState.Unknown => "位置未知",
            GeneratorState.Discovered or GeneratorState.NeedsRepair => "待修理",
            GeneratorState.Producing => $"产电中（存电 {gen.StoredCharge}/{gen.MaxCharge}）",
            GeneratorState.Operational => $"已满（存电 {gen.StoredCharge}/{gen.MaxCharge}）",
            _ => "损坏"
        };

        private static string ExtractionText(ExtractionModel extraction) => extraction.State switch
        {
            ExtractionState.Candidate => "救援未呼叫",
            ExtractionState.Active => "撤离点已激活",
            ExtractionState.Boarding => $"登机 {extraction.BoardingElapsed:F0}/{extraction.BoardingDuration:F0} 秒",
            ExtractionState.Interrupted => "登机中断",
            ExtractionState.Completed => "已撤离",
            _ => "不可用"
        };
    }
}
