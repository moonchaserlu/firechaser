using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 任务模式下的 Q01–Q10 任务链：把权威层、背包、发电机、火堆、光照的现有状态翻译成任务事实，
    /// 处理炉火 / 记录 / 旧地图三类任务交互，并按任务刷出保底物资。
    /// 任务状态只保存在本局内存中；重开（场景重载）时整体重置，任务道具也随之重置，不会形成断点。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Map01QuestDirector : MonoBehaviour
    {
        public const string FactFuelDelivered = "fuel_delivered";
        public const string FactSafehouseLit = "safehouse_lit";
        public const string FactMedicalConfirmed = "medical_confirmed";
        public const string FactCrossedBridge = "crossed_bridge";
        public const string FactCampfireBuilt = "campfire_at_bridge";
        public const string FactMapCompared = "map_compared";

        private const float SupplyCheckInterval = 0.5f;

        [SerializeField] private Map01Authority authority;
        [SerializeField] private Map01QuestChain chain;
        [SerializeField] private Map01RunFlow flow;
        [Tooltip("庇护所室内判定体：照明恢复前关闭常亮。")]
        [SerializeField] private IndoorLight safehouseIndoor;
        [Tooltip("庇护所室内电灯（只是视觉），照明恢复时点亮。")]
        [SerializeField] private Light[] safehouseFixtures = Array.Empty<Light>();
        [Tooltip("任务专用交互点与陈设的父物体；非任务模式下隐藏。")]
        [SerializeField] private GameObject questProps;

        private readonly Dictionary<QuestSupply, LootContainer> spawnedSupplies = new Dictionary<QuestSupply, LootContainer>();
        private Transform supplyParent;
        private float supplyTimer;

        public QuestChainModel Model { get; private set; }
        public Map01QuestChain Chain => chain;
        public bool IsRunning => Model != null;
        public QuestDefinition CurrentQuest => Model != null && !Model.IsFinished && Model.IsStarted ? chain.quests[Model.CurrentIndex] : null;
        /// <summary>最近一次任务完成提示（HUD 显示几秒）。</summary>
        public string Banner { get; private set; } = string.Empty;
        public float BannerRealtime { get; private set; } = -100f;
        public IReadOnlyDictionary<QuestSupply, LootContainer> SpawnedSupplies => spawnedSupplies;

        private PlayerCharacter Player => authority.Player;
        private Inventory Bag => Player.Inventory.Inventory;
        private ItemDefinition Fuel => Player.Placer != null && Player.Placer.Config != null ? Player.Placer.Config.fuelItem : null;

        private void Awake()
        {
            if (authority == null) authority = GetComponent<Map01Authority>();
            if (flow == null) flow = GetComponent<Map01RunFlow>();
        }

        private void Start()
        {
            if (authority == null || chain == null || chain.quests.Count == 0 || !authority.QuestMode)
            {
                // 原救援循环：庇护所保持常亮，任务交互点不出现。
                if (questProps != null) questProps.SetActive(false);
                enabled = false;
                return;
            }

            var list = new List<(string, string)>();
            foreach (var quest in chain.quests) list.Add((quest.questId, quest.completionFact));
            Model = new QuestChainModel(list);
            Model.QuestStarted += OnQuestStarted;
            Model.QuestCompleted += OnQuestCompleted;
            Model.ChainCompleted += () => ShowBanner(chain.chainCompleteText);

            // 开场：燃料即将耗尽，初始携带的燃料收走（可在任务配置里关闭）。
            if (chain.stripStartingFuel && Fuel != null) Bag.Remove(Fuel, Bag.CountOf(Fuel));

            // 开场：庇护所断电，只有炉火附近一小圈安全光。
            SetSafehouseLighting(authority.SafehouseLit);
            authority.SafehousePowered += OnSafehousePowered;
            if (Player.Placer != null) Player.Placer.Built += OnCampfireBuilt;
            supplyParent = new GameObject("Dynamic_QuestSupplies").transform;
            Model.Begin();
        }

        private void OnDestroy()
        {
            if (authority != null) authority.SafehousePowered -= OnSafehousePowered;
            if (authority != null && Player != null && Player.Placer != null) Player.Placer.Built -= OnCampfireBuilt;
        }

        private void Update()
        {
            if (Model == null || authority.Outcome != Map01RunOutcome.InProgress)
            {
                return;
            }

            var position = Player.transform.position;
            if (chain.bridgeArrival.Contains(position)) Model.SetFact(FactCrossedBridge);
            if (IsActive("Q08")) CheckCampfireSite();

            supplyTimer -= Time.deltaTime;
            if (supplyTimer <= 0f)
            {
                supplyTimer = SupplyCheckInterval;
                RefreshSupplies();
            }
        }

        // ---------- 交互（由权威层校验距离与序号后调用） ----------

        public InteractionResult Interact(Map01Interactable station)
        {
            if (Model == null) return InteractionResult.InvalidState;
            switch (station.Station)
            {
                case Map01Station.Hearth:
                    return InteractHearth();
                case Map01Station.Record:
                    return Read(station.EntityId);
                case Map01Station.OldMap:
                    return InteractOldMap();
                default:
                    return InteractionResult.WrongStation;
            }
        }

        private InteractionResult InteractHearth()
        {
            if (IsActive("Q01"))
            {
                var fuel = Fuel;
                if (fuel == null || Bag.CountOf(fuel) == 0)
                {
                    authority.Announce("需要一份燃料才能续上炉火");
                    return InteractionResult.MissingItem;
                }

                // 只扣一次：事实记下后本任务完成，炉火不再收燃料。
                Bag.Remove(fuel, 1);
                authority.Announce("添了燃料，炉火又旺起来了");
                Model.SetFact(FactFuelDelivered);
                return InteractionResult.Accepted;
            }

            if (IsActive("Q03"))
            {
                if (MedicalCount() == 0)
                {
                    authority.Announce("身上没有医疗用品");
                    return InteractionResult.MissingItem;
                }

                // 只确认，不收走：药留给玩家自己用。
                authority.Announce("清点好了药品，留着路上用");
                Model.SetFact(FactMedicalConfirmed);
                return InteractionResult.Accepted;
            }

            authority.Announce(chain.hearthIdleText);
            return InteractionResult.Accepted;
        }

        private InteractionResult Read(string recordId)
        {
            var record = chain.FindRecord(recordId);
            if (record == null) return InteractionResult.UnknownEntity;
            // 提前阅读也记下：对应任务开启时直接认可。
            Model.SetFact(record.fact);
            if (flow != null) flow.OpenReading(record.title, record.body);
            return InteractionResult.Accepted;
        }

        private InteractionResult InteractOldMap()
        {
            if (IsActive("Q10"))
            {
                var quest = chain.Find("Q10");
                Model.SetFact(FactMapCompared);
                if (flow != null) flow.OpenReading("旧地图", $"{quest.completionText}\n\n{chain.chainCompleteText}");
                return InteractionResult.Accepted;
            }

            authority.Announce(chain.oldMapIdleText);
            return InteractionResult.Accepted;
        }

        // ---------- 事实来源 ----------

        private void OnSafehousePowered()
        {
            SetSafehouseLighting(true);
            Model.SetFact(FactSafehouseLit);
        }

        private void OnCampfireBuilt(Campfire fire)
        {
            if (IsActive("Q08") && chain.campfireSite.Contains(fire.transform.position)) Model.SetFact(FactCampfireBuilt);
        }

        /// <summary>Q08：指定区域内有正在燃烧的火堆（包括开启前玩家已建好的）即完成。</summary>
        private void CheckCampfireSite()
        {
            foreach (var fire in Campfire.Burning)
            {
                if (fire != null && fire.IsBurning && chain.campfireSite.Contains(fire.transform.position))
                {
                    Model.SetFact(FactCampfireBuilt);
                    return;
                }
            }
        }

        private void SetSafehouseLighting(bool lit)
        {
            if (safehouseIndoor != null) safehouseIndoor.SetAlwaysLit(lit);
            foreach (var fixture in safehouseFixtures)
            {
                if (fixture != null) fixture.enabled = lit;
            }
        }

        private void OnQuestStarted(int index)
        {
            // 新任务开启时立刻检查保底物资（例如 Q02 出发前的空电池）。
            supplyTimer = 0f;
            if (chain.quests[index].questId == "Q08") CheckCampfireSite();
        }

        private void OnQuestCompleted(int index)
        {
            var quest = chain.quests[index];
            ShowBanner($"完成：{quest.title}　{quest.completionText}");
        }

        private void ShowBanner(string text)
        {
            Banner = text;
            BannerRealtime = Time.realtimeSinceStartup;
        }

        // ---------- 当前一步目标 ----------

        /// <summary>当前任务的一步目标。各任务 steps 的含义见任务链文档。</summary>
        public string CurrentStep()
        {
            var quest = CurrentQuest;
            if (quest == null) return string.Empty;
            switch (quest.questId)
            {
                case "Q01":
                    return quest.Step(Fuel != null && Bag.CountOf(Fuel) > 0 ? 1 : 0);
                case "Q02":
                    return quest.Step(Q02Step());
                case "Q03":
                    return quest.Step(MedicalCount() > 0 ? 1 : 0);
                case "Q08":
                    return quest.Step(Fuel != null && Bag.CountOf(Fuel) > 0 ? 1 : 0);
                default:
                    return quest.Step(0);
            }
        }

        private int Q02Step()
        {
            if (Bag.CountOf(authority.ChargedBattery) > 0) return 5;
            if (GeneratorRepaired())
            {
                return authority.IsCharging || Bag.CountOf(authority.EmptyBattery) > 0 ? 4 : 3;
            }

            if (authority.Generator.IsRepairing) return 2;
            return authority.RepairPart != null && Bag.CountOf(authority.RepairPart) > 0 ? 1 : 0;
        }

        // ---------- 保底物资 ----------

        private void RefreshSupplies()
        {
            foreach (var supply in chain.supplies)
            {
                if (supply.item == null || !NeedsSupply(supply)) continue;
                spawnedSupplies.TryGetValue(supply, out var existing);
                if (existing != null && !existing.IsEmpty) continue;
                if (existing != null) Destroy(existing.gameObject);
                spawnedSupplies[supply] = SpawnSupply(supply);
            }
        }

        /// <summary>任务进行中、尚未完成，且玩家身上没有可用替代品时才需要保底。提前持有就不刷。</summary>
        private bool NeedsSupply(QuestSupply supply)
        {
            if (!IsActive(supply.questId)) return false;
            var item = supply.item;
            switch (supply.questId)
            {
                case "Q02":
                    if (item == authority.RepairPart) return !GeneratorRepaired() && !authority.Generator.IsRepairing && Bag.CountOf(item) == 0;
                    if (item == authority.EmptyBattery)
                    {
                        return !authority.SafehouseLit && !authority.IsCharging
                               && Bag.CountOf(authority.EmptyBattery) == 0 && Bag.CountOf(authority.ChargedBattery) == 0;
                    }

                    return Bag.CountOf(item) == 0;
                case "Q03":
                    return MedicalCount() == 0;
                default:
                    return Bag.CountOf(item) == 0;
            }
        }

        private LootContainer SpawnSupply(QuestSupply supply)
        {
            var prefab = authority.ContainerPrefab;
            if (prefab == null) return null;
            var position = supply.position;
            if (NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas)) position = hit.position;
            var container = Instantiate(prefab, position, Quaternion.identity, supplyParent);
            container.name = $"QuestSupply_{supply.questId}_{supply.item.itemId}";
            container.InitializeWithItems(supply.label, ContainerType.CivilianCrate, 1f, new[] { supply.item });
            return container;
        }

        // ---------- 工具 ----------

        public bool IsActive(string questId) => Model != null && Model.IsActive(questId);

        /// <summary>任务是否已完成；未知 ID 视为未完成。</summary>
        public bool IsCompleted(string questId)
        {
            if (Model == null || string.IsNullOrEmpty(questId)) return false;
            for (var i = 0; i < Model.Count; i++)
            {
                if (Model.IdAt(i) == questId) return Model.StatusOf(i) == QuestStatus.Completed;
            }

            return false;
        }

        /// <summary>任务标题，用于“完成 Q01「火不能灭」后解锁”之类的提示。</summary>
        public string TitleOf(string questId)
        {
            var quest = chain != null ? chain.quests.Find(q => q.questId == questId) : null;
            return quest != null ? quest.title : string.Empty;
        }

        private bool GeneratorRepaired()
        {
            var state = authority.Generator.State;
            return state == GeneratorState.Producing || state == GeneratorState.Operational;
        }

        private int MedicalCount()
        {
            var count = 0;
            foreach (var stack in Bag.Stacks)
            {
                if ((stack.Item.tags & ItemTags.Medical) != 0) count += stack.Quantity;
            }

            return count;
        }

        /// <summary>HUD 用：任务交互点的提示文字。</summary>
        public string Prompt(Map01Interactable station)
        {
            switch (station.Station)
            {
                case Map01Station.Hearth:
                    if (IsActive("Q01")) return "[E] 往炉火里添一份燃料";
                    if (IsActive("Q03")) return "[E] 清点药品（不会收走）";
                    return "炉火";
                case Map01Station.OldMap:
                    return IsActive("Q10") ? "[E] 把运送记录和旧地图对照" : "[E] 看看旧地图";
                default:
                    var record = chain.FindRecord(station.EntityId);
                    var read = record != null && Model.HasFact(record.fact);
                    return record == null ? "[E] 阅读" : $"[E] 阅读：{record.title}{(read ? "（已读）" : string.Empty)}";
            }
        }
    }
}
