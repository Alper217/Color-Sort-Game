using System.Collections.Generic;
using SandSort.Core;
using SandSort.Data;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Custom Unity EditorWindow for Module 1:
    /// Converts PNG pixel art or procedural presets into <see cref="LevelDataSO"/> assets,
    /// previews the quantized sand matrix and bucket queues, and runs the 1000x Headless Bot simulator.
    /// </summary>
    public class LevelGeneratorWindow : EditorWindow
    {
        private Texture2D _sourceTexture;
        private PixelArtPreset _selectedPreset = PixelArtPreset.PixelHeart;
        private int _levelIndex = 1;
        private string _levelTitle = "Pixel Heart";
        private int _width = 60;
        private int _height = 60;
        private GravityMode _gravityMode = GravityMode.CellularAutomata;
        private int _backgroundSandColorId = 1;
        private int _activeDockCapacity = 4;
        private int _columnCount = 4;
        private int _minBucketCapacity = 180;
        private int _maxBucketCapacity = 260;
        private float _scrambleFactor = 0.30f;
        private int _randomSeed = 42;
        private int _botRunCount = 1000;
        private float _botGreedyRate = 0.75f;

        private LevelDataSO _workingPreviewLevel;
        private BotSimulationReport _lastBotReport;
        private Vector2 _scrollPos;

        [MenuItem("SandSort/Level Generator & Bot Simulator", priority = 10)]
        public static void OpenWindow()
        {
            LevelGeneratorWindow window = GetWindow<LevelGeneratorWindow>("SandSort Level Tool");
            window.minSize = new Vector2(540, 680);
            window.Show();
        }

        [MenuItem("SandSort/Generate 3 Showcase Levels Now", priority = 11)]
        public static void GenerateShowcaseLevelsMenu()
        {
            List<LevelDataSO> levels = LevelGenerationEngine.GenerateShowcasePortfolioLevels();
            if (levels.Count > 0)
            {
                BotSimulationReport r1 = HeadlessLevelBot.RunSimulation(levels[0], 1000);
                UnityEngine.Debug.Log(
                    $"[SandSort] 3 Showcase Levels generated in Assets/_Project/Data/Levels/! " +
                    $"Level_01 WinRate: {r1.WinRatePercent:F1}% ({r1.ElapsedMilliseconds} ms for 1000 runs).");
                Selection.activeObject = levels[0];
                EditorGUIUtility.PingObject(levels[0]);
            }
        }

        private void OnEnable()
        {
            EnsureWorkingLevel();
            GeneratePreviewFromPreset();
        }

        private void OnDisable()
        {
            if (_workingPreviewLevel != null)
            {
                DestroyImmediate(_workingPreviewLevel);
            }
        }

        private void EnsureWorkingLevel()
        {
            if (_workingPreviewLevel == null)
            {
                _workingPreviewLevel = CreateInstance<LevelDataSO>();
                _workingPreviewLevel.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void OnGUI()
        {
            EnsureWorkingLevel();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeaderBanner();
            EditorGUILayout.Space(6);

            DrawQuickPortfolioSection();
            EditorGUILayout.Space(8);

            DrawConfigurationSection();
            EditorGUILayout.Space(8);

            DrawActionButtons();
            EditorGUILayout.Space(8);

            DrawBotSimulationReportSection();
            EditorGUILayout.Space(8);

            DrawMatrixAndQueuePreview();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeaderBanner()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft
            };
            GUILayout.Label("Pixel & Sand Sort — Level Generator + 1000x Headless Bot", titleStyle);
            EditorGUILayout.LabelField(
                "PNG veya Prosedürel Piksel Sanatından Deterministik Kum Matrisi & Kova Kuyruğu Üretici",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawQuickPortfolioSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Hızlı Portfolyo Kurulumu (Tek Tıkla 3 Örnek Bölüm)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.25f, 0.78f, 0.45f);
            if (GUILayout.Button("3 Vitrin Bölümünü (Heart, Crown, Potion) + PNG'leri Otomatik Üret ve Kaydet", GUILayout.Height(30)))
            {
                List<LevelDataSO> created = LevelGenerationEngine.GenerateShowcasePortfolioLevels();
                if (created.Count > 0)
                {
                    _workingPreviewLevel.Width = created[0].Width;
                    _workingPreviewLevel.Height = created[0].Height;
                    _workingPreviewLevel.Gravity = created[0].Gravity;
                    _workingPreviewLevel.ActiveDockCapacity = created[0].ActiveDockCapacity;
                    _workingPreviewLevel.Palette = created[0].Palette;
                    _workingPreviewLevel.GridCells = (int[])created[0].GridCells.Clone();
                    _workingPreviewLevel.Columns = created[0].Columns;
                    _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
                    Selection.activeObject = created[0];
                    EditorGUIUtility.PingObject(created[0]);
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigurationSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Görsel Kaynak & Matris Parametreleri", EditorStyles.boldLabel);

            _levelIndex = EditorGUILayout.IntField("Level Index", _levelIndex);
            _levelTitle = EditorGUILayout.TextField("Level Title", _levelTitle);
            _sourceTexture = (Texture2D)EditorGUILayout.ObjectField(
                "Kaynak PNG (Texture2D)",
                _sourceTexture,
                typeof(Texture2D),
                false);

            EditorGUILayout.BeginHorizontal();
            _selectedPreset = (PixelArtPreset)EditorGUILayout.EnumPopup("Veya Dahili Şablon", _selectedPreset);
            if (GUILayout.Button("Şablonu Yükle", GUILayout.Width(120)))
            {
                _levelTitle = _selectedPreset.ToString();
                GeneratePreviewFromPreset();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            _width = EditorGUILayout.IntSlider("Grid Genişliği (Width)", _width, 16, 72);
            _height = EditorGUILayout.IntSlider("Grid Yüksekliği (Height)", _height, 16, 72);
            _gravityMode = (GravityMode)EditorGUILayout.EnumPopup("Yerçekimi Modu (Gravity)", _gravityMode);
            _backgroundSandColorId = EditorGUILayout.IntSlider("PNG Boşluk Arka Plan Kum Rengi (ID)", _backgroundSandColorId, 0, GameConstants.DefaultVibrantPalette.Length - 1);

            EditorGUILayout.Space(4);
            GUILayout.Label("2. Kova Bölümleme & Tersine Dağıtım (Reverse Scrambling)", EditorStyles.boldLabel);
            _activeDockCapacity = EditorGUILayout.IntSlider("Aktif Dock Slot Sayısı (K)", _activeDockCapacity, 3, 6);
            _columnCount = EditorGUILayout.IntSlider("Alt Kova Sütun Sayısı (S)", _columnCount, 2, 6);
            _minBucketCapacity = EditorGUILayout.IntSlider("Min Kova Kapasitesi", _minBucketCapacity, 10, 350);
            _maxBucketCapacity = EditorGUILayout.IntSlider("Max Kova Kapasitesi", Mathf.Max(_minBucketCapacity, _maxBucketCapacity), _minBucketCapacity, 500);
            _scrambleFactor = EditorGUILayout.Slider("Zorluk / Karıştırma Sapması (σ)", _scrambleFactor, 0f, 1f);
            _randomSeed = EditorGUILayout.IntField("Deterministik Seed", _randomSeed);

            EditorGUILayout.Space(4);
            GUILayout.Label("3. Headless AI Bot Ayarları", EditorStyles.boldLabel);
            _botRunCount = EditorGUILayout.IntSlider("Simülasyon Sayısı (Runs)", _botRunCount, 100, 5000);
            _botGreedyRate = EditorGUILayout.Slider("Bot Zeka Oranı (Greedy %)", _botGreedyRate, 0.1f, 1.0f);

            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Matrisi & Kovaları Yeniden Hesapla", GUILayout.Height(32)))
            {
                RebuildWorkingPreview();
                _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
            }

            GUI.backgroundColor = new Color(0.3f, 0.65f, 1f);
            if (GUILayout.Button($"Simulate {_botRunCount} Runs (Bot Test)", GUILayout.Height(32)))
            {
                RebuildWorkingPreview();
                _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(0.95f, 0.75f, 0.2f);
            if (GUILayout.Button($"Level_{_levelIndex:00}.asset Olarak Kaydet", GUILayout.Height(32)))
            {
                SaveWorkingLevelAsAsset();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBotSimulationReportSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Headless Bot Çözülebilirlik Raporu (Pure C# Simulation)", EditorStyles.boldLabel);

            int totalGrains = _workingPreviewLevel.GetTotalGrainCount();
            int totalBucketCap = _workingPreviewLevel.GetTotalBucketCapacity();

            EditorGUILayout.LabelField(
                $"Toplam Kum Tanesi: {totalGrains}  |  Toplam Kova Kapasitesi: {totalBucketCap}  |  " +
                $"Kütle Korunumu: {(totalGrains == totalBucketCap ? "BAŞARILI (%100 Eşit)" : "UYUŞMAZLIK!")}");

            if (_lastBotReport.TotalRuns > 0)
            {
                Rect barRect = GUILayoutUtility.GetRect(18, 22, "TextField");
                float ratio = Mathf.Clamp01(_lastBotReport.WinRatePercent / 100f);
                EditorGUI.ProgressBar(
                    barRect,
                    ratio,
                    $"Win Rate: %{_lastBotReport.WinRatePercent:F1} ({_lastBotReport.Wins} Win / {_lastBotReport.Deadlocks} Deadlock) — {_lastBotReport.GetDifficultyVerdict()}");

                EditorGUILayout.LabelField(
                    $"Ortalama Hamle (Kova): {_lastBotReport.AverageMovesOnWin:F1} (Min: {_lastBotReport.MinMovesOnWin}, Max: {_lastBotReport.MaxMovesOnWin})  |  " +
                    $"Simülasyon Süresi: {_lastBotReport.ElapsedMilliseconds} ms ({_lastBotReport.TotalRuns} oyun)");
            }
            else
            {
                EditorGUILayout.HelpBox("Bot simülasyonunu çalıştırmak için 'Simulate Runs' butonuna basın.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMatrixAndQueuePreview()
        {
            if (_workingPreviewLevel == null || _workingPreviewLevel.GridCells == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Canlı Kum Matrisi & Kova Sütunları Önizlemesi", EditorStyles.boldLabel);

            int w = _workingPreviewLevel.Width;
            int h = _workingPreviewLevel.Height;
            float cellSize = Mathf.Clamp(220f / Mathf.Max(w, h), 4f, 12f);

            Rect gridRect = GUILayoutUtility.GetRect(w * cellSize + 10, h * cellSize + 10);
            float startX = gridRect.x + (gridRect.width - w * cellSize) * 0.5f;
            float startY = gridRect.y + 4f;

            // Background frame
            EditorGUI.DrawRect(
                new Rect(startX - 2, startY - 2, w * cellSize + 4, h * cellSize + 4),
                new Color(0.14f, 0.14f, 0.16f, 1f));

            // Draw cells (y=0 is at the bottom!)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int colorId = _workingPreviewLevel.GetCell(x, y);
                    float drawY = startY + (h - 1 - y) * cellSize;
                    Rect cellRect = new Rect(startX + x * cellSize, drawY, cellSize - 1f, cellSize - 1f);

                    if (colorId >= 0)
                    {
                        EditorGUI.DrawRect(cellRect, _workingPreviewLevel.GetColor(colorId));
                    }
                    else
                    {
                        EditorGUI.DrawRect(cellRect, new Color(0.22f, 0.22f, 0.25f, 1f));
                    }
                }
            }

            // Draw Spawner Column Summaries
            if (_workingPreviewLevel.Columns != null)
            {
                EditorGUILayout.Space(6);
                GUILayout.Label("Alt Kova Sütunları (Sol = İlk Çıkacak Ön Kova):", EditorStyles.miniBoldLabel);

                for (int c = 0; c < _workingPreviewLevel.Columns.Length; c++)
                {
                    BucketData[] buckets = _workingPreviewLevel.Columns[c].Buckets;
                    if (buckets == null) continue;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"Sütun {c + 1} ({buckets.Length} kova):", GUILayout.Width(110));

                    int previewCount = Mathf.Min(buckets.Length, 10);
                    for (int b = 0; b < previewCount; b++)
                    {
                        Color prevGui = GUI.backgroundColor;
                        GUI.backgroundColor = _workingPreviewLevel.GetColor(buckets[b].ColorId);
                        GUILayout.Button($"{buckets[b].MaxCapacity}", GUILayout.Width(34), GUILayout.Height(20));
                        GUI.backgroundColor = prevGui;
                    }

                    if (buckets.Length > previewCount)
                    {
                        GUILayout.Label($"+{buckets.Length - previewCount} daha...", EditorStyles.miniLabel);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void GeneratePreviewFromPreset()
        {
            EnsureWorkingLevel();
            Texture2D tempTex = LevelGenerationEngine.CreateProceduralPresetTexture(
                _selectedPreset,
                _width,
                _height,
                GameConstants.DefaultVibrantPalette);

            LevelGenerationEngine.GenerationConfig cfg = BuildConfig();
            LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, tempTex, cfg);
            DestroyImmediate(tempTex);

            _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
        }

        private void RebuildWorkingPreview()
        {
            EnsureWorkingLevel();
            LevelGenerationEngine.GenerationConfig cfg = BuildConfig();

            if (_sourceTexture != null)
            {
                LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, _sourceTexture, cfg);
            }
            else
            {
                Texture2D tempTex = LevelGenerationEngine.CreateProceduralPresetTexture(
                    _selectedPreset,
                    _width,
                    _height,
                    GameConstants.DefaultVibrantPalette);
                LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, tempTex, cfg);
                DestroyImmediate(tempTex);
            }
        }

        private LevelGenerationEngine.GenerationConfig BuildConfig()
        {
            return new LevelGenerationEngine.GenerationConfig
            {
                Width = _width,
                Height = _height,
                Gravity = _gravityMode,
                ActiveDockCapacity = _activeDockCapacity,
                ColumnCount = _columnCount,
                MinBucketCapacity = _minBucketCapacity,
                MaxBucketCapacity = _maxBucketCapacity,
                ScrambleFactor = _scrambleFactor,
                RandomSeed = _randomSeed,
                FallbackBackgroundColorId = _backgroundSandColorId,
                Palette = GameConstants.DefaultVibrantPalette
            };
        }

        private void SaveWorkingLevelAsAsset()
        {
            RebuildWorkingPreview();

            const string levelsFolder = "Assets/_Project/Data/Levels";
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(levelsFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Levels");

            string assetPath = $"{levelsFolder}/Level_{_levelIndex:00}.asset";
            LevelDataSO asset = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            if (asset == null)
            {
                asset = CreateInstance<LevelDataSO>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.LevelIndex = _levelIndex;
            asset.LevelTitle = _levelTitle;
            asset.Width = _workingPreviewLevel.Width;
            asset.Height = _workingPreviewLevel.Height;
            asset.Gravity = _workingPreviewLevel.Gravity;
            asset.ActiveDockCapacity = _workingPreviewLevel.ActiveDockCapacity;
            asset.Palette = (Color32[])_workingPreviewLevel.Palette.Clone();
            asset.GridCells = (int[])_workingPreviewLevel.GridCells.Clone();
            asset.Columns = (BucketColumnData[])_workingPreviewLevel.Columns.Clone();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Auto-register into the active scene's GameLoopController if present so the user can test immediately!
            SandSort.Gameplay.GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<SandSort.Gameplay.GameLoopController>();
            if (loop != null)
            {
                SerializedObject soLoop = new SerializedObject(loop);
                SerializedProperty levelsProp = soLoop.FindProperty("_levels");
                bool alreadyAdded = false;
                for (int i = 0; i < levelsProp.arraySize; i++)
                {
                    if (levelsProp.GetArrayElementAtIndex(i).objectReferenceValue == asset)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }
                if (!alreadyAdded)
                {
                    int newIdx = levelsProp.arraySize;
                    levelsProp.InsertArrayElementAtIndex(newIdx);
                    levelsProp.GetArrayElementAtIndex(newIdx).objectReferenceValue = asset;
                    soLoop.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(loop);
                }
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            UnityEngine.Debug.Log($"[SandSort] Saved {assetPath} and synced with GameLoopController! (Grains: {asset.GetTotalGrainCount()}, WinRate: %{_lastBotReport.WinRatePercent:F1})");
        }
    }
}

