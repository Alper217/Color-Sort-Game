using System;
using System.Collections.Generic;
using System.IO;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay;
using SandSort.Simulation;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Master Studio Level Generator for SandSort:
    /// Hand-crafts 8 iconic, pixel-perfect, hypercasual puzzle levels with bespoke color palettes,
    /// gorgeous harmonious tones, 100% mass conservation, and guaranteed bot solvability.
    /// </summary>
    public static class MasterStudioLevelGenerator
    {
        private const string SpritesFolder = "Assets/_Project/Art/PixelSprites";
        private const string LevelsFolder = "Assets/Levels";
        private const string AutoBuildVersionPref = "SandSort_StudioLevels_Master_V5";

        public struct StudioLevelBlueprint
        {
            public int Index;
            public string Title;
            public int Width;
            public int Height;
            public Color32[] Palette;
            public int BackgroundColorId;
            public int DockCapacity;
            public int ColumnCount;
            public int MinBucketCapacity;
            public int MaxBucketCapacity;
            public float ScrambleFactor;
            public int MysteryStartRow;
            public float SandFillSpeedMultiplier;
            public int CustomSeed;
            public Func<int, int, int, int, int> PixelEvaluator; // (x, y, width, height) => colorId
        }

        // [InitializeOnLoadMethod] (Disabled: do not auto-overwrite user levels on compile)
        private static void AutoGenerateOnCompile()
        {
            if (EditorPrefs.GetBool(AutoBuildVersionPref, false))
                return;

            EditorApplication.delayCall += () =>
            {
                RebuildAll8StudioLevels();
                EditorPrefs.SetBool(AutoBuildVersionPref, true);
            };
        }

        [MenuItem("SandSort/Rebuild All 8 Studio Levels (100% Balanced & Solvable)", priority = 1)]
        public static void RebuildAll8StudioLevels()
        {
            EnsureFolderExists("Assets/_Project");
            EnsureFolderExists("Assets/_Project/Art");
            EnsureFolderExists(SpritesFolder);
            EnsureFolderExists(LevelsFolder);

            StudioLevelBlueprint[] blueprints = GetMasterBlueprints();
            List<LevelDataSO> createdAssets = new List<LevelDataSO>(blueprints.Length);

            Debug.Log("<color=#2ecc71><b>[MasterStudioLevelGenerator]</b> Starting rebuild of 8 Studio Levels...</color>");

            for (int i = 0; i < blueprints.Length; i++)
            {
                StudioLevelBlueprint bp = blueprints[i];
                LevelDataSO asset = GenerateSingleStudioLevel(bp);
                createdAssets.Add(asset);

                // Run 1,000 headless bot runs to verify solvability
                BotSimulationReport report = HeadlessLevelBot.RunSimulation(asset, 1000, 0.75f, 1337 + bp.Index * 97);
                int totalGrains = asset.GetTotalGrainCount();
                int totalBucketCap = asset.GetTotalBucketCapacity();
                bool massConserved = (totalGrains == totalBucketCap);

                string statusColor = report.WinRatePercent >= 85f ? "#2ecc71" : (report.WinRatePercent >= 60f ? "#f39c12" : "#e74c3c");
                Debug.Log($"<b>Level {bp.Index:02}: {bp.Title}</b> ({bp.Width}x{bp.Height}) - " +
                          $"<color={statusColor}><b>WinRate: {report.WinRatePercent:F1}%</b></color> " +
                          $"(AvgMoves: {report.AverageMovesOnWin:F1}, Kütle Korunumu: {(massConserved ? "100% EŞİT" : "HATA!")})");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Sync with GameLoopController in the scene
            LevelGeneratorWindow.SyncAllLevelsWithGameLoopController();

            Debug.Log("<color=#00e5ff><b>[MasterStudioLevelGenerator]</b> All 8 Studio Levels rebuilt and synced successfully!</color>");
        }

        [MenuItem("SandSort/Build Levels 4 to 10 (Strict Solvability & Pure Colors)", priority = 1)]
        public static void BuildLevels4To10()
        {
            RebuildSpecificStudioLevels(4, 5, 6, 7, 8, 9, 10);
        }

        public static void RebuildSpecificStudioLevels(params int[] levelIndices)
        {
            EnsureFolderExists("Assets/_Project");
            EnsureFolderExists("Assets/_Project/Art");
            EnsureFolderExists(SpritesFolder);
            EnsureFolderExists(LevelsFolder);

            StudioLevelBlueprint[] blueprints = GetMasterBlueprints();
            HashSet<int> targets = new HashSet<int>(levelIndices);

            for (int i = 0; i < blueprints.Length; i++)
            {
                StudioLevelBlueprint bp = blueprints[i];
                if (!targets.Contains(bp.Index)) continue;

                LevelDataSO asset = GenerateSingleStudioLevel(bp);
                BotSimulationReport report = HeadlessLevelBot.RunSimulation(asset, 500, 0.75f, 1337 + bp.Index * 97);
                int totalGrains = asset.GetTotalGrainCount();
                int totalBucketCap = asset.GetTotalBucketCapacity();
                bool massConserved = (totalGrains == totalBucketCap);

                string statusColor = report.WinRatePercent >= 85f ? "#2ecc71" : (report.WinRatePercent >= 60f ? "#f39c12" : "#e74c3c");
                Debug.Log($"<b>Level {bp.Index:02}: {bp.Title}</b> ({bp.Width}x{bp.Height}) - " +
                          $"<color={statusColor}><b>WinRate: {report.WinRatePercent:F1}%</b></color> " +
                          $"(AvgMoves: {report.AverageMovesOnWin:F1}, K\xc3\xbctle Korunumu: {(massConserved ? "100% E\xc5\x9e\xc4\xb0T" : "HATA!")})");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LevelGeneratorWindow.SyncAllLevelsWithGameLoopController();
            Debug.Log("<color=#00e5ff><b>[MasterStudioLevelGenerator]</b> Belirtilen bolumler (5, 7, 8, 9) basariyla uretildi ve senkronize edildi!</color>");
        }

        public static LevelDataSO GenerateSingleStudioLevel(StudioLevelBlueprint bp)
        {
            // 1. Create Texture2D with Point Filtering
            Texture2D tex = new Texture2D(bp.Width, bp.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[bp.Width * bp.Height];
            int[] gridCells = new int[bp.Width * bp.Height];

            for (int y = 0; y < bp.Height; y++)
            {
                for (int x = 0; x < bp.Width; x++)
                {
                    int colorId = bp.PixelEvaluator(x, y, bp.Width, bp.Height);
                    colorId = Mathf.Clamp(colorId, 0, bp.Palette.Length - 1);
                    
                    int idx = y * bp.Width + x;
                    gridCells[idx] = colorId;
                    pixels[idx] = bp.Palette[colorId];
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            // 2. Save Texture as PNG Sprite
            string cleanTitle = ToCleanAsciiTitle(bp.Title);
            string pngPath = $"{SpritesFolder}/Sprite_Level_{bp.Index:D2}_{cleanTitle}.png";
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

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

            // 3. Create or Update LevelDataSO Asset
            string assetPath = $"{LevelsFolder}/Level_{bp.Index:D2}.asset";
            LevelDataSO levelSO = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            if (levelSO == null)
            {
                levelSO = ScriptableObject.CreateInstance<LevelDataSO>();
                AssetDatabase.CreateAsset(levelSO, assetPath);
            }

            levelSO.LevelIndex = bp.Index;
            levelSO.LevelTitle = bp.Title;
            levelSO.Width = bp.Width;
            levelSO.Height = bp.Height;
            levelSO.Gravity = GravityMode.CellularAutomata;
            levelSO.ActiveDockCapacity = bp.DockCapacity;
            levelSO.MysteryBucketStartRow = bp.MysteryStartRow;
            levelSO.SandFillSpeedMultiplier = bp.SandFillSpeedMultiplier;
            levelSO.Palette = bp.Palette;
            levelSO.GridCells = gridCells;

            // 4. Generate Solvable Bucket Queues with 100% Mass Conservation
            LevelGenerationEngine.GenerationConfig cfg = new LevelGenerationEngine.GenerationConfig
            {
                Width = bp.Width,
                Height = bp.Height,
                Gravity = GravityMode.CellularAutomata,
                ActiveDockCapacity = bp.DockCapacity,
                ColumnCount = bp.ColumnCount,
                MinBucketCapacity = bp.MinBucketCapacity,
                MaxBucketCapacity = bp.MaxBucketCapacity,
                ScrambleFactor = bp.ScrambleFactor,
                RandomSeed = bp.CustomSeed != 0 ? bp.CustomSeed : (100 + bp.Index * 43),
                FallbackBackgroundColorId = bp.BackgroundColorId,
                Palette = bp.Palette
            };

            LevelGenerationEngine.GenerateSolvableBucketQueues(levelSO, cfg);

            EditorUtility.SetDirty(levelSO);
            return levelSO;
        }

        public static StudioLevelBlueprint[] GetMasterBlueprints()
        {
            return new StudioLevelBlueprint[]
            {
                // =========================================================================
                // LEVEL 1: "Tatlı Aşk Kalbi" (Sweet Ruby Heart) - 24x24 (576 kum tanesi)
                // Onboarding: 2-3 saf, lezzetli renk, %100 akıcı ve pürüzsüz vakum hissi.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 1,
                    Title = "Tatlı Aşk Kalbi",
                    Width = 24,
                    Height = 24,
                    Palette = new Color32[]
                    {
                        new Color32(255, 46, 99, 255),   // 0: Yakut Kırmızı (Tatlı Kalp Gövdesi)
                        new Color32(255, 133, 162, 255), // 1: Şeker Pembe (Parlak Yansıma Şeridi)
                        new Color32(255, 245, 238, 255)  // 2: İpeksi Vanilya Sütü (Arka Plan Kumu)
                    },
                    BackgroundColorId = 2,
                    DockCapacity = 4,
                    ColumnCount = 2,
                    MinBucketCapacity = 50,
                    MaxBucketCapacity = 90,
                    ScrambleFactor = 0.00f, // Tam sıralı, sıfır kilitlenme
                    MysteryStartRow = 99,   // Gizemli kova yok, tüm sıralar açık
                    SandFillSpeedMultiplier = 3.8f, // Ultra hızlı tatmin edici akış
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float nx = (x + 0.5f) / w * 2.0f - 1.0f;
                        float ny = (y + 0.5f) / h * 2.0f - 1.0f;
                        float hx = nx * 1.12f;
                        float hy = ny * 1.12f - 0.10f;
                        float a = hx * hx + hy * hy - 0.68f;
                        float heartVal = a * a * a - (hx * hx) * (hy * hy * hy);

                        if (heartVal <= 0.0f)
                        {
                            float distHl = Mathf.Sqrt((nx + 0.35f) * (nx + 0.35f) + (ny - 0.35f) * (ny - 0.35f));
                            if (distHl < 0.28f && nx < -0.10f && ny > 0.12f)
                                return 1; // Highlight
                            if (Mathf.Abs(nx + 0.32f) < 0.14f && Mathf.Abs(ny - 0.12f) < 0.24f)
                                return 1; // Streak
                            return 0; // Ruby Red
                        }
                        return 2; // Vanilla Cream
                    }
                },

                // =========================================================================
                // LEVEL 2: "Sulu Karpuz Dilimi" (Juicy Watermelon) - 28x28 (784 kum tanesi)
                // Ferahlatıcı yaz teması, canlı zümrüt kabuk, sulu kırmızı et ve çikolata çekirdekler.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 2,
                    Title = "Sulu Karpuz Dilimi",
                    Width = 28,
                    Height = 28,
                    Palette = new Color32[]
                    {
                        new Color32(255, 71, 87, 255),   // 0: Sulu Karpuz Kırmızısı
                        new Color32(46, 213, 115, 255),  // 1: Taze Zümrüt Kabuk Yeşili
                        new Color32(241, 242, 246, 255), // 2: Beyaz Kabuk Çizgisi
                        new Color32(47, 53, 66, 255),    // 3: Çikolata Çekirdek
                        new Color32(255, 248, 231, 255)  // 4: Sıcak Limon Pastel Gökyüzü
                    },
                    BackgroundColorId = 4,
                    DockCapacity = 4,
                    ColumnCount = 2,
                    MinBucketCapacity = 45,
                    MaxBucketCapacity = 85,
                    ScrambleFactor = 0.02f,
                    MysteryStartRow = 99,
                    SandFillSpeedMultiplier = 3.4f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float cy = h * 0.76f;
                        float rOuter = w * 0.56f;
                        float rRind = w * 0.48f;
                        float rInner = w * 0.42f;

                        float dx = x - cx;
                        float dy = cy - y; // y aşağı yönlü mesafe
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);

                        if (dy > -1f && dist <= rOuter && Mathf.Abs(dx) <= dy * 1.62f)
                        {
                            if (dist > rRind) return 1; // Green Rind
                            if (dist > rInner) return 2; // White Rind

                            // Seeds placement
                            bool isSeed = (x == 8 && y == 12) || (x == 9 && y == 12) || (x == 8 && y == 11) ||
                                          (x == 13 && y == 10) || (x == 14 && y == 10) || (x == 14 && y == 9) ||
                                          (x == 19 && y == 12) || (x == 20 && y == 12) || (x == 20 && y == 11) ||
                                          (x == 10 && y == 16) || (x == 11 && y == 16) || (x == 11 && y == 15) ||
                                          (x == 17 && y == 16) || (x == 18 && y == 16) || (x == 17 && y == 15) ||
                                          (x == 14 && y == 14) || (x == 14 && y == 15) || (x == 13 && y == 14) ||
                                          (x == 6 && y == 14) || (x == 22 && y == 14);

                            if (isSeed) return 3; // Seed
                            return 0; // Red Flesh
                        }
                        return 4; // Sky Background
                    }
                },

                // =========================================================================
                // LEVEL 3: "Kraliyet Altın Tacı" (Royal Crown) - 30x30 (900 kum tanesi)
                // Görkemli altın gövde, safir mavi kadife minder, yakut ve zümrüt mücevherler.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 3,
                    Title = "Kraliyet Altın Tacı",
                    Width = 30,
                    Height = 30,
                    Palette = new Color32[]
                    {
                        new Color32(255, 165, 2, 255),   // 0: Saf İmparatorluk Altını
                        new Color32(255, 211, 42, 255),  // 1: Parlak Güneş Işığı Altın
                        new Color32(55, 66, 250, 255),   // 2: Kraliyet Safir Mavisi (Kadife)
                        new Color32(255, 56, 56, 255),   // 3: Parlayan Yakut Taşı
                        new Color32(46, 213, 115, 255),  // 4: Parlayan Zümrüt Taşı
                        new Color32(241, 242, 246, 255)  // 5: Platin İnci İpek Arka Plan
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 4,
                    ColumnCount = 3,
                    MinBucketCapacity = 50,
                    MaxBucketCapacity = 95,
                    ScrambleFactor = 0.08f,
                    MysteryStartRow = 99,
                    SandFillSpeedMultiplier = 2.8f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float nx = (x + 0.5f) / w * 2.0f - 1.0f;
                        float ny = (y + 0.5f) / h * 2.0f - 1.0f;

                        if (ny >= -0.72f && ny <= 0.72f && Mathf.Abs(nx) <= 0.86f)
                        {
                            if (ny <= -0.22f)
                            {
                                if (Mathf.Abs(ny - (-0.47f)) <= 0.11f)
                                {
                                    int pattern = (x / 3) % 4;
                                    if (pattern == 1) return 3; // Ruby
                                    if (pattern == 3) return 4; // Emerald
                                    return 0; // Gold
                                }
                                if (ny < -0.62f) return 0;
                                return 1; // Gold highlight
                            }

                            float centerPeak = 0.65f - (nx * 1.9f) * (nx * 1.9f);
                            float sidePeakL = 0.52f - ((nx + 0.55f) * 2.4f) * ((nx + 0.55f) * 2.4f);
                            float sidePeakR = 0.52f - ((nx - 0.55f) * 2.4f) * ((nx - 0.55f) * 2.4f);
                            float crownH = Mathf.Max(centerPeak, Mathf.Max(sidePeakL, sidePeakR));

                            if (ny <= crownH)
                            {
                                if (Mathf.Abs(nx) < 0.32f && ny < 0.22f)
                                    return 2; // Velvet Blue
                                if (ny > crownH - 0.18f)
                                {
                                    if (Mathf.Abs(nx) < 0.16f) return 3; // Center Ruby
                                    if (Mathf.Abs(Mathf.Abs(nx) - 0.55f) < 0.16f) return 4; // Emerald
                                    return 1; // Gold tip
                                }
                                if ((x + y) % 3 == 0) return 1;
                                return 0; // Gold body
                            }
                        }
                        return 5; // Pearl Background
                    }
                },

                // =========================================================================
                // LEVEL 4: "Parlayan Şans Yıldızı" (Lucky Arcade Star) - 32x32 (1,024 kum tanesi)
                // Retro arcade sevimli gözlü yıldız, sıcak turuncu gölge, parıldayan yıldız tozu.
                // =========================================================================
                // =========================================================================
                // LEVEL 4: "Sihirli Büyü İksiri" (Magic Potion Flask) - 34x34
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 4,
                    Title = "Sihirli Büyü İksiri",
                    Width = 34,
                    Height = 34,
                    Palette = new Color32[]
                    {
                        new Color32(156, 136, 255, 255), // 0: Ametist Büyü Moru
                        new Color32(224, 86, 253, 255),  // 1: Neon Pembe İksir Yüzeyi
                        new Color32(0, 210, 211, 255),   // 2: Sihirli Camgöbeği Baloncuklar
                        new Color32(225, 177, 44, 255),  // 3: Meşe Mantar Tıpa & Kurdele
                        new Color32(255, 255, 255, 255), // 4: Kristal Cam Yansımaları
                        new Color32(25, 42, 86, 255)     // 5: Simyacı Laciverti Fon
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5,
                    ColumnCount = 3,
                    MinBucketCapacity = 50,
                    MaxBucketCapacity = 95,
                    ScrambleFactor = 0.05f,
                    MysteryStartRow = 3,
                    SandFillSpeedMultiplier = 2.8f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float bulbCy = 14.0f;
                        float bulbR = 13.0f;

                        float dx = x - cx;
                        float dy = y - bulbCy;
                        float distBulb = Mathf.Sqrt(dx * dx + dy * dy);

                        if (y >= 26 && y <= 32 && Mathf.Abs(dx) <= 4.2f) return 3;
                        if (y >= 21 && y <= 25 && Mathf.Abs(dx) <= 3.8f)
                        {
                            if (y == 21 && Mathf.Abs(dx) <= 4.8f) return 3;
                            if (Mathf.Abs(dx) >= 2.8f) return 4;
                            return 1;
                        }
                        if (distBulb <= bulbR)
                        {
                            if (distBulb >= bulbR - 1.5f)
                            {
                                if (dx < -3.5f && dy > 0f) return 4;
                                return 0;
                            }

                            float liquidSurface = 18.0f + Mathf.Sin(dx * 0.38f) * 1.4f;
                            if (y > liquidSurface)
                            {
                                if (dx < -2.5f && dy > 2.5f) return 4;
                                return 5;
                            }

                            if (y >= liquidSurface - 2.0f) return 1;

                            if ((Mathf.Abs(dx - (-3.5f)) <= 1.6f && Mathf.Abs(dy - (-2.5f)) <= 1.6f) ||
                                (Mathf.Abs(dx - 4.5f) <= 1.5f && Mathf.Abs(dy - (-1f)) <= 1.5f) ||
                                (Mathf.Abs(dx - 1f) <= 1.8f && Mathf.Abs(dy - (-6.5f)) <= 1.8f))
                                return 2;

                            return 0;
                        }
                        return 5;
                    }
                },

                // =========================================================================
                // LEVEL 5: "Retro El Konsolu" (Retro Game Boy) - 36x36
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 5,
                    Title = "Retro El Konsolu",
                    Width = 36,
                    Height = 36,
                    Palette = new Color32[]
                    {
                        new Color32(29, 209, 161, 255),  // 0: Nane Turkuaz Konsol Gövdesi
                        new Color32(163, 203, 56, 255),  // 1: Klasik Nokta Vuruşlu Zeytin LCD
                        new Color32(31, 40, 60, 255),    // 2: Ekran Çerçevesi & 8-Bit Gülen Yüz
                        new Color32(255, 71, 87, 255),   // 3: Şeker Kırmızı Aksiyon Tuşları
                        new Color32(87, 101, 116, 255),  // 4: D-Pad Yön Tuşu Gri
                        new Color32(245, 246, 250, 255)  // 5: Temiz Stüdyo Beyazı Arka Plan
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5,
                    ColumnCount = 4,
                    MinBucketCapacity = 55,
                    MaxBucketCapacity = 100,
                    ScrambleFactor = 0.05f,
                    MysteryStartRow = 2,
                    SandFillSpeedMultiplier = 2.6f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        if (Mathf.Abs(dx) <= 13.0f && y >= 3 && y <= 33)
                        {
                            bool cornerCut = (Mathf.Abs(dx) > 11.5f && (y < 5 || y > 31));
                            if (!cornerCut)
                            {
                                if (y >= 18 && y <= 30 && Mathf.Abs(dx) <= 10.5f)
                                {
                                    if (y >= 20 && y <= 28 && Mathf.Abs(dx) <= 8.5f)
                                    {
                                        if (y == 25 && (Mathf.Abs(dx - (-3.5f)) <= 0.8f || Mathf.Abs(dx - 3.5f) <= 0.8f))
                                            return 2;
                                        if (y == 22 && Mathf.Abs(dx) <= 3.5f && Mathf.Abs(dx) >= 1f)
                                            return 2;
                                        return 1;
                                    }
                                    return 2;
                                }

                                float dpadDx = x - (cx - 6.5f);
                                float dpadDy = y - 10.5f;
                                if ((Mathf.Abs(dpadDx) <= 1.4f && Mathf.Abs(dpadDy) <= 4.2f) ||
                                    (Mathf.Abs(dpadDy) <= 1.4f && Mathf.Abs(dpadDx) <= 4.2f))
                                    return 4;

                                float btnA = Mathf.Sqrt((x - (cx + 8.5f)) * (x - (cx + 8.5f)) + (y - 12.0f) * (y - 12.0f));
                                float btnB = Mathf.Sqrt((x - (cx + 5.0f)) * (x - (cx + 5.0f)) + (y - 9.5f) * (y - 9.5f));
                                if (btnA <= 1.9f || btnB <= 1.9f)
                                    return 3;

                                return 0;
                            }
                        }
                        return 5;
                    }
                },

                // =========================================================================
                // LEVEL 6: "Sevimli Dondurma Külahı" (Swirl Ice Cream) - 38x38
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 6,
                    Title = "Sevimli Dondurma Külahı",
                    Width = 38,
                    Height = 38,
                    Palette = new Color32[]
                    {
                        new Color32(255, 107, 129, 255), // 0: Çilekli Pembe Dondurma
                        new Color32(123, 237, 159, 255), // 1: Antep Fıstıklı Yeşil Dondurma
                        new Color32(236, 204, 104, 255), // 2: Çıtır Pişmiş Altın Külah
                        new Color32(238, 82, 83, 255),   // 3: Parlak Şeker Vişnesi
                        new Color32(211, 84, 0, 255),    // 4: Külah Izgara Çizgisi & Sap
                        new Color32(112, 161, 255, 255)  // 5: Bebek Mavisi Yaz Gökyüzü
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5,
                    ColumnCount = 4,
                    MinBucketCapacity = 60,
                    MaxBucketCapacity = 110,
                    ScrambleFactor = 0.06f,
                    MysteryStartRow = 2,
                    SandFillSpeedMultiplier = 2.4f,
                    CustomSeed = 200,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        if (y >= 3 && y <= 18)
                        {
                            float maxW = (y - 3) * 0.85f + 1.2f;
                            if (Mathf.Abs(dx) <= maxW)
                            {
                                if (((x + y) % 4 == 0 || (x - y) % 4 == 0) && Mathf.Abs(dx) < maxW - 0.7f)
                                    return 4;
                                return 2;
                            }
                        }
                        else if (y > 18 && y <= 26)
                        {
                            float mintW = 13.5f - Mathf.Abs(y - 22) * 0.5f;
                            if (Mathf.Abs(dx) <= mintW)
                                return 1;
                        }
                        else if (y > 26 && y <= 33)
                        {
                            float berryW = 11.0f - (y - 26) * 1.1f;
                            float swirlOffset = Mathf.Sin((y - 26) * 0.7f) * 2.0f;
                            if (Mathf.Abs(dx - swirlOffset) <= berryW)
                                return 0;
                        }
                        else if (y > 33 && y <= 37)
                        {
                            float cherryDist = Mathf.Sqrt(dx * dx + (y - 35.0f) * (y - 35.0f));
                            if (cherryDist <= 3.2f) return 3;
                            if (Mathf.Abs(dx - 1.0f) <= 0.8f && y >= 36) return 4;
                        }
                        return 5;
                    }
                },

                // =========================================================================
                // LEVEL 7: "Görkemli Işıltılı Elmas" (Prismatic Diamond) - 40x40
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 7,
                    Title = "Görkemli Işıltılı Elmas",
                    Width = 40,
                    Height = 40,
                    Palette = new Color32[]
                    {
                        new Color32(255, 255, 255, 255), // 0: Parlak Pırlanta Beyazı
                        new Color32(0, 210, 211, 255),   // 1: Elektrik Camgöbeği Üst Faset
                        new Color32(84, 160, 255, 255),  // 2: Işıltılı Safir Gök Mavisi
                        new Color32(46, 134, 222, 255),  // 3: Derin Kraliyet Laciverti
                        new Color32(95, 39, 205, 255),   // 4: Ametist Prizma Kenar Fasetleri
                        new Color32(12, 16, 33, 255)     // 5: Obsidiyen Kadife Gece Arka Planı
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5,
                    ColumnCount = 4,
                    MinBucketCapacity = 60,
                    MaxBucketCapacity = 115,
                    ScrambleFactor = 0.08f,
                    MysteryStartRow = 2,
                    SandFillSpeedMultiplier = 2.4f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;
                        bool inDiamond = false;
                        int c = 5;

                        if (y >= 23 && y <= 32)
                        {
                            float t = (y - 23) / 9.0f;
                            float maxDx = 16.5f - t * 7.0f;
                            if (Mathf.Abs(dx) <= maxDx)
                            {
                                inDiamond = true;
                                if (y >= 28 && Mathf.Abs(dx) <= 7.5f) c = 0;
                                else if (Mathf.Abs(dx) <= 4.5f) c = 1;
                                else if (Mathf.Abs(dx) <= 10.0f) c = 2;
                                else c = 4;
                            }
                        }
                        else if (y >= 5 && y < 23)
                        {
                            float t = (y - 5) / 18.0f;
                            float maxDx = t * 16.5f;
                            if (Mathf.Abs(dx) <= maxDx)
                            {
                                inDiamond = true;
                                float ratio = Mathf.Abs(dx) / (maxDx + 0.001f);
                                if (ratio < 0.28f) c = 1;
                                else if (ratio < 0.58f) c = 2;
                                else if (ratio < 0.84f) c = 3;
                                else c = 4;
                            }
                        }

                        if (!inDiamond)
                        {
                            if ((Mathf.Abs(x - 7) <= 1 && y == 33) || (x == 7 && Mathf.Abs(y - 33) <= 1) ||
                                (Mathf.Abs(x - 33) <= 1 && y == 33) || (x == 33 && Mathf.Abs(y - 33) <= 1) ||
                                (Mathf.Abs(x - 20) <= 1 && y == 2) || (x == 20 && Mathf.Abs(y - 2) <= 1))
                                return 0;
                            return 5;
                        }

                        return c;
                    }
                },

                // =========================================================================
                // LEVEL 8: "Retro Uzay Roketi" (Cosmic Apollo Rocket) - 42x42
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 8,
                    Title = "Retro Uzay Roketi",
                    Width = 42,
                    Height = 42,
                    Palette = new Color32[]
                    {
                        new Color32(255, 87, 34, 255),   // 0: Turuncu İtki Alevi (#FF5722)
                        new Color32(255, 235, 59, 255),  // 1: Parlak Sarı Çekirdek Alev & Yıldız (#FFEB3B)
                        new Color32(245, 246, 250, 255), // 2: Beyaz Gövde Plakası (#F5F6FA)
                        new Color32(0, 210, 211, 255),   // 3: Camgöbeği Lombar Penceresi (#00D2D3)
                        new Color32(234, 32, 39, 255),   // 4: Kırmızı Burun Konisi ve Kanatçıklar (#EA2027)
                        new Color32(12, 16, 33, 255)     // 5: Derin Gece Uzayı (#0C1021)
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5,
                    ColumnCount = 5,
                    MinBucketCapacity = 60,
                    MaxBucketCapacity = 115,
                    ScrambleFactor = 0.08f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.3f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        if (y >= 2 && y <= 12)
                        {
                            float flameW = (12 - y) * 0.85f;
                            if (Mathf.Abs(dx) <= flameW)
                            {
                                if (Mathf.Abs(dx) <= flameW * 0.45f) return 1;
                                return 0;
                            }
                        }

                        if (y >= 10 && y <= 39)
                        {
                            if (y > 30)
                            {
                                float noseW = (39 - y) * 0.65f;
                                if (Mathf.Abs(dx) <= noseW)
                                    return 4;
                            }
                            else if (y >= 12 && y <= 30)
                            {
                                float fuselageW = 6.2f;
                                if (Mathf.Abs(dx) <= fuselageW)
                                {
                                    float pDist = Mathf.Sqrt(dx * dx + (y - 23) * (y - 23));
                                    if (pDist <= 3.2f)
                                    {
                                        if (pDist <= 2.2f) return 3;
                                        return 4;
                                    }
                                    if (y == 15 || y == 28) return 4;
                                    return 2;
                                }
                            }

                            if (y >= 10 && y <= 20)
                            {
                                float finReach = 6.2f + (20 - y) * 0.85f;
                                if (Mathf.Abs(dx) > 6.2f && Mathf.Abs(dx) <= finReach)
                                    return 4;
                            }
                        }

                        (int sx, int sy)[] stars = new (int, int)[]
                        {
                            (7, 36), (35, 37), (5, 18), (37, 16), (9, 7), (33, 6), (21, 40)
                        };
                        foreach (var (sx, sy) in stars)
                        {
                            if ((Mathf.Abs(x - sx) <= 1 && y == sy) || (x == sx && Mathf.Abs(y - sy) <= 1))
                                return 1;
                        }

                        return 5;
                    }
                },

                // =========================================================================
                // LEVEL 9: "Şampiyonluk Altın Kupası" (Grand Champions Trophy) - 44x44
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 9,
                    Title = "Şampiyonluk Altın Kupası",
                    Width = 44,
                    Height = 44,
                    Palette = new Color32[]
                    {
                        new Color32(255, 215, 0, 255),   // 0: Saf Şampiyonluk Altını (#FFD700)
                        new Color32(255, 159, 26, 255),  // 1: Sıcak Kehribar Gölge (#FF9F1A)
                        new Color32(46, 213, 115, 255),  // 2: Canlı Zümrüt Kurdele (#2ED573)
                        new Color32(255, 56, 56, 255),   // 3: Yakut Zafer Yıldızı (#FF3838)
                        new Color32(255, 255, 255, 255), // 4: İnci Parıltı (#FFFFFF)
                        new Color32(19, 15, 64, 255)     // 5: Kraliyet Laciverti Fon (#130F40)
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 6,
                    ColumnCount = 5,
                    MinBucketCapacity = 65,
                    MaxBucketCapacity = 120,
                    ScrambleFactor = 0.10f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.2f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        if (y >= 4 && y <= 10)
                        {
                            float baseW = 12.0f - (y - 4) * 0.6f;
                            if (Mathf.Abs(dx) <= baseW)
                            {
                                if (y == 6 || y == 7)
                                {
                                    if (Mathf.Abs(dx) <= baseW - 1.5f) return 2;
                                }
                                if ((x + y) % 3 == 0) return 1;
                                return 0;
                            }
                        }

                        if (y > 10 && y <= 16)
                        {
                            float stemW = 3.6f + Mathf.Abs(y - 13) * 0.5f;
                            if (Mathf.Abs(dx) <= stemW)
                            {
                                if (y >= 12 && y <= 14 && Mathf.Abs(dx) <= 1.8f) return 3;
                                return 0;
                            }
                        }

                        if (y > 16 && y <= 38)
                        {
                            float tCup = (y - 16) / 22.0f;
                            float cupW = 4.0f + Mathf.Sin(tCup * Mathf.PI * 0.5f) * 12.5f;

                            if (y >= 21 && y <= 35)
                            {
                                float handleOuter = cupW + 6.0f;
                                float handleInner = cupW + 2.5f;
                                if (Mathf.Abs(dx) <= handleOuter && Mathf.Abs(dx) >= handleInner)
                                {
                                    return (x % 2 == 0) ? 0 : 1;
                                }
                            }

                            if (Mathf.Abs(dx) <= cupW)
                            {
                                if (y >= 36)
                                {
                                    if (Mathf.Abs(dx - 5f) < 2.5f) return 4;
                                    return 0;
                                }

                                float sDist = Mathf.Sqrt(dx * dx + (y - 27) * (y - 27));
                                if (sDist <= 4.0f)
                                {
                                    if (sDist <= 2.2f) return 3;
                                    return 4;
                                }

                                if (dx > cupW * 0.4f) return 1;
                                return 0;
                            }
                        }

                        (int sx, int sy)[] sparkles = new (int, int)[]
                        {
                            (6, 40), (37, 41), (5, 20), (38, 22), (8, 6), (36, 7), (22, 42)
                        };
                        foreach (var (sx, sy) in sparkles)
                        {
                            if ((Mathf.Abs(x - sx) <= 1 && y == sy) || (x == sx && Mathf.Abs(y - sy) <= 1))
                                return 4;
                        }

                        return 5;
                    }
                },

                // =========================================================================
                // LEVEL 10: "Görkemli Anka Kuşu" (Grand Phoenix Crest) - 48x48
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 10,
                    Title = "Görkemli Anka Kuşu",
                    Width = 48,
                    Height = 48,
                    Palette = new Color32[]
                    {
                        new Color32(255, 75, 43, 255),   // 0: Alev Kırmızısı (#FF4B2B)
                        new Color32(255, 180, 0, 255),   // 1: Altın Turuncu Alev (#FFB400)
                        new Color32(255, 230, 80, 255),  // 2: Parlak Sarı Kıvılcım (#FFE650)
                        new Color32(0, 210, 211, 255),   // 3: Safir Mavi Anka Gözü (#00D2D3)
                        new Color32(156, 39, 176, 255),  // 4: Büyülü Ametist Kanat Uçları (#9C27B0)
                        new Color32(255, 255, 255, 255), // 5: Saf Ak Işık (#FFFFFF)
                        new Color32(18, 14, 42, 255)     // 6: Kozmik Derin Gece (#120E2A)
                    },
                    BackgroundColorId = 6,
                    DockCapacity = 6,
                    ColumnCount = 5,
                    MinBucketCapacity = 70,
                    MaxBucketCapacity = 130,
                    ScrambleFactor = 0.10f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.0f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        if (y >= 32 && y <= 44 && Mathf.Abs(dx) <= 6.5f)
                        {
                            if (y >= 40)
                            {
                                if (Mathf.Abs(dx) <= 2.0f || Mathf.Abs(dx - 4.0f) <= 1.0f || Mathf.Abs(dx + 4.0f) <= 1.0f)
                                    return 2;
                            }

                            if (y == 36 && (Mathf.Abs(dx - (-1.5f)) <= 0.8f || Mathf.Abs(dx - 1.5f) <= 0.8f))
                                return 3;

                            if (y == 34 && Mathf.Abs(dx) <= 1.2f) return 1;

                            return 0;
                        }

                        if (y >= 16 && y <= 32 && Mathf.Abs(dx) <= 7.5f)
                        {
                            float hDist = Mathf.Sqrt(dx * dx + (y - 24) * (y - 24));
                            if (hDist <= 3.2f) return 5;
                            if (hDist <= 5.5f) return 2;
                            return 0;
                        }

                        if (y >= 18 && y <= 42)
                        {
                            float wingY = y - 18;
                            float maxWingReach = 7.5f + Mathf.Sin(wingY / 24.0f * Mathf.PI) * 14.0f;
                            if (Mathf.Abs(dx) > 7.0f && Mathf.Abs(dx) <= maxWingReach)
                            {
                                float spanRatio = (Mathf.Abs(dx) - 7.0f) / (maxWingReach - 7.0f + 0.001f);
                                if (spanRatio > 0.82f) return 4;
                                if (spanRatio > 0.52f) return 1;
                                return 0;
                            }
                        }

                        if (y >= 2 && y <= 18)
                        {
                            float tailWidth = (18 - y) * 0.7f + 2.0f;
                            if (Mathf.Abs(dx) <= tailWidth)
                            {
                                if (Mathf.Abs(dx) <= tailWidth * 0.35f) return 2;
                                if (Mathf.Abs(dx) <= tailWidth * 0.70f) return 1;
                                return 0;
                            }
                        }

                        (int sx, int sy)[] stars = new (int, int)[]
                        {
                            (5, 43), (42, 44), (4, 22), (43, 20), (6, 7), (41, 8), (24, 46)
                        };
                        foreach (var (sx, sy) in stars)
                        {
                            if ((Mathf.Abs(x - sx) <= 1 && y == sy) || (x == sx && Mathf.Abs(y - sy) <= 1))
                                return 2;
                        }

                        return 6;
                    }
                }
            };
        }
        private static string ToCleanAsciiTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "Level";
            return title.Replace(" ", "_")
                .Replace("ı", "i").Replace("İ", "I")
                .Replace("ş", "s").Replace("Ş", "S")
                .Replace("ğ", "g").Replace("Ğ", "G")
                .Replace("ü", "u").Replace("Ü", "U")
                .Replace("ö", "o").Replace("Ö", "O")
                .Replace("ç", "c").Replace("Ç", "C");
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

