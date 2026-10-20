using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>玩家根组件：汇总玩家各功能组件的引用，并统一开关操控。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health), typeof(PlayerInputReader), typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerShooter), typeof(PlayerInventory), typeof(PlayerInteractor))]
    public sealed class PlayerCharacter : MonoBehaviour
    {
        [SerializeField] private PlayerConfig config;

        private Health health;
        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerShooter shooter;
        private PlayerInventory inventory;
        private PlayerInteractor interactor;
        private LightExposure exposure;
        private CampfirePlacer placer;
        private BleedingStatus bleeding;

        // 惰性获取：其他对象可能在本组件 Awake 之前访问。
        public Health Health => Cached(ref health);
        public PlayerInputReader Input => Cached(ref input);
        public PlayerMotor Motor => Cached(ref motor);
        public PlayerShooter Shooter => Cached(ref shooter);
        public PlayerInventory Inventory => Cached(ref inventory);
        public PlayerInteractor Interactor => Cached(ref interactor);
        /// <summary>可能为空：旧预制体未挂载黑暗暴露组件。</summary>
        public LightExposure Exposure => Cached(ref exposure);
        /// <summary>可能为空：旧预制体未挂载建火组件。</summary>
        public CampfirePlacer Placer => Cached(ref placer);
        /// <summary>可能为空：旧预制体未挂载流血组件。</summary>
        public BleedingStatus Bleeding => Cached(ref bleeding);

        private void Awake()
        {
            Health.Configure(config.maxHealth);
        }

        /// <summary>结算后冻结玩家：停止移动、射击和搜索。</summary>
        /// <summary>结算（撤离成功或死亡）后为 false；怪物据此清除目标。</summary>
        public bool ControlEnabled { get; private set; } = true;

        public void SetControlEnabled(bool enabled)
        {
            ControlEnabled = enabled;
            if (Placer != null)
            {
                Placer.enabled = enabled;
            }

            Input.SetGameplayEnabled(enabled);
            Motor.enabled = enabled;
            Shooter.enabled = enabled;
            Interactor.enabled = enabled;
            if (Exposure != null)
            {
                Exposure.enabled = enabled;
            }
        }

        private T Cached<T>(ref T field) where T : Component
        {
            if (field == null)
            {
                field = GetComponent<T>();
            }

            return field;
        }
    }
}
