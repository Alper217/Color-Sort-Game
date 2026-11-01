using System.IO;
using SandSort.Data;
using SandSort.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Custom Inspector for <see cref="LevelDataSO"/>:
    /// Ensures that any manual changes made to the palette or parameters in the Unity Inspector
    /// are immediately saved, synced with solvable bucket queues, and live-reloaded into the running game!
    /// </summary>
    [CustomEditor(typeof(LevelDataSO))]
    public class LevelDataSOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            LevelDataSO level = (LevelDataSO)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(level);
                AssetDatabase.SaveAssets();

                // Live-refresh running game in Play Mode immediately!
                if (Application.isPlaying)
                {
                    GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
                    if (loop != null)
                    {
                        loop.LoadLevel(level);
                        Debug.Log($"[LevelDataSOEditor] '{level.name}' değişiklikleri çalışan oyuna anında uygulandı!");
                    }
                }
                else
                {
                    // Do not automatically alter starting level on property change
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("⚡ Hızlı İşlemler & Canlı Kontroller", EditorStyles.boldLabel);

            // Button 1: Play This Level Now
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
            string playBtnText = Application.isPlaying
                ? "▶ Bu Seviyeyi Oyunda Anında Başlat (Canlı Yükle)"
                : "▶ Bu Seviyeyi Başlangıç Seviyesi Yap (GameLoopController)";
            if (GUILayout.Button(playBtnText, GUILayout.Height(30)))
            {
                PlayLevelNow(level);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(3);

            // Button 2: Restore Original Sprite Colors
            GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
            if (GUILayout.Button("🎨 Orijinal Sprite Renklerini Yükle & Eşitle", GUILayout.Height(26)))
            {
                RestoreOriginalColorsForLevel(level);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(3);

            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⟲ 90° Sola", GUILayout.Height(24)))
            {
                RotateLevelSO(level, -90);
            }
            if (GUILayout.Button("⟳ 90° Sağa", GUILayout.Height(24)))
            {
                RotateLevelSO(level, 90);
            }
            if (GUILayout.Button("↻ 180°", GUILayout.Height(24)))
            {
                RotateLevelSO(level, 180);
            }
            if (GUILayout.Button("⇄ Yatay", GUILayout.Height(24)))
            {
                FlipLevelSOHorizontal(level);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            // Button 3: Open in Level Generator Tool
            if (GUILayout.Button("🛠 SandSort Level Generator Aracında Düzenle", GUILayout.Height(24)))
            {
                LevelGeneratorWindow window = EditorWindow.GetWindow<LevelGeneratorWindow>("SandSort Level Tool");
                window.Show();
                window.LoadLevelIntoEditor(level);
            }

            EditorGUILayout.EndVertical();
        }

        public static void RotateLevelSO(LevelDataSO level, int degrees)
        {
            if (level == null || level.GridCells == null) return;
            Undo.RecordObject(level, "Rotate Level");

            int w = level.Width;
            int h = level.Height;
            int[] oldCells = level.GridCells;
            int[] newCells = new int[w * h];

            if (degrees == 90) // 90 deg clockwise
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int nx = y;
                        int ny = w - 1 - x;
                        if (nx >= 0 && nx < h && ny >= 0 && ny < w)
                            newCells[ny * h + nx] = oldCells[y * w + x];
                    }
                }
                if (w != h) { level.Width = h; level.Height = w; }
            }
            else if (degrees == -90) // 90 deg counter-clockwise
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int nx = h - 1 - y;
                        int ny = x;
                        if (nx >= 0 && nx < h && ny >= 0 && ny < w)
                            newCells[ny * h + nx] = oldCells[y * w + x];
                    }
                }
                if (w != h) { level.Width = h; level.Height = w; }
            }
            else if (degrees == 180)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int nx = w - 1 - x;
                        int ny = h - 1 - y;
                        newCells[ny * w + nx] = oldCells[y * w + x];
                    }
                }
            }

            level.GridCells = newCells;
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();

            if (Application.isPlaying)
            {
                GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
                if (loop != null) loop.LoadLevel(level);
            }
            Debug.Log($"[SandSort] '{level.name}' {degrees}° döndürüldü ve kaydedildi!");
        }

        public static void FlipLevelSOHorizontal(LevelDataSO level)
        {
            if (level == null || level.GridCells == null) return;
            Undo.RecordObject(level, "Flip Level Horizontal");

            int w = level.Width;
            int h = level.Height;
            int[] oldCells = level.GridCells;
            int[] newCells = new int[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int nx = w - 1 - x;
                    newCells[y * w + nx] = oldCells[y * w + x];
                }
            }

            level.GridCells = newCells;
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();

            if (Application.isPlaying)
            {
                GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
                if (loop != null) loop.LoadLevel(level);
            }
            Debug.Log($"[SandSort] '{level.name}' yatay aynalandı ve kaydedildi!");
        }

        public static void SetSceneStartingLevel(LevelDataSO level)
        {
            if (level == null) return;
            GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
            if (loop != null)
            {
                SerializedObject so = new SerializedObject(loop);
                SerializedProperty levelsProp = so.FindProperty("_levels");
                int targetIdx = -1;
                for (int i = 0; i < levelsProp.arraySize; i++)
                {
                    if (levelsProp.GetArrayElementAtIndex(i).objectReferenceValue == level)
                    {
                        targetIdx = i;
                        break;
                    }
                }

                if (targetIdx >= 0)
                {
                    so.FindProperty("_startingLevelIndex").intValue = targetIdx;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(loop);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loop.gameObject.scene);
                }
            }
        }

        public static void PlayLevelNow(LevelDataSO level)
        {
            if (level == null) return;

            GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
            if (Application.isPlaying)
            {
                if (loop != null)
                {
                    loop.LoadLevel(level);
                    Debug.Log($"[SandSort] Seviye #{level.LevelIndex} ('{level.LevelTitle}') oyuna canlı yüklendi!");
                }
            }
            else
            {
                // In Edit Mode, set _startingLevelIndex on GameLoopController in the scene
                if (loop != null)
                {
                    SerializedObject so = new SerializedObject(loop);
                    SerializedProperty levelsProp = so.FindProperty("_levels");
                    int targetIdx = -1;
                    for (int i = 0; i < levelsProp.arraySize; i++)
                    {
                        if (levelsProp.GetArrayElementAtIndex(i).objectReferenceValue == level)
                        {
                            targetIdx = i;
                            break;
                        }
                    }

                    if (targetIdx >= 0)
                    {
                        so.FindProperty("_startingLevelIndex").intValue = targetIdx;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(loop);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loop.gameObject.scene);
                        Debug.Log($"[SandSort] GameLoopController başlangıç seviyesi Seviye #{level.LevelIndex} ('{level.LevelTitle}') olarak ayarlandı. Oyun anında başlatılıyor!");
                        EditorApplication.isPlaying = true;
                    }
                }
            }
        }

        public static void RestoreOriginalColorsForLevel(LevelDataSO level)
        {
            if (level == null) return;

            // 1. Check matching sprite PNG first (Highest Priority for custom/imported art)
            string[] sprites = Directory.GetFiles("Assets/_Project/Art/PixelSprites", "*.png");
            foreach (string sPath in sprites)
            {
                string fName = Path.GetFileNameWithoutExtension(sPath);
                if (fName.StartsWith($"Sprite_Level_{level.LevelIndex:D2}") || fName.StartsWith($"Sprite_Level_{level.LevelIndex:00}"))
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sPath.Replace('\\', '/'));
                    if (tex != null)
                    {
                        Color32[] dominant = LevelGenerationEngine.ExtractDominantPaletteFromTexture(tex, 12);
                        if (dominant != null && dominant.Length > 0)
                        {
                            level.Palette = dominant;
                            LevelGenerationEngine.DifficultyProfile diff = LevelGenerationEngine.GetDifficultyProfile(level.LevelIndex);
                            LevelGenerationEngine.GenerateSolvableBucketQueues(level, new LevelGenerationEngine.GenerationConfig
                            {
                                Width = level.Width,
                                Height = level.Height,
                                Gravity = level.Gravity,
                                ActiveDockCapacity = level.ActiveDockCapacity,
                                ColumnCount = (level.Columns != null && level.Columns.Length > 0) ? level.Columns.Length : diff.ColumnCount,
                                MinBucketCapacity = diff.MinBucketCapacity,
                                MaxBucketCapacity = diff.MaxBucketCapacity,
                                ScrambleFactor = diff.ScrambleFactor,
                                RandomSeed = 42 + level.LevelIndex,
                                Palette = dominant
                            });

                            EditorUtility.SetDirty(level);
                            AssetDatabase.SaveAssets();

                            if (Application.isPlaying)
                            {
                                GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
                                if (loop != null) loop.LoadLevel(level);
                            }
                            Debug.Log($"[LevelDataSOEditor] '{tex.name}' görselinden {dominant.Length} adet orijinal renk seviyeye uygulandı ve kaydedildi!");
                            return;
                        }
                    }
                }
            }

            // 2. Check Master Studio Blueprint as secondary fallback
            try
            {
                MasterStudioLevelGenerator.StudioLevelBlueprint[] bps = MasterStudioLevelGenerator.GetMasterBlueprints();
                foreach (MasterStudioLevelGenerator.StudioLevelBlueprint bp in bps)
                {
                    if (bp.Index == level.LevelIndex)
                    {
                        level.Palette = (Color32[])bp.Palette.Clone();
                        LevelGenerationEngine.DifficultyProfile diff = LevelGenerationEngine.GetDifficultyProfile(level.LevelIndex);
                        LevelGenerationEngine.GenerateSolvableBucketQueues(level, new LevelGenerationEngine.GenerationConfig
                        {
                            Width = level.Width,
                            Height = level.Height,
                            Gravity = level.Gravity,
                            ActiveDockCapacity = level.ActiveDockCapacity,
                            ColumnCount = (level.Columns != null && level.Columns.Length > 0) ? level.Columns.Length : diff.ColumnCount,
                            MinBucketCapacity = diff.MinBucketCapacity,
                            MaxBucketCapacity = diff.MaxBucketCapacity,
                            ScrambleFactor = diff.ScrambleFactor,
                            RandomSeed = 42 + level.LevelIndex,
                            Palette = level.Palette
                        });

                        EditorUtility.SetDirty(level);
                        AssetDatabase.SaveAssets();

                        if (Application.isPlaying)
                        {
                            GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
                            if (loop != null) loop.LoadLevel(level);
                        }
                        Debug.Log($"[LevelDataSOEditor] Seviye #{level.LevelIndex} ('{level.LevelTitle}') için stüdyo paleti uygulandı ve kaydedildi!");
                        return;
                    }
                }
            }
            catch { }
        }
    }
}
