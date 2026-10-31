using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Simulation;
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

        [Header("Queue Layout Settings")]
        [SerializeField] private float _columnHorizontalSpacing = 0.98f;
        [SerializeField] private float _rowVerticalSpacing = 0.52f;
        [SerializeField] private int _maxVisibleRowsPerColumn = 5;

        private ObjectPool<BucketView> _bucketPool;

        private BucketView[][] _columnViews = Array.Empty<BucketView[]>();
        private int[] _columnHeadPointers = Array.Empty<int>();
        private int[] _columnTotalCounts = Array.Empty<int>();
        private int _columnCount;

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
            _columnCount = levelData.Columns != null ? levelData.Columns.Length : 0;

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
                    view.Setup(bucketData, levelData.GetColor(bucketData.ColorId), c, isFront);
                    view.gameObject.SetActive(b < _maxVisibleRowsPerColumn);
                    _columnViews[c][b] = view;
                }
            }

            _inputEnabled = true;
        }

        public void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;
        }

        private void Update()
        {
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
                remainingView.gameObject.SetActive(isVisible);
                remainingView.SetQueueState(columnIndex, visualOrderFromFront == 0);

                Vector3 targetPos = GetQueueSlotWorldPosition(columnIndex, visualOrderFromFront);
                remainingView.AnimateQueueShift(targetPos);
            }

            EventBus<BucketDeployedToDockEvent>.Publish(
                new BucketDeployedToDockEvent(columnIndex, assignedDockSlot, deployedBucket));

            return true;
        }

        public Vector3 GetQueueSlotWorldPosition(int columnIndex, int depthFromTop)
        {
            if (_fixedColumnRoots != null &&
                columnIndex >= 0 &&
                columnIndex < _fixedColumnRoots.Length &&
                _fixedColumnRoots[columnIndex] != null)
            {
                return _fixedColumnRoots[columnIndex].position + new Vector3(0f, -depthFromTop * _rowVerticalSpacing, 0f);
            }

            float totalWidth = (_columnCount - 1) * _columnHorizontalSpacing;
            float startX = -totalWidth * 0.5f;

            Vector3 localOffset = new Vector3(
                startX + columnIndex * _columnHorizontalSpacing,
                -depthFromTop * _rowVerticalSpacing,
                0f);

            return QueuesRootAnchor.TransformPoint(localOffset);
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
