using System;
using SandSort.Core;
using UnityEngine;

namespace SandSort.Data
{
    /// <summary>
    /// ScriptableObject holding the complete deterministic definition of a puzzle level:
    /// Color palette, 1D flattened pixel/sand grid, spawner bucket queues, and dock rules.
    /// </summary>
    [CreateAssetMenu(fileName = "Level_01", menuName = "SandSort/Level Data", order = 1)]
    public class LevelDataSO : ScriptableObject
    {
        [Header("Level Identification")]
        public int LevelIndex = 1;
        public string LevelTitle = "Pixel Heart";

        [Header("Grid Dimensions & Physics")]
        [Min(4)] public int Width = GameConstants.DefaultGridWidth;
        [Min(4)] public int Height = GameConstants.DefaultGridHeight;
        public GravityMode Gravity = GravityMode.VerticalColumn;

        [Header("Dock & Queue Configuration")]
        [Range(2, 7)] public int ActiveDockCapacity = GameConstants.DefaultDockCapacity;

        [Header("Color Palette")]
        public Color32[] Palette = GameConstants.DefaultVibrantPalette;

        [Header("Flattened Grid Data (index = y * Width + x, -1 = Empty)")]
        [Tooltip("Bottom-left is (0,0). Row y=0 is the bottom exposed frontier.")]
        public int[] GridCells;

        [Header("Bottom Spawner Columns (Index 0 = Top/Front Bucket)")]
        public BucketColumnData[] Columns;

        /// <summary>
        /// Safely retrieves the ColorID at (x, y). Returns -1 if out of bounds or empty.
        /// </summary>
        public int GetCell(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return GameConstants.EmptyCellColorId;

            int index = y * Width + x;
            if (GridCells == null || index < 0 || index >= GridCells.Length)
                return GameConstants.EmptyCellColorId;

            return GridCells[index];
        }

        /// <summary>
        /// Sets the ColorID at (x, y).
        /// </summary>
        public void SetCell(int x, int y, int colorId)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return;

            EnsureGridAllocated();
            GridCells[y * Width + x] = colorId;
        }

        public void EnsureGridAllocated()
        {
            int targetSize = Width * Height;
            if (GridCells == null || GridCells.Length != targetSize)
            {
                GridCells = new int[targetSize];
                for (int i = 0; i < targetSize; i++)
                {
                    GridCells[i] = GameConstants.EmptyCellColorId;
                }
            }
        }

        /// <summary>
        /// Counts total non-empty sand grains in the matrix.
        /// </summary>
        public int GetTotalGrainCount()
        {
            if (GridCells == null) return 0;
            int total = 0;
            for (int i = 0; i < GridCells.Length; i++)
            {
                if (GridCells[i] >= 0) total++;
            }
            return total;
        }

        /// <summary>
        /// Counts total bucket capacity across all columns to verify 100% conservation of mass.
        /// </summary>
        public int GetTotalBucketCapacity()
        {
            if (Columns == null) return 0;
            int total = 0;
            for (int c = 0; c < Columns.Length; c++)
            {
                BucketData[] buckets = Columns[c].Buckets;
                if (buckets == null) continue;
                for (int b = 0; b < buckets.Length; b++)
                {
                    if (buckets[b].IsValid)
                    {
                        total += buckets[b].MaxCapacity;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// Resolves a ColorId to its actual Color32 from the palette.
        /// </summary>
        public Color32 GetColor(int colorId)
        {
            if (Palette != null && colorId >= 0 && colorId < Palette.Length)
            {
                return Palette[colorId];
            }
            return new Color32(255, 255, 255, 255);
        }
    }
}

