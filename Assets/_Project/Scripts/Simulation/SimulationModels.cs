using SandSort.Data;

namespace SandSort.Simulation
{
    /// <summary>
    /// Tracks the deterministic reservation and fill state of a single slot in the Active Dock.
    /// Formula: C_current + C_inFlight < C_max
    /// </summary>
    public struct ActiveDockSlotState
    {
        public bool IsOccupied;
        public int ColorId;
        public int MaxCapacity;
        public int CurrentCount;
        public int InFlightCount;

        public int ReservedTotal => CurrentCount + InFlightCount;
        public int RemainingNeeded => IsOccupied ? (MaxCapacity - (CurrentCount + InFlightCount)) : 0;
        public bool CanRequestMore => IsOccupied && (CurrentCount + InFlightCount) < MaxCapacity;
        public bool IsFull => IsOccupied && CurrentCount >= MaxCapacity;
        public int ProgressPercent => (IsOccupied && MaxCapacity > 0) ? (CurrentCount * 100) / MaxCapacity : 0;

        public void Assign(BucketData bucket)
        {
            IsOccupied = true;
            ColorId = bucket.ColorId;
            MaxCapacity = bucket.MaxCapacity;
            CurrentCount = 0;
            InFlightCount = 0;
        }

        public void Clear()
        {
            IsOccupied = false;
            ColorId = -1;
            MaxCapacity = 0;
            CurrentCount = 0;
            InFlightCount = 0;
        }
    }

    /// <summary>
    /// Value-type record of a single grain's movement during a gravity simulation step.
    /// Used by the visual GridManager to animate falling/sliding grains with zero GC.
    /// </summary>
    public readonly struct GrainMoveRecord
    {
        public readonly int FromIndex;
        public readonly int ToIndex;
        public readonly int FromX;
        public readonly int FromY;
        public readonly int ToX;
        public readonly int ToY;
        public readonly int ColorId;

        public GrainMoveRecord(int fromIndex, int toIndex, int fromX, int fromY, int toX, int toY, int colorId)
        {
            FromIndex = fromIndex;
            ToIndex = toIndex;
            FromX = fromX;
            FromY = fromY;
            ToX = toX;
            ToY = toY;
            ColorId = colorId;
        }
    }

    /// <summary>
    /// Value-type record of a grain extracted/reserved from the Exposed Frontier into an Active Dock slot.
    /// </summary>
    public readonly struct GrainReservationRecord
    {
        public readonly int GridIndex;
        public readonly int X;
        public readonly int Y;
        public readonly int ColorId;
        public readonly int DockSlotIndex;

        public GrainReservationRecord(int gridIndex, int x, int y, int colorId, int dockSlotIndex)
        {
            GridIndex = gridIndex;
            X = x;
            Y = y;
            ColorId = colorId;
            DockSlotIndex = dockSlotIndex;
        }
    }
}

