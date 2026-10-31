using DG.Tweening;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SandSort.UI
{
    /// <summary>
    /// Event-driven UI controller listening to <see cref="EventBus{T}"/> channels for
    /// level start, extraction progress percentage, Win, and Deadlock (Fail) states.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        [Header("HUD References")]
        [SerializeField] private TextMeshProUGUI _levelTitleText;
        [SerializeField] private TextMeshProUGUI _progressPercentText;
        [SerializeField] private Image _progressFillBar;
        [SerializeField] private Button _restartButton;

        [Header("Win & Fail Popup Panels")]
        [SerializeField] private GameObject _winPanel;
        [SerializeField] private Button _winNextLevelButton;
        [SerializeField] private GameObject _failPanel;
        [SerializeField] private Button _failRetryButton;

        [Header("Gameplay Reference")]
        [SerializeField] private GameLoopController _gameLoop;

        private int _totalLevelGrains;

        private void OnEnable()
        {
            EventBus<LevelStartedEvent>.Subscribe(OnLevelStarted);
            EventBus<GrainArrivedInBucketEvent>.Subscribe(OnGrainArrivedInBucket);
            EventBus<LevelWonEvent>.Subscribe(OnLevelWon);
            EventBus<LevelDeadlockedEvent>.Subscribe(OnLevelDeadlocked);
        }

        private void OnDisable()
        {
            EventBus<LevelStartedEvent>.Unsubscribe(OnLevelStarted);
            EventBus<GrainArrivedInBucketEvent>.Unsubscribe(OnGrainArrivedInBucket);
            EventBus<LevelWonEvent>.Unsubscribe(OnLevelWon);
            EventBus<LevelDeadlockedEvent>.Unsubscribe(OnLevelDeadlocked);
        }

        private void Start()
        {
            if (_gameLoop == null)
            {
                ServiceLocator.TryGet(out _gameLoop);
            }

            if (_restartButton != null) _restartButton.onClick.AddListener(OnRestartClicked);
            if (_winNextLevelButton != null) _winNextLevelButton.onClick.AddListener(OnNextLevelClicked);
            if (_failRetryButton != null) _failRetryButton.onClick.AddListener(OnRestartClicked);
        }

        private void OnLevelStarted(in LevelStartedEvent evt)
        {
            _totalLevelGrains = Mathf.Max(1, evt.TotalGrains);

            if (_winPanel != null) _winPanel.SetActive(false);
            if (_failPanel != null) _failPanel.SetActive(false);

            if (_levelTitleText != null)
            {
                _levelTitleText.text = $"LEVEL {evt.LevelIndex} — {evt.LevelTitle}";
            }

            UpdateOverallProgressBar(0f);
        }

        private void OnGrainArrivedInBucket(in GrainArrivedInBucketEvent evt)
        {
            int extracted = Mathf.Max(0, _totalLevelGrains - evt.TotalRemainingGrains);
            float ratio = Mathf.Clamp01((float)extracted / _totalLevelGrains);
            UpdateOverallProgressBar(ratio);
        }

        private void UpdateOverallProgressBar(float ratio01)
        {
            int pct = Mathf.FloorToInt(ratio01 * 100f);
            if (_progressPercentText != null)
            {
                _progressPercentText.SetText("{0}%", pct);
            }

            if (_progressFillBar != null)
            {
                _progressFillBar.fillAmount = ratio01;
            }
        }

        private void OnLevelWon(in LevelWonEvent evt)
        {
            UpdateOverallProgressBar(1f);
            if (_winPanel != null)
            {
                _winPanel.SetActive(true);
                _winPanel.transform.localScale = Vector3.one * 0.7f;
                _winPanel.transform.DOScale(Vector3.one, 0.28f).SetEase(Ease.OutBack);
            }
        }

        private void OnLevelDeadlocked(in LevelDeadlockedEvent evt)
        {
            if (_failPanel != null)
            {
                _failPanel.SetActive(true);
                _failPanel.transform.localScale = Vector3.one * 0.7f;
                _failPanel.transform.DOScale(Vector3.one, 0.28f).SetEase(Ease.OutBack);
            }
        }

        private void OnRestartClicked()
        {
            if (_gameLoop == null) ServiceLocator.TryGet(out _gameLoop);
            _gameLoop?.RestartCurrentLevel();
        }

        private void OnNextLevelClicked()
        {
            if (_gameLoop == null) ServiceLocator.TryGet(out _gameLoop);
            _gameLoop?.LoadNextLevel();
        }
    }
}

