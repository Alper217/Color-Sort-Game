using System.Collections.Generic;
using System.IO;
using SandSort.Core;
using SandSort.Data;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Service for managing color palettes, texture color extractions,
    /// and palette compaction / color removal in SandSort levels.
    /// Resmin orijinal renkleri esas alınır; kullanıcı sadece manuel boyama ile renk değiştirebilir.
    /// </summary>
    public static class LevelPaletteService
    {

        /// <summary>
        /// Counts total sand grains currently using the specified colorId in the matrix.
        /// </summary>
        public static int CountGrainsForColor(LevelDataSO level, int colorId)
        {
            if (level == null || level.GridCells == null) return 0;
            int count = 0;
            for (int i = 0; i < level.GridCells.Length; i++)
            {
                if (level.GridCells[i] == colorId) count++;
            }
            return count;
        }

        /// <summary>
        /// Looks for an existing PNG sprite matching the level index or title in Assets/_Project/Art/PixelSprites/.
        /// </summary>
        public static Texture2D TryFindMatchingSprite(int levelIndex, string levelTitle)
        {
            if (!Directory.Exists(LevelAssetRepository.SpritesFolder)) return null;

            string[] allSprites = Directory.GetFiles(LevelAssetRepository.SpritesFolder, "*.png");
            string indexPrefix1 = $"Sprite_Level_{levelIndex:D2}";
            string indexPrefix2 = $"Sprite_Level_{levelIndex:00}";

            foreach (string sPath in allSprites)
            {
                string fName = Path.GetFileNameWithoutExtension(sPath);
                if (fName.StartsWith(indexPrefix1) || fName.StartsWith(indexPrefix2) ||
                    (!string.IsNullOrEmpty(levelTitle) && levelTitle.Contains(fName)))
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sPath.Replace('\\', '/'));
                    if (tex != null) return tex;
                }
            }

            return null;
        }

        /// <summary>
        /// Restores palette from source texture or loaded asset without forcing default vibrant fallback.
        /// </summary>
        public static bool RestorePaletteFromSource(
            LevelDataSO workingLevel,
            List<Color32> currentPalette,
            Texture2D sourceTexture,
            int levelIndex,
            LevelDataSO loadedAsset)
        {
            Texture2D targetTex = sourceTexture ?? TryFindMatchingSprite(levelIndex, workingLevel?.LevelTitle);

            if (targetTex != null)
            {
                Color32[] extracted = LevelGenerationEngine.ExtractDominantPaletteFromTexture(targetTex, 12);
                if (extracted != null && extracted.Length > 0)
                {
                    currentPalette.Clear();
                    currentPalette.AddRange(extracted);
                    if (workingLevel != null)
                    {
                        workingLevel.Palette = (Color32[])extracted.Clone();
                    }
                    return true;
                }
            }

            if (loadedAsset != null && loadedAsset.Palette != null && loadedAsset.Palette.Length > 0)
            {
                currentPalette.Clear();
                currentPalette.AddRange(loadedAsset.Palette);
                if (workingLevel != null)
                {
                    workingLevel.Palette = (Color32[])loadedAsset.Palette.Clone();
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes unused palette slots from the level and remaps grid cells and bucket queues to clean 0..N-1 IDs.
        /// </summary>
        public static void CompactPaletteToUsedOnly(LevelDataSO level, List<Color32> currentPalette)
        {
            if (level == null || currentPalette == null || currentPalette.Count == 0) return;

            int oldColorCount = currentPalette.Count;
            int[] oldToNew = new int[oldColorCount];
            List<Color32> newPalette = new List<Color32>();

            for (int i = 0; i < oldColorCount; i++)
            {
                int count = CountGrainsForColor(level, i);
                if (count > 0)
                {
                    oldToNew[i] = newPalette.Count;
                    newPalette.Add(currentPalette[i]);
                }
                else
                {
                    oldToNew[i] = -1;
                }
            }

            if (newPalette.Count == 0) return;

            // Remap grid cells
            if (level.GridCells != null)
            {
                for (int i = 0; i < level.GridCells.Length; i++)
                {
                    int oldId = level.GridCells[i];
                    if (oldId >= 0 && oldId < oldColorCount)
                    {
                        level.GridCells[i] = oldToNew[oldId] >= 0 ? oldToNew[oldId] : 0;
                    }
                }
            }

            currentPalette.Clear();
            currentPalette.AddRange(newPalette);
            level.Palette = newPalette.ToArray();
        }

        /// <summary>
        /// Safely removes a single color from the palette, remapping any cells using it to color ID 0.
        /// </summary>
        public static void SafeRemoveColorFromLevel(LevelDataSO level, List<Color32> currentPalette, int removeIdx)
        {
            if (level == null || currentPalette == null || currentPalette.Count <= 2) return;
            if (removeIdx < 0 || removeIdx >= currentPalette.Count) return;

            int oldColorCount = currentPalette.Count;
            currentPalette.RemoveAt(removeIdx);
            level.Palette = currentPalette.ToArray();

            if (level.GridCells != null)
            {
                for (int i = 0; i < level.GridCells.Length; i++)
                {
                    int cid = level.GridCells[i];
                    if (cid == removeIdx)
                    {
                        level.GridCells[i] = 0; // Remap removed color to color 0
                    }
                    else if (cid > removeIdx && cid < oldColorCount)
                    {
                        level.GridCells[i] = cid - 1; // Shift down
                    }
                }
            }
        }
    }
}

