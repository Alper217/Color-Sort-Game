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
    /// <summary>
    /// Custom Unity EditorWindow for Module 1 & Level Management:
    /// Converts PNG pixel art or procedural presets into <see cref="LevelDataSO"/> assets in Assets/Levels,
    /// provides an interactive level manager list with auto-increment [+] Add Level button,
    /// previews the quantized sand matrix in 100% full-resolution Point-filtered glory, and runs the 1000x Headless Bot simulator.
    /// </summary>
    public class LevelGeneratorWindow : EditorWindow
    {
        public const string PrimaryLevelsFolder = "Assets/Levels";

        private Texture2D _sourceTexture;
        private PixelArtPreset _selectedPreset = PixelArtPreset.PixelHeart;
        private int _levelIndex = 1;
        private string _levelTitle = "Pixel Heart";
        private int _width = 48;
        private int _height = 48;
        private GravityMode _gravityMode = GravityMode.CellularAutomata;
        private int _backgroundSandColorId = 1;
        private int _activeDockCapacity = 4;
        private int _columnCount = 4;
        private int _minBucketCapacity = 50;
        private int _maxBucketCapacity = 100;
        private float _scrambleFactor = 0.30f;
        private int _randomSeed = 42;
        private int _mysteryBucketStartRow = 99;
        private float _sandFillSpeedMultiplier = 2.4f;
        private int _botRunCount = 1000;
        private float _botGreedyRate = 0.75f;

        private LevelDataSO _workingPreviewLevel;
        private LevelDataSO _currentLoadedAsset;
        private BotSimulationReport _lastBotReport;
        private Vector2 _scrollPos;
        private Vector2 _levelListScrollPos;
        private bool _levelListFolded = true;

        // Full-Resolution Point-Filtered Preview Texture
        private Texture2D _previewTexture;
        private float _previewDisplaySize = 320f;
        private bool _previewDirty = true;
        private List<Color32> _currentPalette = new List<Color32>(GameConstants.DefaultVibrantPalette);
        private bool _paletteFolded = true;

        // Manual pixel painting
        private enum PaintTool { Brush = 0, Fill = 1, Picker = 2 }
        private bool _paintMode;
        private bool _manualEdited;
        private bool _showGrid = true;
        private PaintTool _paintTool = PaintTool.Brush;
        private int _paintColorId;
        private int _brushSize = 1;
        private bool _strokeActive;
        private bool _strokeIsPicker;
        private Vector2Int _lastPaintCell = new Vector2Int(-1, -1);
        private readonly List<int[]> _undoStack = new List<int[]>();
        private const int MaxUndo = 40;

        [MenuItem("SandSort/Level Generator & Bot Simulator", priority = 10)]
        public static void OpenWindow()
        {
            LevelGeneratorWindow window = GetWindow<LevelGeneratorWindow>("SandSort Level Tool");
            window.minSize = new Vector2(600, 750);
            window.Show();
        }

        [MenuItem("SandSort/Generate 3 Showcase Levels Now", priority = 11)]
        public static void GenerateShowcaseLevelsMenu()
        {
            List<LevelDataSO> levels = LevelGenerationEngine.GenerateShowcasePortfolioLevels();
            if (levels.Count > 0)
            {
                SyncAllLevelsWithGameLoopController();
                BotSimulationReport r1 = HeadlessLevelBot.RunSimulation(levels[0], 1000);
                UnityEngine.Debug.Log(
                    $"[SandSort] 3 Showcase Levels generated in {PrimaryLevelsFolder}/! " +
                    $"Level_01 WinRate: {r1.WinRatePercent:F1}% ({r1.ElapsedMilliseconds} ms for 1000 runs).");
                Selection.activeObject = levels[0];
                EditorGUIUtility.PingObject(levels[0]);
            }
        }

        private void OnEnable()
        {
            EnsureLevelsFolder();
            EnsureWorkingLevel();
            GeneratePreviewFromPreset();
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

        private static void EnsureLevelsFolder()
        {
            if (!AssetDatabase.IsValidFolder(PrimaryLevelsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Levels");
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
            EnsureLevelsFolder();
            EnsureWorkingLevel();
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
                "Assets/Levels/ Klasör Yönetimi, Otomatik [+] Yeni Index Ekleme ve 1000x Headless Bot Doğrulayıcı",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawLevelManagerListSection()
        {
            List<LevelDataSO> existingLevels = GetAllLevelsInProject();
            int nextIndex = 1;
            foreach (LevelDataSO l in existingLevels)
            {
                if (l != null && l.LevelIndex >= nextIndex)
                {
                    nextIndex = l.LevelIndex + 1;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: Header Foldout
            GUIStyle boldHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _levelListFolded = EditorGUILayout.Foldout(
                _levelListFolded,
                $"📁 Seviye Çalma Listesi (Assets/Levels) — [{existingLevels.Count} Seviye Kayıtlı]",
                true,
                boldHeader);

            EditorGUILayout.Space(4);

            // Row 2: Prominent Add Level Button
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
                    EditorGUILayout.HelpBox("Henüz Assets/Levels klasöründe seviye bulunmuyor. Yukarıdaki '[+] Yeni Level Ekle' butonuna basarak ilk seviyenizi anında oluşturabilirsiniz.", MessageType.Info);
                }
                else
                {
                    // Roomy, comfortable scroll height: shows 6-8 levels cleanly without squishing
                    float targetHeight = Mathf.Clamp(existingLevels.Count * 36f + 12f, 160f, 300f);
                    _levelListScrollPos = EditorGUILayout.BeginScrollView(_levelListScrollPos, GUILayout.Height(targetHeight));

                    for (int i = 0; i < existingLevels.Count; i++)
                    {
                        LevelDataSO lvl = existingLevels[i];
                        if (lvl == null) continue;

                        bool isCurrent = (lvl.LevelIndex == _levelIndex);

                        // Clean row container with comfortable height
                        Rect rowRect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(30));

                        if (isCurrent)
                        {
                            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), new Color(1f, 0.75f, 0.1f));
                        }

                        // Level badge
                        GUI.backgroundColor = isCurrent ? new Color(1f, 0.85f, 0.25f) : new Color(0.28f, 0.65f, 0.95f);
                        GUILayout.Label($"#{lvl.LevelIndex:00}", EditorStyles.boldLabel, GUILayout.Width(38), GUILayout.Height(24));
                        GUI.backgroundColor = Color.white;

                        // Title & details
                        string gizemInfo = (lvl.MysteryBucketStartRow >= 90)
                            ? "<color=#55bb55>Açık</color>"
                            : (lvl.MysteryBucketStartRow == 1 ? "<color=#ffaa33>Ön Hariç '?'</color>" : $"<color=#ffaa33>{lvl.MysteryBucketStartRow}. Sıra '?'</color>");

                        string info = $"<b>{lvl.LevelTitle}</b>   <color=#999999>[{lvl.Width}x{lvl.Height}] [{lvl.GetTotalGrainCount()} Kum] [Dock: {lvl.ActiveDockCapacity}] [Gizem: {gizemInfo}]</color>";
                        GUILayout.Label(info, new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(24));

                        GUILayout.FlexibleSpace();

                        // Edit / Load button
                        GUI.backgroundColor = isCurrent ? new Color(0.4f, 0.85f, 0.5f) : new Color(0.3f, 0.75f, 0.45f);
                        if (GUILayout.Button(isCurrent ? "Yüklü ✓" : "Düzenle", EditorStyles.miniButtonLeft, GUILayout.Width(65), GUILayout.Height(22)))
                        {
                            LoadLevelIntoEditor(lvl);
                        }
                        GUI.backgroundColor = Color.white;

                        // Ping in project
                        if (GUILayout.Button("Göster", EditorStyles.miniButtonMid, GUILayout.Width(50), GUILayout.Height(22)))
                        {
                            Selection.activeObject = lvl;
                            EditorGUIUtility.PingObject(lvl);
                        }

                        // Delete level button
                        GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);
                        if (GUILayout.Button("Sil ✕", EditorStyles.miniButtonRight, GUILayout.Width(45), GUILayout.Height(22)))
                        {
                            DeleteLevel(lvl);
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
                    SyncAllLevelsWithGameLoopController();
                }
                GUI.backgroundColor = new Color(0.7f, 0.7f, 0.75f);
                if (GUILayout.Button("1..N Olarak Yeniden Sırala", GUILayout.Height(24), GUILayout.Width(170)))
                {
                    RenumberAllLevelsSequentially();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
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
                    SyncAllLevelsWithGameLoopController();
                    LoadLevelIntoEditor(created[0]);
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void ApplyPaletteChangeLive()
        {
            EnsureWorkingLevel();
            _workingPreviewLevel.Palette = _currentPalette.ToArray();
            _previewDirty = true;
            UpdatePreviewTexture();

            // Direct live-sync with the loaded asset or active level index
            LevelDataSO target = _currentLoadedAsset;
            if (target == null)
            {
                string assetPath = $"{PrimaryLevelsFolder}/Level_{_levelIndex:00}.asset";
                target = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            }

            if (target != null)
            {
                target.Palette = (Color32[])_currentPalette.ToArray().Clone();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            Repaint();
        }

        private int CountGrainsForColor(int colorId)
        {
            if (_workingPreviewLevel == null || _workingPreviewLevel.GridCells == null) return 0;
            int count = 0;
            for (int i = 0; i < _workingPreviewLevel.GridCells.Length; i++)
            {
                if (_workingPreviewLevel.GridCells[i] == colorId) count++;
            }
            return count;
        }

        private void CompactPaletteToUsedOnly()
        {
            EnsureWorkingLevel();
            if (_workingPreviewLevel.GridCells == null) return;

            // Find all unique color IDs present in the grid
            List<int> usedOldIds = new List<int>();
            for (int i = 0; i < _workingPreviewLevel.GridCells.Length; i++)
            {
                int cid = _workingPreviewLevel.GridCells[i];
                if (cid >= 0 && !usedOldIds.Contains(cid))
                {
                    usedOldIds.Add(cid);
                }
            }
            usedOldIds.Sort();

            if (usedOldIds.Count == 0 || usedOldIds.Count == _currentPalette.Count) return;

            // Build new palette and mapping dictionary
            Dictionary<int, int> oldToNewMap = new Dictionary<int, int>();
            List<Color32> newPalette = new List<Color32>();
            for (int n = 0; n < usedOldIds.Count; n++)
            {
                int oldId = usedOldIds[n];
                oldToNewMap[oldId] = n;
                if (oldId >= 0 && oldId < _currentPalette.Count)
                    newPalette.Add(_currentPalette[oldId]);
                else
                    newPalette.Add(new Color32(255, 255, 255, 255));
            }

            // Remap GridCells
            for (int i = 0; i < _workingPreviewLevel.GridCells.Length; i++)
            {
                int cid = _workingPreviewLevel.GridCells[i];
                if (oldToNewMap.TryGetValue(cid, out int newId))
                {
                    _workingPreviewLevel.GridCells[i] = newId;
                }
            }

            // Remap Columns & Buckets
            if (_workingPreviewLevel.Columns != null)
            {
                for (int c = 0; c < _workingPreviewLevel.Columns.Length; c++)
                {
                    if (_workingPreviewLevel.Columns[c].Buckets == null) continue;
                    for (int b = 0; b < _workingPreviewLevel.Columns[c].Buckets.Length; b++)
                    {
                        int cid = _workingPreviewLevel.Columns[c].Buckets[b].ColorId;
                        if (oldToNewMap.TryGetValue(cid, out int newId))
                        {
                            _workingPreviewLevel.Columns[c].Buckets[b].ColorId = newId;
                        }
                    }
                }
            }

            _currentPalette = newPalette;
            _workingPreviewLevel.Palette = newPalette.ToArray();

            // Save directly to asset
            LevelDataSO target = _currentLoadedAsset;
            if (target == null)
            {
                string assetPath = $"{PrimaryLevelsFolder}/Level_{_levelIndex:00}.asset";
                target = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            }

            if (target != null)
            {
                target.Palette = (Color32[])newPalette.ToArray().Clone();
                target.GridCells = (int[])_workingPreviewLevel.GridCells.Clone();
                target.Columns = (BucketColumnData[])_workingPreviewLevel.Columns.Clone();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            _previewDirty = true;
            UpdatePreviewTexture();
            Repaint();
        }

        private void SafeRemoveColorFromLevel(int removeIdx)
        {
            if (removeIdx < 0 || removeIdx >= _currentPalette.Count) return;
            EnsureWorkingLevel();

            _currentPalette.RemoveAt(removeIdx);

            // Re-index grid cells
            if (_workingPreviewLevel.GridCells != null)
            {
                for (int i = 0; i < _workingPreviewLevel.GridCells.Length; i++)
                {
                    int cid = _workingPreviewLevel.GridCells[i];
                    if (cid == removeIdx)
                    {
                        _workingPreviewLevel.GridCells[i] = 0;
                    }
                    else if (cid > removeIdx)
                    {
                        _workingPreviewLevel.GridCells[i] = cid - 1;
                    }
                }
            }

            // Re-index bucket queues
            if (_workingPreviewLevel.Columns != null)
            {
                for (int c = 0; c < _workingPreviewLevel.Columns.Length; c++)
                {
                    if (_workingPreviewLevel.Columns[c].Buckets == null) continue;
                    for (int b = 0; b < _workingPreviewLevel.Columns[c].Buckets.Length; b++)
                    {
                        int cid = _workingPreviewLevel.Columns[c].Buckets[b].ColorId;
                        if (cid == removeIdx)
                        {
                            _workingPreviewLevel.Columns[c].Buckets[b].ColorId = 0;
                        }
                        else if (cid > removeIdx)
                        {
                            _workingPreviewLevel.Columns[c].Buckets[b].ColorId = cid - 1;
                        }
                    }
                }
            }

            _workingPreviewLevel.Palette = _currentPalette.ToArray();

            // Direct live-sync with the loaded asset
            LevelDataSO target = _currentLoadedAsset;
            if (target == null)
            {
                string assetPath = $"{PrimaryLevelsFolder}/Level_{_levelIndex:00}.asset";
                target = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            }

            if (target != null)
            {
                target.Palette = (Color32[])_currentPalette.ToArray().Clone();
                target.GridCells = (int[])_workingPreviewLevel.GridCells.Clone();
                target.Columns = (BucketColumnData[])_workingPreviewLevel.Columns.Clone();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            _previewDirty = true;
            UpdatePreviewTexture();
            Repaint();
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

                // Preset Palettes bar
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Hazır Temalar:", EditorStyles.miniLabel, GUILayout.Width(80));
                if (GUILayout.Button("Vibrant", EditorStyles.miniButton))
                {
                    _currentPalette = new List<Color32>(GameConstants.DefaultVibrantPalette);
                    ApplyPaletteChangeLive();
                }
                if (GUILayout.Button("Pastel", EditorStyles.miniButton))
                {
                    _currentPalette = new List<Color32>
                    {
                        new Color32(255, 115, 160, 255), // Candy Pink
                        new Color32(110, 190, 255, 255), // Baby Blue
                        new Color32(255, 225, 90, 255),  // Banana Yellow
                        new Color32(125, 230, 140, 255), // Mint Green
                        new Color32(190, 140, 255, 255), // Lavender
                        new Color32(255, 165, 110, 255)  // Peach Orange
                    };
                    ApplyPaletteChangeLive();
                }
                if (GUILayout.Button("Neon", EditorStyles.miniButton))
                {
                    _currentPalette = new List<Color32>
                    {
                        new Color32(255, 25, 115, 255),  // Neon Pink
                        new Color32(0, 245, 255, 255),   // Electric Cyan
                        new Color32(255, 245, 0, 255),   // Electric Yellow
                        new Color32(40, 255, 80, 255),   // Neon Green
                        new Color32(175, 40, 255, 255),  // Purple Laser
                        new Color32(255, 125, 0, 255)   // Neon Orange
                    };
                    ApplyPaletteChangeLive();
                }
                if (GUILayout.Button("Gün Batımı", EditorStyles.miniButton))
                {
                    _currentPalette = new List<Color32>
                    {
                        new Color32(255, 60, 40, 255),   // Fire Red (Flames)
                        new Color32(50, 120, 220, 255),  // Soft Blue
                        new Color32(255, 205, 10, 255),  // Golden Yellow (Face)
                        new Color32(25, 30, 65, 255),    // Midnight Navy (Sky)
                        new Color32(150, 50, 180, 255),  // Sunset Violet
                        new Color32(255, 140, 0, 255)    // Amber Orange
                    };
                    ApplyPaletteChangeLive();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);

                int totalGrains = (_workingPreviewLevel != null) ? _workingPreviewLevel.GetTotalGrainCount() : 0;
                int unusedColorCount = 0;
                int removeIdx = -1;

                // Palette Swatches with grain count labels
                for (int i = 0; i < _currentPalette.Count; i++)
                {
                    int grains = CountGrainsForColor(i);
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
                    SafeRemoveColorFromLevel(removeIdx);
                }

                EditorGUILayout.Space(3);

                // Helpful actions bar
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
                    if (GUILayout.Button($"✨ Kullanılmayan {unusedColorCount} Rengi Temizle (Yalnızca Görselin {(_currentPalette.Count - unusedColorCount)} Rengini Bırak)", GUILayout.Height(22)))
                    {
                        CompactPaletteToUsedOnly();
                    }
                }

                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawConfigurationSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Görsel Kaynak & Matris Parametreleri", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _levelIndex = EditorGUILayout.IntField("Level Index", _levelIndex);
            if (GUILayout.Button("Sonraki Boş Indexi Bul", GUILayout.Width(150)))
            {
                List<LevelDataSO> existing = GetAllLevelsInProject();
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
                if (_workingPreviewLevel.GridCells == null || _workingPreviewLevel.GridCells.Length == 0)
                {
                    RebuildWorkingPreview();
                }
                _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(0.95f, 0.75f, 0.2f);
            if (GUILayout.Button($"Level_{_levelIndex:00}.asset Olarak Kaydet (Assets/Levels)", GUILayout.Height(32)))
            {
                SaveWorkingLevelAsAsset();
            }
            GUI.backgroundColor = Color.white;

            string currentAssetPath = $"{PrimaryLevelsFolder}/Level_{_levelIndex:00}.asset";
            LevelDataSO existingAssetForCurrent = _currentLoadedAsset ?? AssetDatabase.LoadAssetAtPath<LevelDataSO>(currentAssetPath);
            if (existingAssetForCurrent != null)
            {
                GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);
                if (GUILayout.Button("Seviyeyi Sil ✕", GUILayout.Height(32), GUILayout.Width(110)))
                {
                    DeleteLevel(existingAssetForCurrent);
                }
                GUI.backgroundColor = Color.white;
            }

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
                    $"Kazanma Oranı: {_lastBotReport.WinRatePercent:F1}% ({_lastBotReport.Wins}/{_lastBotReport.TotalRuns} Oyun | Süre: {_lastBotReport.ElapsedMilliseconds} ms)");

                EditorGUILayout.LabelField(
                    $"Ortalama Hamle: {_lastBotReport.AverageMovesOnWin:F1}  |  Deadlock (Kilitlenme): {_lastBotReport.Deadlocks}  |  Karar: {_lastBotReport.GetDifficultyVerdict()}",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Bölümün çözülebilirliğini doğrulamak için 'Simulate Bot Test' butonuna basınız.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMatrixAndQueuePreview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int w = _workingPreviewLevel != null ? _workingPreviewLevel.Width : 48;
            int h = _workingPreviewLevel != null ? _workingPreviewLevel.Height : 48;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Tam Çözünürlüklü Kum Matrisi Önizlemesi ({w}x{h} = {w * h} Kum)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Önizleme Boyutu:", EditorStyles.miniLabel, GUILayout.Width(95));
            _previewDisplaySize = EditorGUILayout.Slider(_previewDisplaySize, 220f, 760f, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            DrawPaintToolbar();

            if (_previewTexture == null || _previewDirty)
            {
                UpdatePreviewTexture();
            }

            // Draw full-resolution 1:1 Point-filtered texture centered
            if (_previewTexture != null)
            {
                EditorGUILayout.Space(4);
                float displaySize = Mathf.Min(_previewDisplaySize, EditorGUIUtility.currentViewWidth - 40f);

                Rect containerRect = GUILayoutUtility.GetRect(displaySize, displaySize, GUILayout.ExpandWidth(true));
                float posX = containerRect.x + Mathf.Max(0f, (containerRect.width - displaySize) * 0.5f);
                Rect drawRect = new Rect(posX, containerRect.y, displaySize, displaySize);

                // Frame background
                EditorGUI.DrawRect(new Rect(drawRect.x - 2, drawRect.y - 2, displaySize + 4, displaySize + 4), new Color(0.12f, 0.12f, 0.15f, 1f));

                // 100% Crisp Point-Filtered Texture (Shows EVERY single pixel in full fidelity!)
                GUI.DrawTexture(drawRect, _previewTexture, ScaleMode.ScaleToFit);
                HandlePainting(drawRect, w, h);

                EditorGUILayout.LabelField(
                    $"1:1 Gerçek Görsel Önizleme: {w}x{h} piksel sanatı tepsisinde hiç piksel atlanmadan çizilmektedir.",
                    EditorStyles.centeredGreyMiniLabel);
            }

            // Bucket Columns Preview below the grid
            if (_workingPreviewLevel.Columns != null)
            {
                EditorGUILayout.Space(8);
                GUILayout.Label("Alt Kova Sütunları (Sol = İlk Çıkacak Ön Kova):", EditorStyles.boldLabel);

                for (int c = 0; c < _workingPreviewLevel.Columns.Length; c++)
                {
                    BucketData[] buckets = _workingPreviewLevel.Columns[c].Buckets;
                    if (buckets == null) continue;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"Sütun {c + 1} ({buckets.Length} kova):", GUILayout.Width(110));

                    int previewCount = Mathf.Min(buckets.Length, 12);
                    for (int b = 0; b < previewCount; b++)
                    {
                        bool isMystery = (b >= _mysteryBucketStartRow);
                        Color prevGui = GUI.backgroundColor;

                        if (isMystery)
                        {
                            GUI.backgroundColor = new Color(0.18f, 0.16f, 0.22f);
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

        // ---------------------------------------------------------------- Manual painting
        private void DrawLevelNavigation(List<LevelDataSO> levels)
        {
            int pos = -1;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].LevelIndex == _levelIndex) { pos = i; break; }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = levels.Count > 0 && pos != 0;
            if (GUILayout.Button("◀ Önceki", GUILayout.Height(24)))
            {
                int target = pos < 0 ? levels.Count - 1 : pos - 1;
                NavigateToLevel(levels[target]);
            }

            GUI.enabled = true;
            GUILayout.Label(
                pos >= 0 ? $"Level {_levelIndex:00}  ({pos + 1}/{levels.Count})" : $"Level {_levelIndex:00}  (kayıtsız)",
                new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter },
                GUILayout.Height(24), GUILayout.MinWidth(140));

            GUI.enabled = levels.Count > 0 && pos != levels.Count - 1;
            if (GUILayout.Button("Sonraki ▶", GUILayout.Height(24)))
            {
                int target = pos < 0 ? 0 : pos + 1;
                NavigateToLevel(levels[target]);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void NavigateToLevel(LevelDataSO level)
        {
            if (level == null) return;
            if (_undoStack.Count > 0 &&
                !EditorUtility.DisplayDialog("Kaydedilmemiş çizim", "Bu level'daki kaydedilmemiş değişiklikler kaybolacak. Devam edilsin mi?", "Devam", "İptal"))
            {
                return;
            }
            LoadLevelIntoEditor(level);
            GUI.FocusControl(null);
            Repaint();
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
            _scrambleFactor = diff.ScrambleFactor;
            _sandFillSpeedMultiplier = diff.SandFillSpeedMultiplier;

            GeneratePreviewFromPreset();
            RegenerateBucketsFromCurrentGrid();
            GUI.FocusControl(null);
        }

        private void RegenerateBucketsFromCurrentGrid()
        {
            EnsureWorkingLevel();
            _workingPreviewLevel.Palette = _currentPalette.ToArray();
            LevelGenerationEngine.GenerateSolvableBucketQueues(_workingPreviewLevel, BuildConfig());
        }

        private void PushUndo()
        {
            if (_workingPreviewLevel == null || _workingPreviewLevel.GridCells == null) return;
            _undoStack.Add((int[])_workingPreviewLevel.GridCells.Clone());
            if (_undoStack.Count > MaxUndo) _undoStack.RemoveAt(0);
        }

        private void UndoPaint()
        {
            if (_undoStack.Count == 0 || _workingPreviewLevel == null) return;
            int[] snap = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (_workingPreviewLevel.GridCells == null || snap.Length != _workingPreviewLevel.GridCells.Length) return;
            _workingPreviewLevel.GridCells = snap;
            RegenerateBucketsFromCurrentGrid();
            _lastBotReport = default;
            _previewDirty = true;
            UpdatePreviewTexture();
            Repaint();
        }

        private void NewBlankCanvas()
        {
            EnsureWorkingLevel();
            _undoStack.Clear();
            _workingPreviewLevel.Width = _width;
            _workingPreviewLevel.Height = _height;
            _workingPreviewLevel.Gravity = _gravityMode;
            _workingPreviewLevel.ActiveDockCapacity = _activeDockCapacity;
            _workingPreviewLevel.Palette = _currentPalette.ToArray();
            int fill = Mathf.Clamp(_backgroundSandColorId, 0, _currentPalette.Count - 1);
            int[] cells = new int[_width * _height];
            for (int i = 0; i < cells.Length; i++) cells[i] = fill;
            _workingPreviewLevel.GridCells = cells;
            _manualEdited = true;
            RegenerateBucketsFromCurrentGrid();
            _lastBotReport = default;
            _previewDirty = true;
            UpdatePreviewTexture();
        }

        private void DrawPaintToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _paintMode = EditorGUILayout.ToggleLeft("✏ Elle Çizim Modu (önizlemeye tıkla / sürükle)", _paintMode, EditorStyles.boldLabel);

            if (_paintMode)
            {
                _paintTool = (PaintTool)GUILayout.Toolbar((int)_paintTool, new[] { "Fırça", "Doldur (Kova)", "Damlalık" }, GUILayout.Height(24));
                if (_paintTool == PaintTool.Brush)
                {
                    _brushSize = EditorGUILayout.IntSlider("Fırça Boyutu", _brushSize, 1, 8);
                }
                _showGrid = EditorGUILayout.Toggle("Izgara Çizgilerini Göster", _showGrid);

                _paintColorId = Mathf.Clamp(_paintColorId, 0, Mathf.Max(0, _currentPalette.Count - 1));
                GUILayout.Label($"Renk: #{_paintColorId}  (Sağ tık veya Alt+tık = damlalık)", EditorStyles.miniLabel);

                const int perRow = 14;
                for (int i = 0; i < _currentPalette.Count; i++)
                {
                    if (i % perRow == 0) EditorGUILayout.BeginHorizontal();

                    Rect r = GUILayoutUtility.GetRect(26, 26, GUILayout.Width(26), GUILayout.Height(26));
                    if (i == _paintColorId)
                    {
                        EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 0.85f, 0.2f));
                    }
                    EditorGUI.DrawRect(r, _currentPalette[i]);
                    if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                    {
                        _paintColorId = i;
                        Event.current.Use();
                        Repaint();
                    }

                    if (i % perRow == perRow - 1 || i == _currentPalette.Count - 1) EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _undoStack.Count > 0;
                if (GUILayout.Button($"↶ Geri Al ({_undoStack.Count})")) UndoPaint();
                GUI.enabled = true;
                if (GUILayout.Button("Boş Tuval (Arka Plan Rengiyle)")) NewBlankCanvas();
                if (GUILayout.Button("Kaynaktan Yeniden Üret (Çizimi Siler)"))
                {
                    _manualEdited = false;
                    _undoStack.Clear();
                    RebuildWorkingPreviewFromSource();
                    _lastBotReport = default;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "Her fırça darbesinden sonra kova sırası otomatik yeniden hesaplanır (çözülebilir kalır). " +
                    "Bittiğinde 'Simulate' ile bot testi yapıp kaydedin.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void HandlePainting(Rect drawRect, int w, int h)
        {
            Event e = Event.current;
            if (w <= 0 || h <= 0) return;

            // Texture is drawn with ScaleToFit: compute the actual centered grid rect
            float cs = Mathf.Min(drawRect.width / w, drawRect.height / h);
            float gw = cs * w;
            float gh = cs * h;
            float x0 = drawRect.x + (drawRect.width - gw) * 0.5f;
            float y0 = drawRect.y + (drawRect.height - gh) * 0.5f;

            if (_showGrid && e.type == EventType.Repaint && cs >= 4f)
            {
                Color line = new Color(0f, 0f, 0f, 0.35f);
                for (int i = 0; i <= w; i++) EditorGUI.DrawRect(new Rect(x0 + i * cs - 0.5f, y0, 1f, gh), line);
                for (int j = 0; j <= h; j++) EditorGUI.DrawRect(new Rect(x0, y0 + j * cs - 0.5f, gw, 1f), line);
            }

            if (!_paintMode || _workingPreviewLevel == null || _workingPreviewLevel.GridCells == null) return;

            int id = GUIUtility.GetControlID(FocusType.Passive);
            EventType type = e.GetTypeForControl(id);

            bool TryGetCell(Vector2 mp, out Vector2Int cell)
            {
                int cx = Mathf.FloorToInt((mp.x - x0) / cs);
                int cyTop = Mathf.FloorToInt((mp.y - y0) / cs);
                cell = new Vector2Int(cx, h - 1 - cyTop);
                return cx >= 0 && cx < w && cyTop >= 0 && cyTop < h;
            }

            switch (type)
            {
                case EventType.MouseDown:
                {
                    if ((e.button == 0 || e.button == 1) && TryGetCell(e.mousePosition, out Vector2Int cell))
                    {
                        GUIUtility.hotControl = id;
                        _strokeIsPicker = e.button == 1 || e.alt || _paintTool == PaintTool.Picker;
                        if (_strokeIsPicker)
                        {
                            PickColorAt(cell, w);
                        }
                        else
                        {
                            PushUndo();
                            _strokeActive = true;
                            if (_paintTool == PaintTool.Fill) FloodFill(cell, w, h);
                            else PaintBrush(cell, w, h);
                            _lastPaintCell = cell;
                            MarkPaintedPreview();
                        }
                        e.Use();
                    }
                    break;
                }
                case EventType.MouseDrag:
                {
                    if (GUIUtility.hotControl == id && TryGetCell(e.mousePosition, out Vector2Int cell))
                    {
                        if (_strokeIsPicker)
                        {
                            PickColorAt(cell, w);
                        }
                        else if (_paintTool == PaintTool.Brush && cell != _lastPaintCell)
                        {
                            PaintLine(_lastPaintCell, cell, w, h);
                            _lastPaintCell = cell;
                            MarkPaintedPreview();
                        }
                        e.Use();
                    }
                    break;
                }
                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        if (_strokeActive)
                        {
                            _strokeActive = false;
                            _manualEdited = true;
                            RegenerateBucketsFromCurrentGrid();
                            _lastBotReport = default;
                        }
                        e.Use();
                        Repaint();
                    }
                    break;
                }
            }
        }

        private void MarkPaintedPreview()
        {
            _previewDirty = true;
            UpdatePreviewTexture();
            Repaint();
        }

        private void PickColorAt(Vector2Int cell, int w)
        {
            int c = _workingPreviewLevel.GridCells[cell.y * w + cell.x];
            if (c >= 0) _paintColorId = c;
            Repaint();
        }

        private void PaintBrush(Vector2Int center, int w, int h)
        {
            int[] cells = _workingPreviewLevel.GridCells;
            int id = Mathf.Clamp(_paintColorId, 0, Mathf.Max(0, _currentPalette.Count - 1));
            int start = -(_brushSize - 1) / 2;
            for (int dy = 0; dy < _brushSize; dy++)
            {
                for (int dx = 0; dx < _brushSize; dx++)
                {
                    int x = center.x + start + dx;
                    int y = center.y + start + dy;
                    if (x < 0 || x >= w || y < 0 || y >= h) continue;
                    cells[y * w + x] = id;
                }
            }
        }

        private void PaintLine(Vector2Int a, Vector2Int b, int w, int h)
        {
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            if (steps == 0) { PaintBrush(b, w, h); return; }
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                PaintBrush(new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t))), w, h);
            }
        }

        private void FloodFill(Vector2Int start, int w, int h)
        {
            int[] cells = _workingPreviewLevel.GridCells;
            int fillId = Mathf.Clamp(_paintColorId, 0, Mathf.Max(0, _currentPalette.Count - 1));
            int target = cells[start.y * w + start.x];
            if (target == fillId) return;

            Stack<int> stack = new Stack<int>();
            stack.Push(start.y * w + start.x);
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                if (cells[idx] != target) continue;
                cells[idx] = fillId;
                int x = idx % w;
                int y = idx / w;
                if (x > 0) stack.Push(idx - 1);
                if (x < w - 1) stack.Push(idx + 1);
                if (y > 0) stack.Push(idx - w);
                if (y < h - 1) stack.Push(idx + w);
            }
        }

        private void UpdatePreviewTexture()
        {
            if (_workingPreviewLevel == null || _workingPreviewLevel.GridCells == null) return;
            int w = _workingPreviewLevel.Width;
            int h = _workingPreviewLevel.Height;
            if (w <= 0 || h <= 0) return;

            if (_previewTexture == null || _previewTexture.width != w || _previewTexture.height != h)
            {
                if (_previewTexture != null) DestroyImmediate(_previewTexture);
                _previewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int colorId = _workingPreviewLevel.GetCell(x, y);
                    if (colorId >= 0)
                    {
                        px[y * w + x] = _workingPreviewLevel.GetColor(colorId);
                    }
                    else
                    {
                        bool checker = ((x + y) & 1) == 0;
                        px[y * w + x] = checker ? new Color32(30, 28, 38, 255) : new Color32(42, 38, 52, 255);
                    }
                }
            }

            _previewTexture.SetPixels32(px);
            _previewTexture.Apply();
            _previewDirty = false;
        }

        private void GeneratePreviewFromPreset()
        {
            _manualEdited = false;
            _undoStack.Clear();
            GeneratePreviewFromPresetCore();
        }

        private void GeneratePreviewFromPresetCore()
        {
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

        private void RebuildWorkingPreview()
        {
            // Hand-painted / loaded grids must not be overwritten: only recompute the bucket queues.
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
                    _currentPalette.ToArray());
                LevelGenerationEngine.PopulateLevelFromTexture(_workingPreviewLevel, tempTex, cfg);
                DestroyImmediate(tempTex);
            }

            _previewDirty = true;
            UpdatePreviewTexture();
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

        public void LoadLevelIntoEditor(LevelDataSO level)
        {
            if (level == null) return;
            EnsureWorkingLevel();

            _currentLoadedAsset = level;
            _levelIndex = level.LevelIndex;
            _levelTitle = level.LevelTitle;
            _width = level.Width;
            _height = level.Height;
            _gravityMode = level.Gravity;
            _activeDockCapacity = level.ActiveDockCapacity;
            _mysteryBucketStartRow = (level.MysteryBucketStartRow > 0) ? level.MysteryBucketStartRow : 99;
            _sandFillSpeedMultiplier = (level.SandFillSpeedMultiplier > 0.05f) ? level.SandFillSpeedMultiplier : 2.4f;
            _columnCount = (level.Columns != null && level.Columns.Length > 0) ? level.Columns.Length : 4;

            if (level.Palette != null && level.Palette.Length > 0)
            {
                _currentPalette = new List<Color32>(level.Palette);
            }
            else
            {
                _currentPalette = new List<Color32>(GameConstants.DefaultVibrantPalette);
            }

            // Restore matching preset or texture reference
            if (level.LevelTitle == "Royal Crown" || level.LevelTitle.Contains("Crown"))
            {
                _selectedPreset = PixelArtPreset.RoyalCrown;
                _sourceTexture = null;
            }
            else if (level.LevelTitle == "Magic Potion" || level.LevelTitle.Contains("Potion"))
            {
                _selectedPreset = PixelArtPreset.MagicPotion;
                _sourceTexture = null;
            }
            else if (level.LevelTitle == "Pixel Heart" || level.LevelTitle.Contains("Heart"))
            {
                _selectedPreset = PixelArtPreset.PixelHeart;
                _sourceTexture = null;
            }
            else if (level.LevelTitle == "Layered Gem" || level.LevelTitle.Contains("Gem"))
            {
                _selectedPreset = PixelArtPreset.LayeredGem;
                _sourceTexture = null;
            }
            else
            {
                // Try to find sprite by name
                string[] allSprites = Directory.GetFiles("Assets/_Project/Art/PixelSprites", "*.png");
                foreach (string sPath in allSprites)
                {
                    string fName = Path.GetFileNameWithoutExtension(sPath);
                    if (level.LevelTitle.Contains(fName) || fName.Contains($"Level_{level.LevelIndex:02}"))
                    {
                        _sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sPath.Replace('\\', '/'));
                        break;
                    }
                }
            }

            _workingPreviewLevel.LevelIndex = level.LevelIndex;
            _workingPreviewLevel.LevelTitle = level.LevelTitle;
            _workingPreviewLevel.Width = level.Width;
            _workingPreviewLevel.Height = level.Height;
            _workingPreviewLevel.Gravity = level.Gravity;
            _workingPreviewLevel.ActiveDockCapacity = level.ActiveDockCapacity;
            _workingPreviewLevel.MysteryBucketStartRow = level.MysteryBucketStartRow;
            _workingPreviewLevel.Palette = (Color32[])level.Palette.Clone();
            _workingPreviewLevel.GridCells = (int[])level.GridCells.Clone();
            _workingPreviewLevel.Columns = (BucketColumnData[])level.Columns.Clone();
            _manualEdited = true; // keep the loaded grid; 'Yeniden Hesapla' only rebuilds buckets
            _undoStack.Clear();

            _previewDirty = true;
            UpdatePreviewTexture();
            _lastBotReport = HeadlessLevelBot.RunSimulation(_workingPreviewLevel, _botRunCount, _botGreedyRate);
            Selection.activeObject = level;
            EditorGUIUtility.PingObject(level);
        }

        private void AddNewLevelAutomatically(int nextIndex)
        {
            EnsureLevelsFolder();
            _levelIndex = nextIndex;

            // Search for unassigned imported sprites in Assets/_Project/Art/PixelSprites/
            string[] candidateSprites = new[]
            {
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_2.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48P_3.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_4.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_5.png",
                "Assets/_Project/Art/PixelSprites/Sprite_Import_48PX_1.png"
            };

            int candidateOffset = nextIndex - 4; // Level 4 -> Sprite 2, Level 5 -> Sprite 3...
            if (candidateOffset >= 0 && candidateOffset < candidateSprites.Length)
            {
                Texture2D foundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(candidateSprites[candidateOffset]);
                if (foundTex != null)
                {
                    _sourceTexture = foundTex;
                    string spriteName = Path.GetFileNameWithoutExtension(candidateSprites[candidateOffset]);
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

            // Apply smart studio-grade progressive difficulty defaults
            LevelGenerationEngine.DifficultyProfile diff = LevelGenerationEngine.GetDifficultyProfile(nextIndex);
            _width = diff.GridResolution;
            // Progressive difficulty: 2 columns / few big buckets -> 5 columns / many small buckets
            _height = diff.GridResolution;
            _columnCount = diff.ColumnCount;
            _minBucketCapacity = diff.MinBucketCapacity;
            _maxBucketCapacity = diff.MaxBucketCapacity;
            _activeDockCapacity = diff.ActiveDockCapacity;
            _mysteryBucketStartRow = diff.MysteryBucketStartRow;
            _scrambleFactor = diff.ScrambleFactor;
            _sandFillSpeedMultiplier = diff.SandFillSpeedMultiplier;

            SaveWorkingLevelAsAsset();
            SyncAllLevelsWithGameLoopController();
        }

        private void SaveWorkingLevelAsAsset()
        {
            EnsureLevelsFolder();
            if (_workingPreviewLevel.GridCells == null || _workingPreviewLevel.GridCells.Length == 0)
            {
                RebuildWorkingPreview();
            }
            else
            {
                _workingPreviewLevel.Palette = _currentPalette.ToArray();
                _previewDirty = true;
                UpdatePreviewTexture();
            }

            string assetPath = $"{PrimaryLevelsFolder}/Level_{_levelIndex:00}.asset";
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
            asset.MysteryBucketStartRow = _mysteryBucketStartRow;
            asset.SandFillSpeedMultiplier = _sandFillSpeedMultiplier;
            _workingPreviewLevel.SandFillSpeedMultiplier = _sandFillSpeedMultiplier;
            asset.Palette = (Color32[])_workingPreviewLevel.Palette.Clone();
            asset.GridCells = (int[])_workingPreviewLevel.GridCells.Clone();
            asset.Columns = (BucketColumnData[])_workingPreviewLevel.Columns.Clone();

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _currentLoadedAsset = asset;
            SyncAllLevelsWithGameLoopController();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            UnityEngine.Debug.Log($"[SandSort] Seviye '{assetPath}' (Assets/Levels) başarıyla kaydedildi! (Kum: {asset.GetTotalGrainCount()}, WinRate: %{_lastBotReport.WinRatePercent:F1})");
        }

        public static List<LevelDataSO> GetAllLevelsInProject()
        {
            EnsureLevelsFolder();
            string[] guids = AssetDatabase.FindAssets("t:LevelDataSO", new[] { PrimaryLevelsFolder, "Assets/_Project/Data/Levels" });
            List<LevelDataSO> list = new List<LevelDataSO>();
            HashSet<string> seen = new HashSet<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (seen.Add(path))
                {
                    LevelDataSO so = AssetDatabase.LoadAssetAtPath<LevelDataSO>(path);
                    if (so != null)
                    {
                        list.Add(so);
                    }
                }
            }

            list.Sort((a, b) => a.LevelIndex.CompareTo(b.LevelIndex));
            return list;
        }

        private void DeleteLevel(LevelDataSO level)
        {
            if (level == null) return;

            string assetPath = AssetDatabase.GetAssetPath(level);
            bool confirmed = EditorUtility.DisplayDialog(
                "Seviyeyi Kalıcı Olarak Sil",
                $"Level {level.LevelIndex:00} ('{level.LevelTitle}') silinecek.\n\nDosya:\n{assetPath}\n\nBu işlem geri alınamaz. Emin misiniz?",
                "Evet, Sil",
                "İptal");

            if (!confirmed) return;

            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            SyncAllLevelsWithGameLoopController();

            List<LevelDataSO> remaining = GetAllLevelsInProject();
            if (_currentLoadedAsset == level || _currentLoadedAsset == null || _levelIndex == level.LevelIndex)
            {
                if (remaining.Count > 0)
                {
                    LevelDataSO nextToLoad = remaining.Find(l => l.LevelIndex >= level.LevelIndex) ?? remaining[remaining.Count - 1];
                    LoadLevelIntoEditor(nextToLoad);
                }
                else
                {
                    _currentLoadedAsset = null;
                    _workingPreviewLevel = null;
                    EnsureWorkingLevel();
                    _previewDirty = true;
                    UpdatePreviewTexture();
                }
            }

            GUI.FocusControl(null);
            Repaint();
            UnityEngine.Debug.Log($"[SandSort] Seviye '{level.LevelTitle}' (Level {level.LevelIndex:00}) başarıyla silindi ve çalma listesi güncellendi.");
        }

        private void RenumberAllLevelsSequentially()
        {
            List<LevelDataSO> allLevels = GetAllLevelsInProject();
            if (allLevels.Count == 0) return;

            bool confirm = EditorUtility.DisplayDialog(
                "Seviyeleri Yeniden Sırala",
                $"Mevcut {allLevels.Count} seviye sırayla Level 1'den başlayarak yeniden numaralandırılacak (Level_01, Level_02...).\n\nEmin misiniz?",
                "Evet, Sırala",
                "İptal");
            if (!confirm) return;

            AssetDatabase.StartAssetEditing();
            try
            {
                // Pass 1: rename to temporary names to avoid collision
                for (int i = 0; i < allLevels.Count; i++)
                {
                    string curPath = AssetDatabase.GetAssetPath(allLevels[i]);
                    string tempName = $"_temp_renumber_{i}_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
                    AssetDatabase.RenameAsset(curPath, tempName);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            allLevels = GetAllLevelsInProject();
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < allLevels.Count; i++)
                {
                    LevelDataSO lvl = allLevels[i];
                    int newIndex = i + 1;
                    lvl.LevelIndex = newIndex;
                    EditorUtility.SetDirty(lvl);

                    string curPath = AssetDatabase.GetAssetPath(lvl);
                    string targetName = $"Level_{newIndex:00}";
                    AssetDatabase.RenameAsset(curPath, targetName);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            SyncAllLevelsWithGameLoopController();
            List<LevelDataSO> refreshed = GetAllLevelsInProject();
            if (refreshed.Count > 0)
            {
                LoadLevelIntoEditor(refreshed[0]);
            }
            Repaint();
            UnityEngine.Debug.Log($"[SandSort] {allLevels.Count} seviye başarıyla 1..{allLevels.Count} arasında yeniden numaralandırıldı.");
        }

        public static void SyncAllLevelsWithGameLoopController()
        {
            List<LevelDataSO> allLevels = GetAllLevelsInProject();
            SandSort.Gameplay.GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<SandSort.Gameplay.GameLoopController>();
            if (loop != null)
            {
                SerializedObject soLoop = new SerializedObject(loop);
                SerializedProperty levelsProp = soLoop.FindProperty("_levels");
                levelsProp.ClearArray();
                for (int i = 0; i < allLevels.Count; i++)
                {
                    levelsProp.InsertArrayElementAtIndex(i);
                    levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = allLevels[i];
                }
                soLoop.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(loop);
                if (!Application.isPlaying)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loop.gameObject.scene);
                }
                UnityEngine.Debug.Log($"[SandSort] GameLoopController çalma listesi {allLevels.Count} seviye ile senkronize edildi!");
            }
        }
    }
}
