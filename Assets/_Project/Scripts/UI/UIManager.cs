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
        [SerializeField] private Slider _progressSlider;
        [SerializeField] private Button _restartButton;

        [Header("Level Navigation (auto-created at runtime if left empty)")]
        [SerializeField] private Button _prevLevelButton;
        [SerializeField] private Button _nextLevelButton;

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

            if (_prevLevelButton != null) _prevLevelButton.onClick.AddListener(OnPrevLevelClicked);
            if (_nextLevelButton != null) _nextLevelButton.onClick.AddListener(OnNextLevelClicked);

            if (_progressSlider == null && _progressFillBar != null)
            {
                _progressSlider = _progressFillBar.GetComponentInParent<Slider>();
            }
            EnsureFillBarSprite();
            UpdateOverallProgressBar(0f);
        }

        private void EnsureFillBarSprite()
        {
            if (_progressFillBar != null && _progressFillBar.sprite == null)
            {
                Texture2D tex = Texture2D.whiteTexture;
                _progressFillBar.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _progressFillBar.type = Image.Type.Filled;
                _progressFillBar.fillMethod = Image.FillMethod.Horizontal;
                _progressFillBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }

        private void OnPrevLevelClicked()
        {
            if (_gameLoop == null) ServiceLocator.TryGet(out _gameLoop);
            _gameLoop?.LoadPreviousLevel();
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
            float clamped = Mathf.Clamp01(ratio01);
            int pct = Mathf.FloorToInt(clamped * 100f);
            if (_progressPercentText != null)
            {
                _progressPercentText.SetText("{0}%", pct);
            }

            if (_progressSlider != null)
            {
                _progressSlider.minValue = 0f;
                _progressSlider.maxValue = 1f;
                _progressSlider.value = clamped;
            }

            if (_progressFillBar != null)
            {
                EnsureFillBarSprite();
                _progressFillBar.fillAmount = clamped;
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

