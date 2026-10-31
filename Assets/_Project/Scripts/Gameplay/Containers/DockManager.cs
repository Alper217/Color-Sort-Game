using System;
using SandSort.Core;
using SandSort.Data;
using SandSort.Simulation;
using UnityEngine;

namespace SandSort.Gameplay.Containers
{
    /// <summary>
    /// Manages the Active Collection Dock (K slots) using fixed scene Transforms placed in Edit Mode.
    /// Does not spawn or destroy slot meshes at runtime — uses the permanent scene layout.
    /// </summary>
    public sealed class DockManager : MonoBehaviour
    {
        [Header("Fixed Scene Slot Transforms (Placed in Edit Mode)")]
        [SerializeField] private Transform _dockRootAnchor;
        [SerializeField] private Transform[] _fixedSlotTransforms;

        [Header("Bucket Placement Offset Relative to Slot")]
        [SerializeField] private Vector3 _bucketWorldOffset = new Vector3(0f, 0f, -0.25f);

        private BucketView[] _activeBucketViews = Array.Empty<BucketView>();
        private int _slotCount;
        private Action<BucketView> _returnBucketToPoolCallback;

        public Transform DockRootAnchor => _dockRootAnchor != null ? _dockRootAnchor : transform;
        public int SlotCount => _slotCount;

        private void Awake()
        {
            ServiceLocator.Register(this);
        }

        private void OnDestroy()
        {
            ServiceLocator.Unregister(this);
        }

        public void InitializeDock(int slotCapacity, Action<BucketView> returnBucketToPoolCallback)
        {
            _returnBucketToPoolCallback = returnBucketToPoolCallback;
            ClearActiveBuckets();

            int availableFixed = _fixedSlotTransforms != null ? _fixedSlotTransforms.Length : 0;
            _slotCount = availableFixed > 0 ? Mathf.Min(slotCapacity, availableFixed) : Mathf.Clamp(slotCapacity, 2, 7);

            // Toggle visibility if a level uses fewer slots than fixed transforms in scene
            if (_fixedSlotTransforms != null)
            {
                for (int i = 0; i < _fixedSlotTransforms.Length; i++)
                {
                    if (_fixedSlotTransforms[i] != null)
                    {
                        _fixedSlotTransforms[i].gameObject.SetActive(i < _slotCount);
                    }
                }
            }

            if (_activeBucketViews.Length < _slotCount)
            {
                _activeBucketViews = new BucketView[_slotCount];
            }
            else
            {
                Array.Clear(_activeBucketViews, 0, _activeBucketViews.Length);
            }
        }

        public Vector3 GetSlotBucketWorldPosition(int slotIndex)
        {
            if (_fixedSlotTransforms != null &&
                slotIndex >= 0 &&
                slotIndex < _fixedSlotTransforms.Length &&
                _fixedSlotTransforms[slotIndex] != null)
            {
                return _fixedSlotTransforms[slotIndex].position + _bucketWorldOffset;
            }

            float spacing = 0.98f;
            float startX = -((_slotCount - 1) * spacing) * 0.5f;
            return DockRootAnchor.position + new Vector3(startX + slotIndex * spacing, 0f, -0.2f);
        }

        public Vector3 GetSlotIntakeWorldPosition(int slotIndex)
        {
            return GetSlotBucketWorldPosition(slotIndex) + new Vector3(0f, 0.18f, -0.05f);
        }

        public void PlaceBucketViewInSlot(int slotIndex, BucketView bucketView)
        {
            if (slotIndex < 0 || slotIndex >= _slotCount || bucketView == null) return;

            _activeBucketViews[slotIndex] = bucketView;
            bucketView.CachedTransform.SetParent(DockRootAnchor, true);
            Vector3 targetPos = GetSlotBucketWorldPosition(slotIndex);
            bucketView.AnimateDeployToDock(targetPos);
        }

        /// <summary>
        /// Called by the transfer system when a flying sand grain reaches its target bucket in the Active Dock.
        /// Updates the simulator state, refreshes the visual bucket counter, and releases the bucket on 100%.
        /// </summary>
        public void NotifyGrainArrivedAtSlot(int slotIndex, BoardSimulator simulator)
        {
            if (!simulator.ConfirmGrainArrival(slotIndex, out ActiveDockSlotState updatedState, out bool bucketCompleted))
                return;

            BucketView view = (slotIndex >= 0 && slotIndex < _activeBucketViews.Length)
                ? _activeBucketViews[slotIndex]
                : null;

            if (view != null)
            {
                view.UpdateFillProgress(updatedState.CurrentCount, updatedState.MaxCapacity);
            }

            EventBus<GrainArrivedInBucketEvent>.Publish(new GrainArrivedInBucketEvent(
                slotIndex,
                updatedState.ColorId,
                updatedState.CurrentCount,
                updatedState.MaxCapacity,
                simulator.RemainingGridGrains + simulator.TotalInFlightGrains));

            if (bucketCompleted)
            {
                _activeBucketViews[slotIndex] = null;
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
            }
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying || _fixedSlotTransforms == null) return;

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            Vector3 bucketSize = new Vector3(0.78f, 0.39f, 0.39f);

            for (int i = 0; i < _fixedSlotTransforms.Length; i++)
            {
                if (_fixedSlotTransforms[i] == null) continue;
                Gizmos.DrawWireCube(_fixedSlotTransforms[i].position + _bucketWorldOffset, bucketSize);
            }
        }
    }
}
