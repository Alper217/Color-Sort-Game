using System;
using System.Collections.Generic;
using DG.Tweening;
using SandSort.Core;
using SandSort.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandSort.Gameplay.Containers
{
    /// <summary>
    /// Visual component for a 3D color container bucket in the Spawner Queues and Active Conveyor Loop.
    /// Supports:
    /// - Front Queue Row (row == 0): True vibrant palette color, clean 3D look without text clutter.
    /// - Mystery Queue Rows (row >= 1): Pitch-black bucket body with a bold white "?" label.
    /// - Conveyor Belt State: Clean scaled 3D bucket displaying live fill percentage ("0%" .. "100%").
    /// </summary>
    public sealed class BucketView : MonoBehaviour
    {
        private static readonly Color32 MysteryBlackColor = new Color32(18, 18, 24, 255);
        private static MaterialPropertyBlock _sharedPropBlock;
        private static Material _sharedPlasticBucketMaterial;

        [Header("3D Bucket Visual Settings")]
        [SerializeField] private float _targetBucketWorldSize = 0.50f;
        [SerializeField] private float _conveyorScaleMultiplier = 0.86f;
        [SerializeField] private bool _autoNormalizeWorldSize = false;
        [Tooltip("Kovanın üst açık ağzının kameraya doğru görünmesi için Dünya X ekseninde eğim (-38 derece = iç ağzı tam görünür).")]
        [SerializeField] private float _cameraForwardTiltDegrees = -38f;

        [Header("Prefab Görsel Ayarları (Elle Düzenleme)")]
        [Tooltip("Açıksa prefab içindeki kova modeli konumu, açısı ve boyutu kod tarafından ezilmez; prefabdaki ayarlarınız korunur.")]
        [SerializeField] private bool _respectPrefabModelTransform = true;
        [Tooltip("Prefab içindeki TextMeshPro referansı. Elle konumlandırdığınız yazıyı bağlayabilirsiniz.")]
        [SerializeField] private TextMeshPro _capacityText;
        [Tooltip("Açıksa prefab içinde ayarladığınız konumu, fontu, boyutu ve rengi kod bozmaz; sadece metni ('?' veya '%') günceller.")]
        [SerializeField] private bool _respectPrefabLabelTransform = true;
        [Tooltip("Sırada bekleyen gizemli kovanın üzerinde görünecek metin (Varsayılan: '?').")]
        [SerializeField] private string _mysteryLabelText = "?";

        [Header("Ray Pozisyonu (Sadece Raydayken Uygulanır)")]
        [SerializeField] private Vector3 _railBodyLocalPos = new Vector3(0f, 0.00104f, 0.00348f);
        [SerializeField] private Vector3 _railBodyLocalRot = new Vector3(-123.314f, 0f, 0f);

        private readonly List<MeshRenderer> _meshRenderers = new List<MeshRenderer>(4);
        private Transform _transform;
        private Transform _bucketBodyTransform;
        private Vector3 _queueBodyLocalPos = Vector3.zero;
        private Quaternion _queueBodyLocalRot = Quaternion.Euler(-90f, 0f, 0f);
        private BoxCollider _boxCollider;
        private Vector3 _baseScale;
        private bool _hierarchyInitialized;

        private Sequence _activeSequence;
        private Tweener _moveTweener;

        private BucketData _bucketData;
        private Color32 _bucketColor;
        private int _currentCount;
        private int _columnIndex = -1;
        private bool _isFrontSelectable;
        private bool _isMysteryHidden;
        private bool _isOnConveyor;
        private bool _isUnderCornerTunnel;

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
            if (_hierarchyInitialized && _meshRenderers.Count > 0 && _capacityText != null)
                return;

            Transform oldFill = _transform.Find("FillIndicator");
            if (oldFill != null)
            {
                Destroy(oldFill.gameObject);
            }

            // 1. Collect all 3D MeshRenderers belonging to the bucket model (excluding TextMeshPro)
            _meshRenderers.Clear();
            MeshRenderer[] allRenderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                MeshRenderer r = allRenderers[i];
                if (r == null) continue;
                if (r.GetComponent<TextMeshPro>() != null) continue;
                if (r.gameObject.name == "CapacityLabel" || r.gameObject.name == "FillIndicator") continue;

                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                EnsureTintableMaterial(r);
                _meshRenderers.Add(r);
            }

            // 2. Group all direct 3D model children (Bucket body + Handle) under a single rigid ModelPivot container
            //    using worldPositionStays = false so their exact FBX localPositions and localRotations stay 100% intact!
            Quaternion savedRootRot = _transform.rotation;
            _transform.rotation = Quaternion.identity;

            Transform modelPivot = _transform.Find("ModelPivot_3D");
            if (modelPivot == null)
            {
                GameObject pivotObj = new GameObject("ModelPivot_3D");
                modelPivot = pivotObj.transform;
                modelPivot.SetParent(_transform, false);
                modelPivot.localPosition = Vector3.zero;
                modelPivot.localRotation = Quaternion.identity;
                modelPivot.localScale = Vector3.one;

                List<Transform> directModelChildren = new List<Transform>(_transform.childCount);
                for (int i = 0; i < _transform.childCount; i++)
                {
                    Transform child = _transform.GetChild(i);
                    if (child == modelPivot) continue;
                    if (child.name == "CapacityLabel" || child.name == "FillIndicator") continue;
                    if (child.GetComponent<TextMeshPro>() != null) continue;
                    directModelChildren.Add(child);
                }

                for (int i = 0; i < directModelChildren.Count; i++)
                {
                    // worldPositionStays = false preserves exact FBX local coords of Bucket & Handle relative to root!
                    directModelChildren[i].SetParent(modelPivot, false);
                }
            }

            if (_bucketBodyTransform == null && modelPivot != null)
            {
                _bucketBodyTransform = modelPivot.Find("plastic_bucket_001");
                if (_bucketBodyTransform == null && modelPivot.childCount > 0)
                {
                    _bucketBodyTransform = modelPivot.GetChild(0);
                }
                if (_bucketBodyTransform != null)
                {
                    _queueBodyLocalPos = _bucketBodyTransform.localPosition;
                    _queueBodyLocalRot = _bucketBodyTransform.localRotation;

                    // Fix FBX handle X-offset: align the handle mesh's horizontal (X) bounds center with the bucket body's X center
                    // so the handle sits symmetrically on both ears instead of sticking out to the right!
                    MeshRenderer bodyRenderer = _bucketBodyTransform.GetComponent<MeshRenderer>();
                    if (bodyRenderer != null)
                    {
                        float bodyCenterX = bodyRenderer.bounds.center.x;
                        for (int i = 0; i < _meshRenderers.Count; i++)
                        {
                            MeshRenderer mr = _meshRenderers[i];
                            if (mr != null && mr != bodyRenderer)
                            {
                                float dx = bodyCenterX - mr.bounds.center.x;
                                if (Mathf.Abs(dx) > 0.00001f)
                                {
                                    mr.transform.position += new Vector3(dx, 0f, 0f);
                                }
                            }
                        }
                    }
                }
            }

            // 3. ModelPivot transforms: if _respectPrefabModelTransform is true, keep user's manual prefab setup!
            if (!_respectPrefabModelTransform)
            {
                modelPivot.localPosition = Vector3.zero;
                modelPivot.localRotation = Quaternion.Euler(_cameraForwardTiltDegrees, 0f, 0f);

                Bounds worldBounds = CalculateCombinedMeshWorldBounds();
                if (_autoNormalizeWorldSize && worldBounds.size.sqrMagnitude > 0.00001f)
                {
                    float maxDim = Mathf.Max(worldBounds.size.x, Mathf.Max(worldBounds.size.y, worldBounds.size.z));
                    if (maxDim > 0.0001f)
                    {
                        float scaleFactor = _targetBucketWorldSize / maxDim;
                        _transform.localScale *= scaleFactor;
                        worldBounds = CalculateCombinedMeshWorldBounds();
                    }
                }

                if (worldBounds.size.sqrMagnitude > 0.00001f)
                {
                    Vector3 localCenterOffset = _transform.InverseTransformPoint(worldBounds.center);
                    modelPivot.localPosition -= localCenterOffset;
                }
            }

            _baseScale = _transform.localScale;
            if (_baseScale.sqrMagnitude < 0.0001f)
            {
                _baseScale = Vector3.one;
            }

            _transform.rotation = savedRootRot;

            // 4. Configure BoxCollider to match the exact 3D bucket bounds
            if (_boxCollider == null)
            {
                _boxCollider = GetComponent<BoxCollider>();
                if (_boxCollider == null)
                {
                    _boxCollider = gameObject.AddComponent<BoxCollider>();
                }
            }

            Vector3 lossy = _transform.lossyScale;
            float invScaleX = 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
            float invScaleY = 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
            float invScaleZ = 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z));

            if (worldBounds.size.sqrMagnitude > 0.00001f)
            {
                _boxCollider.center = _transform.InverseTransformPoint(worldBounds.center);
                _boxCollider.size = new Vector3(
                    Mathf.Max(0.50f, worldBounds.size.x * 1.15f) * invScaleX,
                    Mathf.Max(0.45f, worldBounds.size.y * 1.15f) * invScaleY,
                    Mathf.Max(0.45f, worldBounds.size.z * 1.15f) * invScaleZ);
            }

            // 5. Locate or position the Label ("?" in mystery queue, "0%..100%" on conveyor)
            if (_capacityText == null)
            {
                _capacityText = GetComponentInChildren<TextMeshPro>(true);
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
                    _capacityText = textObj.AddComponent<TextMeshPro>();
                }
            }

            // Only override label position and font styling if _respectPrefabLabelTransform is false!
            // When true, you can freely position, scale, font-size, and style the label in the Prefab!
            if (_capacityText != null && !_respectPrefabLabelTransform)
            {
                Transform textTr = _capacityText.transform;
                float frontZ = worldBounds.size.sqrMagnitude > 0.00001f
                    ? (worldBounds.min.z - 0.06f)
                    : (_transform.position.z - 0.30f);
                Vector3 labelWorldPos = new Vector3(_transform.position.x, _transform.position.y - 0.13f, frontZ);

                textTr.position = labelWorldPos;
                textTr.rotation = Quaternion.identity;
                textTr.localScale = new Vector3(invScaleX, invScaleY, invScaleZ);

                _capacityText.alignment = TextAlignmentOptions.Center;
                _capacityText.fontSize = 1.75f;
                _capacityText.fontStyle = FontStyles.Bold;
                _capacityText.color = Color.white;
                _capacityText.outlineWidth = 0.28f;
                _capacityText.outlineColor = new Color32(14, 12, 28, 255);
                _capacityText.textWrappingMode = TextWrappingModes.NoWrap;
                _capacityText.rectTransform.sizeDelta = new Vector2(2.2f, 1.0f);

                MeshRenderer textRenderer = _capacityText.GetComponent<MeshRenderer>();
                if (textRenderer != null)
                {
                    textRenderer.sortingOrder = 25;
                }
            }

            _hierarchyInitialized = true;
        }

        private static void EnsureTintableMaterial(MeshRenderer renderer)
        {
            if (_sharedPlasticBucketMaterial == null)
            {
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Standard");
                _sharedPlasticBucketMaterial = new Material(litShader)
                {
                    name = "Mat_PlasticBucket_Shared",
                    enableInstancing = true
                };
                if (_sharedPlasticBucketMaterial.HasProperty("_Smoothness"))
                {
                    _sharedPlasticBucketMaterial.SetFloat("_Smoothness", 0.62f);
                }
            }

            Material[] mats = renderer.sharedMaterials;
            if (mats == null || mats.Length <= 1)
            {
                renderer.sharedMaterial = _sharedPlasticBucketMaterial;
            }
            else
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = _sharedPlasticBucketMaterial;
                }
                renderer.sharedMaterials = mats;
            }
        }

        private Bounds CalculateCombinedMeshWorldBounds()
        {
            if (_meshRenderers.Count == 0)
            {
                return new Bounds(_transform.position, Vector3.zero);
            }

            Bounds combined = _meshRenderers[0].bounds;
            for (int i = 1; i < _meshRenderers.Count; i++)
            {
                if (_meshRenderers[i] != null)
                {
                    combined.Encapsulate(_meshRenderers[i].bounds);
                }
            }
            return combined;
        }

        public void Setup(BucketData data, Color32 color, int columnIndex, bool isFrontOfQueue, bool isMysteryHidden = false)
        {
            EnsureVisualHierarchy();
            KillActiveTweens();

            _bucketData = data;
            _bucketColor = color;
            _currentCount = 0;
            _columnIndex = columnIndex;
            _isFrontSelectable = isFrontOfQueue;
            _isMysteryHidden = isMysteryHidden;
            _isOnConveyor = false;
            _transform.localScale = _baseScale;

            RefreshLabelAndColor(0f);
        }

        public void SetQueueState(int columnIndex, bool isFrontOfQueue, bool isMysteryHidden)
        {
            bool wasMystery = _isMysteryHidden;
            _columnIndex = columnIndex;
            _isFrontSelectable = isFrontOfQueue;
            _isMysteryHidden = isMysteryHidden;
            _isOnConveyor = false;

            RefreshLabelAndColor(0f);

            // If this bucket just stepped up into a revealed row from Mystery ("?") state, play a crisp reveal pop!
            if (wasMystery && !isMysteryHidden)
            {
                _transform.localScale = _baseScale * 0.82f;
                _transform.DOScale(_baseScale, 0.22f).SetEase(Ease.OutBack);
            }
        }

        public void SetInsideCornerTunnel(bool insideTunnel)
        {
            if (_isUnderCornerTunnel == insideTunnel) return;
            _isUnderCornerTunnel = insideTunnel;
            if (_capacityText != null)
            {
                _capacityText.enabled = !insideTunnel;
            }
        }

        public void SetConveyorPerspectiveScale(float scaleMultiplier)
        {
            if (!_isOnConveyor) return;
            _transform.localScale = _baseScale * (_conveyorScaleMultiplier * scaleMultiplier);
        }

        public void UpdateFillProgress(int currentCount, int maxCapacity)
        {
            _currentCount = currentCount;
            _bucketData.MaxCapacity = maxCapacity;

            float ratio = maxCapacity > 0 ? Mathf.Clamp01((float)currentCount / maxCapacity) : 1f;
            int percent = Mathf.FloorToInt(ratio * 100f);

            if (_capacityText != null)
            {
                if (!_respectPrefabLabelTransform)
                {
                    _capacityText.fontSize = 1.65f;
                }
                _capacityText.SetText("{0}%", percent);
            }

            ApplyColorProperties(ratio);
        }

        private void RefreshLabelAndColor(float fillRatio)
        {
            if (_bucketBodyTransform != null)
            {
                if (_isOnConveyor)
                {
                    _bucketBodyTransform.localPosition = _railBodyLocalPos;
                    _bucketBodyTransform.localRotation = Quaternion.Euler(_railBodyLocalRot);
                }
                else
                {
                    _bucketBodyTransform.localPosition = _queueBodyLocalPos;
                    _bucketBodyTransform.localRotation = _queueBodyLocalRot;
                }
            }

            if (_capacityText != null)
            {
                if (_isOnConveyor)
                {
                    int pct = Mathf.FloorToInt(fillRatio * 100f);
                    if (!_respectPrefabLabelTransform) _capacityText.fontSize = 1.65f;
                    _capacityText.SetText("{0}%", pct);
                }
                else if (_isMysteryHidden)
                {
                    if (!_respectPrefabLabelTransform) _capacityText.fontSize = 2.25f;
                    _capacityText.SetText(_mysteryLabelText);
                }
                else
                {
                    // Front/revealed queue bucket: no "0%" text clutter!
                    _capacityText.SetText(string.Empty);
                }
            }

            ApplyColorProperties(fillRatio);
        }

        private void ApplyColorProperties(float fillRatio)
        {
            if (_sharedPropBlock == null)
            {
                _sharedPropBlock = new MaterialPropertyBlock();
            }

            Color displayColor = (_isMysteryHidden && !_isOnConveyor)
                ? (Color)MysteryBlackColor
                : (Color)_bucketColor;

            for (int i = 0; i < _meshRenderers.Count; i++)
            {
                MeshRenderer r = _meshRenderers[i];
                if (r == null) continue;

                int subMeshCount = Mathf.Max(1, r.sharedMaterials.Length);
                for (int m = 0; m < subMeshCount; m++)
                {
                    r.GetPropertyBlock(_sharedPropBlock, m);
                    _sharedPropBlock.SetColor(GameConstants.BaseColorShaderId, displayColor);
                    _sharedPropBlock.SetColor(GameConstants.MainColorShaderId, displayColor);
                    _sharedPropBlock.SetFloat(GameConstants.FillAmountShaderId, fillRatio);
                    r.SetPropertyBlock(_sharedPropBlock, m);
                }
            }
        }

        public void AnimateQueueShift(Vector3 targetWorldPos, float duration = 0.18f)
        {
            _moveTweener?.Kill();
            _moveTweener = CachedTransform
                .DOMove(targetWorldPos, duration)
                .SetEase(Ease.OutBack);
        }

        public void AnimateDeployToDock(Vector3 dockWorldPos, float duration = 0.20f, Action onComplete = null)
        {
            _columnIndex = -1;
            _isFrontSelectable = false;
            _isMysteryHidden = false;
            _isOnConveyor = true;
            _isUnderCornerTunnel = false;
            if (_capacityText != null) _capacityText.enabled = true;
            RefreshLabelAndColor(0f);

            KillActiveTweens();
            Vector3 targetConveyorScale = _baseScale * (_conveyorScaleMultiplier * 1.12f);

            _activeSequence = DOTween.Sequence();
            _activeSequence.Append(_transform.DOJump(dockWorldPos, 0.30f, 1, duration).SetEase(Ease.OutQuad));
            _activeSequence.Join(_transform.DOScale(targetConveyorScale, duration).SetEase(Ease.OutBack));
            if (onComplete != null)
            {
                _activeSequence.OnComplete(() => onComplete.Invoke());
            }
        }

        public void AnimateCompletionAndRelease(Action<BucketView> onCompleteCallback)
        {
            KillActiveTweens();
            Vector3 currentScale = _baseScale * _conveyorScaleMultiplier;
            _transform.localScale = currentScale;

            _activeSequence = DOTween.Sequence();
            _activeSequence.Append(_transform.DOScale(currentScale * 1.22f, 0.12f).SetEase(Ease.OutBack));
            _activeSequence.Append(_transform.DOMoveY(_transform.position.y + 0.45f, 0.16f).SetEase(Ease.InBack));
            _activeSequence.Join(_transform.DOScale(Vector3.zero, 0.16f).SetEase(Ease.InBack));
            _activeSequence.OnComplete(() =>
            {
                _isOnConveyor = false;
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
        }

        private void OnDisable()
        {
            KillActiveTweens();
        }

#if UNITY_EDITOR
        [ContextMenu("Prefab Üzerinde Yazıyı (CapacityLabel) Görünür Yap / Hizala")]
        private void EditorAlignCapacityLabel()
        {
            EnsureVisualHierarchy();
            if (_capacityText != null)
            {
                _capacityText.text = _mysteryLabelText;
                UnityEditor.EditorUtility.SetDirty(gameObject);
            }
        }
#endif
    }
}
