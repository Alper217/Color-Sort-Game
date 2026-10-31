using DG.Tweening;
using SandSort.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandSort.Gameplay.Grid
{
    /// <summary>
    /// Visual representation of a single sand grain (used both in the Grid Matrix and during Bezier flight).
    /// Disables shadow casting/receiving to eliminate dark shadow artifacts and maximize WebGL/Mobile FPS.
    /// </summary>
    public sealed class SandGrainView : MonoBehaviour
    {
        private static MaterialPropertyBlock _sharedPropBlock;

        private MeshRenderer _meshRenderer;
        private Collider _collider;
        private Transform _transform;
        private Tweener _moveTweener;
        private Color32 _currentColor;
        private int _colorId = GameConstants.EmptyCellColorId;

        public Transform CachedTransform => _transform != null ? _transform : (_transform = transform);
        public int ColorId => _colorId;
        public Color32 Color => _currentColor;

        private void Awake()
        {
            CacheComponents();
        }

        public void CacheComponents()
        {
            if (_transform == null) _transform = transform;
            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponentInChildren<MeshRenderer>();
                if (_meshRenderer != null)
                {
                    // Disable shadow casting & receiving for crisp casual look and zero shadow-pass cost
                    _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _meshRenderer.receiveShadows = false;
                }
            }

            if (_collider == null) _collider = GetComponentInChildren<Collider>();
            if (_collider != null && _collider.enabled)
            {
                _collider.enabled = false;
            }
        }

        /// <summary>
        /// Sets the exact 1:1 visual color of the grain via MaterialPropertyBlock without color shifting.
        /// </summary>
        public void SetColor(int colorId, Color32 color, bool isExposedFrontier = false)
        {
            CacheComponents();
            _colorId = colorId;
            _currentColor = color;

            if (_meshRenderer == null) return;

            if (_sharedPropBlock == null)
            {
                _sharedPropBlock = new MaterialPropertyBlock();
            }

            Color exactColor = color;
            _meshRenderer.GetPropertyBlock(_sharedPropBlock);
            _sharedPropBlock.SetColor(GameConstants.BaseColorShaderId, exactColor);
            _sharedPropBlock.SetColor(GameConstants.MainColorShaderId, exactColor);
            _meshRenderer.SetPropertyBlock(_sharedPropBlock);
        }

        public void AnimateToLocalPosition(Vector3 targetLocalPos, float duration)
        {
            CacheComponents();
            _moveTweener?.Kill();

            if (duration <= 0.001f)
            {
                _transform.localPosition = targetLocalPos;
                return;
            }

            _moveTweener = _transform
                .DOLocalMove(targetLocalPos, duration)
                .SetEase(Ease.Linear)
                .SetRecyclable(true);
        }

        public void KillActiveTweens()
        {
            _moveTweener?.Kill();
            _moveTweener = null;
        }

        private void OnDisable()
        {
            KillActiveTweens();
        }
    }
}
