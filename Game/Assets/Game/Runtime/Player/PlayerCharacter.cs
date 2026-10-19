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

        // 惰性获取：其他对象可能在本组件 Awake 之前访问。
        public Health Health => Cached(ref health);
        public PlayerInputReader Input => Cached(ref input);
        public PlayerMotor Motor => Cached(ref motor);
        public PlayerShooter Shooter => Cached(ref shooter);
        public PlayerInventory Inventory => Cached(ref inventory);
        public PlayerInteractor Interactor => Cached(ref interactor);

        private void Awake()
        {
            Health.Configure(config.maxHealth);
        }

        /// <summary>结算后冻结玩家：停止移动、射击和搜索。</summary>
        public void SetControlEnabled(bool enabled)
        {
            Input.SetGameplayEnabled(enabled);
            Motor.enabled = enabled;
            Shooter.enabled = enabled;
            Interactor.enabled = enabled;
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
