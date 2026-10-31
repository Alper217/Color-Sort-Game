namespace SandSort.Data
{
    /// <summary>
    /// Defines how grains in the matrix fall when underlying cells are vacuumed into buckets.
    /// </summary>
    public enum GravityMode : byte
    {
        /// <summary>
        /// Grains stay at their exact coordinates (pure layer-by-layer pixel peeling).
        /// </summary>
        None = 0,

        /// <summary>
        /// Grains fall straight down in their vertical column (preserves vertical column alignment).
        /// </summary>
        VerticalColumn = 1,

        /// <summary>
        /// Full sand cellular automata: grains fall straight down or slide diagonally down-left/down-right.
        /// </summary>
        CellularAutomata = 2
    }
}

