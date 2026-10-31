namespace SandSort.Data
{
    /// <summary>
    /// Extensible cell modifier state for blockers (e.g., Ice, Hidden color).
    /// </summary>
    public enum CellState : byte
    {
        Empty = 0,
        Normal = 1,
        LockedIce = 2,
        Hidden = 3
    }
}

