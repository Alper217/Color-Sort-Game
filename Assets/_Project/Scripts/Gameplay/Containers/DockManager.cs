using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay.Grid;
using SandSort.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandSort.Gameplay.Containers
{
    /// <summary>
    /// Escalator / Conveyor Belt Loop Dock Manager (4-Bucket Active Limit):
    /// Replaces static DockSlots with a continuous 3D escalator loop wrapping around the sand tray.
    /// - Buckets enter at the bottom-center of the conveyor and travel Left-to-Right along the bottom track.
    /// - Buckets can ONLY collect sand while traveling across the bottom collection zone (for a limited duration).
    /// - If not yet 100% full at the bottom-right corner, the bucket rides the escalator up the right side,
    ///   across the top, and down the left side to re-enter the bottom collection zone from left to right!
    /// </summary>
    public sealed class DockManager : MonoBehaviour
    {
        private const int SlatCount = 48;

        private struct ConveyorSlotRuntime
        {
            public bool IsActive;
            public bool HasLandedOnBelt;
            public float DistanceAlongLoop;
            public Vector3 CurrentWorldPos;
        }

        [Header("Scene References")]
        [SerializeField] private Transform _dockRootAnchor;
        [SerializeField] private Transform[] _fixedSlotTransforms;

        [Header("Escalator / Conveyor Belt Settings (Live Tunable)")]
        [Tooltip("Yürüyen bandın hızı (Alt kısımda kovaların ne kadar süre kalarak kum toplayacağını belirler).")]
        [SerializeField, Range(0.4f, 3.5f)] private float _conveyorSpeed = 1.25f;
        [Tooltip("Yeni seçilen kovanın alt bantta başlayacağı nokta (0.50 = Tam ortadan başlar ve sağa doğru gider).")]
        [SerializeField, Range(0.05f, 0.85f)] private float _bottomSpawnNormalizedX = 0.50f;
        [Tooltip("Kovaların bant üzerinde birbirine çarpmaması için minimum takip mesafesi.")]
        [SerializeField, Range(0.50f, 1.60f)] private float _minBucketSpacingOnBelt = 1.10f;
        [SerializeField] private Vector3 _bucketWorldOffset = new Vector3(0f, 0f, -0.28f);

        private BucketView[] _activeBucketViews = Array.Empty<BucketView>();
        private ConveyorSlotRuntime[] _slotRuntimes = Array.Empty<ConveyorSlotRuntime>();
        private int _slotCount = 4;
        private Action<BucketView> _returnBucketToPoolCallback;
        private BoardSimulator _boundSimulator;

        // Conveyor loop rectangle geometry around the tray (in un-tilted board plane, transformed via _boardTiltRotation)
        private float _loopLeftX = -1.82f;
        private float _loopRightX = 1.82f;
        private float _loopBottomY = 0.02f;
        private float _loopTopY = 3.75f;
        private float _loopZ = 0.16f;
        private float _bottomWidth = 3.64f;
        private float _sideHeight = 3.73f;
        private float _totalPerimeter = 14.74f;
        private Vector3 _boardTiltPivot = new Vector3(0f, 1.95f, 0.35f);
        private Quaternion _boardTiltRotation = Quaternion.Euler(28f, 0f, 0f);

        // Procedural 3D Escalator visual objects
        private GameObject _escalatorRootObj;
        private readonly Transform[] _slatTransforms = new Transform[SlatCount];
        private float _slatScrollOffset;
        private Material _trackBedMaterial;
        private Material _trackRailMaterial;
        private Material _trackCornerMaterial;
        private Material _tunnelOpeningMaterial;
        private Material _trackSlatLightMaterial;

        public Transform DockRootAnchor => _dockRootAnchor != null ? _dockRootAnchor : transform;
        public int SlotCount => _slotCount;

        private void Awake()
        {
            ServiceLocator.Register(this);
            if (_minBucketSpacingOnBelt < 1.05f) _minBucketSpacingOnBelt = 1.10f;
            if (_bottomSpawnNormalizedX < 0.35f || _bottomSpawnNormalizedX > 0.65f) _bottomSpawnNormalizedX = 0.50f;
            Ensure3DPerspectiveCameraAndLighting();
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
            if (_escalatorRootObj != null) Destroy(_escalatorRootObj);
            if (_trackBedMaterial != null) Destroy(_trackBedMaterial);
            if (_trackRailMaterial != null) Destroy(_trackRailMaterial);
            if (_trackCornerMaterial != null) Destroy(_trackCornerMaterial);
            if (_tunnelOpeningMaterial != null) Destroy(_tunnelOpeningMaterial);
            if (_trackSlatLightMaterial != null) Destroy(_trackSlatLightMaterial);
        }

        /// <summary>
        /// Ensures the scene camera is in 3D Perspective mode and the Directional Light is angled
        /// so the tray walls, 3D escalator rails/steps, and 3D buckets display realistic 3D depth & specular highlights!
        /// </summary>
        private static void Ensure3DPerspectiveCameraAndLighting()
        {
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                float orthoHalfH = cam.orthographicSize;
                cam.orthographic = false;
                cam.fieldOfView = 36f;
                float dist = orthoHalfH / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Vector3 pos = cam.transform.position;
                cam.transform.position = new Vector3(pos.x, pos.y, -dist);
            }

            Light dirLight = FindFirstObjectByType<Light>();
            if (dirLight != null && dirLight.type == LightType.Directional)
            {
                dirLight.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
                dirLight.intensity = 1.25f;
            }
        }

        public void InitializeDock(int slotCapacity, Action<BucketView> returnBucketToPoolCallback)
        {
            _returnBucketToPoolCallback = returnBucketToPoolCallback;
            ClearActiveBuckets();

            // Ensure buckets on the conveyor always ride cleanly in front of the 3D guard rails (never clipping!)
            if (_bucketWorldOffset.z > -0.24f)
            {
                _bucketWorldOffset.z = -0.28f;
            }

            // Start in the center of the bottom track (0.50 = center)
            if (_bottomSpawnNormalizedX < 0.25f || _bottomSpawnNormalizedX > 0.75f)
            {
                _bottomSpawnNormalizedX = 0.50f;
            }

            // Enforce active slot limit on the conveyor belt (matches BoardSimulator capacity 2..6)
            _slotCount = Mathf.Clamp(slotCapacity, 2, 6);

            // Hide the old static DockSlot boxes since we now use the 3D Escalator Loop around the tray!
            if (_fixedSlotTransforms != null)
            {
                for (int i = 0; i < _fixedSlotTransforms.Length; i++)
                {
                    if (_fixedSlotTransforms[i] != null)
                    {
                        _fixedSlotTransforms[i].gameObject.SetActive(false);
                    }
                }
            }

            if (_activeBucketViews.Length < _slotCount)
            {
                _activeBucketViews = new BucketView[_slotCount];
                _slotRuntimes = new ConveyorSlotRuntime[_slotCount];
            }
            else
            {
                Array.Clear(_activeBucketViews, 0, _activeBucketViews.Length);
                Array.Clear(_slotRuntimes, 0, _slotRuntimes.Length);
            }

            CalculateLoopGeometryAroundTray();
            RebuildEscalatorVisuals();
        }

        public void BindSimulator(BoardSimulator simulator)
        {
            _boundSimulator = simulator;
        }

        private void CalculateLoopGeometryAroundTray()
        {
            GameObject trayObj = GameObject.Find("Static_GridBoardFrame");
            if (trayObj != null)
            {
                // Ensure the tray scale leaves clean room around its 4 sides for the escalator loop inside the 9:16 screen
                Vector3 ls = trayObj.transform.localScale;
                if (ls.x > 3.15f || ls.z > 3.15f)
                {
                    trayObj.transform.localScale = new Vector3(3.05f, ls.y, 3.05f);
                }

                if (ServiceLocator.TryGet(out GridManager gm))
                {
                    gm.CalculateExactTrayCavityLayout();
                    Bounds b = gm.UnrotatedOuterBounds;
                    _boardTiltPivot = gm.UnrotatedBoardCenter;
                    _boardTiltRotation = gm.BoardTiltRotation;

                    const float railOffset = 0.24f;
                    _loopLeftX = b.min.x - railOffset;
                    _loopRightX = b.max.x + railOffset;
                    _loopBottomY = b.min.y - railOffset;
                    _loopTopY = b.max.y + railOffset;
                    _loopZ = b.min.z - 0.04f;
                }
                else
                {
                    MeshRenderer baseRenderer = trayObj.GetComponent<MeshRenderer>();
                    if (baseRenderer != null)
                    {
                        Bounds b = baseRenderer.bounds;
                        _boardTiltPivot = b.center;
                        _boardTiltRotation = Quaternion.Euler(28f, 0f, 0f);
                        const float railOffset = 0.24f;
                        _loopLeftX = b.min.x - railOffset;
                        _loopRightX = b.max.x + railOffset;
                        _loopBottomY = b.min.y - railOffset;
                        _loopTopY = b.max.y + railOffset;
                        _loopZ = b.min.z - 0.04f;
                    }
                }
            }

            _bottomWidth = Mathf.Max(1.0f, _loopRightX - _loopLeftX);
            _sideHeight = Mathf.Max(1.0f, _loopTopY - _loopBottomY);
            _totalPerimeter = 2f * (_bottomWidth + _sideHeight);
        }

        private Vector3 TransformFlatPointToTiltedWorld(Vector3 flatWorldPoint)
        {
            return _boardTiltPivot + _boardTiltRotation * (flatWorldPoint - _boardTiltPivot);
        }

        private void RebuildEscalatorVisuals()
        {
            if (_escalatorRootObj != null)
            {
                Destroy(_escalatorRootObj);
            }

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            // Soft, matte, light-grey housing with a calm lavender belt surface (reference style).
            if (_trackBedMaterial == null)
            {
                _trackBedMaterial = CreateLitMaterial(litShader, "Mat_EscalatorBelt", new Color32(150, 153, 186, 255), 0.22f, 0f);
            }
            if (_trackRailMaterial == null)
            {
                _trackRailMaterial = CreateLitMaterial(litShader, "Mat_EscalatorRail", new Color32(226, 228, 236, 255), 0.45f, 0.05f);
            }
            if (_trackCornerMaterial == null)
            {
                _trackCornerMaterial = CreateLitMaterial(litShader, "Mat_EscalatorCornerCap", new Color32(214, 217, 228, 255), 0.50f, 0.05f);
            }
            if (_tunnelOpeningMaterial == null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
                if (unlitShader == null) unlitShader = litShader;
                _tunnelOpeningMaterial = CreateLitMaterial(unlitShader, "Mat_EscalatorTunnelDarkMouth", new Color32(8, 6, 15, 255), 0f, 0f);
            }
            if (_trackSlatLightMaterial == null)
            {
                _trackSlatLightMaterial = CreateLitMaterial(litShader, "Mat_EscalatorPanelLine", new Color32(112, 116, 152, 255), 0.15f, 0f);
            }

            _escalatorRootObj = new GameObject("EscalatorConveyorLoop_3D");
            _escalatorRootObj.transform.SetParent(DockRootAnchor, true);
            _escalatorRootObj.transform.position = _boardTiltPivot;
            _escalatorRootObj.transform.rotation = _boardTiltRotation;
            _escalatorRootObj.transform.localScale = Vector3.one;

            float cx = (_loopLeftX + _loopRightX) * 0.5f;
            float cy = (_loopBottomY + _loopTopY) * 0.5f;
            const float trackWidth = 0.36f;
            const float bedThickness = 0.16f;
            const float railDepthZ = 0.20f;
            float bedZ = _loopZ + 0.12f;
            float railZ = _loopZ - 0.02f;

            // 1. Flat belt surface (4 sides)
            CreateTrackBar("Bed_Bottom", new Vector3(cx, _loopBottomY, bedZ), new Vector3(_bottomWidth + trackWidth, trackWidth, bedThickness), _trackBedMaterial);
            CreateTrackBar("Bed_Top", new Vector3(cx, _loopTopY, bedZ), new Vector3(_bottomWidth + trackWidth, trackWidth, bedThickness), _trackBedMaterial);
            CreateTrackBar("Bed_Left", new Vector3(_loopLeftX, cy, bedZ), new Vector3(trackWidth, _sideHeight - trackWidth, bedThickness), _trackBedMaterial);
            CreateTrackBar("Bed_Right", new Vector3(_loopRightX, cy, bedZ), new Vector3(trackWidth, _sideHeight - trackWidth, bedThickness), _trackBedMaterial);

            // 2. Solid square walls: thin outer wall + thick inner wall (fills the gap towards the tray)
            const float outerWallT = 0.105f;
            const float innerWallT = 0.20f;
            float outerOff = trackWidth * 0.5f + outerWallT * 0.5f;
            float innerOff = trackWidth * 0.5f + innerWallT * 0.5f;
            float outerW = _bottomWidth + trackWidth + outerWallT * 2f;
            float outerH = _sideHeight + trackWidth + outerWallT * 2f;
            float innerW = Mathf.Max(0.5f, _bottomWidth - trackWidth);
            float innerH = Mathf.Max(0.5f, _sideHeight - trackWidth);

            CreateTrackBar("Wall_Outer_Bottom", new Vector3(cx, _loopBottomY - outerOff, railZ), new Vector3(outerW, outerWallT, railDepthZ), _trackRailMaterial);
            CreateTrackBar("Wall_Outer_Top", new Vector3(cx, _loopTopY + outerOff, railZ), new Vector3(outerW, outerWallT, railDepthZ), _trackRailMaterial);
            CreateTrackBar("Wall_Outer_Left", new Vector3(_loopLeftX - outerOff, cy, railZ), new Vector3(outerWallT, outerH, railDepthZ), _trackRailMaterial);
            CreateTrackBar("Wall_Outer_Right", new Vector3(_loopRightX + outerOff, cy, railZ), new Vector3(outerWallT, outerH, railDepthZ), _trackRailMaterial);

            CreateTrackBar("Wall_Inner_Bottom", new Vector3(cx, _loopBottomY + innerOff, railZ), new Vector3(innerW, innerWallT, railDepthZ), _trackRailMaterial);
            CreateTrackBar("Wall_Inner_Top", new Vector3(cx, _loopTopY - innerOff, railZ), new Vector3(innerW, innerWallT, railDepthZ), _trackRailMaterial);
            CreateTrackBar("Wall_Inner_Left", new Vector3(_loopLeftX + innerOff, cy, railZ), new Vector3(innerWallT, innerH, railDepthZ), _trackRailMaterial);
            CreateTrackBar("Wall_Inner_Right", new Vector3(_loopRightX - innerOff, cy, railZ), new Vector3(innerWallT, innerH, railDepthZ), _trackRailMaterial);

            // 3. Square corner blocks with black tunnel entrances/exits
            float cornerTunnelZ = _loopZ - 0.24f;
            float cornerSideSize = trackWidth + outerWallT * 2.3f;
            Vector3 cornerBoxScale = new Vector3(cornerSideSize, cornerSideSize, 0.44f);
            CreateTrackBar("CornerSquare_BL", new Vector3(_loopLeftX, _loopBottomY, cornerTunnelZ), cornerBoxScale, _trackCornerMaterial);
            CreateTrackBar("CornerSquare_BR", new Vector3(_loopRightX, _loopBottomY, cornerTunnelZ), cornerBoxScale, _trackCornerMaterial);
            CreateTrackBar("CornerSquare_TR", new Vector3(_loopRightX, _loopTopY, cornerTunnelZ), cornerBoxScale, _trackCornerMaterial);
            CreateTrackBar("CornerSquare_TL", new Vector3(_loopLeftX, _loopTopY, cornerTunnelZ), cornerBoxScale, _trackCornerMaterial);

            float halfCorner = cornerSideSize * 0.5f;
            float mouthWidth = trackWidth - 0.02f;
            float mouthThickness = 0.045f;
            float mouthDepthZ = 0.34f;
            float mouthCenterZ = _loopZ - 0.14f;
            Material mouth = _tunnelOpeningMaterial;

            CreateTrackBar("TunnelMouth_BL_Top", new Vector3(_loopLeftX, _loopBottomY + halfCorner + 0.005f, mouthCenterZ), new Vector3(mouthWidth, mouthThickness, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_BL_Right", new Vector3(_loopLeftX + halfCorner + 0.005f, _loopBottomY, mouthCenterZ), new Vector3(mouthThickness, mouthWidth, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_BR_Left", new Vector3(_loopRightX - halfCorner - 0.005f, _loopBottomY, mouthCenterZ), new Vector3(mouthThickness, mouthWidth, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_BR_Top", new Vector3(_loopRightX, _loopBottomY + halfCorner + 0.005f, mouthCenterZ), new Vector3(mouthWidth, mouthThickness, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_TR_Bottom", new Vector3(_loopRightX, _loopTopY - halfCorner - 0.005f, mouthCenterZ), new Vector3(mouthWidth, mouthThickness, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_TR_Left", new Vector3(_loopRightX - halfCorner - 0.005f, _loopTopY, mouthCenterZ), new Vector3(mouthThickness, mouthWidth, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_TL_Right", new Vector3(_loopLeftX + halfCorner + 0.005f, _loopTopY, mouthCenterZ), new Vector3(mouthThickness, mouthWidth, mouthDepthZ), mouth);
            CreateTrackBar("TunnelMouth_TL_Bottom", new Vector3(_loopLeftX, _loopTopY - halfCorner - 0.005f, mouthCenterZ), new Vector3(mouthWidth, mouthThickness, mouthDepthZ), mouth);
            // 4. Thin panel divider lines that scroll around the loop
            for (int i = 0; i < SlatCount; i++)
            {
                GameObject slat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slat.name = $"EscalatorPanelLine_{i:00}";
                Destroy(slat.GetComponent<Collider>());
                slat.transform.SetParent(_escalatorRootObj.transform, false);

                MeshRenderer mr = slat.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _trackSlatLightMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _slatTransforms[i] = slat.transform;
            }

            UpdateEscalatorSlatPositions(0f);
        }

        private static Material CreateLitMaterial(Shader shader, string matName, Color32 color, float smoothness, float metallic)
        {
            Material mat = new Material(shader) { name = matName };
            SetMatColor(mat, color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        private void CreateTrackBar(string barName, Vector3 flatWorldPos, Vector3 scale, Material mat)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = barName;
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(_escalatorRootObj.transform, false);
            bar.transform.localPosition = flatWorldPos - _boardTiltPivot;
            bar.transform.localRotation = Quaternion.identity;
            bar.transform.localScale = scale;

            MeshRenderer mr = bar.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void SetMatColor(Material mat, Color32 c)
        {
            Color col = c;
            if (mat.HasProperty(GameConstants.BaseColorShaderId))
                mat.SetColor(GameConstants.BaseColorShaderId, col);
            if (mat.HasProperty(GameConstants.MainColorShaderId))
                mat.SetColor(GameConstants.MainColorShaderId, col);
            mat.color = col;
        }

        private void Update()
        {
            if (_totalPerimeter <= 0.1f) return;

            float dt = Time.deltaTime;
            UpdateEscalatorSlatPositions(dt);
            AdvanceConveyorBuckets(dt);
        }

        private void UpdateEscalatorSlatPositions(float dt)
        {
            if (_escalatorRootObj == null) return;

            _slatScrollOffset = (_slatScrollOffset + _conveyorSpeed * dt) % _totalPerimeter;
            float spacing = _totalPerimeter / SlatCount;
            float slatZ = _loopZ + 0.035f;

            for (int i = 0; i < SlatCount; i++)
            {
                Transform st = _slatTransforms[i];
                if (st == null) continue;

                float dist = (_slatScrollOffset + i * spacing) % _totalPerimeter;
                EvaluateLoopPositionAndTangent(dist, out Vector3 flatPos, out Vector3 tangent, out _);
                flatPos.z = slatZ;

                // Position in local space of _escalatorRootObj so slats stay 100% flush inside the 3D tilted channel!
                st.localPosition = flatPos - _boardTiltPivot;
                st.localRotation = Quaternion.identity;

                bool horizontalSegment = Mathf.Abs(tangent.x) > 0.5f;
                st.localScale = horizontalSegment
                    ? new Vector3(0.028f, 0.34f, 0.02f)
                    : new Vector3(0.34f, 0.028f, 0.02f);
            }
        }

        private void AdvanceConveyorBuckets(float dt)
        {
            // 1. Advance all landed buckets along the belt loop
            for (int s = 0; s < _slotCount; s++)
            {
                if (!_slotRuntimes[s].IsActive || _activeBucketViews[s] == null)
                {
                    if (_boundSimulator != null)
                    {
                        _boundSimulator.SetSlotConveyorState(s, false, 0.5f);
                    }
                    continue;
                }

                if (_slotRuntimes[s].HasLandedOnBelt)
                {
                    // While on bottom segment collecting sand, cruise at normal speed; speed up on return loop
                    bool currentlyOnBottom = _slotRuntimes[s].DistanceAlongLoop <= _bottomWidth;
                    float effectiveConveyorSpeed = currentlyOnBottom ? _conveyorSpeed : (_conveyorSpeed * 2.2f);
                    float step = effectiveConveyorSpeed * dt;
                    _slotRuntimes[s].DistanceAlongLoop = (_slotRuntimes[s].DistanceAlongLoop + step) % _totalPerimeter;
                }
            }

            // 2. Strict Train-Car Anti-Overlap Solver:
            // Ensure no two buckets on the belt ever penetrate or overlap each other.
            // If bucket i is trailing bucket j, its distance along the loop is clamped behind j by at least _minBucketSpacingOnBelt.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < _slotCount; i++)
                {
                    if (!_slotRuntimes[i].IsActive) continue;
                    for (int j = 0; j < _slotCount; j++)
                    {
                        if (i == j || !_slotRuntimes[j].IsActive) continue;

                        float aheadDelta = (_slotRuntimes[j].DistanceAlongLoop - _slotRuntimes[i].DistanceAlongLoop + _totalPerimeter) % _totalPerimeter;
                        if (aheadDelta >= 0f && aheadDelta < _minBucketSpacingOnBelt)
                        {
                            // Push trailing bucket i back so it never clips into leading bucket j
                            _slotRuntimes[i].DistanceAlongLoop = (_slotRuntimes[j].DistanceAlongLoop - _minBucketSpacingOnBelt + _totalPerimeter) % _totalPerimeter;
                        }
                    }
                }
            }

            // 3. Update transforms and visuals
            for (int s = 0; s < _slotCount; s++)
            {
                if (!_slotRuntimes[s].IsActive || _activeBucketViews[s] == null) continue;

                if (_slotRuntimes[s].HasLandedOnBelt)
                {
                    bool onBottomSegment;
                    Vector3 flatLoopPos;
                    EvaluateLoopPositionAndTangent(
                        _slotRuntimes[s].DistanceAlongLoop,
                        out flatLoopPos,
                        out _,
                        out onBottomSegment);

                    // 1. Corner Tunnel Underpass: smoothly dip bucket deeper in local Z and tuck under the 4 corner squares!
                    float cornerFactor = CalculateCornerTunnelFactor(in flatLoopPos);
                    float localBucketZ = Mathf.Lerp(_bucketWorldOffset.z, 0.16f, cornerFactor);
                    Vector3 flatBucketPos = new Vector3(
                        flatLoopPos.x + _bucketWorldOffset.x,
                        flatLoopPos.y + _bucketWorldOffset.y,
                        _loopZ + localBucketZ);

                    Vector3 worldPos = TransformFlatPointToTiltedWorld(flatBucketPos);

                    // 2. 3D Perspective Scale
                    float depthT = Mathf.InverseLerp(_loopBottomY, _loopTopY, flatLoopPos.y);
                    float perspectiveScale = Mathf.Lerp(1.12f, 0.78f, depthT) * Mathf.Lerp(1.0f, 0.52f, cornerFactor);

                    _slotRuntimes[s].CurrentWorldPos = worldPos;
                    _activeBucketViews[s].CachedTransform.position = worldPos;
                    _activeBucketViews[s].SetConveyorPerspectiveScale(perspectiveScale);
                    _activeBucketViews[s].SetInsideCornerTunnel(cornerFactor > 0.35f);

                    if (_boundSimulator != null)
                    {
                        float normX = onBottomSegment
                            ? Mathf.Clamp01(_slotRuntimes[s].DistanceAlongLoop / _bottomWidth)
                            : 0.5f;
                        _boundSimulator.SetSlotConveyorState(s, onBottomSegment, normX);
                    }
                }
                else
                {
                    // While jumping from queue to belt, hold collection until it lands on the belt
                    if (_boundSimulator != null)
                    {
                        _boundSimulator.SetSlotConveyorState(s, false, _bottomSpawnNormalizedX);
                    }
                }
            }
        }

        private float CalculateCornerTunnelFactor(in Vector3 flatLoopPos)
        {
            float dx = Mathf.Min(Mathf.Abs(flatLoopPos.x - _loopLeftX), Mathf.Abs(flatLoopPos.x - _loopRightX));
            float dy = Mathf.Min(Mathf.Abs(flatLoopPos.y - _loopBottomY), Mathf.Abs(flatLoopPos.y - _loopTopY));
            float cornerDist = Mathf.Sqrt(dx * dx + dy * dy);

            const float outerRadius = 0.42f;
            const float innerRadius = 0.18f;
            if (cornerDist >= outerRadius) return 0f;
            if (cornerDist <= innerRadius) return 1f;

            float t = 1f - (cornerDist - innerRadius) / (outerRadius - innerRadius);
            return t * t * (3f - 2f * t);
        }

        public bool CanAcceptNewBucket()
        {
            // 1. Check if there is an inactive slot available
            bool hasFreeSlot = false;
            for (int s = 0; s < _slotCount; s++)
            {
                if (s < _slotRuntimes.Length && !_slotRuntimes[s].IsActive)
                {
                    hasFreeSlot = true;
                    break;
                }
            }
            if (!hasFreeSlot) return false;

            // 2. Check if the center spawn zone is clear of existing active buckets
            float centerDist = Mathf.Clamp(_bottomSpawnNormalizedX, 0.35f, 0.65f) * _bottomWidth;
            for (int s = 0; s < _slotCount; s++)
            {
                if (s >= _slotRuntimes.Length || !_slotRuntimes[s].IsActive) continue;

                float diff = Mathf.Abs(Mathf.DeltaAngle(
                    (centerDist / _totalPerimeter) * 360f,
                    (_slotRuntimes[s].DistanceAlongLoop / _totalPerimeter) * 360f)) / 360f * _totalPerimeter;

                if (diff < _minBucketSpacingOnBelt + 0.08f)
                {
                    // Center spawn area is currently occupied by an active bucket!
                    return false;
                }
            }

            return true;
        }

        private float FindNonOverlappingSpawnDistance()
        {
            // Center of bottom collection segment (X = 0 in world space) - always start in the center!
            return Mathf.Clamp(_bottomSpawnNormalizedX, 0.35f, 0.65f) * _bottomWidth;
        }

        /// <summary>
        /// Evaluates a distance [0 .. _totalPerimeter) around the 4-sided escalator loop (in un-tilted board plane):
        /// Segment 0 (Bottom, Left -> Right): [0 .. W) -> Active Sand Collection Zone!
        /// Segment 1 (Right, Bottom -> Top):  [W .. W + H)
        /// Segment 2 (Top, Right -> Left):    [W + H .. 2W + H)
        /// Segment 3 (Left, Top -> Bottom):   [2W + H .. 2W + 2H)
        /// </summary>
        private void EvaluateLoopPositionAndTangent(
            float distance,
            out Vector3 flatPos,
            out Vector3 tangent,
            out bool isOnBottomCollectionSegment)
        {
            float d = ((distance % _totalPerimeter) + _totalPerimeter) % _totalPerimeter;

            if (d <= _bottomWidth)
            {
                // Bottom edge: Left -> Right (Active Collection Zone!)
                isOnBottomCollectionSegment = true;
                tangent = Vector3.right;
                flatPos = new Vector3(_loopLeftX + d, _loopBottomY, _loopZ);
                return;
            }

            isOnBottomCollectionSegment = false;
            d -= _bottomWidth;

            if (d <= _sideHeight)
            {
                // Right edge: Bottom -> Top
                tangent = Vector3.up;
                flatPos = new Vector3(_loopRightX, _loopBottomY + d, _loopZ);
                return;
            }

            d -= _sideHeight;

            if (d <= _bottomWidth)
            {
                // Top edge: Right -> Left
                tangent = Vector3.left;
                flatPos = new Vector3(_loopRightX - d, _loopTopY, _loopZ);
                return;
            }

            d -= _bottomWidth;

            // Left edge: Top -> Bottom
            tangent = Vector3.down;
            flatPos = new Vector3(_loopLeftX, _loopTopY - d, _loopZ);
        }

        public Vector3 GetSlotBucketWorldPosition(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < _slotRuntimes.Length && _slotRuntimes[slotIndex].IsActive)
            {
                if (slotIndex < _activeBucketViews.Length && _activeBucketViews[slotIndex] != null)
                {
                    return _activeBucketViews[slotIndex].CachedTransform.position;
                }
                return _slotRuntimes[slotIndex].CurrentWorldPos;
            }

            EvaluateLoopPositionAndTangent(_bottomSpawnNormalizedX * _bottomWidth, out Vector3 flatSpawnPos, out _, out _);
            return TransformFlatPointToTiltedWorld(flatSpawnPos + _bucketWorldOffset);
        }

        public Vector3 GetSlotIntakeWorldPosition(int slotIndex)
        {
            // Target the center of the tilted open circular mouth of the bucket!
            return GetSlotBucketWorldPosition(slotIndex) + new Vector3(0f, 0.05f, -0.04f);
        }

        public System.Collections.Generic.IReadOnlyList<Vector3> GetActiveBucketWorldPositions()
        {
            var list = new System.Collections.Generic.List<Vector3>(_slotCount);
            for (int i = 0; i < _slotCount; i++)
            {
                if (_slotRuntimes != null && i < _slotRuntimes.Length && _slotRuntimes[i].IsActive)
                {
                    list.Add(GetSlotBucketWorldPosition(i));
                }
            }
            return list;
        }

        public void PlaceBucketViewInSlot(int slotIndex, BucketView bucketView, Action onLandedCallback = null)
        {
            if (slotIndex < 0 || slotIndex >= _slotCount || slotIndex >= _activeBucketViews.Length || slotIndex >= _slotRuntimes.Length || bucketView == null) return;

            float spawnDist = FindNonOverlappingSpawnDistance();
            EvaluateLoopPositionAndTangent(spawnDist, out Vector3 flatLoopPos, out _, out _);
            Vector3 targetWorldPos = TransformFlatPointToTiltedWorld(flatLoopPos + _bucketWorldOffset);

            _activeBucketViews[slotIndex] = bucketView;
            _slotRuntimes[slotIndex] = new ConveyorSlotRuntime
            {
                IsActive = true,
                HasLandedOnBelt = false,
                DistanceAlongLoop = spawnDist,
                CurrentWorldPos = targetWorldPos
            };

            bucketView.CachedTransform.SetParent(DockRootAnchor, true);
            int capturedSlot = slotIndex;
            bucketView.AnimateDeployToDock(targetWorldPos, 0.22f, () =>
            {
                if (capturedSlot >= 0 && capturedSlot < _slotRuntimes.Length && _slotRuntimes[capturedSlot].IsActive)
                {
                    _slotRuntimes[capturedSlot].HasLandedOnBelt = true;
                }
                onLandedCallback?.Invoke();
            });
        }

        public void NotifyGrainArrivedAtSlot(int slotIndex, BoardSimulator simulator)
        {
            if (simulator == null || slotIndex < 0) return;

            if (!simulator.ConfirmGrainArrival(slotIndex, out ActiveDockSlotState updatedState, out bool bucketCompleted))
                return;

            BucketView view = (slotIndex >= 0 && slotIndex < _activeBucketViews.Length)
                ? _activeBucketViews[slotIndex]
                : null;

            if (view != null)
            {
                view.UpdateFillProgress(updatedState.CurrentCount, updatedState.MaxCapacity);
                SandSort.Gameplay.VFX.JuicyVFXManager.Instance?.PlayGrainSplashMicroVFX(GetSlotIntakeWorldPosition(slotIndex), view.BucketColor);
            }

            EventBus<GrainArrivedInBucketEvent>.Publish(new GrainArrivedInBucketEvent(
                slotIndex,
                updatedState.ColorId,
                updatedState.CurrentCount,
                updatedState.MaxCapacity,
                simulator.RemainingGridGrains + simulator.TotalInFlightGrains));

            if (bucketCompleted)
            {
                Vector3 fxPos = view != null ? view.CachedTransform.position : GetSlotBucketWorldPosition(slotIndex);
                Color32 fxColor = view != null ? view.BucketColor : (Color32)Color.yellow;
                SandSort.Gameplay.VFX.JuicyVFXManager.Instance?.PlayBucketCompleteVFX(fxPos, fxColor);

                if (slotIndex >= 0 && slotIndex < _activeBucketViews.Length)
                {
                    _activeBucketViews[slotIndex] = null;
                }

                if (slotIndex >= 0 && slotIndex < _slotRuntimes.Length)
                {
                    _slotRuntimes[slotIndex].IsActive = false;
                    _slotRuntimes[slotIndex].HasLandedOnBelt = false;
                }

                if (view != null)
                {
                    view.AnimateCompletionAndRelease(_returnBucketToPoolCallback);
                }

                EventBus<BucketCompletedEvent>.Publish(new BucketCompletedEvent(slotIndex, updatedState.ColorId));
            }
        }

        public void ClearActiveBuckets()
        {
            if (_activeBucketViews == null) return;
            for (int i = 0; i < _activeBucketViews.Length; i++)
            {
                if (_activeBucketViews[i] != null)
                {
                    _activeBucketViews[i].KillActiveTweens();
                    _returnBucketToPoolCallback?.Invoke(_activeBucketViews[i]);
                    _activeBucketViews[i] = null;
                }
                if (_slotRuntimes != null && i < _slotRuntimes.Length)
                {
                    _slotRuntimes[i].IsActive = false;
                    _slotRuntimes[i].HasLandedOnBelt = false;
                }
            }
        }
    }
}
