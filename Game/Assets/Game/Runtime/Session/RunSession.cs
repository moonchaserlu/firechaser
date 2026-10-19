using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildernessExtraction
{
    /// <summary>
    /// 单局流程：出生、暂停、撤离成功/死亡结算和重新开始。
    /// 结算规则在 RunSettlement 中，背包和仓库数据分别由 PlayerInventory 与 SessionStash 持有。
    /// </summary>
    public sealed class RunSession : MonoBehaviour
    {
        // 结算界面出现后，至少经过该秒数才接受 E 键重新开始，避免误触。
        private const float RestartInputDelay = 0.5f;

        [SerializeField] private PlayerCharacter player;
        [SerializeField] private ExtractionZone extractionZone;
        [SerializeField] private Transform playerSpawn;

        private float endedAtRealtime;

        public event Action<RunResult> Ended;
        public event Action<bool> PauseChanged;

        public PlayerCharacter Player => player;
        public ExtractionZone ExtractionZone => extractionZone;
        public Stash Stash => SessionStash.Current;
        public RunResult Result { get; private set; }
        public bool HasEnded => Result != null;
        public bool IsPaused { get; private set; }

        private void Awake()
        {
            Time.timeScale = 1f;
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerCharacter>();
            }

            if (extractionZone == null)
            {
                extractionZone = FindAnyObjectByType<ExtractionZone>();
            }
        }

        private void Start()
        {
            if (playerSpawn != null)
            {
                player.Motor.Teleport(playerSpawn.position);
                player.transform.rotation = playerSpawn.rotation;
            }

            player.Health.Died += OnPlayerDied;
            extractionZone.ExtractionCompleted += OnExtractionCompleted;
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.Health.Died -= OnPlayerDied;
            }

            if (extractionZone != null)
            {
                extractionZone.ExtractionCompleted -= OnExtractionCompleted;
            }

            if (IsPaused)
            {
                Time.timeScale = 1f;
            }
        }

        private void Update()
        {
            if (HasEnded)
            {
                if (player.Input.ConfirmPressed && Time.realtimeSinceStartup - endedAtRealtime >= RestartInputDelay)
                {
                    Restart();
                }

                return;
            }

            if (player.Input.PausePressed)
            {
                SetPaused(!IsPaused);
            }
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused || (paused && HasEnded))
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            player.Input.SetGameplayEnabled(!paused);
            PauseChanged?.Invoke(paused);
        }

        /// <summary>重新加载当前场景开始新一局；仓库保存在 SessionStash 中不受影响。</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            var scene = gameObject.scene;
            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
            }
            else
            {
                SceneManager.LoadScene(scene.name);
            }
        }

        private void OnExtractionCompleted()
        {
            if (!HasEnded)
            {
                End(RunSettlement.Extract(player.Inventory.Inventory, SessionStash.Current));
            }
        }

        private void OnPlayerDied()
        {
            if (!HasEnded)
            {
                End(RunSettlement.Die(player.Inventory.Inventory));
            }
        }

        private void End(RunResult result)
        {
            SetPaused(false);
            Result = result;
            endedAtRealtime = Time.realtimeSinceStartup;
            player.SetControlEnabled(false);
            extractionZone.enabled = false;
            Ended?.Invoke(result);
        }
    }
}
