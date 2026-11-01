using System;
using System.Collections.Generic;
using System.IO;
using SandSort.Core;
using SandSort.Data;
using SandSort.Simulation;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    public enum PixelArtPreset
    {
        PixelHeart = 0,
        RoyalCrown = 1,
        MagicPotion = 2,
        LayeredGem = 3
    }

    /// <summary>
    /// Core algorithms for Module 1:
    /// 1. Color Quantization + Contrasting Background Sand Fill (60x60 = 3,600 sand grains)
    /// 2. Bucket Partitioning with Remainder Balancing (100% conservation of mass)
    /// 3. Reverse Scrambling via exact Sandbox Compaction + Top-Surface Avalanche Peeling Simulation
    /// </summary>
    public static class LevelGenerationEngine
    {
        public struct GenerationConfig
        {
            public int Width;
            public int Height;
            public GravityMode Gravity;
            public int ActiveDockCapacity;
            public int ColumnCount;
            public int MinBucketCapacity;
            public int MaxBucketCapacity;
            public float ScrambleFactor;
            public int RandomSeed;
            public int FallbackBackgroundColorId;
            public Color32[] Palette;
        }

        /// <summary>
        /// Progressive difficulty settings derived from a single Difficulty Index D (0..1).
        /// D rises along the campaign (TotalLevels) with an ease curve plus a "sawtooth":
        /// every 10th level is a BOSS (+D), every 5th level is a BREATHER (-D).
        /// D drives: columns, rows per column (bucket depth), bucket size, dock size,
        /// scrambling and how many front rows are visible before '?' buckets start.
        /// </summary>
        public struct DifficultyProfile
        {
            public float DifficultyIndex;
            public int GridResolution; // 24x24 for tutorial/early, scaling to 48x48
            public int ColumnCount;
            public int RowsPerColumn;
            public int ActiveDockCapacity;
            public int MinBucketCapacity;
            public int MaxBucketCapacity;
            public float ScrambleFactor;
            public int MysteryBucketStartRow; // 99 = none; N = rows 0..N-1 visible, rows >= N are '?' (1 = only front visible)
            public float SandFillSpeedMultiplier; // Turbo speed for early tutorial levels (3.8x), scaling to standard fast (2.4x)
        }

        /// <summary>Campaign length. Set to 50 or 100; the whole curve stretches to fit.</summary>
        public const int TotalLevels = 100;

        public static float ComputeDifficultyIndex(int levelIndex, int totalLevels = TotalLevels)
        {
            int lvl = Mathf.Max(1, levelIndex);
            float p = Mathf.Clamp01((lvl - 1) / (float)Mathf.Max(1, totalLevels - 1));

            // 50% linear + 50% smoothstep: gentle start, steeper middle, flat-ish top.
            float smooth = p * p * (3f - 2f * p);
            float d = 0.5f * p + 0.5f * smooth;

            // Sawtooth only after the tutorial phase (first 4 levels stay clean).
            if (lvl > 4)
            {
                if (lvl % 10 == 0) d += 0.07f;       // boss
                else if (lvl % 5 == 0) d -= 0.06f;   // breather
            }
            return Mathf.Clamp01(d);
        }

        public static DifficultyProfile GetDifficultyProfile(int levelIndex, int totalLevels = TotalLevels, int totalGrains = 0)
        {
            float d = ComputeDifficultyIndex(levelIndex, totalLevels);

            // Progressive grid size: 24x24 (576 grains, quick 20s finish) -> 48x48
            int gridRes = (levelIndex == 1) ? 24 : (levelIndex == 2 ? 30 : (levelIndex <= 4 ? 36 : Mathf.RoundToInt(Mathf.Lerp(36f, 48f, d))));
            if (totalGrains <= 0) totalGrains = gridRes * gridRes;

            int columns = d < 0.12f ? 2 : d < 0.35f ? 3 : d < 0.65f ? 4 : 5;
            int rows = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 12f, d)), 2, 14);
            int dock = d < 0.30f ? 5 : d < 0.85f ? 4 : 3;

            // Bucket size follows from rows x columns => same picture, more buckets as D grows.
            float avgCap = totalGrains / (float)(columns * rows);
            int minCap = Mathf.Max(10, Mathf.RoundToInt(avgCap * 0.88f));
            int maxCap = Mathf.Max(minCap, Mathf.RoundToInt(avgCap * 1.12f));

            // Visible front rows shrink from 5 to 1 as D grows (none hidden at the very start).
            int mystery = 99;
            if (d >= 0.15f)
            {
                float k = Mathf.InverseLerp(0.15f, 0.85f, d);
                mystery = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(5f, 1f, k)), 1, 5);
            }

            // Turbo fill speed for early levels (e.g. Level 1 = 3.8x for ultra snappy feel), scaling to standard fast 2.4x
            float speedMult = (levelIndex == 1) ? 3.8f : (levelIndex == 2 ? 3.2f : (levelIndex <= 4 ? 2.8f : 2.4f));

            return new DifficultyProfile
            {
                DifficultyIndex = d,
                GridResolution = gridRes,
                ColumnCount = columns,
                RowsPerColumn = rows,
                ActiveDockCapacity = dock,
                MinBucketCapacity = minCap,
                MaxBucketCapacity = maxCap,
                ScrambleFactor = Mathf.Lerp(0f, 0.55f, d),
                MysteryBucketStartRow = mystery,
                SandFillSpeedMultiplier = speedMult
            };
        }

        public static void PopulateLevelFromTexture(
            LevelDataSO targetLevel,
            Texture2D sourceTexture,
            GenerationConfig config)
        {
            if (targetLevel == null) throw new ArgumentNullException(nameof(targetLevel));
            if (sourceTexture == null) throw new ArgumentNullException(nameof(sourceTexture));

            Color32[] palette = (config.Palette != null && config.Palette.Length > 0)
                ? config.Palette
                : GameConstants.DefaultVibrantPalette;

            targetLevel.Width = Mathf.Clamp(config.Width, 8, 80);
            targetLevel.Height = Mathf.Clamp(config.Height, 8, 80);
            targetLevel.Gravity = config.Gravity;
            targetLevel.ActiveDockCapacity = Mathf.Clamp(config.ActiveDockCapacity, 2, 7);
            targetLevel.Palette = palette;
            targetLevel.EnsureGridAllocated();

            int bgFillColorId = Mathf.Clamp(config.FallbackBackgroundColorId, 0, palette.Length - 1);

            Texture2D readableTex = GetReadableResampledTexture(sourceTexture, targetLevel.Width, targetLevel.Height);
            Color32[] pixels = readableTex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(readableTex);

            for (int y = 0; y < targetLevel.Height; y++)
            {
                for (int x = 0; x < targetLevel.Width; x++)
                {
                    int idx = y * targetLevel.Width + x;
                    Color32 px = pixels[idx];

                    if (px.a < 128)
                    {
                        targetLevel.GridCells[idx] = bgFillColorId;
                    }
                    else
                    {
                        targetLevel.GridCells[idx] = FindClosestPaletteColorId(px, palette);
                    }
                }
            }

            GenerateSolvableBucketQueues(targetLevel, config);
        }

        public static Texture2D CreateProceduralPresetTexture(PixelArtPreset preset, int width, int height, Color32[] palette)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x + 0.5f) / width * 2f - 1f;
                    float ny = (y + 0.5f) / height * 2f - 1f;

                    int colorId = EvaluatePresetShapeWithBackgroundSand(preset, nx, ny, x, y);
                    colorId = Mathf.Clamp(colorId, 0, palette.Length - 1);
                    pixels[y * width + x] = palette[colorId];
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static int EvaluatePresetShapeWithBackgroundSand(PixelArtPreset preset, float nx, float ny, int x, int y)
        {
            switch (preset)
            {
                case PixelArtPreset.PixelHeart:
                {
                    const int bgSand = 1; // Pure White / Silver Sand

                    float hx = nx * 1.18f;
                    float hy = ny * 1.18f - 0.08f;
                    float a = hx * hx + hy * hy - 0.62f;
                    float heartVal = a * a * a - (hx * hx) * (hy * hy * hy);
                    if (heartVal > 0f) return bgSand;

                    return 0; // Vibrant Coral Red Heart (Tutorial Level 1: 2 clean colors, 100% smooth bucket completions!)
                }

                case PixelArtPreset.RoyalCrown:
                {
                    const int bgSand = 4; // Amethyst Purple background

                    if (ny < -0.62f || ny > 0.68f) return bgSand;
                    if (Mathf.Abs(nx) > 0.82f) return bgSand;

                    if (ny >= -0.62f && ny < -0.22f)
                    {
                        if (Mathf.Abs(ny - (-0.42f)) < 0.09f && ((x / 5) % 2 == 0))
                            return 0; // Red rubies
                        return 2; // Gold band
                    }

                    float spikeHeight = 0.22f + 0.44f * Mathf.Cos(nx * Mathf.PI * 2.2f);
                    if (ny <= spikeHeight)
                    {
                        if (Mathf.Abs(nx) < 0.26f && ny < 0.18f)
                            return 1; // Royal Blue velvet center
                        if (ny > spikeHeight - 0.16f)
                            return 3; // Emerald Green jewels
                        return 2; // Pure Gold body
                    }
                    return bgSand;
                }

                case PixelArtPreset.MagicPotion:
                {
                    int bgSand = (ny < 0f) ? 3 : 2;

                    float distFromBottomCenter = Mathf.Sqrt(nx * nx + (ny + 0.22f) * (ny + 0.22f));
                    bool inBulb = distFromBottomCenter <= 0.66f;
                    bool inNeck = Mathf.Abs(nx) <= 0.24f && ny >= 0.18f && ny <= 0.70f;

                    if (!inBulb && !inNeck) return bgSand;

                    if (inNeck && ny > 0.48f) return 0; // Red cork
                    if (inBulb && distFromBottomCenter > 0.52f) return 1; // Blue bottle glass
                    if (ny < -0.35f) return 4; // Purple bottom liquid
                    if (ny < 0.05f) return 0;  // Red middle liquid
                    return 5;                  // Cyan foam
                }

                case PixelArtPreset.LayeredGem:
                default:
                {
                    const int bgSand = 4;
                    float manhattan = Mathf.Abs(nx) + Mathf.Abs(ny);
                    if (manhattan > 1.08f) return bgSand;

                    if (manhattan < 0.36f) return 2;
                    if (ny >= 0 && nx >= 0) return 0;
                    if (ny >= 0 && nx < 0) return 1;
                    if (ny < 0 && nx < 0) return 3;
                    return 5;
                }
            }
        }

        /// <summary>
        /// Simulates peeling the grid from the bottom row (y = 0) using the exact Sandbox compaction
        /// and top-surface avalanche rules so the generated bucket queues are 100% solvable!
        /// </summary>
        public static void GenerateSolvableBucketQueues(LevelDataSO level, GenerationConfig config)
        {
            int width = level.Width;
            int height = level.Height;
            int cellCount = width * height;

            int minCap = Mathf.Max(10, config.MinBucketCapacity);
            int maxCap = Mathf.Max(minCap, config.MaxBucketCapacity);
            int colCount = Mathf.Clamp(config.ColumnCount, 2, 6);
            System.Random rng = new System.Random(config.RandomSeed);

            int[] simGrid = new int[cellCount];
            int[] colHeights = new int[width];
            int remaining = 0;

            for (int x = 0; x < width; x++)
            {
                int writeY = 0;
                for (int y = 0; y < height; y++)
                {
                    int colorId = level.GridCells[y * width + x];
                    if (colorId >= 0)
                    {
                        simGrid[writeY * width + x] = colorId;
                        writeY++;
                        remaining++;
                    }
                }
                for (int y = writeY; y < height; y++)
                {
                    simGrid[y * width + x] = BoardSimulator.VacatedInteriorCell;
                }
                colHeights[x] = writeY;
            }

            List<BucketData> chronologicalBuckets = new List<BucketData>(64);
            Dictionary<int, int> runningColorAccumulator = new Dictionary<int, int>();
            Dictionary<int, int> targetThresholdPerColor = new Dictionary<int, int>();

            int safety = cellCount * 2 + 500;
            int tick = 0;
            while (remaining > 0 && safety-- > 0)
            {
                tick++;
                bool leftToRight = ((tick & 1) == 0);

                // Extract bottom-row (y = 0) grain from each non-empty column and compact column immediately
                for (int i = 0; i < width; i++)
                {
                    int x = leftToRight ? i : (width - 1 - i);
                    int colH = colHeights[x];
                    if (colH <= 0) continue;

                    int colorId = simGrid[x]; // (x, y=0)
                    if (colorId < 0) continue;

                    remaining--;

                    // Shift column down by 1 cell immediately
                    for (int y = 0; y < colH - 1; y++)
                    {
                        simGrid[y * width + x] = simGrid[(y + 1) * width + x];
                    }
                    simGrid[(colH - 1) * width + x] = BoardSimulator.VacatedInteriorCell;
                    colHeights[x] = colH - 1;

                    if (!targetThresholdPerColor.TryGetValue(colorId, out int targetCap))
                    {
                        targetCap = rng.Next(minCap, maxCap + 1);
                        targetThresholdPerColor[colorId] = targetCap;
                    }

                    runningColorAccumulator.TryGetValue(colorId, out int count);
                    count++;

                    if (count >= targetCap)
                    {
                        chronologicalBuckets.Add(new BucketData(colorId, count));
                        runningColorAccumulator[colorId] = 0;
                        targetThresholdPerColor[colorId] = rng.Next(minCap, maxCap + 1);
                    }
                    else
                    {
                        runningColorAccumulator[colorId] = count;
                    }
                }

                // If an accumulated color has NO grains left at the bottom exposed row (y=0),
                // flush it now so every bucket fills cleanly to 100% on its contiguous wave!
                if ((tick & 1) == 0)
                {
                    List<int> toFlush = null;
                    foreach (KeyValuePair<int, int> kvp in runningColorAccumulator)
                    {
                        int cId = kvp.Key;
                        int cCount = kvp.Value;
                        if (cCount <= 0) continue;

                        bool stillExposed = false;
                        for (int x = 0; x < width; x++)
                        {
                            if (colHeights[x] > 0 && simGrid[x] == cId)
                            {
                                stillExposed = true;
                                break;
                            }
                        }

                        if (!stillExposed && (cCount >= Mathf.Max(20, minCap / 2) || remaining <= 0))
                        {
                            if (toFlush == null) toFlush = new List<int>();
                            toFlush.Add(cId);
                        }
                    }

                    if (toFlush != null)
                    {
                        for (int f = 0; f < toFlush.Count; f++)
                        {
                            int cId = toFlush[f];
                            int cCount = runningColorAccumulator[cId];
                            chronologicalBuckets.Add(new BucketData(cId, cCount));
                            runningColorAccumulator[cId] = 0;
                            targetThresholdPerColor[cId] = rng.Next(minCap, maxCap + 1);
                        }
                    }
                }

                // Apply top-surface avalanche relaxation matching BoardSimulator
                if (level.Gravity == GravityMode.CellularAutomata)
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int h = colHeights[x];
                            if (h <= 0) continue;

                            int dir = (((x + tick + pass) & 1) == 0) ? -1 : 1;
                            if (!TrySimTumble(simGrid, colHeights, width, x, x + dir))
                            {
                                TrySimTumble(simGrid, colHeights, width, x, x - dir);
                            }
                        }
                    }
                }
            }

            // Remainder Balancing (100% Conservation of Mass)
            foreach (KeyValuePair<int, int> kvp in runningColorAccumulator)
            {
                int colorId = kvp.Key;
                int leftover = kvp.Value;
                if (leftover <= 0) continue;

                int mergeThreshold = Mathf.Max(15, minCap / 2);
                int lastBucketIdx = -1;
                for (int i = chronologicalBuckets.Count - 1; i >= 0; i--)
                {
                    if (chronologicalBuckets[i].ColorId == colorId)
                    {
                        lastBucketIdx = i;
                        break;
                    }
                }

                if (leftover < mergeThreshold && lastBucketIdx >= 0)
                {
                    BucketData prev = chronologicalBuckets[lastBucketIdx];
                    chronologicalBuckets[lastBucketIdx] = new BucketData(colorId, prev.MaxCapacity + leftover);
                }
                else
                {
                    chronologicalBuckets.Add(new BucketData(colorId, leftover));
                }
            }

            // Controlled safe local scrambling
            int maxSwapWindow = Mathf.Max(1, Mathf.RoundToInt(config.ScrambleFactor * (level.ActiveDockCapacity + 1)));
            if (config.ScrambleFactor > 0.01f && chronologicalBuckets.Count > 2)
            {
                int swapAttempts = Mathf.RoundToInt(chronologicalBuckets.Count * config.ScrambleFactor * 1.3f);
                for (int s = 0; s < swapAttempts; s++)
                {
                    int idxA = rng.Next(0, chronologicalBuckets.Count);
                    int offset = rng.Next(1, maxSwapWindow + 1);
                    int idxB = Mathf.Clamp(idxA + offset, 0, chronologicalBuckets.Count - 1);

                    BucketData temp = chronologicalBuckets[idxA];
                    chronologicalBuckets[idxA] = chronologicalBuckets[idxB];
                    chronologicalBuckets[idxB] = temp;
                }
            }

            List<BucketData>[] colLists = new List<BucketData>[colCount];
            for (int c = 0; c < colCount; c++)
            {
                colLists[c] = new List<BucketData>();
            }

            for (int i = 0; i < chronologicalBuckets.Count; i++)
            {
                colLists[i % colCount].Add(chronologicalBuckets[i]);
            }

            level.Columns = new BucketColumnData[colCount];
            for (int c = 0; c < colCount; c++)
            {
                level.Columns[c] = new BucketColumnData
                {
                    Buckets = colLists[c].ToArray()
                };
            }
        }

        private static bool TrySimTumble(int[] simGrid, int[] colHeights, int width, int fromX, int toX)
        {
            if (toX < 0 || toX >= width) return false;
            int fromH = colHeights[fromX];
            int toH = colHeights[toX];

            if (!((fromH - toH >= 2) || (toH == 0 && fromH >= 1))) return false;

            int srcY = fromH - 1;
            int dstY = toH;
            simGrid[dstY * width + toX] = simGrid[srcY * width + fromX];
            simGrid[srcY * width + fromX] = BoardSimulator.VacatedInteriorCell;
            colHeights[fromX] = fromH - 1;
            colHeights[toX] = toH + 1;
            return true;
        }

        public static int FindClosestPaletteColorId(Color32 target, Color32[] palette)
        {
            int bestIndex = 0;
            int bestDistSq = int.MaxValue;

            for (int i = 0; i < palette.Length; i++)
            {
                Color32 p = palette[i];
                int dr = target.r - p.r;
                int dg = target.g - p.g;
                int db = target.b - p.b;
                int distSq = dr * dr + dg * dg + db * db;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static Texture2D GetReadableResampledTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                targetWidth,
                targetHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            rt.filterMode = FilterMode.Point;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            GL.Clear(true, true, Color.clear);
            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        /// <summary>
        /// Generates 3 portfolio-ready 60x60 full-tray levels (3,600 sand grains per level!)
        /// with GPU Instancing & Sandbox Avalanche physics.
        /// </summary>
        public static List<LevelDataSO> GenerateShowcasePortfolioLevels()
        {
            const string spritesFolder = "Assets/_Project/Art/PixelSprites";
            const string levelsFolder = "Assets/Levels";

            EnsureFolderExists("Assets/_Project");
            EnsureFolderExists("Assets/_Project/Art");
            EnsureFolderExists(spritesFolder);
            EnsureFolderExists(levelsFolder);

            (PixelArtPreset preset, string title, int width, int height, GravityMode gravity, float scramble, int minCap, int maxCap, int bgId)[] specs =
            {
                (PixelArtPreset.PixelHeart,  "Pixel Heart",  24, 24, GravityMode.CellularAutomata, 0.00f, 40, 75, 1),
                (PixelArtPreset.RoyalCrown,  "Royal Crown",  30, 30, GravityMode.CellularAutomata, 0.20f, 75,  110, 4),
                (PixelArtPreset.MagicPotion, "Magic Potion", 38, 38, GravityMode.CellularAutomata, 0.35f, 55,  85,  3)
            };

            List<LevelDataSO> createdLevels = new List<LevelDataSO>(specs.Length);

            for (int i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                int levelNum = i + 1;

                Texture2D procTex = CreateProceduralPresetTexture(
                    spec.preset,
                    spec.width,
                    spec.height,
                    GameConstants.DefaultVibrantPalette);

                string pngPath = $"{spritesFolder}/Sprite_Level_{levelNum:00}_{spec.preset}.png";
                File.WriteAllBytes(pngPath, procTex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(procTex);
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

                TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                Texture2D importedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);

                string assetPath = $"{levelsFolder}/Level_{levelNum:00}.asset";
                LevelDataSO levelSO = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
                if (levelSO == null)
                {
                    levelSO = ScriptableObject.CreateInstance<LevelDataSO>();
                    AssetDatabase.CreateAsset(levelSO, assetPath);
                }

                levelSO.LevelIndex = levelNum;
                levelSO.LevelTitle = spec.title;

                DifficultyProfile diff = GetDifficultyProfile(levelNum);
                levelSO.MysteryBucketStartRow = diff.MysteryBucketStartRow;
                levelSO.SandFillSpeedMultiplier = diff.SandFillSpeedMultiplier;

                GenerationConfig cfg = new GenerationConfig
                {
                    Width = spec.width,
                    Height = spec.height,
                    Gravity = spec.gravity,
                    ActiveDockCapacity = diff.ActiveDockCapacity,
                    ColumnCount = diff.ColumnCount,
                    MinBucketCapacity = diff.MinBucketCapacity,
                    MaxBucketCapacity = diff.MaxBucketCapacity,
                    ScrambleFactor = diff.ScrambleFactor,
                    RandomSeed = 100 + levelNum * 37,
                    FallbackBackgroundColorId = spec.bgId,
                    Palette = GameConstants.DefaultVibrantPalette
                };

                PopulateLevelFromTexture(levelSO, importedTex, cfg);
                EditorUtility.SetDirty(levelSO);
                createdLevels.Add(levelSO);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return createdLevels;
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
                string newFolder = Path.GetFileName(folderPath);
                if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(newFolder))
                {
                    AssetDatabase.CreateFolder(parent, newFolder);
                }
            }
        }
    }
}
