using System;
using DG.Tweening;
using SandSort.Core;
using SandSort.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandSort.Gameplay.Containers
{
    /// <summary>
    /// Visual component for a color container bucket in the Spawner Queues and Active Dock.
    /// Renders 100% true-to-palette vibrant colors (never dims or alters hue) so player color-matching is unambiguous.
    /// </summary>
    public sealed class BucketView : MonoBehaviour
    {
        private static MaterialPropertyBlock _sharedPropBlock;

        [SerializeField] private MeshRenderer _bodyRenderer;
        [SerializeField] private Transform _innerFillTransform;
        [SerializeField] private MeshRenderer _innerFillRenderer;
        [SerializeField] private TextMeshPro _capacityText;

        private Transform _transform;
        private BoxCollider _boxCollider;
        private Vector3 _baseScale;
        private Sequence _activeSequence;
        private Tweener _moveTweener;
        private Tweener _punchTweener;

        private BucketData _bucketData;
        private Color32 _bucketColor;
        private int _currentCount;
        private int _columnIndex = -1;
        private bool _isFrontSelectable;

        public Transform CachedTransform => _transform != null ? _transform : (_transform = transform);
        public BucketData Data => _bucketData;
        public int ColumnIndex => _columnIndex;
        public bool IsFrontSelectable => _isFrontSelectable;
        public Vector3 BaseScale => _baseScale;

        private void Awake()
        {
            EnsureVisualHierarchy();
        }

        public void EnsureVisualHierarchy()
        {
            if (_transform == null) _transform = transform;
            if (_baseScale.sqrMagnitude < 0.0001f)
            {
                _baseScale = _transform.localScale;
                if (_baseScale.sqrMagnitude < 0.0001f)
                {
                    _baseScale = new Vector3(0.78f, 0.42f, 0.42f);
                }
            }

            if (_bodyRenderer == null)
            {
                _bodyRenderer = GetComponent<MeshRenderer>();
            }
            if (_bodyRenderer != null)
            {
                _bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _bodyRenderer.receiveShadows = false;
            }

            if (_boxCollider == null)
            {
                _boxCollider = GetComponent<BoxCollider>();
                if (_boxCollider == null)
                {
                    _boxCollider = gameObject.AddComponent<BoxCollider>();
                }
            }

            if (_innerFillTransform == null)
            {
                Transform existingFill = _transform.Find("FillIndicator");
                if (existingFill != null)
                {
                    _innerFillTransform = existingFill;
                    _innerFillRenderer = existingFill.GetComponent<MeshRenderer>();
                }
                else
                {
                    GameObject fillObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fillObj.name = "FillIndicator";
                    Destroy(fillObj.GetComponent<Collider>());
                    _innerFillTransform = fillObj.transform;
                    _innerFillTransform.SetParent(_transform, false);
                    _innerFillTransform.localPosition = new Vector3(0f, -0.48f, -0.05f);
                    _innerFillTransform.localScale = new Vector3(0.92f, 0.01f, 0.96f);
                    _innerFillRenderer = fillObj.GetComponent<MeshRenderer>();
                    if (_bodyRenderer != null && _bodyRenderer.sharedMaterial != null)
                    {
                        _innerFillRenderer.sharedMaterial = _bodyRenderer.sharedMaterial;
                    }
                }
            }

            if (_innerFillRenderer != null)
            {
                _innerFillRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _innerFillRenderer.receiveShadows = false;
            }

            if (_capacityText == null)
            {
                Transform existingText = _transform.Find("CapacityLabel");
                if (existingText != null)
                {
                    _capacityText = existingText.GetComponent<TextMeshPro>();
                }
                else
                {
                    GameObject textObj = new GameObject("CapacityLabel");
                    textObj.transform.SetParent(_transform, false);
                    textObj.transform.localPosition = new Vector3(0f, 0.05f, -0.58f);
                    textObj.transform.localRotation = Quaternion.identity;
                    textObj.transform.localScale = new Vector3(
                        1f / Mathf.Max(0.1f, _baseScale.x),
                        1f / Mathf.Max(0.1f, _baseScale.y),
                        1f);

                    _capacityText = textObj.AddComponent<TextMeshPro>();
                    _capacityText.alignment = TextAlignmentOptions.Center;
                    _capacityText.fontSize = 2.2f;
                    _capacityText.fontStyle = FontStyles.Bold;
                    _capacityText.color = Color.white;
                    _capacityText.outlineWidth = 0.24f;
                    _capacityText.outlineColor = new Color32(18, 16, 36, 255);
                    _capacityText.textWrappingMode = TextWrappingModes.NoWrap;
                    _capacityText.rectTransform.sizeDelta = new Vector2(2f, 1f);
                }
            }
        }

        public void Setup(BucketData data, Color32 color, int columnIndex, bool isFrontOfQueue)
        {
            EnsureVisualHierarchy();
            KillActiveTweens();

            _bucketData = data;
            _bucketColor = color;
            _currentCount = 0;
            _columnIndex = columnIndex;
            _isFrontSelectable = isFrontOfQueue;
            _transform.localScale = _baseScale;

            ApplyVisualState();
        }

        public void SetQueueState(int columnIndex, bool isFrontOfQueue)
        {
            _columnIndex = columnIndex;
            _isFrontSelectable = isFrontOfQueue;
            ApplyColorProperties(0f);
        }

        public void UpdateFillProgress(int currentCount, int maxCapacity)
        {
            _currentCount = currentCount;
            _bucketData.MaxCapacity = maxCapacity;

            float ratio = maxCapacity > 0 ? Mathf.Clamp01((float)currentCount / maxCapacity) : 1f;
            int percent = Mathf.FloorToInt(ratio * 100f);

            if (_capacityText != null)
            {
                _capacityText.SetText("{0}/{1}\n<size=70%>{2}%</size>", currentCount, maxCapacity, percent);
            }

            if (_innerFillTransform != null)
            {
                float fillHeight = Mathf.Lerp(0.02f, 0.96f, ratio);
                _innerFillTransform.localScale = new Vector3(0.94f, fillHeight, 0.96f);
                _innerFillTransform.localPosition = new Vector3(0f, -0.48f + fillHeight * 0.5f, -0.05f);
            }

            ApplyColorProperties(ratio);

            if (_punchTweener == null || !_punchTweener.IsActive() || !_punchTweener.IsPlaying())
            {
                _transform.localScale = _baseScale;
                _punchTweener = _transform.DOPunchScale(_baseScale * 0.09f, 0.11f, 6, 0.5f);
            }
        }

        private void ApplyVisualState()
        {
            if (_capacityText != null)
            {
                _capacityText.SetText("0/{0}\n<size=70%>0%</size>", _bucketData.MaxCapacity);
            }

            if (_innerFillTransform != null)
            {
                _innerFillTransform.localScale = new Vector3(0.94f, 0.02f, 0.96f);
                _innerFillTransform.localPosition = new Vector3(0f, -0.47f, -0.05f);
            }

            ApplyColorProperties(0f);
        }

        private void ApplyColorProperties(float fillRatio)
        {
            if (_sharedPropBlock == null)
            {
                _sharedPropBlock = new MaterialPropertyBlock();
            }

            // Always use 100% pure palette color so Yellow never looks like Dark Yellow/Brown!
            Color exactPaletteColor = _bucketColor;

            if (_bodyRenderer != null)
            {
                _bodyRenderer.GetPropertyBlock(_sharedPropBlock);
                _sharedPropBlock.SetColor(GameConstants.BaseColorShaderId, exactPaletteColor);
                _sharedPropBlock.SetColor(GameConstants.MainColorShaderId, exactPaletteColor);
                _sharedPropBlock.SetFloat(GameConstants.FillAmountShaderId, fillRatio);
                _bodyRenderer.SetPropertyBlock(_sharedPropBlock);
            }

            if (_innerFillRenderer != null)
            {
                Color brightFill = Color.Lerp(exactPaletteColor, Color.white, 0.25f);
                _innerFillRenderer.GetPropertyBlock(_sharedPropBlock);
                _sharedPropBlock.SetColor(GameConstants.BaseColorShaderId, brightFill);
                _sharedPropBlock.SetColor(GameConstants.MainColorShaderId, brightFill);
                _innerFillRenderer.SetPropertyBlock(_sharedPropBlock);
            }
        }

        public void AnimateQueueShift(Vector3 targetWorldPos, float duration = 0.18f)
        {
            _moveTweener?.Kill();
            _moveTweener = CachedTransform
                .DOMove(targetWorldPos, duration)
                .SetEase(Ease.OutBack);
        }

        public void AnimateDeployToDock(Vector3 dockWorldPos, float duration = 0.24f)
        {
            _columnIndex = -1;
            _isFrontSelectable = false;
            ApplyColorProperties(0f);

            KillActiveTweens();
            _transform.localScale = _baseScale;

            _activeSequence = DOTween.Sequence();
            _activeSequence.Append(_transform.DOJump(dockWorldPos, 0.45f, 1, duration).SetEase(Ease.OutQuad));
            _activeSequence.Join(_transform.DOPunchScale(_baseScale * 0.18f, duration, 6, 0.6f));
        }

        public void AnimateCompletionAndRelease(Action<BucketView> onCompleteCallback)
        {
            KillActiveTweens();
            _transform.localScale = _baseScale;

            _activeSequence = DOTween.Sequence();
            _activeSequence.Append(_transform.DOScale(_baseScale * 1.25f, 0.14f).SetEase(Ease.OutBack));
            _activeSequence.Append(_transform.DOMoveY(_transform.position.y + 0.65f, 0.18f).SetEase(Ease.InBack));
            _activeSequence.Join(_transform.DOScale(Vector3.zero, 0.18f).SetEase(Ease.InBack));
            _activeSequence.OnComplete(() =>
            {
                _transform.localScale = _baseScale;
                onCompleteCallback?.Invoke(this);
            });
        }

        public void KillActiveTweens()
        {
            _activeSequence?.Kill();
            _activeSequence = null;
            _moveTweener?.Kill();
            _moveTweener = null;
            _punchTweener?.Kill();
            _punchTweener = null;
        }

        private void OnDisable()
        {
            KillActiveTweens();
        }
    }
}
