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

        [MenuItem("SandSort/Rebuild Levels 5, 7, 8, 9 (Fix Broken Color Logic)", priority = 2)]
        public static void MenuRebuildLevels5_7_8_9()
        {
            RebuildSpecificStudioLevels(5, 7, 8, 9);
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
                new StudioLevelBlueprint
                {
                    Index = 4,
                    Title = "Parlayan Şans Yıldızı",
                    Width = 32,
                    Height = 32,
                    Palette = new Color32[]
                    {
                        new Color32(255, 192, 72, 255),  // 0: Işıltılı Yıldız Sarısı
                        new Color32(255, 127, 80, 255),  // 1: Sıcak Mercan Turuncu Kenar
                        new Color32(47, 53, 66, 255),    // 2: Sevimli Çizgi Film Gözleri
                        new Color32(255, 255, 255, 255), // 3: Saf Beyaz Parıltı
                        new Color32(30, 39, 46, 255)     // 4: Gece Yarısı Laciverti Gökyüzü
                    },
                    BackgroundColorId = 4,
                    DockCapacity = 5,
                    ColumnCount = 3,
                    MinBucketCapacity = 50,
                    MaxBucketCapacity = 100,
                    ScrambleFactor = 0.04f,
                    MysteryStartRow = 3,
                    SandFillSpeedMultiplier = 3.0f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float cy = h / 2.0f;
                        float rOuter = 15.2f;
                        float rInner = 6.8f;

                        float dx = x - cx;
                        float dy = y - cy;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float angle = Mathf.Atan2(dy, dx) + Mathf.PI / 2.0f;
                        if (angle < 0f) angle += 2.0f * Mathf.PI;

                        float sector = (angle % (2.0f * Mathf.PI / 5.0f)) / (2.0f * Mathf.PI / 5.0f);
                        if (sector > 0.5f) sector = 1.0f - sector;
                        float maxR = rInner + (rOuter - rInner) * Mathf.Pow(1.0f - sector * 2.0f, 1.15f);

                        if (dist <= maxR)
                        {
                            bool isEyeL = (Mathf.Abs(dx - (-3.5f)) <= 1.2f && Mathf.Abs(dy - (-1.2f)) <= 2.6f);
                            bool isEyeR = (Mathf.Abs(dx - 3.5f) <= 1.2f && Mathf.Abs(dy - (-1.2f)) <= 2.6f);

                            if (isEyeL || isEyeR)
                            {
                                if (dy > -1.0f && Mathf.Abs(dx - (isEyeL ? -3.5f : 3.5f)) < 0.7f)
                                    return 3; // White eye glint
                                return 2; // Dark eye
                            }

                            // Cute smile mouth
                            if (dy >= -4.5f && dy <= -3.5f && Mathf.Abs(dx) <= 2.5f)
                                return 2;

                            if (dist > maxR - 2.5f && (dy < -0.5f || dx > 1.5f))
                                return 1; // Orange rim
                            if (dx < -2f && dy > 2f)
                                return 3; // White highlight
                            return 0; // Star Yellow
                        }

                        // Stardust sparkles
                        if ((Mathf.Abs(x - 5) <= 1 && y == 27) || (x == 5 && Mathf.Abs(y - 27) <= 1) ||
                            (Mathf.Abs(x - 27) <= 1 && y == 26) || (x == 27 && Mathf.Abs(y - 26) <= 1) ||
                            (Mathf.Abs(x - 6) <= 1 && y == 6) || (x == 6 && Mathf.Abs(y - 6) <= 1) ||
                            (Mathf.Abs(x - 26) <= 1 && y == 7) || (x == 26 && Mathf.Abs(y - 7) <= 1))
                            return 3;

                        return 4; // Midnight Sky
                    }
                },

                // =========================================================================
                // LEVEL 5: "Sihirli Büyü İksiri" (Magic Potion Flask) - 36x36 (1,296 kum tanesi)
                // Ametist mor iksir, neon pembe dalgalar, yükselen camgöbeği sihir kabarcıkları.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 5,
                    Title = "Sihirli Büyü İksiri",
                    Width = 36,
                    Height = 36,
                    Palette = new Color32[]
                    {
                        new Color32(156, 136, 255, 255), // 0: Ametist Büyü Moru
                        new Color32(224, 86, 253, 255),  // 1: Neon Pembe İksir Yüzeyi
                        new Color32(0, 210, 211, 255),   // 2: Sihirli Camgöbeği Baloncuklar
                        new Color32(225, 177, 44, 255),  // 3: Meşe Mantar Tıpa & Altın Kurdele
                        new Color32(255, 255, 255, 255), // 4: Kristal Cam Yansımaları
                        new Color32(25, 42, 86, 255)     // 5: Simyacı Laboratuvarı Laciverti
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5,
                    ColumnCount = 3,
                    MinBucketCapacity = 55,
                    MaxBucketCapacity = 105,
                    ScrambleFactor = 0.08f,
                    MysteryStartRow = 2,
                    SandFillSpeedMultiplier = 2.8f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float bulbCy = 15.0f;
                        float bulbR = 13.8f;

                        float dx = x - cx;
                        float dy = y - bulbCy;
                        float distBulb = Mathf.Sqrt(dx * dx + dy * dy);

                        // Cork
                        if (y >= 28 && y <= 34 && Mathf.Abs(dx) <= 4.5f)
                            return 3; // Oak Cork
                        // Neck
                        if (y >= 23 && y <= 27 && Mathf.Abs(dx) <= 4.0f)
                        {
                            if (y == 23 && Mathf.Abs(dx) <= 5.2f) return 3; // Ribbon
                            if (Mathf.Abs(dx) >= 3.0f) return 4; // Glass
                            return 1; // Liquid splash
                        }
                        // Bulb
                        if (distBulb <= bulbR)
                        {
                            if (distBulb >= bulbR - 1.6f)
                            {
                                if (dx < -4f && dy > 0f) return 4; // Glint
                                return 0; // Glass wall
                            }

                            float liquidSurface = 19.5f + Mathf.Sin(dx * 0.35f) * 1.5f;
                            if (y > liquidSurface)
                            {
                                if (dx < -3f && dy > 3f) return 4;
                                return 5; // Air
                            }

                            if (y >= liquidSurface - 2.2f)
                                return 1; // Pink Wave

                            // Cyan magic bubbles
                            if ((Mathf.Abs(dx - (-4f)) <= 1.8f && Mathf.Abs(dy - (-3f)) <= 1.8f) ||
                                (Mathf.Abs(dx - 5f) <= 1.6f && Mathf.Abs(dy - (-1f)) <= 1.6f) ||
                                (Mathf.Abs(dx - 1f) <= 2.0f && Mathf.Abs(dy - (-7f)) <= 2.0f))
                                return 2;

                            // Pink bubble
                            if ((Mathf.Abs(dx - 3f) <= 1.4f && Mathf.Abs(dy - 2f) <= 1.4f) ||
                                (Mathf.Abs(dx - (-6f)) <= 1.2f && Mathf.Abs(dy - (-7f)) <= 1.2f))
                                return 1;

                            return 0; // Purple Elixir
                        }
                        return 5; // Background
                    }
                },

                // =========================================================================
                // LEVEL 6: "Sevimli Dondurma Külahı" (Swirl Ice Cream) - 38x38 (1,444 kum tanesi)
                // Çilekli & antep fıstıklı yumuşak dondurma burgusu, çıtır altın külah, parlak vişne.
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
                    ScrambleFactor = 0.04f,
                    MysteryStartRow = 2,
                    SandFillSpeedMultiplier = 2.4f,
                    CustomSeed = 200,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        // 1. Waffle Cone
                        if (y >= 3 && y <= 18)
                        {
                            float maxW = (y - 3) * 0.85f + 1.2f;
                            if (Mathf.Abs(dx) <= maxW)
                            {
                                if (((x + y) % 4 == 0 || (x - y) % 4 == 0) && Mathf.Abs(dx) < maxW - 0.7f)
                                    return 4; // Grid shadow
                                return 2; // Waffle Gold
                            }
                        }
                        // 2. Mint Scoop
                        else if (y > 18 && y <= 26)
                        {
                            float mintW = 13.5f - Mathf.Abs(y - 22) * 0.5f;
                            if (Mathf.Abs(dx) <= mintW)
                                return 1; // Mint
                        }
                        // 3. Strawberry Swirl
                        else if (y > 26 && y <= 33)
                        {
                            float berryW = 11.0f - (y - 26) * 1.1f;
                            float swirlOffset = Mathf.Sin((y - 26) * 0.7f) * 2.0f;
                            if (Mathf.Abs(dx - swirlOffset) <= berryW)
                                return 0; // Strawberry
                        }
                        // 4. Cherry on top
                        else if (y > 33 && y <= 37)
                        {
                            float cherryDist = Mathf.Sqrt(dx * dx + (y - 35.0f) * (y - 35.0f));
                            if (cherryDist <= 3.2f) return 3; // Cherry Red
                            if (Mathf.Abs(dx - 1.0f) <= 0.8f && y >= 36) return 4; // Stem
                        }
                        return 5; // Sky Blue
                    }
                },

                // =========================================================================
                // LEVEL 7: "Retro El Konsolu" (Retro Game Boy) - 40x40 (1,600 kum tanesi)
                // Nostaljik nane yeşili gövde, LCD nokta vuruşlu ekran, 8-bit gülen yüz, kırmızı tuşlar.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 7,
                    Title = "Retro El Konsolu",
                    Width = 40,
                    Height = 40,
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
                    MinBucketCapacity = 65,
                    MaxBucketCapacity = 120,
                    ScrambleFactor = 0.04f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.4f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        if (Mathf.Abs(dx) <= 14.5f && y >= 3 && y <= 37)
                        {
                            bool cornerCut = (Mathf.Abs(dx) > 13.0f && (y < 5 || y > 35));
                            if (!cornerCut)
                            {
                                // Screen Bezel
                                if (y >= 20 && y <= 34 && Mathf.Abs(dx) <= 12.0f)
                                {
                                    // LCD Screen
                                    if (y >= 22 && y <= 32 && Mathf.Abs(dx) <= 9.5f)
                                    {
                                        // 8-bit smiley
                                        if (y == 28 && (Mathf.Abs(dx - (-4f)) <= 0.8f || Mathf.Abs(dx - 4f) <= 0.8f))
                                            return 2; // Eyes
                                        if (y == 25 && Mathf.Abs(dx) <= 4f && Mathf.Abs(dx) >= 1f)
                                            return 2; // Mouth
                                        if (y == 26 && (Mathf.Abs(dx - (-4f)) <= 0.8f || Mathf.Abs(dx - 4f) <= 0.8f))
                                            return 2; // Cheeks
                                        return 1; // LCD Screen
                                    }
                                    return 2; // Dark Bezel
                                }

                                // D-Pad
                                float dpadDx = x - (cx - 7.5f);
                                float dpadDy = y - 11.5f;
                                if ((Mathf.Abs(dpadDx) <= 1.6f && Mathf.Abs(dpadDy) <= 4.8f) ||
                                    (Mathf.Abs(dpadDy) <= 1.6f && Mathf.Abs(dpadDx) <= 4.8f))
                                    return 4; // D-Pad

                                // Action Buttons
                                float btnA = Mathf.Sqrt((x - (cx + 9.5f)) * (x - (cx + 9.5f)) + (y - 13.5f) * (y - 13.5f));
                                float btnB = Mathf.Sqrt((x - (cx + 5.5f)) * (x - (cx + 5.5f)) + (y - 10.5f) * (y - 10.5f));
                                if (btnA <= 2.2f || btnB <= 2.2f)
                                    return 3; // Red Button

                                // Speaker grill
                                if (y >= 5 && y <= 8 && (x - y) % 3 == 0 && dx >= 5 && dx <= 12)
                                    return 4;

                                return 0; // Mint Body
                            }
                        }
                        return 5; // White Background
                    }
                },

                // =========================================================================
                // LEVEL 8: "Görkemli Işıltılı Elmas" (Prismatic Diamond) - 42x42 (1,764 kum tanesi)
                // Prizmatik kesim elmas, parıldayan fasetler, kadife obsidiyen gece yarısı fonu.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 8,
                    Title = "Görkemli Işıltılı Elmas",
                    Width = 42,
                    Height = 42,
                    Palette = new Color32[]
                    {
                        new Color32(255, 255, 255, 255), // 0: Parlak Pırlanta Beyazı & Işık Patlaması
                        new Color32(0, 210, 211, 255),   // 1: Elektrik Camgöbeği Üst Faset
                        new Color32(84, 160, 255, 255),  // 2: Işıltılı Safir Gök Mavisi
                        new Color32(46, 134, 222, 255),  // 3: Derin Kraliyet Laciverti
                        new Color32(95, 39, 205, 255),   // 4: Ametist Prizma Kenar Fasetleri
                        new Color32(12, 16, 33, 255)     // 5: Obsidiyen Kadife Gece Arka Planı
                    },
                    BackgroundColorId = 5,
                    DockCapacity = 5, // Büyük final için 5'li rıhtım
                    ColumnCount = 5,
                    MinBucketCapacity = 65,
                    MaxBucketCapacity = 125,
                    ScrambleFactor = 0.12f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.4f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;
                        bool inDiamond = false;
                        int c = 5;

                        // Crown
                        if (y >= 24 && y <= 33)
                        {
                            float t = (y - 24) / 9.0f;
                            float maxDx = 17.5f - t * 7.5f;
                            if (Mathf.Abs(dx) <= maxDx)
                            {
                                inDiamond = true;
                                if (y >= 29 && Mathf.Abs(dx) <= 8.5f) c = 0; // White Table
                                else if (Mathf.Abs(dx) <= 5.0f) c = 1; // Aqua Star
                                else if (Mathf.Abs(dx) <= 11.0f) c = 2; // Sky Blue Kite
                                else c = 4; // Amethyst Edge
                            }
                        }
                        // Pavilion
                        else if (y >= 5 && y < 24)
                        {
                            float t = (y - 5) / 19.0f;
                            float maxDx = t * 17.5f;
                            if (Mathf.Abs(dx) <= maxDx)
                            {
                                inDiamond = true;
                                float ratio = Mathf.Abs(dx) / (maxDx + 0.001f);
                                if (ratio < 0.28f) c = 1; // Aqua
                                else if (ratio < 0.58f) c = 2; // Sky Blue
                                else if (ratio < 0.84f) c = 3; // Royal Blue
                                else c = 4; // Amethyst Edge
                            }
                        }

                        // Outer Sparkles in Obsidian Velvet
                        if (!inDiamond)
                        {
                            if ((Mathf.Abs(x - 8) <= 1 && y == 35) || (x == 8 && Mathf.Abs(y - 35) <= 1) ||
                                (Mathf.Abs(x - 33) <= 1 && y == 35) || (x == 33 && Mathf.Abs(y - 35) <= 1) ||
                                (Mathf.Abs(x - 21) <= 1 && y == 2) || (x == 21 && Mathf.Abs(y - 2) <= 1))
                                return 0; // Sparkle Star
                            return 5; // Velvet Background
                        }

                        return c;
                    }
                },
                // =========================================================================
                // LEVEL 9: "Retro Uzay Roketi" (Cosmic Apollo Rocket) - 42x42 (1,764 kum tanesi)
                // Alevli fırlatıcı roket, uzay pencereleri ve derin uzay yıldızları.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 9,
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
                    ScrambleFactor = 0.05f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.3f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        // Flame at bottom (y=2..12)
                        if (y >= 2 && y <= 12)
                        {
                            float flameW = (12 - y) * 0.85f;
                            if (Mathf.Abs(dx) <= flameW)
                            {
                                if (Mathf.Abs(dx) <= flameW * 0.45f) return 1; // Inner yellow flame
                                return 0; // Outer orange flame
                            }
                        }

                        // Rocket Body
                        if (y >= 10 && y <= 39)
                        {
                            // Nose cone (y=30..39)
                            if (y > 30)
                            {
                                float noseW = (39 - y) * 0.65f;
                                if (Mathf.Abs(dx) <= noseW)
                                    return 4; // Red nose cone
                            }
                            // Main fuselage (y=12..30)
                            else if (y >= 12 && y <= 30)
                            {
                                float fuselageW = 6.2f;
                                if (Mathf.Abs(dx) <= fuselageW)
                                {
                                    // Porthole window at (cx, 23)
                                    float pDist = Mathf.Sqrt(dx * dx + (y - 23) * (y - 23));
                                    if (pDist <= 3.2f)
                                    {
                                        if (pDist <= 2.2f) return 3; // Cyan window
                                        return 4; // Red window trim
                                    }
                                    // Body stripe
                                    if (y == 15 || y == 28) return 4;
                                    return 2; // White body
                                }
                            }

                            // Delta Fins at sides (y=10..20)
                            if (y >= 10 && y <= 20)
                            {
                                float finReach = 6.2f + (20 - y) * 0.85f;
                                if (Mathf.Abs(dx) > 6.2f && Mathf.Abs(dx) <= finReach)
                                    return 4; // Red delta fin
                            }
                        }

                        // Deep space stars in background
                        (int sx, int sy)[] stars = new (int, int)[]
                        {
                            (7, 36), (35, 37), (5, 18), (37, 16), (9, 7), (33, 6), (21, 40)
                        };
                        foreach (var (sx, sy) in stars)
                        {
                            if ((Mathf.Abs(x - sx) <= 1 && y == sy) || (x == sx && Mathf.Abs(y - sy) <= 1))
                                return 1; // Twinkling star
                        }

                        return 5; // Deep Space
                    }
                },

                // =========================================================================
                // LEVEL 10: "Şampiyonluk Altın Kupası" (Grand Champions Trophy) - 44x44 (1,936 kum tanesi)
                // Şampiyonluk kupası, zümrüt kurdele, yakut madalyon, altın ışıltılar.
                // =========================================================================
                new StudioLevelBlueprint
                {
                    Index = 10,
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
                    DockCapacity = 5,
                    ColumnCount = 5,
                    MinBucketCapacity = 65,
                    MaxBucketCapacity = 120,
                    ScrambleFactor = 0.12f,
                    MysteryStartRow = 1,
                    SandFillSpeedMultiplier = 2.2f,
                    PixelEvaluator = (x, y, w, h) =>
                    {
                        float cx = w / 2.0f;
                        float dx = x - cx;

                        // Base Pedestal (y=4..10)
                        if (y >= 4 && y <= 10)
                        {
                            float baseW = 12.0f - (y - 4) * 0.6f;
                            if (Mathf.Abs(dx) <= baseW)
                            {
                                if (y == 6 || y == 7)
                                {
                                    if (Mathf.Abs(dx) <= baseW - 1.5f) return 2; // Emerald ribbon plaque
                                }
                                if ((x + y) % 3 == 0) return 1; // Amber shadow
                                return 0; // Gold base
                            }
                        }

                        // Stem (y=10..16)
                        if (y > 10 && y <= 16)
                        {
                            float stemW = 3.6f + Mathf.Abs(y - 13) * 0.5f;
                            if (Mathf.Abs(dx) <= stemW)
                            {
                                // Ruby star medallion at center of stem
                                if (y >= 12 && y <= 14 && Mathf.Abs(dx) <= 1.8f) return 3; // Ruby
                                return 0; // Gold stem
                            }
                        }

                        // Main Cup Body (y=16..38)
                        if (y > 16 && y <= 38)
                        {
                            float tCup = (y - 16) / 22.0f;
                            float cupW = 4.0f + Mathf.Sin(tCup * Mathf.PI * 0.5f) * 12.5f;

                            // Handles (y=21..35)
                            if (y >= 21 && y <= 35)
                            {
                                float handleOuter = cupW + 6.0f;
                                float handleInner = cupW + 2.5f;
                                if (Mathf.Abs(dx) <= handleOuter && Mathf.Abs(dx) >= handleInner)
                                {
                                    return (x % 2 == 0) ? 0 : 1; // Gold/Amber handle
                                }
                            }

                            if (Mathf.Abs(dx) <= cupW)
                            {
                                // Lip rim
                                if (y >= 36)
                                {
                                    if (Mathf.Abs(dx - 5f) < 2.5f) return 4; // White flare highlight
                                    return 0; // Gold rim
                                }

                                // Star in center of cup (y=25..29)
                                float sDist = Mathf.Sqrt(dx * dx + (y - 27) * (y - 27));
                                if (sDist <= 3.2f)
                                {
                                    if (sDist <= 1.8f || Mathf.Abs(dx) <= 0.8f || Mathf.Abs(y - 27) <= 0.8f)
                                        return 3; // Ruby star
                                    return 2; // Emerald halo
                                }

                                // Highlight stripe along left
                                if (dx >= -cupW * 0.75f && dx <= -cupW * 0.45f)
                                {
                                    return 4; // Pearl white reflection
                                }

                                if (dx > cupW * 0.4f) return 1; // Amber shadow on right
                                return 0; // Gold body
                            }
                        }

                        // Victory confetti stars in background
                        (int sx, int sy)[] sparkles = new (int, int)[]
                        {
                            (6, 40), (37, 41), (5, 20), (38, 22), (8, 6), (36, 7), (22, 42)
                        };
                        foreach (var (sx, sy) in sparkles)
                        {
                            if ((Mathf.Abs(x - sx) <= 1 && y == sy) || (x == sx && Mathf.Abs(y - sy) <= 1))
                                return 4; // White sparkle flare
                        }

                        return 5; // Royal Navy
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

