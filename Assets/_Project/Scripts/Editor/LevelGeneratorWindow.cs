using System;
using System.Collections.Generic;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Master Level Generator & Manager Window for SandSort:
    /// Coordinates level playlist management, pixel grid painting, color palettes,
    /// solvable bucket generation, and headless AI bot verification.
    /// Refactored into a clean, modular architecture.
    /// </summary>
    public sealed class LevelGeneratorWindow : EditorWindow
    {
        // Sub-services
        private readonly LevelGridPainter _painter = new LevelGridPainter();

        // Level Configuration State
        private int _levelIndex = 1;
        private string _levelTitle = "Level 01";
        private int _width = 32;
        private int _height = 32;
        private GravityMode _gravityMode = GravityMode.CellularAutomata;
        private int _activeDockCapacity = 4;
        private int _columnCount = 4;
        private int _minBucketCapacity = 45;
        private int _maxBucketCapacity = 90;
        private float _scrambleFactor = 0.08f;
        private int _randomSeed = 42;
        private int _mysteryBucketStartRow = 99;
        private float _sandFillSpeedMultiplier = 2.4f;
        private int _backgroundSandColorId = 1;

        // Visual / Painting State
        private Texture2D _sourceTexture;
        private PixelArtPreset _selectedPreset = PixelArtPreset.PixelHeart;
        private readonly List<Color32> _currentPalette = new List<Color32>();
        private LevelDataSO _workingPreviewLevel;
        private LevelDataSO _currentLoadedAsset;
        private Texture2D _previewTexture;
        private bool _previewDirty = true;
        private bool _manualEdited;

        // UI Foldouts & Scroll
        private Vector2 _scrollPos;
        private Vector2 _levelListScrollPos;
        private bool _levelListFolded = true;
        private bool _paletteFolded = true;
        private string _lastSavedMessage = "";
        private double _lastSavedTime = 0;

        // Bot Simulation State
        private int _botRunCount = 500;
        private float _botGreedyRate = 0.82f;
        private BotSimulationReport _lastBotReport;

        [MenuItem("SandSort/Level Generator & Manager", priority = 0)]
        public static void OpenWindow()
        {
            LevelGeneratorWindow window = GetWindow<LevelGeneratorWindow>("SandSort Level Tool");
            window.minSize = new Vector2(620, 780);
            window.Show();
        }

        [MenuItem("SandSort/Generate 3 Showcase Levels Now", priority = 11)]
        public static void GenerateShowcaseLevelsMenu()
        {
            List<LevelDataSO> levels = LevelGenerationEngine.GenerateShowcasePortfolioLevels();
            if (levels.Count > 0)
            {
                LevelAssetRepository.SyncAllLevelsWithGameLoopController(0);
                BotSimulationReport r1 = HeadlessLevelBot.RunSimulation(levels[0], 1000);
                Debug.Log($"[SandSort] 3 Showcase Levels generated in Assets/Levels/! Level_01 WinRate: {r1.WinRatePercent:F1}%");
                Selection.activeObject = levels[0];
                EditorGUIUtility.PingObject(levels[0]);
            }
        }

        public static List<LevelDataSO> GetAllLevelsInProject()
        {
            return LevelAssetRepository.GetAllLevelsInProject();
        }

        public static void SyncAllLevelsWithGameLoopController(int? startingIndex = null)
        {
            LevelAssetRepository.SyncAllLevelsWithGameLoopController(startingIndex);
        }

        private void OnEnable()
        {
            LevelAssetRepository.EnsureLevelsFolder();
            _levelIndex = EditorPrefs.GetInt("SandSort_SelectedLevelIndex", _levelIndex > 0 ? _levelIndex : 1);

            LevelDataSO existing = LevelAssetRepository.LoadLevelByIndex(_levelIndex);
            if (existing != null)
            {
                LoadLevelIntoEditor(existing);
            }
            else
            {
                List<LevelDataSO> all = LevelAssetRepository.GetAllLevelsInProject();
                if (all.Count > 0 && all[0] != null)
                {
                    LoadLevelIntoEditor(all[0]);
                }
                else
                {
                    InitDefaultLevel();
                }
            }

            _painter.ClearUndo();
            _previewDirty = true;
            UpdatePreviewTexture();
        }

        private void OnDisable()
        {
            if (_workingPreviewLevel != null)
            {
                DestroyImmediate(_workingPreviewLevel);
            }
            if (_previewTexture != null)
            {
                DestroyImmediate(_previewTexture);
            }
        }

        private void EnsureWorkingLevel()
        {
            if (_workingPreviewLevel == null)
            {
                _workingPreviewLevel = CreateInstance<LevelDataSO>();
                _workingPreviewLevel.hideFlags = HideFlags.HideAndDontSave;

                if (_currentPalette.Count > 0)
                {
                    _workingPreviewLevel.Palette = _currentPalette.ToArray();
                }
                else if (_currentLoadedAsset != null && _currentLoadedAsset.Palette != null && _currentLoadedAsset.Palette.Length > 0)
                {
                    _currentPalette.Clear();
                    _currentPalette.AddRange(_currentLoadedAsset.Palette);
                    _workingPreviewLevel.Palette = (Color32[])_currentLoadedAsset.Palette.Clone();
                }
            }
        }

        private void InitDefaultLevel()
        {
            EnsureWorkingLevel();
            _currentPalette.Clear();
            _currentPalette.AddRange(GameConstants.DefaultVibrantPalette);
            _workingPreviewLevel.Palette = _currentPalette.ToArray();
            _selectedPreset = PixelArtPreset.PixelHeart;
            RebuildWorkingPreviewFromSource();
        }

        public void LoadLevelIntoEditor(LevelDataSO level)
        {
            if (level == null) return;
            EnsureWorkingLevel();

            _currentLoadedAsset = level;
            _levelIndex = level.LevelIndex;
            _levelTitle = level.LevelTitle;
            EditorPrefs.SetInt("SandSort_SelectedLevelIndex", _levelIndex);

            _width = level.Width;
            _height = level.Height;
            _gravityMode = level.Gravity;
            _activeDockCapacity = level.ActiveDockCapacity;
            _mysteryBucketStartRow = (level.MysteryBucketStartRow > 0) ? level.MysteryBucketStartRow : 99;
            _sandFillSpeedMultiplier = (level.SandFillSpeedMultiplier > 0.05f) ? level.SandFillSpeedMultiplier : 2.4f;
            _columnCount = (level.Columns != null && level.Columns.Length > 0) ? level.Columns.Length : 4;

            // Load Palette directly from asset, preserving all user modifications
            _currentPalette.Clear();
            if (level.Palette != null && level.Palette.Length > 0)
            {
                _currentPalette.AddRange(level.Palette);
            }
            else
            {
                _currentPalette.AddRange(GameConstants.DefaultVibrantPalette);
            }

            // Match sprite texture reference without wiping it for custom titles
            _sourceTexture = LevelPaletteService.TryFindMatchingSprite(level.LevelIndex, level.LevelTitle);

            _workingPreviewLevel.LevelIndex = level.LevelIndex;
            _workingPreviewLevel.LevelTitle = level.LevelTitle;
            _workingPreviewLevel.Width = level.Width;
            _workingPreviewLevel.Height = level.Height;
            _workingPreviewLevel.Gravity = level.Gravity;
            _workingPreviewLevel.ActiveDockCapacity = level.ActiveDockCapacity;
            _workingPreviewLevel.MysteryBucketStartRow = level.MysteryBucketStartRow;
            _workingPreviewLevel.Palette = (Color32[])level.Palette.Clone();
            _workingPreviewLevel.GridCells = (level.GridCells != null) ? (int[])level.GridCells.Clone() : new int[level.Width * level.Height];
            _workingPreviewLevel.Columns = (level.Columns != null) ? (BucketColumnData[])level.Columns.Clone() : Array.Empty<BucketColumnData>();

            _manualEdited = true; // Mark as loaded grid so preset evaluation never replaces it
            _painter.ClearUndo();
            _previewDirty = true;
            UpdatePreviewTexture();

            _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
            Selection.activeObject = level;
            EditorGUIUtility.PingObject(level);
        }

        private void OnGUI()
        {
            LevelAssetRepository.EnsureLevelsFolder();
            EnsureWorkingLevel();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = new Color(0.25f, 0.75f, 1f);
                EditorGUILayout.HelpBox("▶ Oyun Çalışıyor (Play Mode). Seviye düzenleyici güvenli izleme modunda.", MessageType.Info);
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndVertical();
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeaderBanner();
            EditorGUILayout.Space(6);

            DrawLevelManagerListSection();
            EditorGUILayout.Space(8);

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
            GUILayout.Label("Pixel & Sand Sort — Level Manager & Generator", titleStyle);
            EditorGUILayout.LabelField(
                "Assets/Levels/ Klasör Yönetimi, Renk Paleti Stüdyosu, Piksel Çizim ve 1000x Headless Bot Doğrulayıcı",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawLevelManagerListSection()
        {
            List<LevelDataSO> existingLevels = LevelAssetRepository.GetAllLevelsInProject();
            int nextIndex = 1;
            foreach (var lvl in existingLevels)
            {
                if (lvl != null && lvl.LevelIndex >= nextIndex)
                {
                    nextIndex = lvl.LevelIndex + 1;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle boldHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _levelListFolded = EditorGUILayout.Foldout(
                _levelListFolded,
                $"📁 Seviye Çalma Listesi (Assets/Levels) — [{existingLevels.Count} Seviye Kayıtlı]",
                true,
                boldHeader);

            EditorGUILayout.Space(4);

            GUI.backgroundColor = new Color(0.22f, 0.82f, 0.38f);
            if (GUILayout.Button($"[+] Yeni Level Ekle (Otomatik: Level {nextIndex:00})", GUILayout.Height(30)))
            {
                AddNewLevelAutomatically(nextIndex);
            }
            GUI.backgroundColor = Color.white;

            DrawLevelNavigation(existingLevels);

            if (_levelListFolded)
            {
                EditorGUILayout.Space(6);

                if (existingLevels.Count == 0)
                {
                    EditorGUILayout.HelpBox("Henüz Assets/Levels klasöründe seviye bulunmuyor. '[+] Yeni Level Ekle' butonuna basarak ilk seviyenizi oluşturabilirsiniz.", MessageType.Info);
                }
                else
                {
                    float targetHeight = Mathf.Clamp(existingLevels.Count * 36f + 12f, 160f, 300f);
                    _levelListScrollPos = EditorGUILayout.BeginScrollView(_levelListScrollPos, GUILayout.Height(targetHeight));

                    for (int i = 0; i < existingLevels.Count; i++)
                    {
                        LevelDataSO lvl = existingLevels[i];
                        if (lvl == null) continue;

                        bool isCurrent = (lvl.LevelIndex == _levelIndex);
                        Rect rowRect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(30));

                        if (isCurrent)
                        {
                            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), new Color(1f, 0.75f, 0.1f));
                        }

                        GUI.backgroundColor = isCurrent ? new Color(1f, 0.85f, 0.25f) : new Color(0.28f, 0.65f, 0.95f);
                        GUILayout.Label($"#{lvl.LevelIndex:00}", EditorStyles.boldLabel, GUILayout.Width(38), GUILayout.Height(24));
                        GUI.backgroundColor = Color.white;

                        string gizemInfo = (lvl.MysteryBucketStartRow >= 90)
                            ? "<color=#55bb55>Açık</color>"
                            : (lvl.MysteryBucketStartRow == 1 ? "<color=#ffaa33>Ön Hariç '?'</color>" : $"<color=#ffaa33>{lvl.MysteryBucketStartRow}. Sıra '?'</color>");

                        string info = $"<b>{lvl.LevelTitle}</b>   <color=#999999>[{lvl.Width}x{lvl.Height}] [{lvl.GetTotalGrainCount()} Kum] [Dock: {lvl.ActiveDockCapacity}] [Gizem: {gizemInfo}]</color>";
                        GUILayout.Label(info, new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(24));

                        GUILayout.FlexibleSpace();

                        GUI.backgroundColor = isCurrent ? new Color(0.4f, 0.85f, 0.5f) : new Color(0.3f, 0.75f, 0.45f);
                        if (GUILayout.Button(isCurrent ? "Yüklü ✓" : "Düzenle", EditorStyles.miniButtonLeft, GUILayout.Width(65), GUILayout.Height(22)))
                        {
                            LoadLevelIntoEditor(lvl);
                        }
                        GUI.backgroundColor = Color.white;

                        if (GUILayout.Button("Göster", EditorStyles.miniButtonMid, GUILayout.Width(50), GUILayout.Height(22)))
                        {
                            Selection.activeObject = lvl;
                            EditorGUIUtility.PingObject(lvl);
                        }

                        GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);
                        if (GUILayout.Button("Sil ✕", EditorStyles.miniButtonRight, GUILayout.Width(45), GUILayout.Height(22)))
                        {
                            if (LevelAssetRepository.DeleteLevel(lvl))
                            {
                                List<LevelDataSO> remaining = LevelAssetRepository.GetAllLevelsInProject();
                                if (remaining.Count > 0) LoadLevelIntoEditor(remaining[0]);
                            }
                        }
                        GUI.backgroundColor = Color.white;

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.2f, 0.55f, 0.88f);
                if (GUILayout.Button("Tüm Seviyeleri Sahneye (GameLoopController) Senkronize Et", GUILayout.Height(24)))
                {
                    LevelAssetRepository.SyncAllLevelsWithGameLoopController(_levelIndex - 1);
                }
                GUI.backgroundColor = new Color(0.7f, 0.7f, 0.75f);
                if (GUILayout.Button("1..N Olarak Yeniden Sırala", GUILayout.Height(24), GUILayout.Width(170)))
                {
                    LevelAssetRepository.RenumberAllLevelsSequentially();
                    List<LevelDataSO> refreshed = LevelAssetRepository.GetAllLevelsInProject();
                    if (refreshed.Count > 0) LoadLevelIntoEditor(refreshed[0]);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLevelNavigation(List<LevelDataSO> levels)
        {
            int pos = -1;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].LevelIndex == _levelIndex) { pos = i; break; }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = levels.Count > 0 && pos > 0;
            if (GUILayout.Button("◀ Önceki", GUILayout.Height(24)))
            {
                int target = pos - 1;
                LoadLevelIntoEditor(levels[target]);
            }

            GUI.enabled = true;
            GUILayout.Label(
                pos >= 0 ? $"Level {_levelIndex:00}  ({pos + 1}/{levels.Count})" : $"Level {_levelIndex:00}  (kayıtsız)",
                new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter },
                GUILayout.Height(24), GUILayout.MinWidth(140));

            GUI.enabled = levels.Count > 0 && pos >= 0 && pos < levels.Count - 1;
            if (GUILayout.Button("Sonraki ▶", GUILayout.Height(24)))
            {
                int target = pos + 1;
                LoadLevelIntoEditor(levels[target]);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawQuickPortfolioSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Hızlı Vitrin Kurulumu (Tek Tıkla 3 Örnek Bölüm)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.25f, 0.78f, 0.45f);
            if (GUILayout.Button("3 Vitrin Bölümünü (Heart, Crown, Potion) Assets/Levels/ İçine Üret ve Kaydet", GUILayout.Height(28)))
            {
                List<LevelDataSO> created = LevelGenerationEngine.GenerateShowcasePortfolioLevels();
                if (created.Count > 0)
                {
                    LevelAssetRepository.SyncAllLevelsWithGameLoopController(0);
                    LoadLevelIntoEditor(created[0]);
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(2);
            GUI.backgroundColor = new Color(0.95f, 0.55f, 0.15f);
            if (GUILayout.Button("✨ 5, 7, 8, 9. Bölümleri Master Studio Şablonlarıyla Onar (İksir, Konsol, Elmas, Roket)", GUILayout.Height(30)))
            {
                MasterStudioLevelGenerator.RebuildSpecificStudioLevels(5, 7, 8, 9);
                LevelDataSO refreshed = LevelAssetRepository.LoadLevelByIndex(_levelIndex);
                if (refreshed != null) LoadLevelIntoEditor(refreshed);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        private void DrawConfigurationSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Görsel Kaynak & Matris Parametreleri", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int inputLevelIndex = EditorGUILayout.IntField("Level Index", _levelIndex);
            if (EditorGUI.EndChangeCheck() && inputLevelIndex != _levelIndex)
            {
                _levelIndex = inputLevelIndex;
                EditorPrefs.SetInt("SandSort_SelectedLevelIndex", _levelIndex);
                LevelDataSO existingLvl = LevelAssetRepository.LoadLevelByIndex(_levelIndex);
                if (existingLvl != null)
                {
                    LoadLevelIntoEditor(existingLvl);
                }
            }
            if (GUILayout.Button("Sonraki Boş Indexi Bul", GUILayout.Width(150)))
            {
                List<LevelDataSO> existing = LevelAssetRepository.GetAllLevelsInProject();
                int highest = 0;
                foreach (var l in existing)
                {
                    if (l != null && l.LevelIndex > highest) highest = l.LevelIndex;
                }
                _levelIndex = highest + 1;
                _levelTitle = $"Level {_levelIndex:00}";
            }
            EditorGUILayout.EndHorizontal();

            _levelTitle = EditorGUILayout.TextField("Level Title", _levelTitle);
            _sourceTexture = (Texture2D)EditorGUILayout.ObjectField(
                "Kaynak PNG (Texture2D)",
                _sourceTexture,
                typeof(Texture2D),
                false);

            if (_sourceTexture != null)
            {
                GUI.backgroundColor = new Color(0.2f, 0.75f, 0.95f);
                if (GUILayout.Button($"📥 '{_sourceTexture.name}' Görselini ve Orijinal Renklerini İçe Aktar", GUILayout.Height(24)))
                {
                    ImportTextureAndExtractPalette(_sourceTexture);
                }
                GUI.backgroundColor = Color.white;
            }

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
            _backgroundSandColorId = EditorGUILayout.IntSlider("PNG Boşluk Arka Plan Kum Rengi (ID)", _backgroundSandColorId, 0, Mathf.Max(0, _currentPalette.Count - 1));

            EditorGUILayout.Space(4);
            DrawPaletteSection();

            EditorGUILayout.Space(4);
            GUILayout.Label("2. Kova Bölümleme, Zorluk & Gizem ('?') Ayarları", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.2f, 0.65f, 0.95f);
            LevelGenerationEngine.DifficultyProfile currentDiff = LevelGenerationEngine.GetDifficultyProfile(_levelIndex);
            if (GUILayout.Button($"⚡ Level {_levelIndex} Zorluk Profilini Uygula ({currentDiff.GridResolution}x{currentDiff.GridResolution}, {currentDiff.ColumnCount} Sütun)", GUILayout.Height(26)))
            {
                ApplyDifficultyProfileForCurrentLevel();
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginChangeCheck();
            _activeDockCapacity = EditorGUILayout.IntSlider("Aktif Dock Slot Sayısı (K)", _activeDockCapacity, 2, 6);
            _columnCount = EditorGUILayout.IntSlider("Alt Kova Sütun Sayısı (S)", _columnCount, 2, 6);
            _minBucketCapacity = EditorGUILayout.IntSlider("Min Kova Kapasitesi", _minBucketCapacity, 10, 350);
            _maxBucketCapacity = EditorGUILayout.IntSlider("Max Kova Kapasitesi", Mathf.Max(_minBucketCapacity, _maxBucketCapacity), _minBucketCapacity, 500);
            _scrambleFactor = EditorGUILayout.Slider("Zorluk / Karıştırma Sapması (σ)", _scrambleFactor, 0f, 1f);
            _randomSeed = EditorGUILayout.IntField("Deterministik Seed", _randomSeed);
            _mysteryBucketStartRow = EditorGUILayout.IntSlider("Gizemli Kova Satırı (99: Yok, 1: Ön Hariç, 2: 3. Sıra)", _mysteryBucketStartRow, 1, 99);

            EditorGUILayout.Space(2);
            _sandFillSpeedMultiplier = EditorGUILayout.Slider(
                new GUIContent("⚡ Kum Dolum Hızı (Speed)", "Bu seviyeye özel kum dolum ve çekim hızı çarpanı. 1.0 = Normal, 2.5 = Hızlı, 3.8+ = Aşırı Hızlı."),
                _sandFillSpeedMultiplier, 1.0f, 5.0f);

            if (EditorGUI.EndChangeCheck())
            {
                RegenerateBucketsFromCurrentGrid();
                _previewDirty = true;
                UpdatePreviewTexture();
                ApplyPaletteChangeLive();
            }

            EditorGUILayout.Space(4);
            GUILayout.Label("3. Headless AI Bot Ayarları", EditorStyles.boldLabel);
            _botRunCount = EditorGUILayout.IntSlider("Simülasyon Sayısı (Runs)", _botRunCount, 100, 5000);
            _botGreedyRate = EditorGUILayout.Slider("Bot Zeka Oranı (Greedy %)", _botGreedyRate, 0.1f, 1.0f);

            EditorGUILayout.EndVertical();
        }

        private void DrawPaletteSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _paletteFolded = EditorGUILayout.Foldout(
                _paletteFolded,
                $"🎨 Seviye Renk Paleti ({_currentPalette.Count} Renk) — [Canlı Düzenleyici]",
                true,
                EditorStyles.boldLabel);

            if (_paletteFolded)
            {
                EditorGUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Hazır Temalar:", EditorStyles.miniLabel, GUILayout.Width(80));

                GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
                if (GUILayout.Button("🎨 Orijinal Renkler", EditorStyles.miniButton))
                {
                    if (LevelPaletteService.RestorePaletteFromSource(_workingPreviewLevel, _currentPalette, _sourceTexture, _levelIndex, _currentLoadedAsset))
                    {
                        ApplyPaletteChangeLive();
                    }
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Vibrant", EditorStyles.miniButton))
                {
                    _currentPalette.Clear();
                    _currentPalette.AddRange(GameConstants.DefaultVibrantPalette);
                    ApplyPaletteChangeLive();
                }
                if (GUILayout.Button("Pastel", EditorStyles.miniButton))
                {
                    _currentPalette.Clear();
                    _currentPalette.AddRange(LevelPaletteService.PastelPalette);
                    ApplyPaletteChangeLive();
                }
                if (GUILayout.Button("Neon", EditorStyles.miniButton))
                {
                    _currentPalette.Clear();
                    _currentPalette.AddRange(LevelPaletteService.NeonPalette);
                    ApplyPaletteChangeLive();
                }
                if (GUILayout.Button("Gün Batımı", EditorStyles.miniButton))
                {
                    _currentPalette.Clear();
                    _currentPalette.AddRange(LevelPaletteService.SunsetPalette);
                    ApplyPaletteChangeLive();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);

                int totalGrains = (_workingPreviewLevel != null) ? _workingPreviewLevel.GetTotalGrainCount() : 0;
                int unusedColorCount = 0;
                int removeIdx = -1;

                for (int i = 0; i < _currentPalette.Count; i++)
                {
                    int grains = LevelPaletteService.CountGrainsForColor(_workingPreviewLevel, i);
                    bool isUsed = grains > 0;
                    if (!isUsed) unusedColorCount++;
                    float pct = (totalGrains > 0) ? (grains * 100f / totalGrains) : 0f;

                    EditorGUILayout.BeginHorizontal();

                    Color col = _currentPalette[i];
                    EditorGUI.BeginChangeCheck();
                    Color newCol = EditorGUILayout.ColorField(GUIContent.none, col, true, false, false, GUILayout.Width(65), GUILayout.Height(20));
                    if (EditorGUI.EndChangeCheck())
                    {
                        _currentPalette[i] = (Color32)newCol;
                        ApplyPaletteChangeLive();
                    }

                    string labelText = isUsed
                        ? $"<b>Renk #{i} (ID: {i})</b>  —  <color=#44ee77><b>{grains} Kum</b> (%{pct:F1})</color>  [Görselde Aktif]"
                        : $"<b>Renk #{i} (ID: {i})</b>  —  <color=#888888>0 Kum (Bu görselde yok)</color>";

                    GUILayout.Label(labelText, new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(20));

                    if (_currentPalette.Count > 2)
                    {
                        GUI.backgroundColor = isUsed ? new Color(1f, 0.45f, 0.45f) : new Color(0.75f, 0.75f, 0.75f);
                        if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(18)))
                        {
                            removeIdx = i;
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (removeIdx >= 0)
                {
                    _painter.PushUndo(_workingPreviewLevel);
                    LevelPaletteService.SafeRemoveColorFromLevel(_workingPreviewLevel, _currentPalette, removeIdx);
                    ApplyPaletteChangeLive();
                }

                EditorGUILayout.Space(3);

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.35f, 0.75f, 0.45f);
                if (GUILayout.Button("+ Yeni Renk Ekle", GUILayout.Height(22)))
                {
                    _currentPalette.Add(new Color32(255, 255, 255, 255));
                    ApplyPaletteChangeLive();
                }

                if (unusedColorCount > 0)
                {
                    GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
                    if (GUILayout.Button($"✨ Kullanılmayan {unusedColorCount} Rengi Temizle", GUILayout.Height(22)))
                    {
                        _painter.PushUndo(_workingPreviewLevel);
                        LevelPaletteService.CompactPaletteToUsedOnly(_workingPreviewLevel, _currentPalette);
                        ApplyPaletteChangeLive();
                    }
                }

                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPaintToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Araç:", EditorStyles.miniLabel, GUILayout.Width(35));

            GUI.backgroundColor = _painter.ActiveTool == PaintTool.Brush ? new Color(0.3f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button("🖌 Fırça", EditorStyles.toolbarButton, GUILayout.Width(60))) _painter.ActiveTool = PaintTool.Brush;

            GUI.backgroundColor = _painter.ActiveTool == PaintTool.Line ? new Color(0.3f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button("📏 Çizgi", EditorStyles.toolbarButton, GUILayout.Width(60))) _painter.ActiveTool = PaintTool.Line;

            GUI.backgroundColor = _painter.ActiveTool == PaintTool.FloodFill ? new Color(0.3f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button("🪣 Doldur", EditorStyles.toolbarButton, GUILayout.Width(65))) _painter.ActiveTool = PaintTool.FloodFill;

            GUI.backgroundColor = _painter.ActiveTool == PaintTool.ColorPicker ? new Color(0.3f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button("💧 Damlalık", EditorStyles.toolbarButton, GUILayout.Width(65))) _painter.ActiveTool = PaintTool.ColorPicker;

            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);
            GUILayout.Label("Boyut:", EditorStyles.miniLabel, GUILayout.Width(40));
            _painter.BrushSize = EditorGUILayout.IntSlider(_painter.BrushSize, 1, 5, GUILayout.Width(100));

            GUILayout.FlexibleSpace();

            GUI.enabled = _painter.UndoCount > 0;
            if (GUILayout.Button($"⟲ Geri Al ({_painter.UndoCount})", EditorStyles.toolbarButton, GUILayout.Width(85)))
            {
                if (_painter.UndoPaint(_workingPreviewLevel))
                {
                    RegenerateBucketsFromCurrentGrid();
                    _previewDirty = true;
                    UpdatePreviewTexture();
                }
            }
            GUI.enabled = true;

            if (GUILayout.Button("🗑 Temizle", EditorStyles.toolbarButton, GUILayout.Width(65)))
            {
                _painter.NewBlankCanvas(_workingPreviewLevel, _width, _height, _backgroundSandColorId);
                RegenerateBucketsFromCurrentGrid();
                _previewDirty = true;
                UpdatePreviewTexture();
            }

            _painter.ShowGridLines = GUILayout.Toggle(_painter.ShowGridLines, "Izgara", EditorStyles.toolbarButton, GUILayout.Width(50));

            EditorGUILayout.EndHorizontal();

            // Swatch palette row for painting
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Çizim Rengi:", EditorStyles.miniLabel, GUILayout.Width(75));

            int perRow = 12;
            for (int i = 0; i < _currentPalette.Count; i++)
            {
                Rect r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
                if (i == _painter.PaintColorId)
                {
                    EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 0.85f, 0.2f));
                }
                EditorGUI.DrawRect(r, _currentPalette[i]);

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _painter.PaintColorId = i;
                    Event.current.Use();
                    Repaint();
                }

                if (i % perRow == perRow - 1 && i < _currentPalette.Count - 1)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(79);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawMatrixAndQueuePreview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Piksel Matrisi & Kova Sırası Önizleme (Canlı Çizilebilir)", EditorStyles.boldLabel);

            DrawPaintToolbar();
            EditorGUILayout.Space(4);

            int w = _workingPreviewLevel.Width;
            int h = _workingPreviewLevel.Height;

            float canvasSize = 340f;
            Rect drawRect = GUILayoutUtility.GetRect(canvasSize, canvasSize, GUILayout.Width(canvasSize), GUILayout.Height(canvasSize));
            drawRect.x = (EditorGUIUtility.currentViewWidth - canvasSize) * 0.5f;

            if (_previewDirty || _previewTexture == null)
            {
                UpdatePreviewTexture();
            }

            if (_previewTexture != null)
            {
                GUI.DrawTexture(drawRect, _previewTexture, ScaleMode.StretchToFill);
            }

            // Grid lines overlay
            if (_painter.ShowGridLines && w > 0 && h > 0 && w <= 48 && h <= 48)
            {
                Color lineCol = new Color(0f, 0f, 0f, 0.25f);
                float cw = drawRect.width / w;
                float ch = drawRect.height / h;

                for (int x = 0; x <= w; x++)
                {
                    EditorGUI.DrawRect(new Rect(drawRect.x + x * cw, drawRect.y, 1f, drawRect.height), lineCol);
                }
                for (int y = 0; y <= h; y++)
                {
                    EditorGUI.DrawRect(new Rect(drawRect.x, drawRect.y + y * ch, drawRect.width, 1f), lineCol);
                }
            }

            // Handle painting input
            _painter.HandlePainting(Event.current, drawRect, _workingPreviewLevel, _currentPalette.Count, () =>
            {
                _manualEdited = true;
                RegenerateBucketsFromCurrentGrid();
                _previewDirty = true;
                UpdatePreviewTexture();
                Repaint();
            });

            EditorGUILayout.Space(8);

            // Spawner queues visual preview
            if (_workingPreviewLevel.Columns != null && _workingPreviewLevel.Columns.Length > 0)
            {
                GUILayout.Label($"Spawner Kova Kuyrukları ({_workingPreviewLevel.Columns.Length} Sütun):", EditorStyles.boldLabel);

                for (int c = 0; c < _workingPreviewLevel.Columns.Length; c++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"Sütun {c + 1}:", GUILayout.Width(55));

                    BucketData[] buckets = _workingPreviewLevel.Columns[c].Buckets;
                    if (buckets == null || buckets.Length == 0)
                    {
                        GUILayout.Label("<Boş>", EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                        continue;
                    }

                    int previewCount = Mathf.Min(8, buckets.Length);
                    Color prevGui = GUI.backgroundColor;

                    for (int b = 0; b < previewCount; b++)
                    {
                        bool isMystery = (b >= _workingPreviewLevel.MysteryBucketStartRow);
                        if (isMystery)
                        {
                            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                            GUILayout.Button($"? ({buckets[b].MaxCapacity})", GUILayout.Width(46), GUILayout.Height(20));
                        }
                        else
                        {
                            GUI.backgroundColor = _workingPreviewLevel.GetColor(buckets[b].ColorId);
                            GUILayout.Button($"{buckets[b].MaxCapacity}", GUILayout.Width(38), GUILayout.Height(20));
                        }
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
                float winPct = _lastBotReport.WinRatePercent;

                EditorGUI.DrawRect(barRect, new Color(0.18f, 0.18f, 0.22f));
                Color winBarColor = winPct >= 85f ? new Color(0.18f, 0.78f, 0.35f) : (winPct >= 60f ? new Color(0.95f, 0.65f, 0.15f) : new Color(0.9f, 0.25f, 0.25f));
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * (winPct / 100f), barRect.height), winBarColor);

                string reportText = $"Kazanma Oranı: %{winPct:F1} ({_lastBotReport.Wins}/{_lastBotReport.TotalRuns}) | Ortalama Hamle: {_lastBotReport.AverageMovesOnWin:F1} | Süre: {_lastBotReport.ElapsedMilliseconds} ms";
                GUIStyle barLabelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(barRect, reportText, barLabelStyle);
            }

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
                if (_workingPreviewLevel.GridCells == null || _workingPreviewLevel.GridCells.Length == 0)
                {
                    RebuildWorkingPreview();
                }
                _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.45f);
            if (GUILayout.Button($"💾  Level_{_levelIndex:00}.asset OLARAK KAYDET (Assets/Levels)", GUILayout.Height(38)))
            {
                SaveWorkingLevelAsAsset();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastSavedMessage) && (EditorApplication.timeSinceStartup - _lastSavedTime < 6.0))
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(_lastSavedMessage, MessageType.Info);
            }
        }

        private void ApplyDifficultyProfileForCurrentLevel()
        {
            LevelGenerationEngine.DifficultyProfile diff = LevelGenerationEngine.GetDifficultyProfile(_levelIndex);
            _width = diff.GridResolution;
            _height = diff.GridResolution;
            _columnCount = diff.ColumnCount;
            _minBucketCapacity = diff.MinBucketCapacity;
            _maxBucketCapacity = diff.MaxBucketCapacity;
            _activeDockCapacity = diff.ActiveDockCapacity;
            _mysteryBucketStartRow = diff.MysteryBucketStartRow;
            _sandFillSpeedMultiplier = diff.SandFillSpeedMultiplier;

            RegenerateBucketsFromCurrentGrid();
            _previewDirty = true;
            UpdatePreviewTexture();
            ApplyPaletteChangeLive();
        }

        private void ApplyPaletteChangeLive()
        {
            EnsureWorkingLevel();
            _workingPreviewLevel.Palette = _currentPalette.ToArray();
            RegenerateBucketsFromCurrentGrid();
            _previewDirty = true;
            UpdatePreviewTexture();

            // Sync with current loaded asset without overwriting if editing different level
            LevelDataSO target = _currentLoadedAsset;
            if (target != null && target.LevelIndex == _levelIndex)
            {
                target.Palette = (Color32[])_currentPalette.ToArray().Clone();
                if (_workingPreviewLevel.GridCells != null && _workingPreviewLevel.GridCells.Length > 0)
                {
                    target.GridCells = (int[])_workingPreviewLevel.GridCells.Clone();
                }
                if (_workingPreviewLevel.Columns != null && _workingPreviewLevel.Columns.Length > 0)
                {
                    target.Columns = (BucketColumnData[])_workingPreviewLevel.Columns.Clone();
                }
                EditorUtility.SetDirty(target);
            }

            // Live-refresh running game in Play Mode immediately!
            if (Application.isPlaying)
            {
                GameLoopController loop = FindFirstObjectByType<GameLoopController>();
                if (loop != null && target != null)
                {
                    loop.LoadLevel(target);
                }
            }

            Repaint();
        }

        private void RegenerateBucketsFromCurrentGrid()
        {
            EnsureWorkingLevel();
            _workingPreviewLevel.Palette = _currentPalette.ToArray();
            LevelGenerationEngine.GenerationConfig cfg = BuildConfig();
            LevelGenerationEngine.GenerateSolvableBucketQueues(_workingPreviewLevel, cfg);
        }

        private void RebuildWorkingPreview()
        {
            if (_manualEdited && _workingPreviewLevel != null
                && _workingPreviewLevel.GridCells != null && _workingPreviewLevel.GridCells.Length > 0)
            {
                RegenerateBucketsFromCurrentGrid();
                _previewDirty = true;
                UpdatePreviewTexture();
                return;
            }
            RebuildWorkingPreviewFromSource();
        }

        private void RebuildWorkingPreviewFromSource()
        {
            EnsureWorkingLevel();
            LevelGenerationEngine.GenerationConfig cfg = BuildConfig();
            Texture2D tex = _sourceTexture ?? LevelPaletteService.TryFindMatchingSprite(_levelIndex, _levelTitle);

            if (tex != null)
            {
                LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, tex, cfg);
            }
            else
            {
                Texture2D tempTex = LevelGenerationEngine.CreateProceduralPresetTexture(
                    _selectedPreset,
                    _width,
                    _height,
                    _currentPalette.ToArray());
                LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, tempTex, cfg);
                DestroyImmediate(tempTex);
            }

            _previewDirty = true;
            UpdatePreviewTexture();
        }

        private void GeneratePreviewFromPreset()
        {
            _manualEdited = false;
            _painter.ClearUndo();
            EnsureWorkingLevel();

            Texture2D tempTex = LevelGenerationEngine.CreateProceduralPresetTexture(
                _selectedPreset,
                _width,
                _height,
                _currentPalette.ToArray());

            LevelGenerationEngine.GenerationConfig cfg = BuildConfig();
            LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, tempTex, cfg);
            DestroyImmediate(tempTex);

            _previewDirty = true;
            UpdatePreviewTexture();
            _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
        }

        private void ImportTextureAndExtractPalette(Texture2D tex)
        {
            if (tex == null) return;
            Color32[] extracted = LevelGenerationEngine.ExtractDominantPaletteFromTexture(tex, 12);
            if (extracted != null && extracted.Length > 0)
            {
                _currentPalette.Clear();
                _currentPalette.AddRange(extracted);
            }
            _width = tex.width;
            _height = tex.height;
            _manualEdited = false;
            RebuildWorkingPreviewFromSource();
            ApplyPaletteChangeLive();
            SaveWorkingLevelAsAsset();
        }

        private void AddNewLevelAutomatically(int nextIndex)
        {
            LevelAssetRepository.EnsureLevelsFolder();
            _levelIndex = nextIndex;

            string[] candidateSprites = new[]
            {
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_2.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48P_3.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_4.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_5.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_1.png"
            };

            int candidateOffset = nextIndex - 4;
            if (candidateOffset >= 0 && candidateOffset < candidateSprites.Length)
            {
                Texture2D foundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(candidateSprites[candidateOffset]);
                if (foundTex != null)
                {
                    _sourceTexture = foundTex;
                    string spriteName = System.IO.Path.GetFileNameWithoutExtension(candidateSprites[candidateOffset]);
                    _levelTitle = spriteName.Replace("Sprite_Import_", "Level ");
                }
                else
                {
                    _levelTitle = $"Level {nextIndex:00}";
                }
            }
            else
            {
                _levelTitle = $"Level {nextIndex:00}";
            }

            LevelGenerationEngine.DifficultyProfile diff = LevelGenerationEngine.GetDifficultyProfile(nextIndex);
            _width = diff.GridResolution;
            _height = diff.GridResolution;
            _activeDockCapacity = diff.ActiveDockCapacity;
            _columnCount = diff.ColumnCount;
            _minBucketCapacity = diff.MinBucketCapacity;
            _maxBucketCapacity = diff.MaxBucketCapacity;
            _scrambleFactor = diff.ScrambleFactor;
            _mysteryBucketStartRow = diff.MysteryBucketStartRow;
            _sandFillSpeedMultiplier = diff.SandFillSpeedMultiplier;
            _randomSeed = 100 + nextIndex * 37;

            if (_sourceTexture != null)
            {
                ImportTextureAndExtractPalette(_sourceTexture);
            }
            else
            {
                _selectedPreset = (PixelArtPreset)(nextIndex % 4);
                GeneratePreviewFromPreset();
            }

            _manualEdited = false;
            _painter.ClearUndo();
            Selection.activeObject = null;
            GUI.FocusControl(null);
            Repaint();
        }

        private void SaveWorkingLevelAsAsset()
        {
            LevelAssetRepository.EnsureLevelsFolder();
            EnsureWorkingLevel();

            _workingPreviewLevel.LevelIndex = _levelIndex;
            _workingPreviewLevel.LevelTitle = _levelTitle;
            _workingPreviewLevel.Width = _width;
            _workingPreviewLevel.Height = _height;
            _workingPreviewLevel.Gravity = _gravityMode;
            _workingPreviewLevel.ActiveDockCapacity = _activeDockCapacity;
            _workingPreviewLevel.MysteryBucketStartRow = _mysteryBucketStartRow;
            _workingPreviewLevel.SandFillSpeedMultiplier = _sandFillSpeedMultiplier;
            _workingPreviewLevel.Palette = _currentPalette.ToArray();

            if (_workingPreviewLevel.GridCells == null || _workingPreviewLevel.GridCells.Length != _width * _height)
            {
                if (!_manualEdited)
                {
                    RebuildWorkingPreview();
                }
            }

            RegenerateBucketsFromCurrentGrid();
            _previewDirty = true;
            UpdatePreviewTexture();

            LevelDataSO savedAsset = LevelAssetRepository.SaveLevelAsset(
                _currentLoadedAsset,
                _levelIndex,
                _levelTitle,
                _width,
                _height,
                _gravityMode,
                _activeDockCapacity,
                _mysteryBucketStartRow,
                _sandFillSpeedMultiplier,
                _workingPreviewLevel.Palette,
                _workingPreviewLevel.GridCells,
                _workingPreviewLevel.Columns);

            _currentLoadedAsset = savedAsset;
            EditorPrefs.SetInt("SandSort_SelectedLevelIndex", _levelIndex);

            // Live-refresh running game in Play Mode immediately!
            if (Application.isPlaying)
            {
                GameLoopController loop = FindFirstObjectByType<GameLoopController>();
                if (loop != null)
                {
                    loop.LoadLevel(savedAsset);
                    Debug.Log($"[LevelGeneratorWindow] Çalışan oyundaki seviye anında güncellendi ve yeniden yüklendi!");
                }
            }

            Selection.activeObject = savedAsset;
            EditorGUIUtility.PingObject(savedAsset);
            _lastSavedMessage = $"✓ Level {_levelIndex:00} ('{_levelTitle}') Başarıyla Kaydedildi! ({DateTime.Now:HH:mm:ss})";
            _lastSavedTime = EditorApplication.timeSinceStartup;
            Debug.Log($"[SandSort] {_lastSavedMessage} (Sütun: {savedAsset.Columns.Length}, Kum: {savedAsset.GetTotalGrainCount()})");
            Repaint();
        }

        private void UpdatePreviewTexture()
        {
            LevelGridPainter.UpdatePreviewTexture(_workingPreviewLevel, ref _previewTexture);
            _previewDirty = false;
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
                Palette = _currentPalette.ToArray()
            };
        }
    }
}
