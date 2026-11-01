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
        [SerializeField] private bool _autoNormalizeWorldSize = true;
        [Tooltip("Kovanın üst açık ağzının kameraya doğru görünmesi için Dünya X ekseninde eğim (-38 derece = iç ağzı tam görünür).")]
        [SerializeField] private float _cameraForwardTiltDegrees = -38f;
        [SerializeField] private TextMeshPro _capacityText;

        [Header("Label ('?' / '%') Manual Placement")]
        [Tooltip("'?' yazısının varsayılan konumuna göre kayması (dünya biriminde: X sağ, Y yukarı, Z kameraya doğru -). Örn. Y = 0.1 yazıyı yukarı kaydırır. Play modda canlı değiştirilebilir.")]
        [SerializeField] private Vector3 _mysteryLabelOffset = new Vector3(0f, 0.26f, -0.08f);
        [SerializeField, Range(0.5f, 6f)] private float _mysteryLabelFontSize = 2.97f;
        [Tooltip("Konveyördeki '%' yazısının varsayılan konumuna göre kayması.")]
        [SerializeField] private Vector3 _percentLabelOffset = Vector3.zero;
        [SerializeField, Range(0.5f, 6f)] private float _percentLabelFontSize = 1.65f;
        private Vector3 _labelBaseLocalPos;
        private bool _labelBaseCaptured;

        // Live tuning: editing these values on ANY BucketView (scene instance or prefab) while playing
        // is mirrored into these statics so every bucket updates instantly.
        private static bool s_hasLiveLabelOverride;
        private static Vector3 s_mysteryOffset;
        private static float s_mysteryFontSize;
        private static Vector3 s_percentOffset;
        private static float s_percentFontSize;
        private static int s_labelVersion;
        private int _appliedLabelVersion;

        private Vector3 MysteryOffset => s_hasLiveLabelOverride ? s_mysteryOffset : _mysteryLabelOffset;
        private float MysteryFontSize => s_hasLiveLabelOverride ? s_mysteryFontSize : _mysteryLabelFontSize;
        private Vector3 PercentOffset => s_hasLiveLabelOverride ? s_percentOffset : _percentLabelOffset;
        private float PercentFontSize => s_hasLiveLabelOverride ? s_percentFontSize : _percentLabelFontSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLiveLabelState()
        {
            s_hasLiveLabelOverride = false;
            s_defaultsCaptured = false;
            s_labelVersion = 0;
        }

        private void LateUpdate()
        {
            if (_appliedLabelVersion == s_labelVersion) return;
            _appliedLabelVersion = s_labelVersion;
            if (_capacityText == null || !_hierarchyInitialized) return;
            float ratio = _bucketData.MaxCapacity > 0 ? Mathf.Clamp01((float)_currentCount / _bucketData.MaxCapacity) : 0f;
            RefreshLabelAndColor(ratio);
        }

        private readonly List<MeshRenderer> _meshRenderers = new List<MeshRenderer>(4);
        private Transform _transform;
        private Transform _bucketBodyTransform;
        private Vector3 _queueBodyLocalPos = Vector3.zero;
        private Quaternion _queueBodyLocalRot = Quaternion.Euler(-90f, 0f, 0f);
        private static readonly Vector3 RailBodyLocalPos = new Vector3(0f, 0.00104f, 0.00348f);
        private static readonly Quaternion RailBodyLocalRot = Quaternion.Euler(-123.314f, 0f, 0f);
        private BoxCollider _boxCollider;
        private Vector3 _baseScale;
        private bool _hierarchyInitialized;

        private Sequence _activeSequence;
        private Tweener _moveTweener;
        private Tweener _holoSheenTweener;
        private float _holoSheenAmount = 0f;

        public void TriggerHoloSheenFlash(float peakAmount = 0.85f, float duration = 0.28f)
        {
            _holoSheenTweener?.Kill();
            _holoSheenAmount = peakAmount;
            _holoSheenTweener = DOTween.To(() => _holoSheenAmount, x =>
            {
                _holoSheenAmount = x;
                ApplyColorProperties(_currentCount > 0 ? (float)_currentCount / Mathf.Max(1, _bucketData.MaxCapacity) : 0f);
            }, 0f, duration).SetEase(Ease.OutQuad);
        }

        private BucketData _bucketData;
        private Color32 _bucketColor;
        private int _currentCount;
        private int _columnIndex = -1;
        private bool _isFrontSelectable;
        private bool _isMysteryHidden;
        private bool _isOnConveyor;
        private bool _isUnderCornerTunnel;

        private static Material _sharedSandFillMaterial;

        private const int SandSegments = 24;
        private float _sandBottomZ;
        private float _sandTopZ;
        private float _sandBottomRadius;
        private float _sandTopRadius;
        private float _sandCenterX;
        private float _sandCenterY;

        private Transform _sandFillTransform;
        private MeshFilter _sandFillMeshFilter;
        private MeshRenderer _sandFillRenderer;
        private Mesh _sandFillMesh;
        private Vector3[] _sandFillVertices;
        private MaterialPropertyBlock _sandFillPropBlock;


        public Transform CachedTransform => _transform != null ? _transform : (_transform = transform);
        public BucketData Data => _bucketData;
        public int ColumnIndex => _columnIndex;
        public bool IsFrontSelectable => _isFrontSelectable;
        public Vector3 BaseScale => _baseScale;

        private static bool s_defaultsCaptured;
        private static Vector3 s_defMysteryOffset;
        private static float s_defMysterySize;
        private static Vector3 s_defPercentOffset;
        private static float s_defPercentSize;

        /// <summary>Current label tuning values (live override if set, otherwise the prefab values).</summary>
        public static void GetLabelTuning(out Vector3 mysteryOffset, out float mysterySize, out Vector3 percentOffset, out float percentSize)
        {
            if (s_hasLiveLabelOverride)
            {
                mysteryOffset = s_mysteryOffset; mysterySize = s_mysteryFontSize;
                percentOffset = s_percentOffset; percentSize = s_percentFontSize;
            }
            else
            {
                mysteryOffset = s_defMysteryOffset; mysterySize = s_defaultsCaptured ? s_defMysterySize : 2.25f;
                percentOffset = s_defPercentOffset; percentSize = s_defaultsCaptured ? s_defPercentSize : 1.65f;
            }
        }

        /// <summary>Live-applies label tuning to every bucket (used by the on-screen tuner).</summary>
        public static void SetLabelTuning(Vector3 mysteryOffset, float mysterySize, Vector3 percentOffset, float percentSize)
        {
            s_hasLiveLabelOverride = true;
            s_mysteryOffset = mysteryOffset; s_mysteryFontSize = mysterySize;
            s_percentOffset = percentOffset; s_percentFontSize = percentSize;
            s_labelVersion++;
        }

        private void Awake()
        {
            if (!s_defaultsCaptured)
            {
                s_defaultsCaptured = true;
                s_defMysteryOffset = _mysteryLabelOffset; s_defMysterySize = _mysteryLabelFontSize;
                s_defPercentOffset = _percentLabelOffset; s_defPercentSize = _percentLabelFontSize;
            }
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
                if (r.gameObject.name == "CapacityLabel" || r.gameObject.name == "FillIndicator" || r.gameObject.name == "SandFillSurface") continue;

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
                    if (child.name == "CapacityLabel" || child.name == "FillIndicator" || child.name == "SandFillSurface") continue;
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

            if (_bucketBodyTransform != null)
            {
                EnsureSandFillObject();
            }

            // 3a. Tilt the unified ModelPivot (Bucket body + Handle locked together as one rigid piece!)
            modelPivot.localPosition = Vector3.zero;
            modelPivot.localRotation = Quaternion.Euler(_cameraForwardTiltDegrees, 0f, 0f);

            // 3b. Normalize scale to target world size
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

            _baseScale = _transform.localScale;
            if (_baseScale.sqrMagnitude < 0.0001f)
            {
                _baseScale = Vector3.one * 0.5f;
            }

            // 3c. Center the rigid ModelPivot in local space so worldBounds.center == _transform.position
            if (worldBounds.size.sqrMagnitude > 0.00001f)
            {
                Vector3 localCenterOffset = _transform.InverseTransformPoint(worldBounds.center);
                modelPivot.localPosition -= localCenterOffset;
                worldBounds = CalculateCombinedMeshWorldBounds();
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

            // 5. Create or position the Label ("?" in mystery queue, "0%..100%" on conveyor) centered on the front face
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

            if (_capacityText != null)
            {
                Transform textTr = _capacityText.transform;
                float frontZ = worldBounds.size.sqrMagnitude > 0.00001f
                    ? (worldBounds.min.z - 0.06f)
                    : (_transform.position.z - 0.30f);

                // Use the exact horizontal (X) bounds center of the bucket body mesh (ignoring handle asymmetry)
                float bodyCenterX = _transform.position.x;
                if (_bucketBodyTransform != null)
                {
                    MeshRenderer bodyMr = _bucketBodyTransform.GetComponent<MeshRenderer>();
                    if (bodyMr != null) bodyCenterX = bodyMr.bounds.center.x;
                }
                else if (_meshRenderers.Count > 0 && _meshRenderers[0] != null)
                {
                    bodyCenterX = _meshRenderers[0].bounds.center.x;
                }

                // Position the percentage label on the lower-front body below the open circular rim (exact match to screenshot!)
                Vector3 labelWorldPos = new Vector3(bodyCenterX, _transform.position.y - 0.13f, frontZ);

                textTr.position = labelWorldPos;
                textTr.rotation = Quaternion.identity;
                textTr.localScale = new Vector3(invScaleX, invScaleY, invScaleZ);

                // Ensure RectTransform pivot is dead-center (0.5, 0.5) so center-alignment aligns to bucket axis
                RectTransform rt = _capacityText.rectTransform;
                if (rt != null)
                {
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(2.2f, 1.0f);
                }

                _labelBaseLocalPos = textTr.localPosition;
                _labelBaseCaptured = true;

                // CenterGeoAligned centers on actual glyph vertex geometry rather than font advance/sidebearings
                _capacityText.alignment = TextAlignmentOptions.CenterGeoAligned;
                _capacityText.fontSize = 1.75f;
                _capacityText.fontStyle = FontStyles.Bold;
                _capacityText.color = Color.white;
                _capacityText.outlineWidth = 0.28f;
                _capacityText.outlineColor = new Color32(14, 12, 28, 255);
                _capacityText.textWrappingMode = TextWrappingModes.NoWrap;

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
                if (litShader == null && renderer != null && renderer.sharedMaterial != null)
                {
                    litShader = renderer.sharedMaterial.shader;
                }

                if (litShader != null)
                {
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
                else if (renderer != null && renderer.sharedMaterial != null)
                {
                    _sharedPlasticBucketMaterial = new Material(renderer.sharedMaterial);
                    _sharedPlasticBucketMaterial.enableInstancing = true;
                }
            }

            if (_sharedPlasticBucketMaterial != null && renderer != null)
            {
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
                ApplyLabelPlacement(PercentOffset);
                _capacityText.fontSize = PercentFontSize;
                _capacityText.SetText("{0}%", percent);
            }

            ApplyColorProperties(ratio);
            UpdateSandFillVisual(ratio);

            if (_sandFillTransform != null && _sandFillTransform.gameObject.activeSelf)
            {
                _sandFillTransform.DOKill(true);
                _sandFillTransform.localScale = Vector3.one;
                _sandFillTransform.DOPunchScale(new Vector3(0.025f, 0.025f, 0.015f), 0.10f, 3, 0.5f);
            }
        }

        private void RefreshLabelAndColor(float fillRatio)
        {
            if (_bucketBodyTransform != null)
            {
                if (_isOnConveyor)
                {
                    _bucketBodyTransform.localPosition = RailBodyLocalPos;
                    _bucketBodyTransform.localRotation = RailBodyLocalRot;
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
                    ApplyLabelPlacement(PercentOffset);
                    _capacityText.fontSize = PercentFontSize;
                    _capacityText.SetText("{0}%", pct);
                }
                else if (_isMysteryHidden)
                {
                    ApplyLabelPlacement(MysteryOffset);
                    _capacityText.alignment = TextAlignmentOptions.CenterGeoAligned;
                    _capacityText.fontSize = MysteryFontSize;
                    _capacityText.SetText("?");
                }
                else
                {
                    // Front/revealed queue bucket: no "0%" text clutter!
                    _capacityText.SetText(string.Empty);
                }
            }

            ApplyColorProperties(fillRatio);
            UpdateSandFillVisual(fillRatio);
        }

        private void ApplyLabelPlacement(Vector3 offset)
        {
            if (!_labelBaseCaptured || _capacityText == null) return;
            // Offsets are in WORLD units (independent of the bucket's own scale) so changes are clearly visible.
            Vector3 lossy = CachedTransform.lossyScale;
            Vector3 localOffset = new Vector3(
                offset.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                offset.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                offset.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            _capacityText.transform.localPosition = _labelBaseLocalPos + localOffset;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Live-tune label offsets from the Inspector while playing: mirror values to all buckets.
            if (!Application.isPlaying) return;
            s_hasLiveLabelOverride = true;
            s_mysteryOffset = _mysteryLabelOffset;
            s_mysteryFontSize = _mysteryLabelFontSize;
            s_percentOffset = _percentLabelOffset;
            s_percentFontSize = _percentLabelFontSize;
            s_labelVersion++;
        }
#endif

        private void EnsureSandFillObject()
        {
            if (_bucketBodyTransform == null) return;

            if (_sandFillTransform == null)
            {
                Transform existing = _bucketBodyTransform.Find("SandFillSurface");
                if (existing != null)
                {
                    _sandFillTransform = existing;
                }
            }

            if (_sandFillTransform == null)
            {
                GameObject fillGo = new GameObject("SandFillSurface");
                fillGo.transform.SetParent(_bucketBodyTransform, false);
                _sandFillTransform = fillGo.transform;
            }

            _sandFillTransform.localPosition = Vector3.zero;
            _sandFillTransform.localRotation = Quaternion.identity;
            _sandFillTransform.localScale = Vector3.one;

            if (_sandFillMeshFilter == null)
            {
                _sandFillMeshFilter = _sandFillTransform.GetComponent<MeshFilter>();
                if (_sandFillMeshFilter == null) _sandFillMeshFilter = _sandFillTransform.gameObject.AddComponent<MeshFilter>();
            }

            if (_sandFillRenderer == null)
            {
                _sandFillRenderer = _sandFillTransform.GetComponent<MeshRenderer>();
                if (_sandFillRenderer == null) _sandFillRenderer = _sandFillTransform.gameObject.AddComponent<MeshRenderer>();
                _sandFillRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _sandFillRenderer.receiveShadows = false;
            }

            EnsureSandFillMaterial(_sandFillRenderer);

            if (_sandFillMesh == null || _sandFillMeshFilter.sharedMesh != _sandFillMesh)
            {
                InitializeProceduralSandMesh();
            }
        }

        private static void EnsureSandFillMaterial(MeshRenderer renderer)
        {
            if (_sharedSandFillMaterial == null)
            {
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (litShader == null) litShader = Shader.Find("Standard");
                if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Unlit");

                if (litShader != null)
                {
                    _sharedSandFillMaterial = new Material(litShader)
                    {
                        name = "Mat_SandFill_Shared",
                        enableInstancing = true
                    };
                    if (_sharedSandFillMaterial.HasProperty("_Smoothness")) _sharedSandFillMaterial.SetFloat("_Smoothness", 0.04f);
                    if (_sharedSandFillMaterial.HasProperty("_Glossiness")) _sharedSandFillMaterial.SetFloat("_Glossiness", 0.04f);
                }
            }

            if (_sharedSandFillMaterial != null && renderer != null)
            {
                renderer.sharedMaterial = _sharedSandFillMaterial;
            }
        }

        private void InitializeProceduralSandMesh()
        {
            if (_sandFillMesh == null)
            {
                _sandFillMesh = new Mesh
                {
                    name = "Mesh_ProceduralSandFill",
                    hideFlags = HideFlags.DontSave
                };
            }

            MeshFilter bodyMf = _bucketBodyTransform != null ? _bucketBodyTransform.GetComponent<MeshFilter>() : null;
            Bounds b = (bodyMf != null && bodyMf.sharedMesh != null)
                ? bodyMf.sharedMesh.bounds
                : new Bounds(Vector3.zero, new Vector3(0.01264f, 0.01264f, 0.01269f));

            _sandBottomZ = b.min.z + b.size.z * 0.065f;
            _sandTopZ = b.min.z + b.size.z * 0.930f;
            _sandBottomRadius = b.extents.x * 0.690f;
            _sandTopRadius = b.extents.x * 0.880f;
            _sandCenterX = b.center.x;
            _sandCenterY = b.center.y;

            int totalVerts = 1 + SandSegments + SandSegments + SandSegments + 1 + SandSegments;
            if (_sandFillVertices == null || _sandFillVertices.Length != totalVerts)
            {
                _sandFillVertices = new Vector3[totalVerts];
            }

            Vector3[] normals = new Vector3[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];
            List<int> tris = new List<int>(96 * 3);

            // 1. Top flat disk: center + SandSegments perimeter
            int topCenterIdx = 0;
            _sandFillVertices[topCenterIdx] = new Vector3(_sandCenterX, _sandCenterY, _sandBottomZ);
            normals[topCenterIdx] = Vector3.forward;
            uvs[topCenterIdx] = new Vector2(0.5f, 0.5f);

            int topRimStart = 1;
            for (int i = 0; i < SandSegments; i++)
            {
                float angle = (2f * Mathf.PI * i) / SandSegments;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);
                int idx = topRimStart + i;
                _sandFillVertices[idx] = new Vector3(_sandCenterX + cosA * _sandBottomRadius, _sandCenterY + sinA * _sandBottomRadius, _sandBottomZ);
                normals[idx] = Vector3.forward;
                uvs[idx] = new Vector2(0.5f + 0.5f * cosA, 0.5f + 0.5f * sinA);
            }

            for (int i = 0; i < SandSegments; i++)
            {
                int next = (i + 1) % SandSegments;
                tris.Add(topCenterIdx);
                tris.Add(topRimStart + i);
                tris.Add(topRimStart + next);
            }

            // 2. Side wall: SandSegments top + SandSegments bottom
            int sideTopStart = topRimStart + SandSegments;
            int sideBotStart = sideTopStart + SandSegments;

            for (int i = 0; i < SandSegments; i++)
            {
                float angle = (2f * Mathf.PI * i) / SandSegments;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);
                Vector3 radialNorm = new Vector3(cosA, sinA, 0f);

                _sandFillVertices[sideTopStart + i] = new Vector3(_sandCenterX + cosA * _sandBottomRadius, _sandCenterY + sinA * _sandBottomRadius, _sandBottomZ);
                normals[sideTopStart + i] = radialNorm;
                uvs[sideTopStart + i] = new Vector2((float)i / SandSegments, 1f);

                _sandFillVertices[sideBotStart + i] = new Vector3(_sandCenterX + cosA * _sandBottomRadius, _sandCenterY + sinA * _sandBottomRadius, _sandBottomZ);
                normals[sideBotStart + i] = radialNorm;
                uvs[sideBotStart + i] = new Vector2((float)i / SandSegments, 0f);
            }

            for (int i = 0; i < SandSegments; i++)
            {
                int next = (i + 1) % SandSegments;
                int t0 = sideTopStart + i;
                int t1 = sideTopStart + next;
                int b0 = sideBotStart + i;
                int b1 = sideBotStart + next;

                tris.Add(t0);
                tris.Add(b0);
                tris.Add(b1);

                tris.Add(t0);
                tris.Add(b1);
                tris.Add(t1);
            }

            // 3. Bottom disk
            int botCenterIdx = sideBotStart + SandSegments;
            _sandFillVertices[botCenterIdx] = new Vector3(_sandCenterX, _sandCenterY, _sandBottomZ);
            normals[botCenterIdx] = Vector3.back;
            uvs[botCenterIdx] = new Vector2(0.5f, 0.5f);

            int botRimStart = botCenterIdx + 1;
            for (int i = 0; i < SandSegments; i++)
            {
                float angle = (2f * Mathf.PI * i) / SandSegments;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);
                int idx = botRimStart + i;
                _sandFillVertices[idx] = new Vector3(_sandCenterX + cosA * _sandBottomRadius, _sandCenterY + sinA * _sandBottomRadius, _sandBottomZ);
                normals[idx] = Vector3.back;
                uvs[idx] = new Vector2(0.5f + 0.5f * cosA, 0.5f + 0.5f * sinA);
            }

            for (int i = 0; i < SandSegments; i++)
            {
                int next = (i + 1) % SandSegments;
                tris.Add(botCenterIdx);
                tris.Add(botRimStart + next);
                tris.Add(botRimStart + i);
            }

            _sandFillMesh.Clear();
            _sandFillMesh.vertices = _sandFillVertices;
            _sandFillMesh.normals = normals;
            _sandFillMesh.uv = uvs;
            _sandFillMesh.triangles = tris.ToArray();

            if (_sandFillMeshFilter != null)
            {
                _sandFillMeshFilter.sharedMesh = _sandFillMesh;
            }
        }

        private void UpdateSandFillVisual(float fillRatio)
        {
            EnsureSandFillObject();
            if (_sandFillTransform == null || _sandFillMeshFilter == null) return;

            if (fillRatio <= 0.005f || (_isMysteryHidden && !_isOnConveyor))
            {
                _sandFillTransform.gameObject.SetActive(false);
                return;
            }

            _sandFillTransform.gameObject.SetActive(true);

            float clampedRatio = Mathf.Clamp01(fillRatio);
            float zSurface = Mathf.Lerp(_sandBottomZ, _sandTopZ, clampedRatio);
            float rSurface = Mathf.Lerp(_sandBottomRadius, _sandTopRadius, clampedRatio);

            if (_sandFillVertices == null || _sandFillVertices.Length < 98)
            {
                InitializeProceduralSandMesh();
            }

            // Update top center
            _sandFillVertices[0] = new Vector3(_sandCenterX, _sandCenterY, zSurface);

            int topRimStart = 1;
            int sideTopStart = topRimStart + SandSegments;

            for (int i = 0; i < SandSegments; i++)
            {
                float angle = (2f * Mathf.PI * i) / SandSegments;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);
                Vector3 vTop = new Vector3(_sandCenterX + cosA * rSurface, _sandCenterY + sinA * rSurface, zSurface);

                _sandFillVertices[topRimStart + i] = vTop;
                _sandFillVertices[sideTopStart + i] = vTop;
            }

            _sandFillMesh.vertices = _sandFillVertices;
            _sandFillMesh.RecalculateBounds();

            if (_sandFillRenderer != null)
            {
                if (_sandFillPropBlock == null) _sandFillPropBlock = new MaterialPropertyBlock();
                _sandFillRenderer.GetPropertyBlock(_sandFillPropBlock);
                Color c = _bucketColor;
                _sandFillPropBlock.SetColor(GameConstants.BaseColorShaderId, c);
                _sandFillPropBlock.SetColor(GameConstants.MainColorShaderId, c);
                _sandFillRenderer.SetPropertyBlock(_sandFillPropBlock);
            }
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

        /// <summary>
        /// Bucket rolls out of the depot gate: starts small at the tunnel mouth and grows while sliding into its queue slot.
        /// </summary>
        public void AnimateEmergeFromDepot(Vector3 startWorldPos, Vector3 targetWorldPos, float duration = 0.40f)
        {
            _moveTweener?.Kill();
            _activeSequence?.Kill();
            _transform.position = startWorldPos;
            _transform.localScale = _baseScale * 0.35f;

            _activeSequence = DOTween.Sequence();
            _activeSequence.Append(_transform.DOMove(targetWorldPos, duration).SetEase(Ease.OutCubic));
            _activeSequence.Join(_transform.DOScale(_baseScale, duration).SetEase(Ease.OutBack));
            _activeSequence.OnComplete(() => { _transform.localScale = _baseScale; });
        }

        public void AnimateQueueShift(Vector3 targetWorldPos, float duration = 0.18f)
        {
            if (_activeSequence != null && _activeSequence.IsActive())
            {
                _activeSequence.Kill(true);
            }
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
    }
}
