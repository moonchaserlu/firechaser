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

                Notify(lost > 0 ? $"平安回家：带回 {ArrivalItems} 件，仓库和背包都满了，丢掉 {lost} 件" : $"平安回家：带回 {ArrivalItems} 件，已放进仓库");
            }
            else if (outcome == RaidOutcome.Died)
            {
                Profile.RecordRaid();
                Notify("你在黑暗里倒下了，带出去的东西都没了");
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
            if (OpenPanel == null) return;
            OpenPanel = null;
            player.Input.SetGameplayEnabled(true);
            PanelChanged?.Invoke(null);
        }

        // ---------- 仓库 ----------

        public ShelterResult MoveToBackpack(ItemDefinition item) => Move(Profile.Stash, Bag, item, "背包满了");

        public ShelterResult MoveToStash(ItemDefinition item) => Move(Bag, Profile.Stash, item, "仓库满了");

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
                Notify(result == CraftResult.MissingIngredients ? "背包里的材料不够" : result == CraftResult.InventoryFull ? "背包满了" : "做不了");
                return ShelterResult.CraftFailed;
            }

            Profile.Save();
            Notify($"做好了：{recipe.result.displayName}");
            return ShelterResult.Done;
        }

        // ---------- 出发 ----------

        /// <summary>带着背包里的东西出发去 Map01。背包空着也可以走。</summary>
        public void Depart()
        {
            ClosePanel();
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
