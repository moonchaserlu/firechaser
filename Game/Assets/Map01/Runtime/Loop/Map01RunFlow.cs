using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// Map01 单局流程：Esc 暂停/继续；阅读剧情记录、使用工作台时同样暂停（阅读 E 或 Esc 合上，工作台 Esc 离开）；结算（救援成功或死亡）后按 E 重新开始。
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
        private int readingOpenedFrame = -1;
        private int readingClosedFrame = -1;

        public bool IsPaused { get; private set; }
        public event Action<bool> PauseChanged;
        /// <summary>正在阅读记录：时间暂停，黑暗、怪物和发电机都不推进。</summary>
        public bool IsReading { get; private set; }
        public string ReadingTitle { get; private set; } = string.Empty;
        public string ReadingBody { get; private set; } = string.Empty;
        /// <summary>正在使用工作台：时间暂停，由合成面板接管输入。</summary>
        public bool IsCrafting { get; private set; }
        public Map01Interactable CraftingStation { get; private set; }

        private Map01Authority Authority => authority != null ? authority : authority = FindAnyObjectByType<Map01Authority>();

        private void Start()
        {
            Time.timeScale = 1f;
            Authority.RunEnded += _ =>
            {
                CloseReading();
                CloseCrafting();
                SetPaused(false);
                endedAtRealtime = Time.realtimeSinceStartup;
            };
        }

        private void OnDestroy()
        {
            if (IsPaused || IsReading || IsCrafting)
            {
                Time.timeScale = 1f;
            }
        }

        private void Update()
        {
            var player = Authority.Player;
            // 合上记录的那一下 E 不应同帧触发其他交互，下一帧再恢复操作。
            if (readingClosedFrame >= 0 && Time.frameCount > readingClosedFrame)
            {
                readingClosedFrame = -1;
                if (Authority.Outcome == Map01RunOutcome.InProgress && !IsPaused && !IsReading && !IsCrafting) player.Input.SetGameplayEnabled(true);
            }

            if (Authority.Outcome != Map01RunOutcome.InProgress)
            {
                if (player.Input.ConfirmPressed && endedAtRealtime >= 0f && Time.realtimeSinceStartup - endedAtRealtime >= RestartInputDelay)
                {
                    Restart();
                }

                return;
            }

            if (IsReading)
            {
                // 打开记录的那一下 E 不算合上。
                if (Time.frameCount > readingOpenedFrame && (player.Input.ConfirmPressed || player.Input.PausePressed))
                {
                    CloseReading();
                }

                return;
            }

            if (IsCrafting)
            {
                if (player.Input.PausePressed) CloseCrafting();
                return;
            }

            // 建火预览/建造中 Esc 用于取消，不同时触发暂停。
            if (player.Input.PausePressed && (player.Placer == null || !player.Placer.ConsumesCancelThisFrame))
            {
                SetPaused(!IsPaused);
            }
        }

        /// <summary>打开一份记录：沿用暂停方式冻结时间，阅读时不会受黑暗伤害。</summary>
        public void OpenReading(string title, string body)
        {
            if (Authority.Outcome != Map01RunOutcome.InProgress || IsPaused || IsCrafting)
            {
                return;
            }

            ReadingTitle = title ?? string.Empty;
            ReadingBody = body ?? string.Empty;
            readingOpenedFrame = Time.frameCount;
            if (!IsReading)
            {
                IsReading = true;
                Time.timeScale = 0f;
                Authority.Player.Input.SetGameplayEnabled(false);
            }
        }

        public void CloseReading()
        {
            if (!IsReading)
            {
                return;
            }

            IsReading = false;
            Time.timeScale = 1f;
            readingClosedFrame = Time.frameCount;
        }

        /// <summary>打开工作台：冻结时间、关闭操作，合成面板接管鼠标与键盘。</summary>
        public void OpenCrafting(Map01Interactable station)
        {
            if (station == null || Authority.Outcome != Map01RunOutcome.InProgress || IsPaused || IsReading || IsCrafting)
            {
                return;
            }

            IsCrafting = true;
            CraftingStation = station;
            Time.timeScale = 0f;
            Authority.Player.Input.SetGameplayEnabled(false);
        }

        public void CloseCrafting()
        {
            if (!IsCrafting)
            {
                return;
            }

            IsCrafting = false;
            CraftingStation = null;
            Time.timeScale = 1f;
            readingClosedFrame = Time.frameCount;
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused || (paused && (Authority.Outcome != Map01RunOutcome.InProgress || IsReading || IsCrafting)))
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
