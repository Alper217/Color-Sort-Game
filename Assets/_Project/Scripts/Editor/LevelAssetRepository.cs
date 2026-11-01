using System;
using System.Collections.Generic;
using System.IO;
using SandSort.Data;
using SandSort.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Handles disk persistence, asset management, and scene synchronization for LevelDataSO assets.
    /// Manages Assets/Levels/ folder, PNG sprite exports, sequential renumbering, and GameLoopController sync.
    /// </summary>
    public static class LevelAssetRepository
    {
        public const string PrimaryLevelsFolder = "Assets/Levels";
        public const string SecondaryLevelsFolder = "Assets/_Project/Data/Levels";
        public const string SpritesFolder = "Assets/_Project/Art/PixelSprites";

        /// <summary>
        /// Ensures the primary levels folder exists in the project.
        /// </summary>
        public static void EnsureLevelsFolder()
        {
            if (!AssetDatabase.IsValidFolder(PrimaryLevelsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Levels");
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
            {
                AssetDatabase.CreateFolder("Assets", "_Project");
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Art");
            }

            if (!AssetDatabase.IsValidFolder(SpritesFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art", "PixelSprites");
            }
        }

        /// <summary>
        /// Returns all LevelDataSO assets in the project, sorted by LevelIndex.
        /// </summary>
        public static List<LevelDataSO> GetAllLevelsInProject()
        {
            EnsureLevelsFolder();
            string[] guids = AssetDatabase.FindAssets("t:LevelDataSO", new[] { PrimaryLevelsFolder, SecondaryLevelsFolder });
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

        /// <summary>
        /// Loads a level by its 1-based level index.
        /// </summary>
        public static LevelDataSO LoadLevelByIndex(int levelIndex)
        {
            EnsureLevelsFolder();
            string assetPath = $"{PrimaryLevelsFolder}/Level_{levelIndex:00}.asset";
            LevelDataSO lvl = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            if (lvl == null)
            {
                string fallbackPath = $"{SecondaryLevelsFolder}/Level_{levelIndex:00}.asset";
                lvl = AssetDatabase.LoadAssetAtPath<LevelDataSO>(fallbackPath);
            }
            return lvl;
        }

        /// <summary>
        /// Saves or updates a LevelDataSO asset file on disk, preserving palette, grid, and column definitions.
        /// </summary>
        public static LevelDataSO SaveLevelAsset(
            LevelDataSO existingAsset,
            int levelIndex,
            string title,
            int width,
            int height,
            GravityMode gravity,
            int dockCapacity,
            int mysteryStartRow,
            float speedMult,
            Color32[] palette,
            int[] gridCells,
            BucketColumnData[] columns)
        {
            EnsureLevelsFolder();
            string assetPath = $"{PrimaryLevelsFolder}/Level_{levelIndex:00}.asset";
            LevelDataSO target = existingAsset;

            if (target == null || target.LevelIndex != levelIndex)
            {
                target = AssetDatabase.LoadAssetAtPath<LevelDataSO>(assetPath);
            }

            if (target == null)
            {
                target = ScriptableObject.CreateInstance<LevelDataSO>();
                AssetDatabase.CreateAsset(target, assetPath);
            }

            target.LevelIndex = levelIndex;
            target.LevelTitle = title;
            target.Width = width;
            target.Height = height;
            target.Gravity = gravity;
            target.ActiveDockCapacity = dockCapacity;
            target.MysteryBucketStartRow = mysteryStartRow;
            target.SandFillSpeedMultiplier = speedMult;
            target.Palette = (Color32[])palette.Clone();
            target.GridCells = (int[])gridCells.Clone();
            target.Columns = (columns != null) ? (BucketColumnData[])columns.Clone() : Array.Empty<BucketColumnData>();

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            // Export PNG sprite for thumbnail/preview
            SaveLevelSpritePng(target, levelIndex, title);

            // Synchronize with scene GameLoopController, keeping the current level selected!
            SyncAllLevelsWithGameLoopController(levelIndex - 1);

            return target;
        }

        /// <summary>
        /// Exports the level's grid pixels to a PNG file in Assets/_Project/Art/PixelSprites/.
        /// </summary>
        public static void SaveLevelSpritePng(LevelDataSO level, int levelIndex, string title)
        {
            if (level == null || level.GridCells == null || level.Palette == null) return;
            int pw = level.Width;
            int ph = level.Height;
            if (pw <= 0 || ph <= 0) return;

            try
            {
                Texture2D diskTex = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };

                Color32[] px32 = new Color32[pw * ph];
                for (int py = 0; py < ph; py++)
                {
                    for (int px = 0; px < pw; px++)
                    {
                        int cid = level.GetCell(px, py);
                        if (cid >= 0 && cid < level.Palette.Length)
                        {
                            px32[py * pw + px] = level.Palette[cid];
                        }
                        else
                        {
                            px32[py * pw + px] = new Color32(0, 0, 0, 0);
                        }
                    }
                }

                diskTex.SetPixels32(px32);
                diskTex.Apply();

                string cleanTitle = ToCleanAsciiTitle(title);
                string targetPngPath = $"{SpritesFolder}/Sprite_Level_{levelIndex:D2}_{cleanTitle}.png";

                File.WriteAllBytes(targetPngPath, diskTex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(diskTex);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LevelAssetRepository] PNG sprite kaydedilirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Synchronizes all levels with the GameLoopController in the active scene.
        /// PRESERVES the selected starting level index or sets it to the specified index.
        /// Never blindly resets _startingLevelIndex to 0!
        /// </summary>
        public static void SyncAllLevelsWithGameLoopController(int? preferredStartingIndex = null)
        {
            List<LevelDataSO> allLevels = GetAllLevelsInProject();
            GameLoopController loop = UnityEngine.Object.FindFirstObjectByType<GameLoopController>();
            if (loop == null) return;

            SerializedObject soLoop = new SerializedObject(loop);
            SerializedProperty levelsProp = soLoop.FindProperty("_levels");
            SerializedProperty startIdxProp = soLoop.FindProperty("_startingLevelIndex");

            int currentStartingIdx = startIdxProp.intValue;

            levelsProp.ClearArray();
            for (int i = 0; i < allLevels.Count; i++)
            {
                levelsProp.InsertArrayElementAtIndex(i);
                levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = allLevels[i];
            }

            // Decide new starting index: prefer explicit argument, else preserve current if valid
            int newStartingIdx;
            if (preferredStartingIndex.HasValue)
            {
                newStartingIdx = Mathf.Clamp(preferredStartingIndex.Value, 0, Mathf.Max(0, allLevels.Count - 1));
            }
            else
            {
                newStartingIdx = Mathf.Clamp(currentStartingIdx, 0, Mathf.Max(0, allLevels.Count - 1));
            }

            startIdxProp.intValue = newStartingIdx;

            soLoop.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loop);

            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loop.gameObject.scene);
            }

            Debug.Log($"[LevelAssetRepository] GameLoopController çalma listesi {allLevels.Count} seviye ile senkronize edildi (Başlangıç: #{newStartingIdx + 1}).");
        }

        /// <summary>
        /// Deletes a LevelDataSO asset file from disk.
        /// </summary>
        public static bool DeleteLevel(LevelDataSO level)
        {
            if (level == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(level);
            if (string.IsNullOrEmpty(assetPath)) return false;

            bool confirmed = EditorUtility.DisplayDialog(
                "Seviyeyi Kalıcı Olarak Sil",
                $"Level {level.LevelIndex:00} ('{level.LevelTitle}') silinecek.\n\nDosya:\n{assetPath}\n\nBu işlem geri alınamaz. Emin misiniz?",
                "Evet, Kalıcı Olarak Sil",
                "İptal");

            if (!confirmed) return false;

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SyncAllLevelsWithGameLoopController();
            return true;
        }

        /// <summary>
        /// Renumbers all levels sequentially from 1 to N.
        /// </summary>
        public static void RenumberAllLevelsSequentially()
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
                for (int i = 0; i < allLevels.Count; i++)
                {
                    string curPath = AssetDatabase.GetAssetPath(allLevels[i]);
                    string tempName = $"_temp_renumber_{i}_{Guid.NewGuid().ToString().Substring(0, 8)}";
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
            Debug.Log($"[LevelAssetRepository] {allLevels.Count} seviye başarıyla 1..{allLevels.Count} arasında yeniden numaralandırıldı.");
        }

        /// <summary>
        /// Sanitizes Turkish and special characters for clean cross-platform asset paths.
        /// </summary>
        public static string ToCleanAsciiTitle(string title)
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
    }
}

