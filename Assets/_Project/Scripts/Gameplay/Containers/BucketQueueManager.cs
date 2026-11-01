using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SandSort.Gameplay.Containers
{
    /// <summary>
    /// Manages the bottom S spawner columns of buckets using fixed scene column spawn points.
    /// Supports both WebGL Mouse and Mobile Touch via New Input System.
    /// </summary>
    public sealed class BucketQueueManager : MonoBehaviour
    {
        [Header("Fixed Scene Column Spawn Roots (Placed in Edit Mode)")]
        [SerializeField] private Transform _queuesRootAnchor;
        [SerializeField] private Transform[] _fixedColumnRoots;
        [SerializeField] private GameObject _bucketPrefab;
        [SerializeField] private Camera _gameplayCamera;

        [Header("Queue Layout & Mystery ('?') Progression Settings")]
        [SerializeField] private float _columnHorizontalSpacing = 1.15f;
        [SerializeField] private float _rowVerticalSpacing = 0.58f;
        [Tooltip("Kovaları kameraya doğru öne (eksi değer) ya da geriye (artı değer) alır. Platform da kovaları takip eder.")]
        [SerializeField] private float _bucketZOffset = -0.20f;
        [Tooltip("Her sütunda ekranda aynı anda kaç satır kova görüneceği (Örn: 3 satır).")]
        [SerializeField, Range(2, 5)] private int _maxVisibleRowsPerColumn = 3;
        [Tooltip("Kaçıncı satırdan itibaren kovaların üstü simsiyah ve '?' (Gizemli Kova) olarak geleceği (1 = İlk satır açık, arkadaki tüm satırlar '?').")]
        [SerializeField, Range(1, 5)] private int _mysteryBucketStartRow = 1;

        [Header("Bucket Queue Platform (Tray-style Shelf)")]
        [SerializeField] private bool _buildQueuePlatform = true;
        [Tooltip("Platformun kovaların arkasındaki derinliği (daha büyük = daha geride).")]
        [SerializeField] private float _platformZOffset = 1.0f;
        [Tooltip("Platformun geriye eğim açısı (üstteki kum tepsisiyle aynı: 28).")]
        [SerializeField, Range(0f, 50f)] private float _platformTiltDegrees = 28f;
        [Tooltip("Yuvaları kovaların görsel merkezine otomatik hizalar (kova modelinin pivotu merkezde değilse bunu açık bırak).")]
        [SerializeField] private bool _autoCenterOnBuckets = true;
        [Tooltip("Otomatik hizalamaya ek elle kaydırma (X sağ, Y yukarı). Yuvalar kovaların altında kalıyorsa Y'yi artır.")]
        [SerializeField] private Vector2 _platformManualOffset = Vector2.zero;
        [Tooltip("Kovaların arkası ile platform arasında bırakılan güvenlik boşluğu. Kova platforma giriyorsa artır.")]
        [SerializeField] private float _platformSafetyGap = 0.15f;
        [SerializeField] private float _platformMargin = 0.16f;
        [SerializeField] private float _platformWallThickness = 0.12f;
        [SerializeField] private float _slotWidth = 0.80f;
        [Tooltip("Alt duvardaki kovaların çıktığı koyu girişin genişliği (yuva genişliğine oranı).")]
        [SerializeField, Range(0.3f, 1f)] private float _entranceWidthRatio = 0.8f;

        private ObjectPool<BucketView> _bucketPool;

        private GameObject _platformRoot;
        private GameObject[] _frontHighlights = Array.Empty<GameObject>();
        private Vector3[] _depotSpawnWorldPos = Array.Empty<Vector3>();
        private Material _platformPlateMat;
        private Material _platformWallMat;
        private Material _platformSlotMat;
        private Material _platformHighlightMat;
        private Material _platformMouthMat;

        private BucketView[][] _columnViews = Array.Empty<BucketView[]>();
        private int[] _columnHeadPointers = Array.Empty<int>();
        private int[] _columnTotalCounts = Array.Empty<int>();
        private int _columnCount;
        private int _activeMysteryStartRow = 1;

        private BoardSimulator _simulator;
        private DockManager _dockManager;
        private bool _inputEnabled;

        public Transform QueuesRootAnchor => _queuesRootAnchor != null ? _queuesRootAnchor : transform;

        private void Awake()
        {
            ServiceLocator.Register(this);
            EnsurePoolInitialized();
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
            if (_platformRoot != null) Destroy(_platformRoot);
            if (_platformPlateMat != null) Destroy(_platformPlateMat);
            if (_platformWallMat != null) Destroy(_platformWallMat);
            if (_platformSlotMat != null) Destroy(_platformSlotMat);
            if (_platformHighlightMat != null) Destroy(_platformHighlightMat);
            if (_platformMouthMat != null) Destroy(_platformMouthMat);
        }

        private void EnsurePoolInitialized()
        {
            if (_bucketPool != null) return;

            _bucketPool = new ObjectPool<BucketView>(
                initialCapacity: 32,
                factory: CreateBucketViewInstance,
                onGet: view => view.gameObject.SetActive(true),
                onRelease: view =>
                {
                    view.KillActiveTweens();
                    view.gameObject.SetActive(false);
                },
                onDestroy: view =>
                {
                    if (view != null) Destroy(view.gameObject);
                },
                prewarm: true);
        }

        private BucketView CreateBucketViewInstance()
        {
            GameObject go;
            if (_bucketPrefab != null)
            {
                go = Instantiate(_bucketPrefab, QueuesRootAnchor);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(QueuesRootAnchor, false);
                go.transform.localScale = new Vector3(0.78f, 0.40f, 0.40f);
            }

            go.name = "PooledBucket";
            if (!go.TryGetComponent(out BucketView view))
            {
                view = go.AddComponent<BucketView>();
            }
            view.EnsureVisualHierarchy();
            return view;
        }

        public void ReturnBucketToPool(BucketView view)
        {
            if (view == null) return;
            view.CachedTransform.SetParent(QueuesRootAnchor, false);
            _bucketPool.Release(view);
        }

        public void BuildQueues(LevelDataSO levelData, BoardSimulator simulator, DockManager dockManager)
        {
            EnsurePoolInitialized();
            ClearAllQueueVisuals();

            _simulator = simulator;
            _dockManager = dockManager;
            _columnCount = simulator.ColumnCount;
            _maxVisibleRowsPerColumn = 3;
            _activeMysteryStartRow = (levelData != null && levelData.MysteryBucketStartRow > 0) ? levelData.MysteryBucketStartRow : _mysteryBucketStartRow;

            if (_columnViews.Length < _columnCount)
            {
                _columnViews = new BucketView[_columnCount][];
                _columnHeadPointers = new int[_columnCount];
                _columnTotalCounts = new int[_columnCount];
            }

            for (int c = 0; c < _columnCount; c++)
            {
                int len = simulator.GetColumnBucketCount(c);

                if (_columnViews[c] == null || _columnViews[c].Length < len)
                {
                    _columnViews[c] = new BucketView[Math.Max(len, 16)];
                }

                _columnHeadPointers[c] = 0;
                _columnTotalCounts[c] = len;

                for (int b = 0; b < len; b++)
                {
                    BucketData bucketData = simulator.GetColumnBucketAt(c, b);
                    BucketView view = _bucketPool.Get();
                    Transform vt = view.CachedTransform;
                    vt.SetParent(QueuesRootAnchor, true);
                    vt.position = GetQueueSlotWorldPosition(c, b);
                    vt.rotation = Quaternion.identity;

                    bool isFront = (b == 0);
                    bool isMystery = (b >= _activeMysteryStartRow);
                    view.Setup(bucketData, levelData.GetColor(bucketData.ColorId), c, isFront, isMystery);
                    view.gameObject.SetActive(b < _maxVisibleRowsPerColumn);
                    _columnViews[c][b] = view;
                }
            }

            RebuildQueuePlatform();
            _inputEnabled = true;
        }

        public void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;
        }

        private void Update()
        {
            UpdateFrontHighlights();
            if (!_inputEnabled || _simulator == null) return;

            if (TryGetPointerDownScreenPosition(out Vector2 screenPos))
            {
                HandlePointerClick(screenPos);
            }
        }

        private static bool TryGetPointerDownScreenPosition(out Vector2 screenPos)
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }

            screenPos = default;
            return false;
        }

        private void HandlePointerClick(Vector2 screenPos)
        {
            if (_gameplayCamera == null)
            {
                _gameplayCamera = Camera.main;
                if (_gameplayCamera == null) return;
            }

            Ray ray = _gameplayCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                if (hit.collider != null && hit.collider.TryGetComponent(out BucketView clickedBucket))
                {
                    int colIdx = clickedBucket.ColumnIndex;
                    if (colIdx >= 0)
                    {
                        TryDeployColumnFrontBucket(colIdx);
                    }
                }
            }
        }

        public bool TryDeployColumnFrontBucket(int columnIndex)
        {
            if (_simulator == null || _dockManager == null) return false;
            if (columnIndex < 0 || columnIndex >= _columnCount) return false;

            int head = _columnHeadPointers[columnIndex];
            if (head >= _columnTotalCounts[columnIndex]) return false;

            if (!_simulator.TryDeployBucketFromColumn(columnIndex, out int assignedDockSlot, out BucketData deployedBucket))
                return false;

            BucketView deployedView = _columnViews[columnIndex][head];
            _columnViews[columnIndex][head] = null;
            _columnHeadPointers[columnIndex] = head + 1;

            _dockManager.PlaceBucketViewInSlot(assignedDockSlot, deployedView);

            int newHead = _columnHeadPointers[columnIndex];
            int total = _columnTotalCounts[columnIndex];
            for (int b = newHead; b < total; b++)
            {
                BucketView remainingView = _columnViews[columnIndex][b];
                if (remainingView == null) continue;

                int visualOrderFromFront = b - newHead;
                bool isVisible = visualOrderFromFront < _maxVisibleRowsPerColumn;
                bool isMystery = visualOrderFromFront >= _activeMysteryStartRow;
                bool wasVisible = remainingView.gameObject.activeSelf;
                remainingView.gameObject.SetActive(isVisible);
                remainingView.SetQueueState(columnIndex, visualOrderFromFront == 0, isMystery);

                Vector3 targetPos = GetQueueSlotWorldPosition(columnIndex, visualOrderFromFront);
                if (isVisible && !wasVisible && columnIndex < _depotSpawnWorldPos.Length && _depotSpawnWorldPos[columnIndex] != Vector3.zero)
                {
                    // Newly revealed bucket: rolls out of the depot gate into the last row.
                    remainingView.AnimateEmergeFromDepot(_depotSpawnWorldPos[columnIndex], targetPos);
                }
                else
                {
                    remainingView.AnimateQueueShift(targetPos);
                }
            }

            EventBus<BucketDeployedToDockEvent>.Publish(
                new BucketDeployedToDockEvent(columnIndex, assignedDockSlot, deployedBucket));

            return true;
        }

        private void EnsurePlatformMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Unlit/Color");
            if (unlit == null) unlit = lit;

            if (_platformPlateMat == null) _platformPlateMat = MakePlatformMaterial(lit, "Mat_QueuePlate", new Color32(150, 153, 186, 255), 0.14f);
            if (_platformWallMat == null) _platformWallMat = MakePlatformMaterial(lit, "Mat_QueueWall", new Color32(226, 228, 236, 255), 0.45f);
            if (_platformSlotMat == null) _platformSlotMat = MakePlatformMaterial(lit, "Mat_QueueSlot", new Color32(104, 108, 146, 255), 0.15f);
            if (_platformMouthMat == null) _platformMouthMat = MakePlatformMaterial(unlit, "Mat_QueueDepotMouth", new Color32(10, 8, 20, 255), 0f);
            if (_platformHighlightMat == null) _platformHighlightMat = MakePlatformMaterial(unlit, "Mat_QueueFrontHighlight", new Color32(255, 200, 50, 255), 0f);
        }

        private static Material MakePlatformMaterial(Shader shader, string matName, Color32 color, float smoothness)
        {
            Material mat = new Material(shader) { name = matName };
            Color c = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            mat.color = c;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        private static GameObject CreatePlatformBox(string boxName, Transform parent, Vector3 localPos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// Builds a tilted tray-style shelf behind the bucket queue. Every slot is placed by casting a camera ray through
        /// the corresponding bucket and intersecting it with the tilted plate, so slots line up with the buckets exactly
        /// on screen (regardless of perspective, tilt or depth).
        /// </summary>
        private void RebuildQueuePlatform()
        {
            if (_platformRoot != null) Destroy(_platformRoot);
            _frontHighlights = Array.Empty<GameObject>();
            _depotSpawnWorldPos = Array.Empty<Vector3>();
            if (!_buildQueuePlatform || _columnCount <= 0) return;

            EnsurePlatformMaterials();

            int rows = Mathf.Max(1, _maxVisibleRowsPerColumn);
            Vector2 vis = (_autoCenterOnBuckets ? MeasureBucketVisualOffset() : Vector2.zero) + _platformManualOffset;
            Vector3 visOff = new Vector3(vis.x, vis.y, 0f);

            Vector3 firstB = GetQueueSlotWorldPosition(0, 0) + visOff;
            Vector3 lastColB = GetQueueSlotWorldPosition(_columnCount - 1, 0) + visOff;
            Vector3 lastRowB = GetQueueSlotWorldPosition(0, rows - 1) + visOff;

            // Guarantee the plate never cuts into the buckets: its nearest (bottom) edge comes forward when tilted,
            // so push the plate back by (bucket depth behind pivot) + (forward shift of the bottom edge) + safety gap.
            float tiltRad = _platformTiltDegrees * Mathf.Deg2Rad;
            float halfPlateV = ((firstB.y - lastRowB.y) * 0.5f + _rowVerticalSpacing * 0.5f + _platformMargin) / Mathf.Max(0.3f, Mathf.Cos(tiltRad));
            float requiredZ = MeasureBucketBackDepth() + halfPlateV * Mathf.Sin(tiltRad) + _platformSafetyGap;
            float zOffset = Mathf.Max(_platformZOffset, requiredZ);

            Vector3 center = new Vector3((firstB.x + lastColB.x) * 0.5f, (firstB.y + lastRowB.y) * 0.5f, firstB.z + zOffset);
            Quaternion tilt = Quaternion.Euler(_platformTiltDegrees, 0f, 0f);
            Vector3 right = tilt * Vector3.right;
            Vector3 up = tilt * Vector3.up;
            Vector3 normal = tilt * Vector3.back;

            Camera cam = _gameplayCamera != null ? _gameplayCamera : Camera.main;
            bool persp = cam != null && !cam.orthographic;

            void Project(Vector3 b, out float u, out float v, out float f)
            {
                Vector3 origin;
                Vector3 dir;
                if (persp) { origin = cam.transform.position; dir = (b - origin).normalized; }
                else { origin = b; dir = cam != null ? cam.transform.forward : Vector3.forward; }

                float denom = Vector3.Dot(dir, normal);
                float t = Mathf.Abs(denom) > 1e-4f ? Vector3.Dot(center - origin, normal) / denom : 0f;
                Vector3 p = origin + dir * t;
                Vector3 d = p - center;
                u = Vector3.Dot(d, right);
                v = Vector3.Dot(d, up);
                f = persp ? t / Mathf.Max(0.001f, (b - origin).magnitude) : 1f;
            }

            // Perspective size factor at the plate center (plate is farther away than the buckets)
            Project(new Vector3(center.x, center.y, firstB.z), out _, out _, out float f0);

            float[,] su = new float[_columnCount, rows];
            float[,] sv = new float[_columnCount, rows];
            for (int c = 0; c < _columnCount; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    Vector3 b = GetQueueSlotWorldPosition(c, r) + visOff;
                    Project(b, out su[c, r], out sv[c, r], out _);
                }
            }

            float slotW = _slotWidth * f0;
            float pitchV = rows > 1 ? Mathf.Abs(sv[0, 0] - sv[0, 1]) : _rowVerticalSpacing * f0;
            float slotH = pitchV * 0.92f;
            float margin = _platformMargin * f0;
            float wall = _platformWallThickness * f0;
            float wallB = Mathf.Max(wall * 1.8f, slotH * 0.62f); // thicker bottom wall hosts the depot entrances

            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            for (int c = 0; c < _columnCount; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    uMin = Mathf.Min(uMin, su[c, r]); uMax = Mathf.Max(uMax, su[c, r]);
                    vMin = Mathf.Min(vMin, sv[c, r]); vMax = Mathf.Max(vMax, sv[c, r]);
                }
            }
            uMin -= slotW * 0.5f + margin; uMax += slotW * 0.5f + margin;
            vMin -= slotH * 0.5f + margin; vMax += slotH * 0.5f + margin;

            float pu = (uMin + uMax) * 0.5f;
            float pv = (vMin + vMax) * 0.5f;
            float pw = uMax - uMin;
            float ph = vMax - vMin;

            _platformRoot = new GameObject("BucketQueuePlatform");
            Transform root = _platformRoot.transform;
            root.position = center;
            root.rotation = tilt;

            // Plate
            CreatePlatformBox("Plate", root, new Vector3(pu, pv, 0f), new Vector3(pw, ph, 0.10f), _platformPlateMat);

            // Square walls (thin top/sides, thicker bottom)
            float wz = -0.05f;
            float topEdge = pv + ph * 0.5f + wall;
            float botEdge = pv - ph * 0.5f - wallB;
            float sideH = topEdge - botEdge;
            float sideCv = (topEdge + botEdge) * 0.5f;
            CreatePlatformBox("Wall_Top", root, new Vector3(pu, pv + ph * 0.5f + wall * 0.5f, wz), new Vector3(pw + wall * 2f, wall, 0.14f), _platformWallMat);
            float bottomWallV = pv - ph * 0.5f - wallB * 0.5f;
            CreatePlatformBox("Wall_Bottom", root, new Vector3(pu, bottomWallV, wz), new Vector3(pw + wall * 2f, wallB, 0.14f), _platformWallMat);
            CreatePlatformBox("Wall_Left", root, new Vector3(pu - pw * 0.5f - wall * 0.5f, sideCv, wz), new Vector3(wall, sideH, 0.14f), _platformWallMat);
            CreatePlatformBox("Wall_Right", root, new Vector3(pu + pw * 0.5f + wall * 0.5f, sideCv, wz), new Vector3(wall, sideH, 0.14f), _platformWallMat);

            // Slots, front highlights, and dark bucket entrances embedded in the bottom wall
            _frontHighlights = new GameObject[_columnCount];
            _depotSpawnWorldPos = new Vector3[_columnCount];
            float frameT = 0.04f * f0;
            for (int c = 0; c < _columnCount; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    CreatePlatformBox($"Slot_{c}_{r}", root, new Vector3(su[c, r], sv[c, r], -0.062f), new Vector3(slotW, slotH, 0.02f), _platformSlotMat);
                }

                float fu = su[c, 0];
                float fv = sv[c, 0];
                GameObject hl = new GameObject($"FrontHighlight_{c}");
                hl.transform.SetParent(root, false);
                float hz = -0.08f;
                float fw = slotW + frameT;
                float fh = slotH + frameT;
                CreatePlatformBox("Top", hl.transform, new Vector3(fu, fv + fh * 0.5f, hz), new Vector3(fw + frameT, frameT, 0.02f), _platformHighlightMat);
                CreatePlatformBox("Bottom", hl.transform, new Vector3(fu, fv - fh * 0.5f, hz), new Vector3(fw + frameT, frameT, 0.02f), _platformHighlightMat);
                CreatePlatformBox("Left", hl.transform, new Vector3(fu - fw * 0.5f, fv, hz), new Vector3(frameT, fh, 0.02f), _platformHighlightMat);
                CreatePlatformBox("Right", hl.transform, new Vector3(fu + fw * 0.5f, fv, hz), new Vector3(frameT, fh, 0.02f), _platformHighlightMat);
                _frontHighlights[c] = hl;

                // Depot gate: a light-grey housing that stands proud of the bottom wall (like the conveyor corner blocks)
                // with a dark tunnel mouth, a hood lip above and a dark floor sill. Buckets emerge from here.
                float eu = su[c, rows - 1];
                float housingW = slotW * 1.06f;
                float housingZ = -0.12f;
                CreatePlatformBox($"DepotHousing_{c}", root, new Vector3(eu, bottomWallV, housingZ), new Vector3(housingW, wallB, 0.26f), _platformWallMat);

                float mouthW = housingW * _entranceWidthRatio;
                float mouthH = wallB * 0.58f;
                float mouthV = bottomWallV - wallB * 0.08f;
                float frontZ = housingZ - 0.13f;
                CreatePlatformBox($"DepotMouth_{c}", root, new Vector3(eu, mouthV, frontZ - 0.005f), new Vector3(mouthW, mouthH, 0.02f), _platformMouthMat);
                // soft inner shadow strip along the top of the opening for depth
                CreatePlatformBox($"DepotMouthShade_{c}", root, new Vector3(eu, mouthV + mouthH * 0.5f - mouthH * 0.12f, frontZ - 0.012f), new Vector3(mouthW, mouthH * 0.24f, 0.02f), _platformSlotMat);
                // dark sill so the opening reads as a tunnel floor
                CreatePlatformBox($"DepotSill_{c}", root, new Vector3(eu, mouthV - mouthH * 0.5f - wallB * 0.03f, frontZ - 0.008f), new Vector3(mouthW * 1.04f, wallB * 0.06f, 0.025f), _platformPlateMat);

                // World point (on the bucket plane, along the camera ray through the mouth) where the bucket emerges from
                Vector3 mouthWorld = root.TransformPoint(new Vector3(eu, mouthV, frontZ));
                float bucketPlaneZ = firstB.z;
                if (persp)
                {
                    Vector3 camPos = cam.transform.position;
                    Vector3 rayDir = (mouthWorld - camPos).normalized;
                    float tPlane = Mathf.Abs(rayDir.z) > 1e-4f ? (bucketPlaneZ - camPos.z) / rayDir.z : 0f;
                    _depotSpawnWorldPos[c] = camPos + rayDir * tPlane;
                }
                else
                {
                    _depotSpawnWorldPos[c] = new Vector3(mouthWorld.x, mouthWorld.y, bucketPlaneZ);
                }            }
        }
        /// <summary>
        /// Offset between the front bucket's pivot and the visual center of its mesh (XY, world space),
        /// so the platform slots are centered on what the player actually sees.
        /// </summary>
        private Vector2 MeasureBucketVisualOffset()
        {
            if (_columnViews == null || _columnViews.Length == 0 || _columnViews[0] == null || _columnViews[0].Length == 0)
                return Vector2.zero;

            BucketView v = _columnViews[0][0];
            if (v == null) return Vector2.zero;

            Bounds b = default;
            bool has = false;
            MeshRenderer[] rs = v.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i].GetComponent<TextMeshPro>() != null) continue;
                if (!has) { b = rs[i].bounds; has = true; }
                else b.Encapsulate(rs[i].bounds);
            }
            if (!has) return Vector2.zero;

            Vector3 d = b.center - v.CachedTransform.position;
            return new Vector2(d.x, d.y);
        }

        /// <summary>How far the bucket mesh extends BEHIND its pivot (world +Z), including its tilted rim/bottom.</summary>
        private float MeasureBucketBackDepth()
        {
            if (_columnViews == null || _columnViews.Length == 0 || _columnViews[0] == null || _columnViews[0].Length == 0)
                return 0.3f;

            BucketView v = _columnViews[0][0];
            if (v == null) return 0.3f;

            float maxZ = float.MinValue;
            MeshRenderer[] rs = v.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i].GetComponent<TextMeshPro>() != null) continue;
                maxZ = Mathf.Max(maxZ, rs[i].bounds.max.z);
            }
            if (maxZ == float.MinValue) return 0.3f;
            return Mathf.Max(0f, maxZ - v.CachedTransform.position.z);
        }

        private void UpdateFrontHighlights()
        {
            if (_frontHighlights == null || _frontHighlights.Length == 0) return;

            if (_platformHighlightMat != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
                Color c = Color.Lerp(new Color32(255, 190, 40, 255), new Color32(255, 240, 140, 255), pulse);
                if (_platformHighlightMat.HasProperty("_BaseColor")) _platformHighlightMat.SetColor("_BaseColor", c);
                if (_platformHighlightMat.HasProperty("_Color")) _platformHighlightMat.SetColor("_Color", c);
            }

            for (int c = 0; c < _frontHighlights.Length; c++)
            {
                if (_frontHighlights[c] == null) continue;
                bool hasBucket = c < _columnHeadPointers.Length && c < _columnTotalCounts.Length
                                 && _columnHeadPointers[c] < _columnTotalCounts[c];
                if (_frontHighlights[c].activeSelf != hasBucket) _frontHighlights[c].SetActive(hasBucket);
            }
        }

        public Vector3 GetQueueSlotWorldPosition(int columnIndex, int depthFromTop)
        {
            float baseY = QueuesRootAnchor.position.y;
            float baseZ = QueuesRootAnchor.position.z;
            if (_fixedColumnRoots != null && _fixedColumnRoots.Length > 0 && _fixedColumnRoots[0] != null)
            {
                baseY = _fixedColumnRoots[0].position.y;
                baseZ = _fixedColumnRoots[0].position.z;
            }

            float spacingX = _columnCount <= 2 ? 1.22f : (_columnCount == 3 ? 1.05f : _columnHorizontalSpacing);
            float totalWidth = (_columnCount - 1) * spacingX;
            float startX = QueuesRootAnchor.position.x - totalWidth * 0.5f;

            return new Vector3(
                startX + columnIndex * spacingX,
                baseY - depthFromTop * _rowVerticalSpacing,
                baseZ + _bucketZOffset);
        }

        public void ClearAllQueueVisuals()
        {
            if (_columnViews == null) return;
            for (int c = 0; c < _columnViews.Length; c++)
            {
                if (_columnViews[c] == null) continue;
                for (int b = 0; b < _columnViews[c].Length; b++)
                {
                    if (_columnViews[c][b] != null)
                    {
                        _bucketPool.Release(_columnViews[c][b]);
                        _columnViews[c][b] = null;
                    }
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying || _fixedColumnRoots == null) return;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.75f);
            Vector3 bucketSize = new Vector3(0.78f, 0.39f, 0.39f);

            for (int c = 0; c < _fixedColumnRoots.Length; c++)
            {
                if (_fixedColumnRoots[c] == null) continue;
                for (int row = 0; row < 4; row++)
                {
                    Vector3 pos = _fixedColumnRoots[c].position + new Vector3(0f, -row * _rowVerticalSpacing, 0f);
                    Gizmos.DrawWireCube(pos, bucketSize);
                }
            }
        }
    }
}
