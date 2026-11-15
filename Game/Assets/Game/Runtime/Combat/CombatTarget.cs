using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    public enum CombatTargetKind
    {
        Player,
        Bot,
    }

    /// <summary>
    /// 怪物可索敌的目标（玩家与 AI Bot）。怪物对所有目标使用同一套视觉、追击与攻击规则。
    /// 死亡、结算或撤离离场的目标自动失效。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class CombatTarget : MonoBehaviour
    {
        private static readonly List<CombatTarget> active = new List<CombatTarget>();

        [SerializeField] private CombatTargetKind kind = CombatTargetKind.Player;

        private Health health;
        private BleedingStatus bleeding;
        private PlayerCharacter player;
        private AiBotController bot;

        public static IReadOnlyList<CombatTarget> Active => active;

        public CombatTargetKind Kind => kind;
        public Health Health => health != null ? health : health = GetComponent<Health>();
        /// <summary>可能为空：目前只有玩家会流血。</summary>
        public BleedingStatus Bleeding => bleeding;

        public bool IsValid
        {
            get
            {
                if (!isActiveAndEnabled || Health.IsDead) return false;
                if (player != null && !player.ControlEnabled) return false;
                return bot == null || bot.IsAlive;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            active.Clear();
        }

        private void Awake()
        {
            health = GetComponent<Health>();
            bleeding = GetComponent<BleedingStatus>();
            player = GetComponent<PlayerCharacter>();
            bot = GetComponent<AiBotController>();
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        public static CombatTarget FindPlayer()
        {
            foreach (var target in active)
            {
                if (target.kind == CombatTargetKind.Player) return target;
            }

            return null;
        }
    }
}
