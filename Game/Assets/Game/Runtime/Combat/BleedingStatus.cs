using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 玩家流血：近战命中时由敌人调用 Bleed()。流血期间移动会在地面留下血迹（有效光内不留），
    /// 血迹写入 BloodTrailRegistry.Scene 供怪物循迹，并同步显示/移除地面标记。血迹只属于本局。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BleedingStatus : MonoBehaviour
    {
        [SerializeField] private BleedingConfig config;
        [Tooltip("地面血迹外观（无碰撞体）。")]
        [SerializeField] private GameObject markPrefab;

        private readonly Dictionary<int, GameObject> markViews = new Dictionary<int, GameObject>();
        private readonly List<int> staleViews = new List<int>();
        private BleedingModel model;
        private Health health;
        private Vector3 lastPosition;
        private float lastMovedTime = float.NegativeInfinity;

        public event Action BleedingStarted;

        public BleedingConfig Config => config;
        public bool IsBleeding => Model.IsBleeding;
        public float Remaining => Model.Remaining;

        private BleedingModel Model => model ??= config != null
            ? new BleedingModel(config.bleedDuration, config.dropInterval)
            : new BleedingModel(8f, 1f);

        private static BloodTrailRegistry Registry => BloodTrailRegistry.Scene;

        private void Awake()
        {
            health = GetComponent<Health>();
            // 新一局（场景重载）开始时清空上一局残留的血迹记录。
            Registry.Clear();
            if (config != null)
            {
                Registry.MaxMarks = config.maxMarks;
                Registry.Lifetime = config.markLifetime;
            }

            lastPosition = transform.position;
        }

        private void OnDestroy()
        {
            Registry.Clear();
        }

        /// <summary>开始或刷新流血（不叠层）。</summary>
        public void Bleed()
        {
            if (health != null && health.IsDead) return;
            var wasBleeding = Model.IsBleeding;
            Model.Bleed();
            if (!wasBleeding) BleedingStarted?.Invoke();
        }

        /// <summary>止血（绷带、急救喷雾）。</summary>
        public void StopBleeding()
        {
            Model.Stop();
        }

        private void Update()
        {
            var position = transform.position;
            var moved = position - lastPosition;
            moved.y = 0f;
            if (moved.sqrMagnitude > 0.000001f) lastMovedTime = Time.time;
            lastPosition = position;

            if (health != null && health.IsDead)
            {
                Model.Stop();
            }

            var grace = config != null ? config.movingGrace : 0.25f;
            var moving = Time.time - lastMovedTime <= grace;
            if (Model.Tick(Time.deltaTime, moving, LightRegistry.Scene.IsLit(position)))
            {
                Registry.Add(position, Time.time);
            }

            Registry.Prune(Time.time);
            SyncViews();
        }

        private void SyncViews()
        {
            staleViews.Clear();
            foreach (var id in markViews.Keys)
            {
                var alive = false;
                foreach (var mark in Registry.Marks)
                {
                    if (mark.Id == id) { alive = true; break; }
                }

                if (!alive) staleViews.Add(id);
            }

            foreach (var id in staleViews)
            {
                if (markViews[id] != null) Destroy(markViews[id]);
                markViews.Remove(id);
            }

            if (markPrefab == null) return;
            foreach (var mark in Registry.Marks)
            {
                if (markViews.ContainsKey(mark.Id)) continue;
                var view = Instantiate(markPrefab, mark.Position + Vector3.up * 0.02f, Quaternion.Euler(0f, mark.Id * 47f, 0f));
                markViews.Add(mark.Id, view);
            }
        }
    }
}
