using UnityEngine;
using UnityEngine.InputSystem;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 玩家一侧：找到最近的地图设施，按 E 向权威层发请求；商店里按 1–4 购买，工作台按 E 打开合成面板。
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
                return;
            }

            Focus = Map01Interactable.FindNearest(transform.position, Authority.InteractRange);
            var container = player.Interactor.Focus;
            if (Focus != null && container != null && container.HorizontalDistanceTo(transform.position) < Focus.HorizontalDistanceTo(transform.position))
            {
                Focus = null;
            }

            if (Focus == null || player.Interactor.IsSearching)
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
            var keyboard = Keyboard.current;
            if (keyboard == null) return -1;
            if (keyboard.digit1Key.wasPressedThisFrame) return 0;
            if (keyboard.digit2Key.wasPressedThisFrame) return 1;
            if (keyboard.digit3Key.wasPressedThisFrame) return 2;
            if (keyboard.digit4Key.wasPressedThisFrame) return 3;
            return -1;
        }


    }
}
