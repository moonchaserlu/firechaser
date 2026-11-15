using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 玩家一侧：找到最近的地图设施，按 F（E 备用）向权威层发请求；商店里按 1–4 购买（此时数字键不切武器、不用物品），工作台按 F 打开合成面板。
    /// 附近有可搜索容器且更近时让给现有的 PlayerInteractor，两者不会同时响应。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class Map01Interactor : MonoBehaviour
    {
        [SerializeField] private Map01Authority authority;

        private PlayerCharacter player;
        private int sequence;

        public Map01Interactable Focus { get; private set; }

        private Map01Authority Authority => authority != null ? authority : authority = FindAnyObjectByType<Map01Authority>();
        private Map01RunFlow flow;
        private Map01RunFlow Flow => flow != null ? flow : flow = FindAnyObjectByType<Map01RunFlow>();

        private void Awake()
        {
            player = GetComponent<PlayerCharacter>();
        }

        private void Update()
        {
            if (Authority == null || !player.ControlEnabled)
            {
                Focus = null;
                player.Input.HotkeysSuppressed = false;
                return;
            }

            Focus = Map01Interactable.FindNearest(transform.position, Authority.InteractRange);
            var container = player.Interactor.Focus;
            if (Focus != null && container != null && container.HorizontalDistanceTo(transform.position) < Focus.HorizontalDistanceTo(transform.position))
            {
                Focus = null;
            }

            // 站在商店前时 1–4 用于购买，让 PlayerShooter / PlayerItemUser 忽略数字键。
            player.Input.HotkeysSuppressed = Focus != null && Focus.Station == Map01Station.ShopTerminal;

            // 背包、工作台、暂停时操作已关闭：数字键不再买东西。
            if (Focus == null || player.Interactor.IsSearching || !player.Input.GameplayEnabled)
            {
                return;
            }

            if (Focus.Station == Map01Station.ShopTerminal)
            {
                var index = ShopKey();
                if (index >= 0)
                {
                    Send(InteractionAction.Buy, index);
                }

                return;
            }

            if (Focus.Station == Map01Station.Workbench)
            {
                if (player.Input.InteractPressed && Flow != null) Flow.OpenCrafting(Focus);
                return;
            }

            if (player.Input.InteractPressed)
            {
                Send(DefaultAction(Focus), 0);
            }
        }

        public InteractionResult Send(InteractionAction action, int argument)
        {
            return SendTo(Focus, action, argument);
        }

        /// <summary>向指定设施发请求（合成面板打开时世界暂停，Focus 不再刷新）。</summary>
        public InteractionResult SendTo(Map01Interactable station, InteractionAction action, int argument)
        {
            return station == null ? InteractionResult.UnknownEntity : Authority.Request(new InteractionRequest(station.EntityId, action, ++sequence, argument));
        }

        /// <summary>在背包里合成：不需要站在工作台旁。</summary>
        public InteractionResult SendFieldCraft(int recipeIndex)
        {
            return Authority.Request(new InteractionRequest(Map01Authority.FieldCraftEntity, InteractionAction.Craft, ++sequence, recipeIndex));
        }

        private InteractionAction DefaultAction(Map01Interactable station)
        {
            switch (station.Station)
            {
                case Map01Station.RecycleTerminal:
                    return InteractionAction.Sell;
                case Map01Station.BatterySlot:
                    return InteractionAction.DepositBattery;
                case Map01Station.Altar:
                    return InteractionAction.Sacrifice;
                case Map01Station.Hearth:
                case Map01Station.Record:
                case Map01Station.OldMap:
                    return InteractionAction.Quest;
                default:
                    var generator = Authority.Generator;
                    return generator.State == GeneratorState.Producing || generator.State == GeneratorState.Operational
                        ? InteractionAction.ChargeBattery
                        : InteractionAction.RepairGenerator;
            }
        }

        private static int ShopKey()
        {
            return PlayerInputReader.DigitPressedThisFrame(4);
        }


    }
}
