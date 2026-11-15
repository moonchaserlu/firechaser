using System;
using UnityEngine;

namespace WildernessExtraction.Shelter
{
    public enum ShelterResult
    {
        Done,
        NotFound,
        NoSpace,
        CraftFailed
    }

    /// <summary>
    /// 地下安全屋的规则层（第一版只有靶场、仓库、工作台和出口）：读写局外存档，处理回家结算、
    /// 仓库与背包之间搬运、合成和出发。界面（<see cref="ShelterHud"/>）只调用这里的方法；每次改动后立即存档。
    /// </summary>
    public sealed class ShelterController : MonoBehaviour
    {
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private ItemDatabase database;
        [SerializeField] private CraftingRecipeBook recipes;
        [Tooltip("新存档的初始物资，放进仓库。")]
        [SerializeField] private ItemDefinition[] starterKit = Array.Empty<ItemDefinition>();
        [SerializeField, Min(0.5f)] private float interactRange = 2.6f;

        public event Action<string> Message;
        public event Action<ShelterStation> PanelChanged;

        public PlayerProfile Profile { get; private set; }
        public PlayerCharacter Player => player;
        public Inventory Bag => player.Inventory.Inventory;
        public CraftingRecipeBook Recipes => recipes;
        public ShelterStation Focus { get; private set; }
        public ShelterStation OpenPanel { get; private set; }
        public string LastMessage { get; private set; } = string.Empty;
        /// <summary>本次进入安全屋时的回家结算（无则 None）。</summary>
        public RaidOutcome ArrivalOutcome { get; private set; }
        public int ArrivalItems { get; private set; }
        /// <summary>这一趟选好的守灯人 buff（每次回到安全屋清空）。</summary>
        public RaidBuffKind SelectedBuff => RaidBuffs.Selected;
        /// <summary>没选 buff 就要出发：等玩家确认。</summary>
        public bool ConfirmingDepartWithoutBuff { get; private set; }

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerCharacter>();
        }

        private void Start()
        {
            var isNew = !System.IO.File.Exists(PlayerProfile.SavePath);
            Profile = PlayerProfile.LoadOrCreate(database);
            if (isNew)
            {
                foreach (var item in starterKit)
                {
                    if (item != null) Profile.Stash.TryAdd(item);
                }
            }

            // 安全屋里背包从空开始（预制体自带的初始物品只用于直接打开地图的旧玩法）。
            Bag.TakeAll();
            // 只有靶场里能开枪（由 ShelterRange 打开）；家里不建火。
            if (player.Shooter != null) player.Shooter.enabled = false;
            if (player.Placer != null) player.Placer.enabled = false;

            // 守灯人的 buff 只管一趟：回到安全屋就要重新选。
            RaidBuffs.Clear();
            ReceiveArrival();
            Profile.Save();
        }

        private void ReceiveArrival()
        {
            var (outcome, items) = RaidTransfer.Collect();
            ArrivalOutcome = outcome;
            if (outcome == RaidOutcome.Returned)
            {
                Profile.RecordRaid();
                var lost = 0;
                foreach (var item in items)
                {
                    ArrivalItems++;
                    // 仓库放不下的留在背包里，再放不下才算丢失。
                    if (!Profile.Stash.TryAdd(item) && !Bag.TryAdd(item)) lost++;
                }

                Notify(lost > 0 ? Loc.F("平安回家：带回 {0} 件，仓库和背包都满了，丢掉 {1} 件", ArrivalItems, lost) : Loc.F("平安回家：带回 {0} 件，已放进仓库", ArrivalItems));
            }
            else if (outcome == RaidOutcome.Died)
            {
                Profile.RecordRaid();
                Notify(Loc.T("你在黑暗里倒下了，带出去的东西都没了"));
            }
        }

        private void Update()
        {
            if (OpenPanel != null)
            {
                if (player.Input.PausePressed) ClosePanel();
                return;
            }

            Focus = ShelterStation.FindNearest(player.transform.position, interactRange);
            if (Focus != null && player.Input.InteractPressed)
            {
                OpenStation(Focus);
            }
        }

        public void OpenStation(ShelterStation station)
        {
            if (station == null) return;
            OpenPanel = station;
            player.Input.SetGameplayEnabled(false);
            PanelChanged?.Invoke(station);
        }

        public void ClosePanel()
        {
            ConfirmingDepartWithoutBuff = false;
            if (OpenPanel == null) return;
            OpenPanel = null;
            player.Input.SetGameplayEnabled(true);
            PanelChanged?.Invoke(null);
        }

        // ---------- 仓库 ----------

        public ShelterResult MoveToBackpack(ItemDefinition item) => Move(Profile.Stash, Bag, item, Loc.T("背包满了"));

        public ShelterResult MoveToStash(ItemDefinition item) => Move(Bag, Profile.Stash, item, Loc.T("仓库满了"));

        private ShelterResult Move(Inventory from, Inventory to, ItemDefinition item, string fullMessage)
        {
            if (item == null || from.CountOf(item) == 0) return ShelterResult.NotFound;
            if (!to.TryAdd(item))
            {
                Notify(fullMessage);
                return ShelterResult.NoSpace;
            }

            from.Remove(item, 1);
            Profile.Save();
            return ShelterResult.Done;
        }

        // ---------- 合成 ----------

        /// <summary>用背包里的材料合成。安全屋里所有配方都可用（任务解锁只在地图任务模式里生效）。</summary>
        public ShelterResult Craft(CraftingRecipe recipe)
        {
            if (recipe == null) return ShelterResult.NotFound;
            var result = Crafting.TryCraft(Bag, recipe);
            if (result != CraftResult.Crafted)
            {
                Notify(result == CraftResult.MissingIngredients ? Loc.T("背包里的材料不够") : result == CraftResult.InventoryFull ? Loc.T("背包满了") : Loc.T("做不了"));
                return ShelterResult.CraftFailed;
            }

            Profile.Save();
            Notify(Loc.F("做好了：{0}", recipe.result.DisplayName));
            return ShelterResult.Done;
        }

        // ---------- 守灯人 ----------

        /// <summary>选一个这一趟的 buff（再选别的会替换，选 None 取消）。</summary>
        public void SelectBuff(RaidBuffKind kind)
        {
            RaidBuffs.Select(kind);
            ConfirmingDepartWithoutBuff = false;
            Notify(kind == RaidBuffKind.None ? Loc.T("没有带上守灯人的 buff") : Loc.F("守灯人给了你「{0}」，这一趟有效", Loc.T(RaidBuffs.Get(kind).Name)));
        }

        // ---------- 出发 ----------

        /// <summary>
        /// 出口面板的“出发”：没选 buff 时先进入确认状态（“没有选择 buff，确定要出发吗？”），
        /// 再按一次 <see cref="ConfirmDepart"/> 才出发。返回是否已出发。
        /// </summary>
        public bool RequestDepart()
        {
            if (RaidBuffs.Selected == RaidBuffKind.None)
            {
                ConfirmingDepartWithoutBuff = true;
                return false;
            }

            Depart();
            return true;
        }

        public void ConfirmDepart() => Depart();

        public void CancelDepart() => ConfirmingDepartWithoutBuff = false;

        /// <summary>直接去找守灯人（确认框里的“回去选”）。</summary>
        public void OpenLampkeeper()
        {
            var station = ShelterStation.Find(ShelterStationKind.Lampkeeper);
            ClosePanel();
            if (station != null) OpenStation(station);
        }

        /// <summary>带着背包里的东西出发去 Map01。背包空着也可以走。</summary>
        public void Depart()
        {
            ClosePanel();
            // buff 留在 RaidBuffs.Selected 里随这一趟带进地图（RaidTransfer.Active 期间生效）。
            RaidTransfer.Begin(Bag.Items);
            Profile.Save();
            GameScenes.Load(GameScenes.Map01);
        }

        private void Notify(string text)
        {
            LastMessage = text;
            Message?.Invoke(text);
        }
    }
}
