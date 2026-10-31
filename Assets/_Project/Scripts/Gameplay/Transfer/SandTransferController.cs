using System;
using SandSort.Core;
using SandSort.Gameplay.Containers;
using SandSort.Gameplay.Grid;
using SandSort.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandSort.Gameplay.Transfer
{
    /// <summary>
    /// High-performance, Zero-GC, Zero-GameObject GPU-Instanced Bezier Transfer Layer.
    /// Renders hundreds of simultaneously flying sand grains along Quadratic Bezier arcs
    /// using <see cref="Graphics.DrawMeshInstanced"/> with zero GameObject overhead.
    /// </summary>
    public sealed class SandTransferController : MonoBehaviour
    {
        private const int MaxBatchSize = 1023;

        private struct PendingEmissionItem
        {
            public Vector3 StartWorldPos;
            public int ColorId;
            public int DockSlotIndex;
            public float ScheduledLaunchTime;
        }

        private struct ActiveGrainFlight
        {
            public Vector3 P0;
            public Vector3 P1;
            public Vector3 P2;
            public int ColorId;
            public int DockSlotIndex;
            public float Elapsed;
            public float Duration;
        }

        [Header("Scene References")]
        [SerializeField] private Transform _transferRootAnchor;
        [SerializeField] private GameObject _sandGrainPrefab;

        [Header("Bezier & Stagger Timing")]
        [SerializeField] private float _staggerIntervalPerSlot = GameConstants.DefaultGrainEmitInterval;
        [SerializeField] private float _flightDuration = GameConstants.DefaultGrainFlightDuration;
        [SerializeField] private float _arcHeightOffset = 0.55f;

        private PendingEmissionItem[] _pendingEmissions = new PendingEmissionItem[4096];
        private int _pendingCount;

        private ActiveGrainFlight[] _activeFlights = new ActiveGrainFlight[2048];
        private int _activeFlightCount;

        private float[] _nextAllowedEmitTimePerSlot = new float[16];

        // Pre-allocated GPU instancing matrices per colorId
        private Matrix4x4[][] _flightMatricesPerColor = Array.Empty<Matrix4x4[]>();
        private int[] _flightCountsPerColor = Array.Empty<int>();

        private BoardSimulator _simulator;
        private DockManager _dockManager;
        private GridManager _gridManager;
        private Vector3 _flyingGrainScale = new Vector3(0.08f, 0.08f, 0.08f);

        public int TotalPendingAndActiveVisuals => _pendingCount + _activeFlightCount;
        public Transform TransferRootAnchor => _transferRootAnchor != null ? _transferRootAnchor : transform;

        private void Awake()
        {
            ServiceLocator.Register(this);
            EnsureBuffersAllocated(8);
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
        }

        private void EnsureBuffersAllocated(int colorCount)
        {
            if (_flightMatricesPerColor.Length >= colorCount) return;

            _flightMatricesPerColor = new Matrix4x4[colorCount][];
            _flightCountsPerColor = new int[colorCount];
            for (int c = 0; c < colorCount; c++)
            {
                _flightMatricesPerColor[c] = new Matrix4x4[MaxBatchSize];
            }
        }

        public void BindSession(BoardSimulator simulator, DockManager dockManager, Vector3 grainScale)
        {
            ClearAllTransfers();
            _simulator = simulator;
            _dockManager = dockManager;
            if (_gridManager == null) ServiceLocator.TryGet(out _gridManager);

            if (grainScale.x > 0.005f)
            {
                _flyingGrainScale = grainScale * 1.15f;
            }
        }

        public void ConfigureTiming(float staggerIntervalPerSlot, float flightDuration)
        {
            _staggerIntervalPerSlot = Mathf.Clamp(staggerIntervalPerSlot, 0.0005f, 0.1f);
            _flightDuration = Mathf.Clamp(flightDuration, 0.08f, 1.5f);
        }

        public void EnqueueGrainTransfer(Vector3 startWorldPos, int colorId, Color32 color, int dockSlotIndex)
        {
            if (_pendingCount >= _pendingEmissions.Length)
            {
                Array.Resize(ref _pendingEmissions, _pendingEmissions.Length * 2);
            }

            int safeSlot = Mathf.Clamp(dockSlotIndex, 0, _nextAllowedEmitTimePerSlot.Length - 1);
            float now = Time.time;

            // Cap any stagger window to at most 0.035s so grains NEVER sit in a multi-second invisible queue
            // after leaving the tray! Every extracted grain lifts off immediately as part of the live stream.
            float nextSlotTime = Mathf.Max(now, _nextAllowedEmitTimePerSlot[safeSlot]);
            if (nextSlotTime - now > 0.035f)
            {
                nextSlotTime = now + UnityEngine.Random.Range(0f, 0.035f);
            }
            _nextAllowedEmitTimePerSlot[safeSlot] = nextSlotTime + Mathf.Min(_staggerIntervalPerSlot, 0.006f);

            PendingEmissionItem item = new PendingEmissionItem
            {
                StartWorldPos = startWorldPos,
                ColorId = colorId,
                DockSlotIndex = dockSlotIndex,
                ScheduledLaunchTime = nextSlotTime
            };

            if (nextSlotTime <= now + 0.001f)
            {
                LaunchBezierFlight(in item);
            }
            else
            {
                _pendingEmissions[_pendingCount++] = item;
            }
        }

        private void Update()
        {
            if (_simulator == null || _dockManager == null) return;

            float now = Time.time;
            float dt = Time.deltaTime;

            // 1. Launch due emissions
            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                if (now >= _pendingEmissions[i].ScheduledLaunchTime)
                {
                    LaunchBezierFlight(in _pendingEmissions[i]);
                    _pendingCount--;
                    _pendingEmissions[i] = _pendingEmissions[_pendingCount];
                }
            }

            if (_activeFlightCount == 0 && _pendingCount == 0) return;

            if (_gridManager == null) ServiceLocator.TryGet(out _gridManager);
            Mesh mesh = _gridManager != null ? _gridManager.GrainMesh : null;

            Array.Clear(_flightCountsPerColor, 0, _flightCountsPerColor.Length);
            Quaternion identityRot = Quaternion.identity;

            // Render any micro-staggered pending grains at their start world position so ZERO grains are ever invisible!
            for (int i = 0; i < _pendingCount; i++)
            {
                int cId = _pendingEmissions[i].ColorId;
                if (cId >= 0 && cId < _flightCountsPerColor.Length)
                {
                    int idx = _flightCountsPerColor[cId];
                    if (idx < MaxBatchSize)
                    {
                        _flightMatricesPerColor[cId][idx] = Matrix4x4.TRS(
                            _pendingEmissions[i].StartWorldPos, identityRot, _flyingGrainScale);
                        _flightCountsPerColor[cId] = idx + 1;
                    }
                }
            }

            // 2. Advance active Bezier flights and collect matrices for GPU instancing
            for (int i = _activeFlightCount - 1; i >= 0; i--)
            {
                ref ActiveGrainFlight flight = ref _activeFlights[i];
                flight.Elapsed += dt;
                float t = flight.Elapsed / flight.Duration;

                if (t >= 1f)
                {
                    int slotIdx = flight.DockSlotIndex;
                    _activeFlightCount--;
                    _activeFlights[i] = _activeFlights[_activeFlightCount];
                    _dockManager.NotifyGrainArrivedAtSlot(slotIdx, _simulator);
                }
                else
                {
                    float easedT = t * t * (3f - 2f * t);
                    Vector3 pos = EvaluateQuadraticBezier(in flight.P0, in flight.P1, in flight.P2, easedT);

                    int cId = flight.ColorId;
                    if (cId >= 0 && cId < _flightCountsPerColor.Length)
                    {
                        int idx = _flightCountsPerColor[cId];
                        if (idx < MaxBatchSize)
                        {
                            _flightMatricesPerColor[cId][idx] = Matrix4x4.TRS(pos, identityRot, _flyingGrainScale);
                            _flightCountsPerColor[cId] = idx + 1;
                        }
                    }
                }
            }

            // 3. Draw all flying grains via GPU Instancing
            if (mesh != null && _gridManager != null)
            {
                for (int c = 0; c < _flightCountsPerColor.Length; c++)
                {
                    int count = _flightCountsPerColor[c];
                    if (count <= 0) continue;

                    Material mat = _gridManager.GetInstancedMaterialForColor(c);
                    if (mat != null)
                    {
                        Graphics.DrawMeshInstanced(
                            mesh,
                            0,
                            mat,
                            _flightMatricesPerColor[c],
                            count,
                            null,
                            ShadowCastingMode.Off,
                            false);
                    }
                }
            }
        }

        private void LaunchBezierFlight(in PendingEmissionItem item)
        {
            if (_activeFlightCount >= _activeFlights.Length)
            {
                Array.Resize(ref _activeFlights, _activeFlights.Length * 2);
            }

            Vector3 p0 = item.StartWorldPos;
            Vector3 p2 = _dockManager.GetSlotIntakeWorldPosition(item.DockSlotIndex);

            Vector3 mid = (p0 + p2) * 0.5f;
            float horizontalSpread = (p0.x - p2.x) * 0.25f;
            Vector3 p1 = new Vector3(
                mid.x + horizontalSpread,
                Mathf.Lerp(p0.y, p2.y, 0.35f) + _arcHeightOffset,
                mid.z - 0.35f);

            _activeFlights[_activeFlightCount++] = new ActiveGrainFlight
            {
                P0 = p0,
                P1 = p1,
                P2 = p2,
                ColorId = item.ColorId,
                DockSlotIndex = item.DockSlotIndex,
                Elapsed = 0f,
                Duration = Mathf.Max(0.08f, _flightDuration)
            };
        }

        public static Vector3 EvaluateQuadraticBezier(in Vector3 p0, in Vector3 p1, in Vector3 p2, float t)
        {
            float oneMinusT = 1f - t;
            float a = oneMinusT * oneMinusT;
            float b = 2f * oneMinusT * t;
            float c = t * t;

            return new Vector3(
                a * p0.x + b * p1.x + c * p2.x,
                a * p0.y + b * p1.y + c * p2.y,
                a * p0.z + b * p1.z + c * p2.z);
        }

        public void ClearAllTransfers()
        {
            _pendingCount = 0;
            _activeFlightCount = 0;
            Array.Clear(_nextAllowedEmitTimePerSlot, 0, _nextAllowedEmitTimePerSlot.Length);
        }
    }
}
