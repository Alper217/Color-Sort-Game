using SandSort.Core;

namespace SandSort.Data
{
    public readonly struct LevelStartedEvent : IGameEvent
    {
        public readonly int LevelIndex;
        public readonly string LevelTitle;
        public readonly int TotalGrains;

        public LevelStartedEvent(int levelIndex, string levelTitle, int totalGrains)
        {
            LevelIndex = levelIndex;
            LevelTitle = levelTitle;
            TotalGrains = totalGrains;
        }
    }

    public readonly struct BucketDeployedToDockEvent : IGameEvent
    {
        public readonly int SourceColumnIndex;
        public readonly int DockSlotIndex;
        public readonly BucketData Bucket;

        public BucketDeployedToDockEvent(int sourceColumnIndex, int dockSlotIndex, BucketData bucket)
        {
            SourceColumnIndex = sourceColumnIndex;
            DockSlotIndex = dockSlotIndex;
            Bucket = bucket;
        }
    }

    public readonly struct GrainReservedEvent : IGameEvent
    {
        public readonly int GridIndex;
        public readonly int GridX;
        public readonly int GridY;
        public readonly int ColorId;
        public readonly int DockSlotIndex;

        public GrainReservedEvent(int gridIndex, int gridX, int gridY, int colorId, int dockSlotIndex)
        {
            GridIndex = gridIndex;
            GridX = gridX;
            GridY = gridY;
            ColorId = colorId;
            DockSlotIndex = dockSlotIndex;
        }
    }

    public readonly struct GrainArrivedInBucketEvent : IGameEvent
    {
        public readonly int DockSlotIndex;
        public readonly int ColorId;
        public readonly int CurrentCount;
        public readonly int MaxCapacity;
        public readonly int TotalRemainingGrains;

        public GrainArrivedInBucketEvent(
            int dockSlotIndex,
            int colorId,
            int currentCount,
            int maxCapacity,
            int totalRemainingGrains)
        {
            DockSlotIndex = dockSlotIndex;
            ColorId = colorId;
            CurrentCount = currentCount;
            MaxCapacity = maxCapacity;
            TotalRemainingGrains = totalRemainingGrains;
        }

        public int ProgressPercent => MaxCapacity > 0 ? (CurrentCount * 100) / MaxCapacity : 100;
    }

    public readonly struct BucketCompletedEvent : IGameEvent
    {
        public readonly int DockSlotIndex;
        public readonly int ColorId;

        public BucketCompletedEvent(int dockSlotIndex, int colorId)
        {
            DockSlotIndex = dockSlotIndex;
            ColorId = colorId;
        }
    }

    public readonly struct LevelWonEvent : IGameEvent
    {
        public readonly int LevelIndex;

        public LevelWonEvent(int levelIndex)
        {
            LevelIndex = levelIndex;
        }
    }

    public readonly struct LevelDeadlockedEvent : IGameEvent
    {
        public readonly int LevelIndex;
        public readonly int RemainingGrains;

        public LevelDeadlockedEvent(int levelIndex, int remainingGrains)
        {
            LevelIndex = levelIndex;
            RemainingGrains = remainingGrains;
        }
    }
}

