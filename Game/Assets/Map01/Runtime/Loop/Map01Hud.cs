using System.Text;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>Map01 占位 HUD（IMGUI）：价值、电量、背包、发电机、撤离、交互提示与商店；任务模式下另有任务追踪与阅读面板。只读权威层的状态。</summary>
    public sealed class Map01Hud : MonoBehaviour
    {
        [SerializeField] private Map01Authority authority;
        [SerializeField] private Map01Interactor interactor;
        private Map01RunFlow flow;
        private Map01QuestDirector quests;
        private GUIStyle reading;

        private GUIStyle panel;
        private GUIStyle center;
        private float messageTime;
        private string message;

        private void Start()
        {
            if (authority == null) authority = FindAnyObjectByType<Map01Authority>();
            if (interactor == null) interactor = FindAnyObjectByType<Map01Interactor>();
            flow = FindAnyObjectByType<Map01RunFlow>();
            quests = authority.QuestDirector;
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
            if (authority.QuestMode) status.AppendLine($"庇护所照明：{(authority.SafehouseLit ? "已恢复" : "断电（只有炉火）")}");
            status.AppendLine($"安全屋电量：{ledger.Power}/{ledger.PowerRequiredForRescue}");
            status.AppendLine($"背包：{bag.UsedSlots}/{bag.Capacity}");
            status.AppendLine($"发电机：{GeneratorText(gen)}");
            status.AppendLine($"撤离：{ExtractionText(authority.Extraction)}");
            status.Append(LightText());
            GUI.Box(new Rect(12, 12, 300, authority.QuestMode ? 176 : 154), status.ToString(), panel);
            DrawQuestTracker();

            var focus = interactor != null ? interactor.Focus : null;
            if (focus != null && (flow == null || !flow.IsCrafting))
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 230, Screen.height - 150, 460, PromptHeight(focus)), Prompt(focus), panel);
            }

            if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 4f)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 230, 16, 460, 34), message, panel);
            }

            if (authority.Outcome != Map01RunOutcome.InProgress)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 220, Screen.height * 0.5f - 90, 440, 180), EndText(), center);
            }
            else if (flow != null && flow.IsReading)
            {
                reading ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 17, wordWrap = true, richText = true, padding = new RectOffset(24, 24, 18, 18) };
                var width = Mathf.Min(620f, Screen.width - 40f);
                GUI.Box(new Rect(Screen.width * 0.5f - width * 0.5f, Screen.height * 0.5f - 200, width, 400),
                    $"<b>{flow.ReadingTitle}</b>\n\n{flow.ReadingBody}\n\n<color=#aaaaaa>[E] / [Esc] 合上</color>", reading);
            }
            else if (flow != null && flow.IsPaused)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 200, Screen.height * 0.5f - 40, 400, 80), "已暂停\n按 Esc 继续", center);
            }
        }

        private void DrawQuestTracker()
        {
            if (quests == null || !quests.IsRunning) return;
            var quest = quests.CurrentQuest;
            var text = quest != null
                ? $"<b>{quest.questId} {quest.title}</b>\n{quests.CurrentStep()}"
                : $"<b>任务完成</b>\n{quests.Chain.chainCompleteText}";
            GUI.Box(new Rect(Screen.width - 372, 12, 360, 78), text, panel);
            if (!string.IsNullOrEmpty(quests.Banner) && Time.realtimeSinceStartup - quests.BannerRealtime < 6f)
            {
                GUI.Box(new Rect(Screen.width - 372, 96, 360, 58), $"<color=#ffd27a>{quests.Banner}</color>", panel);
            }
        }

        private string Prompt(Map01Interactable focus)
        {
            if (Map01Interactable.IsQuestStation(focus.Station))
            {
                return quests != null && quests.IsRunning ? quests.Prompt(focus) : string.Empty;
            }

            switch (focus.Station)
            {
                case Map01Station.RecycleTerminal:
                    return "[E] 回收背包里的回收物，换成价值";
                case Map01Station.BatterySlot:
                    return authority.QuestMode && !authority.SafehouseLit
                        ? "[E] 装入充好的电池，恢复庇护所照明"
                        : "[E] 放入充好的电池，为安全屋供电";
                case Map01Station.Altar:
                    return authority.Daylight != null && authority.Daylight.IsDay
                        ? "祭坛已熄灭，天已经亮了"
                        : "[E] 献祭一块别人的身份牌，让天亮起来";
                case Map01Station.Workbench:
                    return "[E] 使用工作台";
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

        private float PromptHeight(Map01Interactable focus) => focus.Station switch
        {
            Map01Station.ShopTerminal => 120,
            _ => 34
        };

        private string EndText()
        {
            var result = authority.Result;
            var stash = SessionStash.Current;
            var text = new StringBuilder(authority.Outcome == Map01RunOutcome.Rescued ? "救援成功\n" : "行动失败\n");
            if (result != null)
            {
                text.AppendLine(authority.Outcome == Map01RunOutcome.Rescued
                    ? $"带出 {result.Items.Count} 件，价值 {result.Value}"
                    : $"丢失 {result.Items.Count} 件，价值 {result.Value}");
            }

            text.AppendLine($"仓库累计价值 {stash.TotalValue}");
            text.Append("按 E 重新开始");
            return text.ToString();
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
