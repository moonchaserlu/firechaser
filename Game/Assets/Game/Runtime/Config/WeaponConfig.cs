using UnityEngine;

namespace WildernessExtraction
{
    public enum WeaponKind { Firearm, Melee }
    public enum AmmoCaliber { None, NineMillimeter, TwelveGauge, FiveFiveSix }

    /// <summary>基础武器数值。默认值来自 VerticalSlicePlan 第 4 节。</summary>
    [CreateAssetMenu(menuName = "Wilderness/Config/Weapon", fileName = "WeaponConfig")]
    public sealed class WeaponConfig : ScriptableObject
    {
        [Min(1)] public int magazineSize = 8;
        public string weaponId = "old_pistol";
        public string displayName = "旧手枪";
        public WeaponKind kind = WeaponKind.Firearm;
        public AmmoCaliber caliber = AmmoCaliber.NineMillimeter;
        public ItemDefinition ammoItem;
        [Min(0f)] public float damage = 25f;
        [Tooltip("两次射击之间的最短间隔（秒）。")]
        [Min(0.01f)] public float fireInterval = 0.25f;
        [Min(0f)] public float reloadDuration = 1.4f;

        [Tooltip("射线最大距离（米）。策划未给定，灰盒临时值。")]
        [Min(1f)] public float range = 25f;
        [Min(0f)] public float effectiveRange = 18f;
        [Range(0f, 1f)] public float minimumDamageMultiplier = 0.7f;
        [Min(0)] public int rayCount = 1;
        [Range(0f, 45f)] public float standingSpread = 2f;
        [Range(0f, 45f)] public float movingSpread = 4f;
        [Min(0f)] public float soundRadius = 14f;
        [Min(0f)] public float staminaCost;
        [Range(0f, 180f)] public float meleeAngle = 80f;
    }
}
