using System;
using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>某一段里的一句提示：afterFact 到齐后换成这一句（留空表示进入该段时就显示）。</summary>
    [Serializable]
    public sealed class TutorialLine
    {
        public string afterFact = string.Empty;
        [TextArea(1, 3)] public string text = string.Empty;
    }

    [Serializable]
    public sealed class TutorialStep
    {
        public string stepId = "step";
        [Tooltip("右上角显示的段名。")]
        public string title = string.Empty;
        [Tooltip("这一段需要的事实，全部到齐才完成。事实名见 TutorialFacts。")]
        public string[] requiredFacts = Array.Empty<string>();
        [Tooltip("按顺序排列：显示最后一条 afterFact 已到齐的提示。")]
        public TutorialLine[] lines = Array.Empty<TutorialLine>();
    }

    /// <summary>
    /// 开局教学关《最后一段路》：段落、提示文案和只在教学里生效的规则偏差。
    /// 只在缺失时由编辑器生成，之后由策划在 Inspector 里改，重建场景不会覆盖。
    /// </summary>
    [CreateAssetMenu(menuName = "Wilderness/Tutorial/Tutorial Config", fileName = "TutorialConfig")]
    public sealed class TutorialConfig : ScriptableObject
    {
        public TutorialStep[] steps = Array.Empty<TutorialStep>();

        [Header("字幕")]
        [TextArea(1, 3)] public string openingCaption = "火把只剩一点余烬。\n前人沿路立了一排油灯，还没来得及点亮。";
        [Min(0f)] public float openingSeconds = 4f;
        [TextArea(1, 3)] public string endingCaption = "墙后是一座还算完好的地下安全屋。\n从这里出发，去找火。";
        [Min(0f)] public float endingSeconds = 3f;

        [Header("引导")]
        [Tooltip("同一段这么多秒没有任何进展，提示条闪烁一次重新提醒。")]
        [Min(1f)] public float idleHintSeconds = 12f;
        [Tooltip("累计移动这么多米记为“会走路了”。")]
        [Min(0.5f)] public float moveDistance = 3f;
        [Tooltip("门前连续停留这么多秒完成教学；受伤或离开会中断。")]
        [Min(0.5f)] public float exitHoldSeconds = 3f;

        [Header("教学规则偏差（只在教学关生效）")]
        [Tooltip("黑暗伤害不会让生命低于该值：先教会“疼”，不教“死”。")]
        [Min(1f)] public float darknessHealthFloor = 30f;
        [Tooltip("被怪物打到生命低于该值时倒下，在最近一盏已点亮的引路灯旁满血醒来。须大于教学怪物的单次伤害。")]
        [Min(1f)] public float knockdownHealth = 15f;
        [Tooltip("最后一段倒下这么多次后撤走一只巡逻失火者，避免卡关。")]
        [Min(1)] public int knockdownsBeforeEasing = 2;

        [Header("完成后")]
        [Tooltip("走完或跳过教学后进入的场景，存档也改记为它：地下安全屋，玩家在那里自己决定什么时候出去。")]
        public string nextScenePath = GameScenes.Shelter;
    }

    /// <summary>教学导演认得的事实名，配置里的 requiredFacts / afterFact 只能用这些。</summary>
    public static class TutorialFacts
    {
        public const string Moved = "moved";
        public const string Sprinted = "sprinted";
        public const string FeltWarning = "felt_warning";
        public const string BackpackOpened = "backpack_opened";
        public const string HasFuel = "has_fuel";
        public const string CampfirePreview = "campfire_preview";
        public const string CampfireBuilt = "campfire_built";
        public const string Dodged = "dodged";
        public const string EnemyAttacking = "enemy_attacking";
        public const string PistolTaken = "pistol_taken";
        public const string TargetHit = "target_hit";
        public const string TargetsCleared = "targets_cleared";
        public const string MagazineEmpty = "magazine_empty";
        public const string Reloaded = "reloaded";
        public const string AtExit = "at_exit";
        public const string ExitHeld = "exit_held";
        public const string ExplosiveTaken = "explosive_taken";
        public const string ChargePlaced = "charge_placed";
        public const string WallBlown = "wall_blown";
        /// <summary>走到炸开的墙洞口：之后由过场接管，角色自己走下台阶。</summary>
        public const string StairsReached = "stairs_reached";
        /// <summary>过场播完：已经下到安全屋。</summary>
        public const string EnteredShelter = "entered_shelter";
    }
}
