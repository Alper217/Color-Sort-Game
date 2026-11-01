using System.Collections.Generic;
using DG.Tweening;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay;
using SandSort.Gameplay.Containers;
using SandSort.Gameplay.VFX;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SandSort.UI
{
    /// <summary>
    /// Event-driven, high-polish UI controller listening to <see cref="EventBus{T}"/> channels.
    /// Features:
    /// - Live progress bar & level title HUD
    /// - Dream / Peak / Good Job style 3D Win Popup (Sunburst, Confetti Cannons, 3-Star Slam, Breathing 3D Buttons)
    /// - Motivational Fail Popup (Near-Miss Progress Bar, Deadlock Smoke VFX, 3D Retry Button)
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

        private int _totalLevelGrains = 1;
        private int _currentLevelIndex = 1;

        // Procedural Win Popup Runtime Objects
        private Image _winDimmerImage;
        private Transform _winCardRoot;
        private Transform _winSunburstTr;
        private TextMeshProUGUI _winSubtitleText;
        private readonly List<Transform> _winStars = new List<Transform>(3);
        private Tweener _winSunburstTweener;
        private Tweener _winButtonPulseTweener;

        // Procedural Fail Popup Runtime Objects
        private Image _failDimmerImage;
        private Transform _failCardRoot;
        private TextMeshProUGUI _failPercentText;
        private TextMeshProUGUI _failMotivateText;
        private Image _failProgressBarFill;
        private Tweener _failButtonPulseTweener;

        private Sequence _activePopupSequence;
        private bool _isPopupSetupDone;

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
            KillAllPopupTweens();
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

            UpdateOverallProgressBar(0f, immediate: true);
            SetupJuicyPopups();
        }

        private Tweener _hudProgressTweener;
        private float _currentProgressRatio;

        private void EnsureFillBarSprite()
        {
            // Do not override or replace the scene's existing sprite or rectangular geometry
        }

        #region Juicy Popup Architecture (Dream / Peak Style)

        private void SetupJuicyPopups()
        {
            if (_isPopupSetupDone) return;
            _isPopupSetupDone = true;

            SetupWinPopupHierarchy();
            SetupFailPopupHierarchy();

            if (_winPanel != null) _winPanel.SetActive(false);
            if (_failPanel != null) _failPanel.SetActive(false);
        }

        private void SetupWinPopupHierarchy()
        {
            if (_winPanel == null) return;

            Transform canvasTr = _winPanel.transform.parent;
            Sprite roundedRect = ProceduralUIUtility.GetOrCreateRoundedRectSprite(128, 30);
            Sprite sunburst = ProceduralUIUtility.GetOrCreateSunburstSprite(256, 14);
            Sprite goldStar = ProceduralUIUtility.GetOrCreateStarSprite(96, true);
            Sprite emptyStar = ProceduralUIUtility.GetOrCreateStarSprite(96, false);

            // 1. Fullscreen Dark Navy Dimmer Overlay
            GameObject dimmerObj = new GameObject("Win_DimmerOverlay");
            dimmerObj.transform.SetParent(canvasTr, false);
            dimmerObj.transform.SetSiblingIndex(_winPanel.transform.GetSiblingIndex());
            RectTransform dimmerRt = dimmerObj.AddComponent<RectTransform>();
            dimmerRt.anchorMin = Vector2.zero;
            dimmerRt.anchorMax = Vector2.one;
            dimmerRt.sizeDelta = Vector2.zero;
            _winDimmerImage = dimmerObj.AddComponent<Image>();
            _winDimmerImage.color = new Color(0.04f, 0.06f, 0.14f, 0.78f);
            dimmerObj.SetActive(false);

            // 2. Hide old flat panel sprite
            Image oldImg = _winPanel.GetComponent<Image>();
            if (oldImg != null) oldImg.color = new Color(0, 0, 0, 0);

            RectTransform winRt = _winPanel.GetComponent<RectTransform>();
            winRt.anchorMin = new Vector2(0.5f, 0.5f);
            winRt.anchorMax = new Vector2(0.5f, 0.5f);
            winRt.sizeDelta = new Vector2(760, 680);
            winRt.anchoredPosition = Vector2.zero;

            // 3. Rotating Golden Sunburst
            GameObject sunburstObj = new GameObject("Sunburst_Rays");
            sunburstObj.transform.SetParent(_winPanel.transform, false);
            RectTransform sbRt = sunburstObj.AddComponent<RectTransform>();
            sbRt.anchoredPosition = new Vector2(0f, 65f);
            sbRt.sizeDelta = new Vector2(850, 850);
            Image sbImg = sunburstObj.AddComponent<Image>();
            sbImg.sprite = sunburst;
            sbImg.color = new Color(1f, 0.85f, 0.30f, 0.45f);
            sbImg.raycastTarget = false;
            _winSunburstTr = sunburstObj.transform;

            // 4. Card Container (3D Layered Rounded Box)
            GameObject cardObj = new GameObject("Win_3DCard");
            cardObj.transform.SetParent(_winPanel.transform, false);
            RectTransform cardRt = cardObj.AddComponent<RectTransform>();
            cardRt.anchoredPosition = Vector2.zero;
            cardRt.sizeDelta = new Vector2(620, 520);
            _winCardRoot = cardObj.transform;

            // Card 3D Bottom Lip / Shadow
            CreateSlicedImage(cardObj.transform, "Card_ShadowLip", roundedRect, new Vector2(0, -9), new Vector2(620, 520), new Color32(28, 48, 80, 255));
            // Card Blue Frame
            CreateSlicedImage(cardObj.transform, "Card_BorderFrame", roundedRect, new Vector2(0, 0), new Vector2(620, 520), new Color32(45, 108, 176, 255));
            // Card Inner Warm Cream Face
            CreateSlicedImage(cardObj.transform, "Card_CreamFace", roundedRect, new Vector2(0, 4), new Vector2(596, 496), new Color32(255, 253, 248, 255));

            // 5. Header Ribbon Banner ("LEVEL COMPLETED!")
            CreateSlicedImage(cardObj.transform, "Ribbon_Shadow", roundedRect, new Vector2(0, 248), new Vector2(460, 84), new Color32(185, 115, 0, 255));
            CreateSlicedImage(cardObj.transform, "Ribbon_Face", roundedRect, new Vector2(0, 254), new Vector2(460, 84), new Color32(255, 184, 0, 255));

            TextMeshProUGUI ribbonText = CreateTMPLabel(cardObj.transform, "Ribbon_Text", "LEVEL COMPLETED!", new Vector2(0, 256), new Vector2(440, 70), 34, FontStyles.Bold, Color.white);
            TrySetOutline(ribbonText, 0.32f, new Color32(92, 56, 0, 255));

            _winSubtitleText = CreateTMPLabel(cardObj.transform, "Win_Subtitle", "LEVEL CLEARED!", new Vector2(0, 192), new Vector2(400, 40), 20, FontStyles.Bold, new Color32(65, 85, 115, 255));

            // 6. 3-Star Celebration Cluster
            GameObject starCluster = new GameObject("StarCluster");
            starCluster.transform.SetParent(cardObj.transform, false);

            Vector2[] starPos = new Vector2[]
            {
                new Vector2(-120f, 65f),  // Left
                new Vector2(0f, 90f),     // Center (Big)
                new Vector2(120f, 65f)    // Right
            };
            Vector2[] starSizes = new Vector2[]
            {
                new Vector2(92f, 92f),
                new Vector2(122f, 122f),
                new Vector2(92f, 92f)
            };

            _winStars.Clear();
            for (int i = 0; i < 3; i++)
            {
                GameObject slot = new GameObject($"StarSlot_{i}");
                slot.transform.SetParent(starCluster.transform, false);
                RectTransform slotRt = slot.AddComponent<RectTransform>();
                slotRt.anchoredPosition = starPos[i];
                slotRt.sizeDelta = starSizes[i];

                // Empty star background placeholder
                Image emptyImg = slot.AddComponent<Image>();
                emptyImg.sprite = emptyStar;
                emptyImg.raycastTarget = false;

                // Gold star that drops in
                GameObject goldObj = new GameObject($"GoldStar_{i}");
                goldObj.transform.SetParent(slot.transform, false);
                RectTransform goldRt = goldObj.AddComponent<RectTransform>();
                goldRt.anchoredPosition = Vector2.zero;
                goldRt.sizeDelta = starSizes[i];
                Image goldImg = goldObj.AddComponent<Image>();
                goldImg.sprite = goldStar;
                goldImg.raycastTarget = false;

                goldObj.transform.localScale = Vector3.zero;
                _winStars.Add(goldObj.transform);
            }

            // 7. 3D "CONTINUE" Button Integration
            if (_winNextLevelButton != null)
            {
                _winNextLevelButton.transform.SetParent(cardObj.transform, false);
                StyleAs3DButton(_winNextLevelButton, roundedRect, new Vector2(0, -170), new Vector2(360, 78),
                    new Color32(46, 204, 113, 255), new Color32(30, 132, 73, 255), "NEXT LEVEL");
            }
        }

        private void SetupFailPopupHierarchy()
        {
            if (_failPanel == null) return;

            Transform canvasTr = _failPanel.transform.parent;
            Sprite roundedRect = ProceduralUIUtility.GetOrCreateRoundedRectSprite(128, 30);
            Sprite warningBadge = ProceduralUIUtility.GetOrCreateWarningBadgeSprite(96);

            // 1. Fullscreen Dark Purple Dimmer Overlay
            GameObject dimmerObj = new GameObject("Fail_DimmerOverlay");
            dimmerObj.transform.SetParent(canvasTr, false);
            dimmerObj.transform.SetSiblingIndex(_failPanel.transform.GetSiblingIndex());
            RectTransform dimmerRt = dimmerObj.AddComponent<RectTransform>();
            dimmerRt.anchorMin = Vector2.zero;
            dimmerRt.anchorMax = Vector2.one;
            dimmerRt.sizeDelta = Vector2.zero;
            _failDimmerImage = dimmerObj.AddComponent<Image>();
            _failDimmerImage.color = new Color(0.06f, 0.04f, 0.14f, 0.82f);
            dimmerObj.SetActive(false);

            // 2. Hide old flat panel image
            Image oldImg = _failPanel.GetComponent<Image>();
            if (oldImg != null) oldImg.color = new Color(0, 0, 0, 0);

            RectTransform failRt = _failPanel.GetComponent<RectTransform>();
            failRt.anchorMin = new Vector2(0.5f, 0.5f);
            failRt.anchorMax = new Vector2(0.5f, 0.5f);
            failRt.sizeDelta = new Vector2(760, 680);
            failRt.anchoredPosition = Vector2.zero;

            // 3. Card Container (3D Layered Rounded Box)
            GameObject cardObj = new GameObject("Fail_3DCard");
            cardObj.transform.SetParent(_failPanel.transform, false);
            RectTransform cardRt = cardObj.AddComponent<RectTransform>();
            cardRt.anchoredPosition = Vector2.zero;
            cardRt.sizeDelta = new Vector2(620, 520);
            _failCardRoot = cardObj.transform;

            // Card 3D Bottom Lip / Shadow
            CreateSlicedImage(cardObj.transform, "Card_ShadowLip", roundedRect, new Vector2(0, -9), new Vector2(620, 520), new Color32(44, 18, 38, 255));
            // Card Purple Frame
            CreateSlicedImage(cardObj.transform, "Card_BorderFrame", roundedRect, new Vector2(0, 0), new Vector2(620, 520), new Color32(84, 38, 70, 255));
            // Card Inner Warm Cream Face
            CreateSlicedImage(cardObj.transform, "Card_CreamFace", roundedRect, new Vector2(0, 4), new Vector2(596, 496), new Color32(255, 248, 244, 255));

            // 4. Header Ribbon Banner ("OUT OF SPACE!")
            CreateSlicedImage(cardObj.transform, "Ribbon_Shadow", roundedRect, new Vector2(0, 248), new Vector2(440, 84), new Color32(179, 36, 50, 255));
            CreateSlicedImage(cardObj.transform, "Ribbon_Face", roundedRect, new Vector2(0, 254), new Vector2(440, 84), new Color32(255, 71, 87, 255));

            TextMeshProUGUI ribbonText = CreateTMPLabel(cardObj.transform, "Ribbon_Text", "OUT OF SPACE!", new Vector2(0, 256), new Vector2(420, 70), 34, FontStyles.Bold, Color.white);
            TrySetOutline(ribbonText, 0.32f, new Color32(74, 14, 19, 255));

            // 5. Warning Badge Icon
            GameObject badgeObj = new GameObject("Warning_Badge");
            badgeObj.transform.SetParent(cardObj.transform, false);
            RectTransform bRt = badgeObj.AddComponent<RectTransform>();
            bRt.anchoredPosition = new Vector2(0f, 130f);
            bRt.sizeDelta = new Vector2(88f, 88f);
            Image bImg = badgeObj.AddComponent<Image>();
            bImg.sprite = warningBadge;
            bImg.raycastTarget = false;

            // 6. Near-Miss Motivation Box
            CreateSlicedImage(cardObj.transform, "NearMiss_Box", roundedRect, new Vector2(0, 5), new Vector2(490, 116), new Color32(245, 236, 245, 255));

            _failMotivateText = CreateTMPLabel(cardObj.transform, "NearMiss_Title", "Almost there! You sorted:", new Vector2(0, 42), new Vector2(460, 30), 19, FontStyles.Bold, new Color32(72, 52, 100, 255));

            // Near-Miss Progress Bar Slot
            CreateSlicedImage(cardObj.transform, "NearMiss_SlotBg", roundedRect, new Vector2(0, 8), new Vector2(420, 24), new Color32(215, 205, 225, 255));

            // Near-Miss Fill Image
            GameObject fillObj = new GameObject("NearMiss_Fill");
            fillObj.transform.SetParent(cardObj.transform, false);
            RectTransform fillRt = fillObj.AddComponent<RectTransform>();
            fillRt.anchoredPosition = new Vector2(0, 8);
            fillRt.sizeDelta = new Vector2(420, 24);
            _failProgressBarFill = fillObj.AddComponent<Image>();
            _failProgressBarFill.sprite = roundedRect;
            _failProgressBarFill.type = Image.Type.Filled;
            _failProgressBarFill.fillMethod = Image.FillMethod.Horizontal;
            _failProgressBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _failProgressBarFill.color = new Color32(255, 159, 67, 255);

            _failPercentText = CreateTMPLabel(cardObj.transform, "NearMiss_PercentText", "0% COMPLETE", new Vector2(0, -28), new Vector2(400, 32), 20, FontStyles.Bold, new Color32(211, 84, 0, 255));

            // 7. 3D "TRY AGAIN" Button Integration
            if (_failRetryButton != null)
            {
                _failRetryButton.transform.SetParent(cardObj.transform, false);
                StyleAs3DButton(_failRetryButton, roundedRect, new Vector2(0, -170), new Vector2(360, 78),
                    new Color32(255, 159, 67, 255), new Color32(211, 84, 0, 255), "TRY AGAIN");
            }
        }

        private static Image CreateSlicedImage(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TMP_FontAsset _defaultFontAsset;

        private void ResolveDefaultFont()
        {
            if (_defaultFontAsset != null) return;
            if (_levelTitleText != null && _levelTitleText.font != null) _defaultFontAsset = _levelTitleText.font;
            else if (_progressPercentText != null && _progressPercentText.font != null) _defaultFontAsset = _progressPercentText.font;
            else _defaultFontAsset = TMP_Settings.defaultFontAsset;
        }

        private TextMeshProUGUI CreateTMPLabel(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();

            ResolveDefaultFont();
            if (_defaultFontAsset != null)
            {
                tmp.font = _defaultFontAsset;
            }

            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void TrySetOutline(TextMeshProUGUI tmp, float width, Color32 color)
        {
            if (tmp == null) return;
            try
            {
                if (tmp.fontSharedMaterial != null || tmp.font != null)
                {
                    tmp.outlineWidth = width;
                    tmp.outlineColor = color;
                }
            }
            catch
            {
                // Safe fallback if TMP material instance cannot be cloned on early frame
            }
        }

        private void StyleAs3DButton(Button button, Sprite roundedRect, Vector2 pos, Vector2 size, Color faceColor, Color shadowColor, string labelText)
        {
            RectTransform btnRt = button.GetComponent<RectTransform>();
            btnRt.anchoredPosition = pos;
            btnRt.sizeDelta = size;

            Image baseImg = button.GetComponent<Image>();
            if (baseImg != null)
            {
                baseImg.color = new Color(0, 0, 0, 0); // transparent hit area
            }

            // Shadow Lip
            Transform shadowTr = button.transform.Find("Btn_ShadowLip");
            Image shadowImg;
            if (shadowTr == null)
            {
                shadowImg = CreateSlicedImage(button.transform, "Btn_ShadowLip", roundedRect, new Vector2(0, -6), size, shadowColor);
                shadowTr = shadowImg.transform;
            }
            else
            {
                shadowImg = shadowTr.GetComponent<Image>();
                if (shadowImg != null)
                {
                    shadowImg.sprite = roundedRect;
                    shadowImg.color = shadowColor;
                    shadowImg.rectTransform.anchoredPosition = new Vector2(0, -6);
                    shadowImg.rectTransform.sizeDelta = size;
                }
            }

            // Front Face
            Transform faceTr = button.transform.Find("Btn_Face");
            Image faceImg;
            if (faceTr == null)
            {
                faceImg = CreateSlicedImage(button.transform, "Btn_Face", roundedRect, Vector2.zero, size, faceColor);
                faceTr = faceImg.transform;
            }
            else
            {
                faceImg = faceTr.GetComponent<Image>();
                if (faceImg != null)
                {
                    faceImg.sprite = roundedRect;
                    faceImg.color = faceColor;
                    faceImg.rectTransform.anchoredPosition = Vector2.zero;
                    faceImg.rectTransform.sizeDelta = size;
                }
            }

            shadowTr.SetSiblingIndex(0);
            faceTr.SetSiblingIndex(1);

            // Re-use or create TMP Label component
            TextMeshProUGUI tmp = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp == null)
            {
                tmp = CreateTMPLabel(button.transform, "Btn_Text", labelText, new Vector2(0, 2), size, 28, FontStyles.Bold, Color.white);
            }
            else
            {
                tmp.gameObject.name = "Btn_Text";
                tmp.transform.SetParent(button.transform, false);
                tmp.rectTransform.anchoredPosition = new Vector2(0, 2);
                tmp.rectTransform.sizeDelta = size;
                ResolveDefaultFont();
                if (_defaultFontAsset != null) tmp.font = _defaultFontAsset;
                tmp.text = labelText;
                tmp.fontSize = 28;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
                tmp.gameObject.SetActive(true);
            }

            // CRITICAL: Text MUST be the frontmost sibling so it is never hidden behind Btn_Face!
            tmp.transform.SetAsLastSibling();

            TrySetOutline(tmp, 0.28f, new Color32(20, 20, 20, 200));
        }

        #endregion

        #region Event Callbacks & Polish Animations

        private void OnPrevLevelClicked()
        {
            if (_gameLoop == null) ServiceLocator.TryGet(out _gameLoop);
            _gameLoop?.LoadPreviousLevel();
        }

        private void OnLevelStarted(in LevelStartedEvent evt)
        {
            _totalLevelGrains = Mathf.Max(1, evt.TotalGrains);
            _currentLevelIndex = evt.LevelIndex;

            KillAllPopupTweens();

            if (_winPanel != null) _winPanel.SetActive(false);
            if (_winDimmerImage != null) _winDimmerImage.gameObject.SetActive(false);

            if (_failPanel != null) _failPanel.SetActive(false);
            if (_failDimmerImage != null) _failDimmerImage.gameObject.SetActive(false);

            if (_levelTitleText != null)
            {
                _levelTitleText.text = $"LEVEL {evt.LevelIndex} — {evt.LevelTitle}";
            }

            UpdateOverallProgressBar(0f, immediate: true);
        }

        private void OnGrainArrivedInBucket(in GrainArrivedInBucketEvent evt)
        {
            int extracted = Mathf.Max(0, _totalLevelGrains - evt.TotalRemainingGrains);
            float ratio = Mathf.Clamp01((float)extracted / _totalLevelGrains);
            UpdateOverallProgressBar(ratio);
        }

        private void UpdateOverallProgressBar(float ratio01, bool immediate = false)
        {
            float targetRatio = Mathf.Clamp01(ratio01);

            _hudProgressTweener?.Kill();

            if (immediate)
            {
                _currentProgressRatio = targetRatio;
                ApplyProgressFill(_currentProgressRatio);
            }
            else
            {
                _hudProgressTweener = DOTween.To(() => _currentProgressRatio, x =>
                {
                    _currentProgressRatio = x;
                    ApplyProgressFill(_currentProgressRatio);
                }, targetRatio, 0.35f).SetEase(Ease.OutQuad);
            }
        }

        private void ApplyProgressFill(float ratio)
        {
            int pct = Mathf.FloorToInt(ratio * 100f);
            if (_progressPercentText != null)
            {
                _progressPercentText.SetText("{0}%", pct);
            }

            if (_progressSlider != null)
            {
                _progressSlider.minValue = 0f;
                _progressSlider.maxValue = 1f;
                _progressSlider.value = ratio;
            }

            if (_progressFillBar != null)
            {
                // If the bar is configured as an Image.Type.Filled with a sprite, use fillAmount
                if (_progressFillBar.sprite != null && _progressFillBar.type == Image.Type.Filled)
                {
                    _progressFillBar.fillAmount = ratio;
                }
                else
                {
                    // Clean slider behavior via RectTransform horizontal anchor stretching:
                    // Preserves the sharp square/rectangular look without distorting pixel aspect ratio
                    RectTransform rt = _progressFillBar.rectTransform;
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0f, 0f);
                        rt.anchorMax = new Vector2(ratio, 1f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }
                }
            }
        }

        private void OnLevelWon(in LevelWonEvent evt)
        {
            UpdateOverallProgressBar(1f);
            SetupJuicyPopups();
            KillAllPopupTweens();

            // 1. Dual Confetti Cannons
            JuicyVFXManager.Instance?.PlayWinConfettiCannons();

            if (_winPanel == null) return;

            // 2. Fullscreen Dimmer Fade-in
            if (_winDimmerImage != null)
            {
                _winDimmerImage.gameObject.SetActive(true);
                Color c = _winDimmerImage.color;
                c.a = 0f;
                _winDimmerImage.color = c;
                _winDimmerImage.DOFade(0.78f, 0.25f);
            }

            _winPanel.SetActive(true);
            if (_winSubtitleText != null)
            {
                _winSubtitleText.text = $"LEVEL {evt.LevelIndex} CLEARED!";
            }

            // 3. Rotating Golden Sunburst
            if (_winSunburstTr != null)
            {
                _winSunburstTr.localScale = Vector3.zero;
                _winSunburstTr.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack);
                _winSunburstTweener = _winSunburstTr
                    .DORotate(new Vector3(0f, 0f, -360f), 20f, RotateMode.FastBeyond360)
                    .SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear);
            }

            // 4. Card Slam In
            if (_winCardRoot != null)
            {
                _winCardRoot.localScale = Vector3.one * 0.4f;
                _winCardRoot.DOScale(Vector3.one, 0.32f).SetEase(Ease.OutBack);
            }

            // 5. Staggered 3-Star Slam Sequence
            for (int i = 0; i < _winStars.Count; i++)
            {
                _winStars[i].localScale = Vector3.zero;
            }

            _activePopupSequence = DOTween.Sequence();
            _activePopupSequence.SetDelay(0.38f);

            // Left Star
            if (_winStars.Count > 0)
            {
                _activePopupSequence.AppendCallback(() =>
                {
                    _winStars[0].localScale = Vector3.one * 2.4f;
                    _winStars[0].DOScale(Vector3.one, 0.18f).SetEase(Ease.OutBack);
                });
            }

            // Center Big Star
            if (_winStars.Count > 1)
            {
                _activePopupSequence.AppendInterval(0.14f);
                _activePopupSequence.AppendCallback(() =>
                {
                    _winStars[1].localScale = Vector3.one * 2.8f;
                    _winStars[1].DOScale(Vector3.one, 0.20f).SetEase(Ease.OutBack);
                    _winCardRoot?.DOPunchScale(Vector3.one * 0.04f, 0.12f, 2, 0.5f);
                });
            }

            // Right Star
            if (_winStars.Count > 2)
            {
                _activePopupSequence.AppendInterval(0.14f);
                _activePopupSequence.AppendCallback(() =>
                {
                    _winStars[2].localScale = Vector3.one * 2.4f;
                    _winStars[2].DOScale(Vector3.one, 0.18f).SetEase(Ease.OutBack);
                });
            }

            // 6. Next Button Pop & Breathing Loop
            if (_winNextLevelButton != null)
            {
                _winNextLevelButton.transform.localScale = Vector3.zero;
                _activePopupSequence.AppendInterval(0.10f);
                _activePopupSequence.AppendCallback(() =>
                {
                    _winNextLevelButton.transform.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack).OnComplete(() =>
                    {
                        _winButtonPulseTweener = _winNextLevelButton.transform
                            .DOScale(Vector3.one * 1.05f, 0.65f)
                            .SetLoops(-1, LoopType.Yoyo)
                            .SetEase(Ease.InOutSine);
                    });
                });
            }
        }

        private void OnLevelDeadlocked(in LevelDeadlockedEvent evt)
        {
            SetupJuicyPopups();
            KillAllPopupTweens();

            // 1. Stuck Buckets Smoke & Camera Shake
            if (ServiceLocator.TryGet(out DockManager dockMgr))
            {
                JuicyVFXManager.Instance?.PlayFailDeadlockVFX(dockMgr.GetActiveBucketWorldPositions());
            }
            else
            {
                JuicyVFXManager.Instance?.TriggerCameraMicroPunch(0.05f, 0.22f);
            }

            // Calculate Near-Miss Extraction Ratio
            int extracted = Mathf.Max(0, _totalLevelGrains - evt.RemainingGrains);
            float ratio = Mathf.Clamp01((float)extracted / _totalLevelGrains);
            int pct = Mathf.FloorToInt(ratio * 100f);

            // 2. Delay slightly before opening popup so player sees the deadlock jam
            _activePopupSequence = DOTween.Sequence();
            _activePopupSequence.SetDelay(0.32f);
            _activePopupSequence.AppendCallback(() =>
            {
                if (_failPanel == null) return;

                if (_failDimmerImage != null)
                {
                    _failDimmerImage.gameObject.SetActive(true);
                    Color c = _failDimmerImage.color;
                    c.a = 0f;
                    _failDimmerImage.color = c;
                    _failDimmerImage.DOFade(0.82f, 0.25f);
                }

                _failPanel.SetActive(true);

                // 3. Card Pop In + Error Shake
                if (_failCardRoot != null)
                {
                    _failCardRoot.localScale = Vector3.one * 0.4f;
                    _failCardRoot.DOScale(Vector3.one, 0.28f).SetEase(Ease.OutBack).OnComplete(() =>
                    {
                        _failCardRoot.DOShakePosition(0.20f, new Vector3(14f, 0f, 0f), 18, 90f, false, true);
                    });
                }

                // 4. Fill Near-Miss Motivation Bar
                if (_failProgressBarFill != null)
                {
                    _failProgressBarFill.fillAmount = 0f;
                    _failProgressBarFill.DOFillAmount(ratio, 0.65f).SetEase(Ease.OutCubic);
                }

                if (_failPercentText != null)
                {
                    _failPercentText.text = $"{pct}% COMPLETE";
                }

                if (_failMotivateText != null)
                {
                    _failMotivateText.text = pct >= 70 ? "So close! Try again!" : "Keep practicing! You can do it!";
                }

                // 5. Retry Button Pop & Breathing Loop
                if (_failRetryButton != null)
                {
                    _failRetryButton.transform.localScale = Vector3.zero;
                    _failRetryButton.transform.DOScale(Vector3.one, 0.25f).SetDelay(0.35f).SetEase(Ease.OutBack).OnComplete(() =>
                    {
                        _failButtonPulseTweener = _failRetryButton.transform
                            .DOScale(Vector3.one * 1.05f, 0.65f)
                            .SetLoops(-1, LoopType.Yoyo)
                            .SetEase(Ease.InOutSine);
                    });
                }
            });
        }

        private void KillAllPopupTweens()
        {
            _hudProgressTweener?.Kill();
            _hudProgressTweener = null;
            _activePopupSequence?.Kill();
            _activePopupSequence = null;
            _winSunburstTweener?.Kill();
            _winSunburstTweener = null;
            _winButtonPulseTweener?.Kill();
            _winButtonPulseTweener = null;
            _failButtonPulseTweener?.Kill();
            _failButtonPulseTweener = null;
        }

        private void OnRestartClicked()
        {
            KillAllPopupTweens();
            if (_gameLoop == null) ServiceLocator.TryGet(out _gameLoop);
            _gameLoop?.RestartCurrentLevel();
        }

        private void OnNextLevelClicked()
        {
            KillAllPopupTweens();
            if (_gameLoop == null) ServiceLocator.TryGet(out _gameLoop);
            _gameLoop?.LoadNextLevel();
        }

        #endregion
    }
}
