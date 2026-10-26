using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// Map01 单局流程：Esc 暂停/继续；结算（救援成功或死亡）后按 E 重新开始。
    /// 仓库保存在 SessionStash 中，重开不受影响。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Map01RunFlow : MonoBehaviour
    {
        // 结算出现后至少经过该秒数才接受 E 键重开，避免误触。
        private const float RestartInputDelay = 0.5f;

        /// <summary>
        /// 场景重载方式。编辑器里由编辑器脚本注入按路径加载（场景未登记 Build Settings 时也能重开）；
        /// 运行时代码不引用 UnityEditor。
        /// </summary>
        public static Action<Scene> ReloadOverride;

        [SerializeField] private Map01Authority authority;

        private float endedAtRealtime = -1f;

        public bool IsPaused { get; private set; }
        public event Action<bool> PauseChanged;

        private Map01Authority Authority => authority != null ? authority : authority = FindAnyObjectByType<Map01Authority>();

        private void Start()
        {
            Time.timeScale = 1f;
            Authority.RunEnded += _ =>
            {
                SetPaused(false);
                endedAtRealtime = Time.realtimeSinceStartup;
            };
        }

        private void OnDestroy()
        {
            if (IsPaused)
            {
                Time.timeScale = 1f;
            }
        }

        private void Update()
        {
            var player = Authority.Player;
            if (Authority.Outcome != Map01RunOutcome.InProgress)
            {
                if (player.Input.ConfirmPressed && endedAtRealtime >= 0f && Time.realtimeSinceStartup - endedAtRealtime >= RestartInputDelay)
                {
                    Restart();
                }

                return;
            }

            // 建火预览/建造中 Esc 用于取消，不同时触发暂停。
            if (player.Input.PausePressed && (player.Placer == null || !player.Placer.ConsumesCancelThisFrame))
            {
                SetPaused(!IsPaused);
            }
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused || (paused && Authority.Outcome != Map01RunOutcome.InProgress))
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Authority.Player.Input.SetGameplayEnabled(!paused);
            PauseChanged?.Invoke(paused);
        }

        /// <summary>重新加载当前场景开始新一局。</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            var scene = gameObject.scene;
            if (ReloadOverride != null)
            {
                ReloadOverride(scene);
            }
            else if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
            }
            else
            {
                SceneManager.LoadScene(scene.name);
            }
        }
    }
}
