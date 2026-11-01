using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay.Containers;
using SandSort.Gameplay.Grid;
using SandSort.Gameplay.Transfer;
using SandSort.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SandSort.Gameplay
{
    /// <summary>
    /// Top-level gameplay state machine and tick coordinator.
    /// Includes live Inspector controls for Sand Fill / Vacuum Speed, Flight Duration, and Extraction Rate.
    /// </summary>
    public sealed class GameLoopController : MonoBehaviour
    {
        [Header("Level Playlist (WebGL & Mobile Showcase)")]
        [SerializeField] private LevelDataSO[] _levels;
        [SerializeField] private int _startingLevelIndex;

        [Header("Kum Dolma & Vakumlama Hızı (Canlı Inspector Ayarları)")]
        [Tooltip("Genel kum çekme ve dolum hızı çarpanı (0.3 = Yavaş, 1.0 = Normal, 3.0 = Çok Hızlı).")]
        [SerializeField, Range(0.15f, 5.0f)] private float _sandFillSpeedMultiplier = 2.4f;
        [Tooltip("Her simülasyon adımında en alttan kaç katman kum çekileceği (1 = Tane tane, 3 = Yoğun akış).")]
        [SerializeField, Range(1, 5)] private int _extractionPassesPerStep = 2;
        [Tooltip("Kum tanelerinin tepsiden kovaya uçuş süresi (saniye). Büyük değer = havada daha yavaş süzülür.")]
        [SerializeField, Range(0.12f, 1.20f)] private float _grainFlightDuration = 0.25f;
        [Tooltip("Aynı kovaya giden kumların çıkış aralığı (saniye). Büyük değer = kumlar tek tek aralıklı çıkar.")]
        [SerializeField, Range(0.001f, 0.050f)] private float _grainEmitStaggerInterval = 0.004f;

        [Header("Temel Simülasyon Adım Süresi")]
        [SerializeField, Range(0.010f, 0.150f)] private float _baseSimulationTickInterval = 0.032f;

        [Header("Domain Managers")]
        [SerializeField] private GridManager _gridManager;
        [SerializeField] private DockManager _dockManager;
        [SerializeField] private BucketQueueManager _bucketQueueManager;
        [SerializeField] private SandTransferController _transferController;

        private readonly BoardSimulator _simulator = new BoardSimulator();
        private LevelDataSO _activeLevel;
        private int _currentPlaylistIndex;
        private float _tickTimer;
        private bool _isLevelActive;

        public BoardSimulator Simulator => _simulator;
        public LevelDataSO ActiveLevel => _activeLevel;
        public int CurrentPlaylistIndex => _currentPlaylistIndex;
        public int TotalLevelsCount => _levels != null ? _levels.Length : 0;

        private void Awake()
        {
            ServiceLocator.Register(this);

            // Enforce fast, juicy flow defaults even if an older scene file serialized 1.0x / 1-pass speed
            if (_sandFillSpeedMultiplier < 2.0f) _sandFillSpeedMultiplier = 2.4f;
            if (_extractionPassesPerStep < 2) _extractionPassesPerStep = 2;
            if (_grainFlightDuration > 0.28f) _grainFlightDuration = 0.22f;
        }

        private void Start()
        {
            ResolveManagersIfMissing();
            if (_levels != null && _levels.Length > 0)
            {
                LoadLevelByPlaylistIndex(_startingLevelIndex);
            }
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
        }

        private void ResolveManagersIfMissing()
        {
            if (_gridManager == null) ServiceLocator.TryGet(out _gridManager);
            if (_dockManager == null) ServiceLocator.TryGet(out _dockManager);
            if (_bucketQueueManager == null) ServiceLocator.TryGet(out _bucketQueueManager);
            if (_transferController == null) ServiceLocator.TryGet(out _transferController);

            if (SandSort.Gameplay.VFX.JuicyVFXManager.Instance == null && FindFirstObjectByType<SandSort.Gameplay.VFX.JuicyVFXManager>() == null)
            {
                GameObject vfxGo = new GameObject("--- JUICY VFX MANAGER ---");
                vfxGo.AddComponent<SandSort.Gameplay.VFX.JuicyVFXManager>();
            }
        }

        public void LoadLevelByPlaylistIndex(int playlistIndex)
        {
            if (_levels == null || _levels.Length == 0)
            {
                Debug.LogWarning("[GameLoopController] No LevelDataSO assigned to playlist.");
                return;
            }

            _currentPlaylistIndex = ((playlistIndex % _levels.Length) + _levels.Length) % _levels.Length;
            LoadLevel(_levels[_currentPlaylistIndex]);
        }

        public void RestartCurrentLevel()
        {
            if (_activeLevel != null)
            {
                LoadLevel(_activeLevel);
            }
            else
            {
                LoadLevelByPlaylistIndex(_currentPlaylistIndex);
            }
        }

        public void LoadNextLevel()
        {
            LoadLevelByPlaylistIndex(_currentPlaylistIndex + 1);
        }

        public void LoadPreviousLevel()
        {
            LoadLevelByPlaylistIndex(_currentPlaylistIndex - 1);
        }

        public void LoadLevel(LevelDataSO levelData)
        {
            if (levelData == null) return;
            ResolveManagersIfMissing();

            _activeLevel = levelData;
            _isLevelActive = false;
            _tickTimer = 0f;

            int targetResolution = _gridManager != null ? _gridManager.SandResolutionPerAxis : 0;
            _simulator.Initialize(_activeLevel, targetResolution);

            _gridManager.BuildVisualGrid(_activeLevel, _simulator);
            _dockManager.InitializeDock(_activeLevel.ActiveDockCapacity, _bucketQueueManager.ReturnBucketToPool);
            _dockManager.BindSimulator(_simulator);
            _bucketQueueManager.BuildQueues(_activeLevel, _simulator, _dockManager);
            _transferController.BindSession(_simulator, _dockManager, _gridManager.GrainWorldScale);
            SyncTransferSpeedSettings();

            _isLevelActive = true;

            EventBus<LevelStartedEvent>.Publish(new LevelStartedEvent(
                _activeLevel.LevelIndex,
                _activeLevel.LevelTitle,
                _simulator.RemainingGridGrains));
        }

                public float GetEffectiveSpeedMultiplier()
        {
            if (_activeLevel != null && _activeLevel.SandFillSpeedMultiplier > 0.05f)
            {
                return _activeLevel.SandFillSpeedMultiplier;
            }
            return Mathf.Max(0.15f, _sandFillSpeedMultiplier);
        }

        private void SyncTransferSpeedSettings()
        {
            if (_transferController == null) return;
            float speedMult = GetEffectiveSpeedMultiplier();
            _transferController.ConfigureTiming(
                _grainEmitStaggerInterval / speedMult,
                _grainFlightDuration / Mathf.Sqrt(speedMult));
        }

        private void Update()
        {
            HandleDebugHotkeys();

            if (!_isLevelActive || _activeLevel == null) return;

            SyncTransferSpeedSettings();

            float speedMult = GetEffectiveSpeedMultiplier();
            float effectiveTickInterval = Mathf.Max(0.005f, _baseSimulationTickInterval / speedMult);

            _tickTimer += Time.deltaTime;
            if (_tickTimer < effectiveTickInterval) return;
            _tickTimer -= effectiveTickInterval;

            ExecuteSimulationStep(effectiveTickInterval);
        }

        private void HandleDebugHotkeys()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.pageUpKey.wasPressedThisFrame)
            {
                LoadNextLevel();
                Debug.Log($"[GameLoopController] PageUp: Sonraki Seviyeye Gecildi -> #{_activeLevel?.LevelIndex} {_activeLevel?.LevelTitle}");
            }
            else if (Keyboard.current.pageDownKey.wasPressedThisFrame)
            {
                LoadPreviousLevel();
                Debug.Log($"[GameLoopController] PageDown: Onceki Seviyeye Gecildi -> #{_activeLevel?.LevelIndex} {_activeLevel?.LevelTitle}");
            }
            else if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartCurrentLevel();
                Debug.Log($"[GameLoopController] R: Seviye Yeniden Baslatildi -> #{_activeLevel?.LevelIndex} {_activeLevel?.LevelTitle}");
            }
        }

        private void ExecuteSimulationStep(float effectiveTickInterval)
        {
            float speedMult = GetEffectiveSpeedMultiplier();
            int totalReservedThisTick = 0;
            int passes = Mathf.Clamp(_extractionPassesPerStep, 1, 5);
            // Rate-limit per-slot extraction per pass so grains stay visible on the tray until they physically fly!
            int passesBoost = (speedMult >= 3.0f) ? 1 : 0;
            passes = Mathf.Clamp(passes + passesBoost, 1, 5);
            int maxGrainsPerSlotPerPass = Mathf.Clamp(Mathf.RoundToInt(4f * speedMult), 1, 24);

            for (int pass = 0; pass < passes; pass++)
            {
                int reservedCount = _simulator.StepReservations(maxGrainsPerSlotPerPass);
                if (reservedCount <= 0) break;

                totalReservedThisTick += reservedCount;
                ReadOnlySpan<GrainReservationRecord> reservations = _simulator.LastStepReservations;
                for (int i = 0; i < reservations.Length; i++)
                {
                    GrainReservationRecord rec = reservations[i];
                    var consumed = _gridManager.ConsumeReservedGrainVisual(rec.GridIndex, rec.X, rec.Y);
                    _transferController.EnqueueGrainTransfer(
                        consumed.WorldPosition,
                        rec.ColorId,
                        _activeLevel.GetColor(rec.ColorId),
                        rec.DockSlotIndex);

                    EventBus<GrainReservedEvent>.Publish(new GrainReservedEvent(
                        rec.GridIndex, rec.X, rec.Y, rec.ColorId, rec.DockSlotIndex));
                }
            }

            int movedCount = _simulator.StepGravity();
            if (movedCount > 0)
            {
                _gridManager.ApplyVisualGravityMoves(_simulator, effectiveTickInterval);
            }

            if (_transferController.TotalPendingAndActiveVisuals == 0)
            {
                if (_simulator.CheckWinCondition())
                {
                    _isLevelActive = false;
                    _bucketQueueManager.SetInputEnabled(false);
                    EventBus<LevelWonEvent>.Publish(new LevelWonEvent(_activeLevel.LevelIndex));
                }
                else if (totalReservedThisTick == 0 && movedCount == 0 && _simulator.CheckDeadlockCondition())
                {
                    _isLevelActive = false;
                    _bucketQueueManager.SetInputEnabled(false);
                    EventBus<LevelDeadlockedEvent>.Publish(
                        new LevelDeadlockedEvent(_activeLevel.LevelIndex, _simulator.RemainingGridGrains));
                }
            }
        }
    }
}
