using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay.Containers;
using SandSort.Gameplay.Grid;
using SandSort.Gameplay.Transfer;
using SandSort.Simulation;
using UnityEngine;

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
        [SerializeField, Range(0.15f, 5.0f)] private float _sandFillSpeedMultiplier = 1.0f;
        [Tooltip("Her simülasyon adımında en alttan kaç katman kum çekileceği (1 = Tane tane, 3 = Yoğun akış).")]
        [SerializeField, Range(1, 5)] private int _extractionPassesPerStep = 1;
        [Tooltip("Kum tanelerinin tepsiden kovaya uçuş süresi (saniye). Büyük değer = havada daha yavaş süzülür.")]
        [SerializeField, Range(0.12f, 1.20f)] private float _grainFlightDuration = 0.38f;
        [Tooltip("Aynı kovaya giden kumların çıkış aralığı (saniye). Büyük değer = kumlar tek tek aralıklı çıkar.")]
        [SerializeField, Range(0.001f, 0.050f)] private float _grainEmitStaggerInterval = 0.006f;

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
            _bucketQueueManager.BuildQueues(_activeLevel, _simulator, _dockManager);
            _transferController.BindSession(_simulator, _dockManager, _gridManager.GrainWorldScale);
            SyncTransferSpeedSettings();

            _isLevelActive = true;

            EventBus<LevelStartedEvent>.Publish(new LevelStartedEvent(
                _activeLevel.LevelIndex,
                _activeLevel.LevelTitle,
                _simulator.RemainingGridGrains));
        }

        private void SyncTransferSpeedSettings()
        {
            if (_transferController == null) return;
            float speedMult = Mathf.Max(0.15f, _sandFillSpeedMultiplier);
            _transferController.ConfigureTiming(
                _grainEmitStaggerInterval / speedMult,
                _grainFlightDuration / Mathf.Sqrt(speedMult));
        }

        private void Update()
        {
            if (!_isLevelActive || _activeLevel == null) return;

            SyncTransferSpeedSettings();

            float speedMult = Mathf.Max(0.15f, _sandFillSpeedMultiplier);
            float effectiveTickInterval = Mathf.Max(0.005f, _baseSimulationTickInterval / speedMult);

            _tickTimer += Time.deltaTime;
            if (_tickTimer < effectiveTickInterval) return;
            _tickTimer -= effectiveTickInterval;

            ExecuteSimulationStep(effectiveTickInterval);
        }

        private void ExecuteSimulationStep(float effectiveTickInterval)
        {
            int totalReservedThisTick = 0;
            int passes = Mathf.Clamp(_extractionPassesPerStep, 1, 5);
            // Rate-limit per-slot extraction per pass so grains stay visible on the tray until they physically fly!
            int maxGrainsPerSlotPerPass = Mathf.Clamp(Mathf.RoundToInt(4f * _sandFillSpeedMultiplier), 1, 14);

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
