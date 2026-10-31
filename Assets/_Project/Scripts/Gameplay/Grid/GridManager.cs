using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandSort.Gameplay.Grid
{
    /// <summary>
    /// High-performance GPU-Instanced Fine Sand Renderer & Smooth Sandbox Physics Integrator.
    /// Features:
    /// 1. Inspector-editable sand resolution (_sandResolutionPerAxis: e.g., 85x85 = 7,225 or 100x100 = 10,000 grains!)
    /// 2. Unlit 3-tone micro-speckle sand shading + organic position jitter so individual spheres fuse into a dense sand texture!
    /// 3. Exact inner-wall tray fitting + Edit Mode Scene Gizmos for manual placement.
    /// </summary>
    public sealed class GridManager : MonoBehaviour
    {
        private const int MaxBatchSize = 1023;
        private const int ShadesPerColor = 3;

        [Header("Scene References (Move/Scale Tray Manually in Scene!)")]
        [SerializeField] private Transform _gridRootAnchor;
        [SerializeField] private Transform _boardFrameVisual;
        [SerializeField] private GameObject _sandGrainPrefab;

        [Header("Değiştirilebilir Kum Tanecik Sayısı (Resolution)")]
        [Tooltip("Her eksendeki kum taneciği sayısı. Örn: 85 = 7.225 kum, 100 = 10.000 kum, 120 = 14.400 kum!")]
        [SerializeField, Range(24, 130)] private int _sandResolutionPerAxis = 88;
        [Tooltip("Kumların robotik kare ızgara gibi görünmesini engelleyen doğal kum dağılımı (Jitter).")]
        [SerializeField, Range(0f, 0.25f)] private float _organicSandJitter = 0.06f;
        [Tooltip("Kum taneciklerinin birbirine kaynaşıp hiç boşluk bırakmaması için ölçek çarpanı.")]
        [SerializeField, Range(1.10f, 1.85f)] private float _grainScaleMultiplier = 1.56f;

        [Header("Board Layout & Sandbox Feel")]
        [SerializeField] private bool _autoFitToTrayBounds = true;
        [SerializeField] private float _boardWorldWidth = 3.20f;
        [SerializeField] private float _boardWorldHeight = 3.20f;
        [SerializeField] private float _gravityAcceleration = 15.0f;
        [SerializeField] private float _maxFallSpeed = 7.0f;
        [SerializeField] private float _avalancheSlideSpeed = 8.5f;

        private Mesh _grainMesh;
        private Material _baseGrainMaterial;

        // 3 micro-shade instanced materials per palette color for natural sandy texture without 3D ball rims
        private Material[] _shadedInstancedMaterials = Array.Empty<Material>();
        private Matrix4x4[][][] _shadeMatrixBatches = Array.Empty<Matrix4x4[][]>();
        private int[] _shadeInstanceCounts = Array.Empty<int>();
        private int _paletteColorCount;

        // Smooth continuous visual offsets
        private float[] _columnVisualOffsetY = Array.Empty<float>();
        private float[] _columnFallVelocityY = Array.Empty<float>();
        private float[] _cellSlideOffsetX = Array.Empty<float>();
        private float[] _cellSlideOffsetY = Array.Empty<float>();

        private BoardSimulator _simulator;
        private LevelDataSO _currentLevel;
        private int _width;
        private int _height;
        private float _cellSizeX;
        private float _cellSizeY;
        private Vector3 _bottomLeftWorldOrigin;
        private Vector3 _cellVisualScale;

        public Transform GridRootAnchor => _gridRootAnchor != null ? _gridRootAnchor : transform;
        public int SandResolutionPerAxis => _sandResolutionPerAxis;
        public int TotalSimulatedGrains => _sandResolutionPerAxis * _sandResolutionPerAxis;
        public Vector3 GrainWorldScale => _cellVisualScale;
        public Mesh GrainMesh => _grainMesh;

        private void Awake()
        {
            ServiceLocator.Register(this);
            EnsureMeshAndBaseMaterialLoaded();
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
            CleanupMaterials();
        }

        private void EnsureMeshAndBaseMaterialLoaded()
        {
            if (_grainMesh != null && _baseGrainMaterial != null) return;

            if (_sandGrainPrefab != null)
            {
                MeshFilter mf = _sandGrainPrefab.GetComponentInChildren<MeshFilter>();
                if (mf != null) _grainMesh = mf.sharedMesh;

                MeshRenderer mr = _sandGrainPrefab.GetComponentInChildren<MeshRenderer>();
                if (mr != null) _baseGrainMaterial = mr.sharedMaterial;
            }

            if (_grainMesh == null || _baseGrainMaterial == null)
            {
                GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (_grainMesh == null) _grainMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
                if (_baseGrainMaterial == null) _baseGrainMaterial = tempSphere.GetComponent<MeshRenderer>().sharedMaterial;
                DestroyImmediate(tempSphere);
            }
        }

        private void RebuildPaletteMaterials(Color32[] palette)
        {
            EnsureMeshAndBaseMaterialLoaded();
            CleanupMaterials();

            _paletteColorCount = (palette != null && palette.Length > 0) ? palette.Length : 6;
            int totalShadeBuckets = _paletteColorCount * ShadesPerColor;

            // Prefer URP Unlit shader so spheres don't have dark 3D ball rims and fuse into fine sand!
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

            _shadedInstancedMaterials = new Material[totalShadeBuckets];
            _shadeInstanceCounts = new int[totalShadeBuckets];
            _shadeMatrixBatches = new Matrix4x4[totalShadeBuckets][][];

            float[] shadeMultipliers = { 0.94f, 1.00f, 1.06f };

            for (int c = 0; c < _paletteColorCount; c++)
            {
                Color baseCol = (palette != null && c < palette.Length) ? (Color)palette[c] : Color.white;

                for (int s = 0; s < ShadesPerColor; s++)
                {
                    int bucketIdx = c * ShadesPerColor + s;
                    Material mat = unlitShader != null
                        ? new Material(unlitShader)
                        : new Material(_baseGrainMaterial);

                    mat.name = $"InstancedFineSand_{c}_Shade_{s}";
                    mat.enableInstancing = true;

                    float mult = shadeMultipliers[s];
                    Color shadedCol = new Color(
                        Mathf.Clamp01(baseCol.r * mult),
                        Mathf.Clamp01(baseCol.g * mult),
                        Mathf.Clamp01(baseCol.b * mult),
                        1f);

                    if (mat.HasProperty(GameConstants.BaseColorShaderId))
                        mat.SetColor(GameConstants.BaseColorShaderId, shadedCol);
                    if (mat.HasProperty(GameConstants.MainColorShaderId))
                        mat.SetColor(GameConstants.MainColorShaderId, shadedCol);

                    _shadedInstancedMaterials[bucketIdx] = mat;

                    // Pre-allocate 6 batches of 1023 per shade bucket (supports up to 18,000+ total grains)
                    _shadeMatrixBatches[bucketIdx] = new Matrix4x4[6][];
                    for (int b = 0; b < 6; b++)
                    {
                        _shadeMatrixBatches[bucketIdx][b] = new Matrix4x4[MaxBatchSize];
                    }
                }
            }
        }

        public Material GetInstancedMaterialForColor(int colorId)
        {
            int shadeBucket = colorId * ShadesPerColor + 1; // Middle pure shade
            if (_shadedInstancedMaterials != null && shadeBucket >= 0 && shadeBucket < _shadedInstancedMaterials.Length)
            {
                return _shadedInstancedMaterials[shadeBucket];
            }
            return _baseGrainMaterial;
        }

        private void CleanupMaterials()
        {
            if (_shadedInstancedMaterials == null) return;
            for (int i = 0; i < _shadedInstancedMaterials.Length; i++)
            {
                if (_shadedInstancedMaterials[i] != null)
                {
                    Destroy(_shadedInstancedMaterials[i]);
                }
            }
            _shadedInstancedMaterials = Array.Empty<Material>();
        }

        public void BuildVisualGrid(LevelDataSO levelData, BoardSimulator simulator)
        {
            _currentLevel = levelData;
            _simulator = simulator;
            _width = simulator.Width;
            _height = simulator.Height;
            int totalCells = _width * _height;

            RebuildPaletteMaterials(levelData.Palette);

            if (_columnVisualOffsetY.Length < _width)
            {
                _columnVisualOffsetY = new float[_width];
                _columnFallVelocityY = new float[_width];
            }
            Array.Clear(_columnVisualOffsetY, 0, _width);
            Array.Clear(_columnFallVelocityY, 0, _width);

            if (_cellSlideOffsetX.Length < totalCells)
            {
                _cellSlideOffsetX = new float[totalCells];
                _cellSlideOffsetY = new float[totalCells];
            }
            Array.Clear(_cellSlideOffsetX, 0, totalCells);
            Array.Clear(_cellSlideOffsetY, 0, totalCells);

            CalculateExactTrayCavityLayout();
        }

        /// <summary>
        /// Reads the exact world inner faces of the 4 child walls of the manually placed tray (_boardFrameVisual)
        /// so the sand matrix always fits 100% inside the tray walls wherever the user places or scales it!
        /// </summary>
        public void CalculateExactTrayCavityLayout()
        {
            Vector3 centerWorld = GridRootAnchor.position;

            if (_autoFitToTrayBounds && _boardFrameVisual != null)
            {
                MeshRenderer[] renderers = _boardFrameVisual.GetComponentsInChildren<MeshRenderer>();
                MeshRenderer baseRenderer = _boardFrameVisual.GetComponent<MeshRenderer>();

                if (baseRenderer != null)
                {
                    Bounds outerBounds = baseRenderer.bounds;
                    Vector3 frameCenter = outerBounds.center;

                    float innerMinX = outerBounds.min.x;
                    float innerMaxX = outerBounds.max.x;
                    float innerMinY = outerBounds.min.y;
                    float innerMaxY = outerBounds.max.y;

                    for (int i = 0; i < renderers.Length; i++)
                    {
                        MeshRenderer r = renderers[i];
                        if (r == baseRenderer) continue;

                        Bounds wb = r.bounds;
                        Vector3 wc = wb.center;

                        if (wb.size.x < wb.size.y * 0.4f)
                        {
                            if (wc.x < frameCenter.x)
                                innerMinX = Mathf.Max(innerMinX, wb.max.x);
                            else
                                innerMaxX = Mathf.Min(innerMaxX, wb.min.x);
                        }
                        else if (wb.size.y < wb.size.x * 0.4f)
                        {
                            if (wc.y < frameCenter.y)
                                innerMinY = Mathf.Max(innerMinY, wb.max.y);
                            else
                                innerMaxY = Mathf.Min(innerMaxY, wb.min.y);
                        }
                    }

                    const float wallSafetyInset = 0.005f;
                    _boardWorldWidth = Mathf.Max(0.5f, (innerMaxX - innerMinX) - wallSafetyInset * 2f);
                    _boardWorldHeight = Mathf.Max(0.5f, (innerMaxY - innerMinY) - wallSafetyInset * 2f);

                    // outerBounds.min.z is the front surface of the tray floor facing the camera (-Z).
                    // Place sand just in front of the floor surface (-0.055f) so the floor never occludes it,
                    // while remaining recessed inside the protruding side walls!
                    float cavityZ = outerBounds.min.z - 0.055f;
                    centerWorld = new Vector3(
                        (innerMinX + innerMaxX) * 0.5f,
                        (innerMinY + innerMaxY) * 0.5f,
                        cavityZ);
                }
            }

            int w = _width > 0 ? _width : _sandResolutionPerAxis;
            int h = _height > 0 ? _height : _sandResolutionPerAxis;

            float uniformCellSize = Mathf.Min(_boardWorldWidth / w, _boardWorldHeight / h);
            _cellSizeX = uniformCellSize;
            _cellSizeY = uniformCellSize;

            float actualGridW = w * uniformCellSize;
            float actualGridH = h * uniformCellSize;

            _bottomLeftWorldOrigin = new Vector3(
                centerWorld.x - actualGridW * 0.5f + uniformCellSize * 0.5f,
                centerWorld.y - actualGridH * 0.5f + uniformCellSize * 0.5f,
                centerWorld.z);

            float xyScale = uniformCellSize * _grainScaleMultiplier;
            _cellVisualScale = new Vector3(xyScale, xyScale, uniformCellSize * 0.65f);
        }

        public void ClearVisualGrid()
        {
            _simulator = null;
        }

        public Vector3 GridToWorldPosition(int x, int y)
        {
            return new Vector3(
                _bottomLeftWorldOrigin.x + x * _cellSizeX,
                _bottomLeftWorldOrigin.y + y * _cellSizeY,
                _bottomLeftWorldOrigin.z);
        }

        public Vector3ConsumeResult ConsumeReservedGrainVisual(int gridIndex, int x, int y)
        {
            Vector3 worldPos = GridToWorldPosition(x, y);
            if (x >= 0 && x < _width)
            {
                worldPos.y += _columnVisualOffsetY[x];
                _columnVisualOffsetY[x] = Mathf.Min(_columnVisualOffsetY[x] + _cellSizeY, _cellSizeY * 4.5f);
            }
            return new Vector3ConsumeResult(worldPos);
        }

        public void ApplyVisualGravityMoves(BoardSimulator simulator, float stepDuration)
        {
            ReadOnlySpan<GrainMoveRecord> moves = simulator.LastStepMoves;
            for (int i = 0; i < moves.Length; i++)
            {
                GrainMoveRecord move = moves[i];
                if (move.ToIndex >= 0 && move.ToIndex < _cellSlideOffsetX.Length)
                {
                    _cellSlideOffsetX[move.ToIndex] = (move.FromX - move.ToX) * _cellSizeX;
                    _cellSlideOffsetY[move.ToIndex] = (move.FromY - move.ToY) * _cellSizeY;
                }
            }
        }

        public void RefreshExposedFrontierVisuals(BoardSimulator simulator)
        {
        }

        private void Update()
        {
            if (_simulator == null || _currentLevel == null || _grainMesh == null) return;

            // Keep tray fit live in case the user drags or scales the tray at runtime!
            CalculateExactTrayCavityLayout();

            float dt = Time.deltaTime;
            IntegrateSmoothSandboxOffsets(dt);
            RenderInstancedSandMatrix();
        }

        private void IntegrateSmoothSandboxOffsets(float dt)
        {
            for (int x = 0; x < _width; x++)
            {
                if (_columnVisualOffsetY[x] > 0.0001f)
                {
                    _columnFallVelocityY[x] = Mathf.Min(_maxFallSpeed, _columnFallVelocityY[x] + _gravityAcceleration * dt);
                    _columnVisualOffsetY[x] -= _columnFallVelocityY[x] * dt;
                    if (_columnVisualOffsetY[x] <= 0f)
                    {
                        _columnVisualOffsetY[x] = 0f;
                        _columnFallVelocityY[x] = 0f;
                    }
                }
                else
                {
                    _columnVisualOffsetY[x] = 0f;
                    _columnFallVelocityY[x] = 0f;
                }
            }

            float slideDelta = _avalancheSlideSpeed * _cellSizeX * dt;
            ReadOnlySpan<int> colHeights = _simulator.ColumnHeights;

            for (int x = 0; x < _width; x++)
            {
                int h = colHeights[x];
                for (int y = 0; y < h; y++)
                {
                    int idx = y * _width + x;
                    if (_cellSlideOffsetX[idx] != 0f)
                    {
                        _cellSlideOffsetX[idx] = Mathf.MoveTowards(_cellSlideOffsetX[idx], 0f, slideDelta);
                    }
                    if (_cellSlideOffsetY[idx] != 0f)
                    {
                        _cellSlideOffsetY[idx] = Mathf.MoveTowards(_cellSlideOffsetY[idx], 0f, slideDelta);
                    }
                }
            }
        }

        private void RenderInstancedSandMatrix()
        {
            int totalBuckets = _shadedInstancedMaterials.Length;
            if (totalBuckets == 0) return;

            Array.Clear(_shadeInstanceCounts, 0, totalBuckets);

            ReadOnlySpan<int> grid = _simulator.Grid;
            ReadOnlySpan<int> colHeights = _simulator.ColumnHeights;
            Quaternion identityRot = Quaternion.identity;

            float minAllowedX = _bottomLeftWorldOrigin.x;
            float maxAllowedX = _bottomLeftWorldOrigin.x + (_width - 1) * _cellSizeX;
            float minAllowedY = _bottomLeftWorldOrigin.y;
            float maxAllowedY = _bottomLeftWorldOrigin.y + (_height - 1) * _cellSizeY;
            float jitterAmp = _organicSandJitter * _cellSizeX;

            for (int x = 0; x < _width; x++)
            {
                int colH = colHeights[x];
                if (colH <= 0) continue;

                float baseX = _bottomLeftWorldOrigin.x + x * _cellSizeX;
                float colOffsetY = _columnVisualOffsetY[x];

                for (int y = 0; y < colH; y++)
                {
                    int idx = y * _width + x;
                    int colorId = grid[idx];
                    if (colorId < 0 || colorId >= _paletteColorCount) continue;

                    // Deterministic hash for organic micro-jitter and 3-tone sand speckle
                    uint hash = (uint)(x * 73856093 ^ y * 19349663);
                    float jx = ((hash & 0xFF) / 127.5f - 1f) * jitterAmp;
                    float jy = (((hash >> 8) & 0xFF) / 127.5f - 1f) * jitterAmp;
                    int shadeOffset = (int)(hash % ShadesPerColor);

                    float posX = Mathf.Clamp(baseX + _cellSlideOffsetX[idx] + jx, minAllowedX, maxAllowedX);
                    float posY = Mathf.Clamp(
                        _bottomLeftWorldOrigin.y + y * _cellSizeY + colOffsetY + _cellSlideOffsetY[idx] + jy,
                        minAllowedY,
                        maxAllowedY);

                    Vector3 worldPos = new Vector3(posX, posY, _bottomLeftWorldOrigin.z);

                    int bucketIdx = colorId * ShadesPerColor + shadeOffset;
                    int countForBucket = _shadeInstanceCounts[bucketIdx];
                    int batchIdx = countForBucket / MaxBatchSize;
                    int indexInBatch = countForBucket % MaxBatchSize;

                    if (batchIdx < _shadeMatrixBatches[bucketIdx].Length)
                    {
                        _shadeMatrixBatches[bucketIdx][batchIdx][indexInBatch] =
                            Matrix4x4.TRS(worldPos, identityRot, _cellVisualScale);
                        _shadeInstanceCounts[bucketIdx] = countForBucket + 1;
                    }
                }
            }

            // Submit GPU Instanced Draw Calls
            for (int b = 0; b < totalBuckets; b++)
            {
                int remaining = _shadeInstanceCounts[b];
                if (remaining <= 0) continue;

                Material mat = _shadedInstancedMaterials[b];
                int batchIdx = 0;
                while (remaining > 0 && batchIdx < _shadeMatrixBatches[b].Length)
                {
                    int drawCount = Mathf.Min(remaining, MaxBatchSize);
                    Graphics.DrawMeshInstanced(
                        _grainMesh,
                        0,
                        mat,
                        _shadeMatrixBatches[b][batchIdx],
                        drawCount,
                        null,
                        ShadowCastingMode.Off,
                        false);

                    remaining -= drawCount;
                    batchIdx++;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            CalculateExactTrayCavityLayout();

            Gizmos.color = new Color(0.2f, 0.95f, 0.5f, 0.85f);
            Vector3 center = new Vector3(
                _bottomLeftWorldOrigin.x + (_sandResolutionPerAxis - 1) * _cellSizeX * 0.5f,
                _bottomLeftWorldOrigin.y + (_sandResolutionPerAxis - 1) * _cellSizeY * 0.5f,
                _bottomLeftWorldOrigin.z);
            Vector3 size = new Vector3(_sandResolutionPerAxis * _cellSizeX, _sandResolutionPerAxis * _cellSizeY, 0.05f);
            Gizmos.DrawWireCube(center, size);
        }

        public readonly struct Vector3ConsumeResult
        {
            public readonly Vector3 WorldPosition;
            public Vector3ConsumeResult(Vector3 worldPos) => WorldPosition = worldPos;
        }
    }
}
