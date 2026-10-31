using System.Collections.Generic;
using SandSort.Core;
using SandSort.Data;
using SandSort.Gameplay;
using SandSort.Gameplay.Containers;
using SandSort.Gameplay.Grid;
using SandSort.Gameplay.Transfer;
using SandSort.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SandSort.EditorTools
{
    /// <summary>
    /// Studio-standard Static Scene Layout Builder:
    /// Configures Royal Indigo/Purple-Blue theme, disables shadow artifacts on trays/slots,
    /// and wires the permanent scene skeleton.
    /// </summary>
    public static class SceneSetupTool
    {
        [MenuItem("SandSort/Setup Playable Test Scene (Auto-Wire Prefabs)", priority = 1)]
        public static void SetupPlayableScene()
        {
            // 1. Regenerate 3 showcase levels (28x28 = 784 grains, 100% full tray with background sand color & Cellular Automata)
            List<LevelDataSO> levels = LevelGenerationEngine.GenerateShowcasePortfolioLevels();

            // 2. Load User Greybox Prefabs from Assets/Prefabs/
            GameObject grainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SandGrain.prefab");
            GameObject bucketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bucket.prefab");
            GameObject dockSlotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DockSlot.prefab");
            GameObject boardFramePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GridBoardFrame.prefab");

            if (bucketPrefab != null && bucketPrefab.GetComponent<BucketView>() == null)
            {
                bucketPrefab.AddComponent<BucketView>();
                EditorUtility.SetDirty(bucketPrefab);
            }
            if (grainPrefab != null && grainPrefab.GetComponent<SandGrainView>() == null)
            {
                grainPrefab.AddComponent<SandGrainView>();
                EditorUtility.SetDirty(grainPrefab);
            }

            // 3. Configure Main Camera for 3D Perspective Portrait Framing (Reveals true 3D depth of tray, slots & buckets!)
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            mainCam.orthographic = false;
            mainCam.fieldOfView = 36f;
            mainCam.transform.position = new Vector3(0f, 0.95f, -13.6f);
            mainCam.transform.rotation = Quaternion.Euler(3.2f, 0f, 0f);
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color32(36, 27, 75, 255);

            // 4. Clean up only the runtime managers if re-running; NEVER destroy existing Static Environment or Custom UI Canvas!
            DestroyExistingIfFound("--- SANDSORT GAMEPLAY ---");
            DestroyExistingIfFound("--- SCENE LAYOUT ANCHORS ---");

            const int slotCount = 4;
            const float horizontalSpacing = 0.96f;
            float startX = -((slotCount - 1) * horizontalSpacing) * 0.5f;

            // 5. Reuse existing Static Environment if the user already customized it; only create if missing!
            GameObject envRoot = GameObject.Find("--- STATIC ENVIRONMENT (FIXED) ---");
            GameObject gridAnchor;
            Transform frameInstanceTransform = null;
            GameObject dockAnchor;
            Transform[] fixedSlotTransforms = new Transform[slotCount];
            GameObject queuesAnchor;
            Transform[] fixedColumnRoots = new Transform[slotCount];
            GameObject transferAnchor;

            if (envRoot != null)
            {
                gridAnchor = envRoot.transform.Find("1_GridBoardCenter")?.gameObject ?? envRoot;
                frameInstanceTransform = gridAnchor.transform.Find("Static_GridBoardFrame");
                dockAnchor = envRoot.transform.Find("2_ActiveDockSlots_Fixed")?.gameObject ?? envRoot;
                for (int i = 0; i < slotCount; i++)
                {
                    fixedSlotTransforms[i] = dockAnchor.transform.Find($"DockSlot_Fixed_{i + 1}");
                }
                queuesAnchor = envRoot.transform.Find("3_BucketQueueColumns_Fixed")?.gameObject ?? envRoot;
                for (int c = 0; c < slotCount; c++)
                {
                    fixedColumnRoots[c] = queuesAnchor.transform.Find($"ColumnSpawn_Fixed_{c + 1}");
                }
                transferAnchor = envRoot.transform.Find("4_SandTransferAnchor")?.gameObject ?? envRoot;
            }
            else
            {
                envRoot = new GameObject("--- STATIC ENVIRONMENT (FIXED) ---");

                // 5a. Top Sand Board Area & Frame
                gridAnchor = new GameObject("1_GridBoardCenter");
                gridAnchor.transform.SetParent(envRoot.transform, false);
                gridAnchor.transform.localPosition = new Vector3(0f, 1.95f, 0f);

                if (boardFramePrefab != null)
                {
                    GameObject frameInst = (GameObject)PrefabUtility.InstantiatePrefab(boardFramePrefab, gridAnchor.transform);
                    frameInst.name = "Static_GridBoardFrame";
                    frameInst.transform.localPosition = new Vector3(0f, 0f, 0.35f);
                    frameInst.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    frameInst.transform.localScale = new Vector3(3.75f, 0.18f, 3.75f);
                    TintHierarchyRenderers(frameInst, new Color32(24, 19, 52, 255), new Color32(82, 71, 138, 255));
                    frameInstanceTransform = frameInst.transform;
                }

                // 5b. Middle Active Dock Area (4 Fixed DockSlot Prefabs placed side-by-side facing the camera)
                dockAnchor = new GameObject("2_ActiveDockSlots_Fixed");
                dockAnchor.transform.SetParent(envRoot.transform, false);
                dockAnchor.transform.localPosition = new Vector3(0f, -0.38f, 0f);

                for (int i = 0; i < slotCount; i++)
                {
                    GameObject slotObj;
                    if (dockSlotPrefab != null)
                    {
                        slotObj = (GameObject)PrefabUtility.InstantiatePrefab(dockSlotPrefab, dockAnchor.transform);
                    }
                    else
                    {
                        slotObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        slotObj.transform.SetParent(dockAnchor.transform, false);
                    }

                    slotObj.name = $"DockSlot_Fixed_{i + 1}";
                    slotObj.transform.localPosition = new Vector3(startX + i * horizontalSpacing, 0f, 0.25f);
                    slotObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    slotObj.transform.localScale = new Vector3(0.88f, 0.22f, 0.50f);
                    TintHierarchyRenderers(slotObj, new Color32(20, 15, 44, 255), new Color32(96, 84, 160, 255));
                    fixedSlotTransforms[i] = slotObj.transform;
                }

                // 5c. Bottom Spawner Column Roots (4 Fixed Spawn Points)
                queuesAnchor = new GameObject("3_BucketQueueColumns_Fixed");
                queuesAnchor.transform.SetParent(envRoot.transform, false);
                queuesAnchor.transform.localPosition = new Vector3(0f, -1.22f, 0f);

                for (int c = 0; c < slotCount; c++)
                {
                    GameObject colRoot = new GameObject($"ColumnSpawn_Fixed_{c + 1}");
                    colRoot.transform.SetParent(queuesAnchor.transform, false);
                    colRoot.transform.localPosition = new Vector3(startX + c * horizontalSpacing, 0f, 0f);
                    fixedColumnRoots[c] = colRoot.transform;
                }

                // 5d. Flying Sand Transfer Container
                transferAnchor = new GameObject("4_SandTransferAnchor");
                transferAnchor.transform.SetParent(envRoot.transform, false);
                transferAnchor.transform.localPosition = Vector3.zero;
            }

            // 6. Create Gameplay System Managers
            GameObject systemRoot = new GameObject("--- SANDSORT GAMEPLAY ---");
            GridManager gridManager = systemRoot.AddComponent<GridManager>();
            DockManager dockManager = systemRoot.AddComponent<DockManager>();
            BucketQueueManager queueManager = systemRoot.AddComponent<BucketQueueManager>();
            SandTransferController transferController = systemRoot.AddComponent<SandTransferController>();
            GameLoopController gameLoop = systemRoot.AddComponent<GameLoopController>();

            // Wire GridManager (Auto-fits to Static_GridBoardFrame inner cavity + 88x88 = 7,744 fine sand grains!)
            SerializedObject soGrid = new SerializedObject(gridManager);
            soGrid.FindProperty("_gridRootAnchor").objectReferenceValue = gridAnchor.transform;
            soGrid.FindProperty("_boardFrameVisual").objectReferenceValue = frameInstanceTransform;
            soGrid.FindProperty("_sandGrainPrefab").objectReferenceValue = grainPrefab;
            soGrid.FindProperty("_sandResolutionPerAxis").intValue = 88;
            soGrid.FindProperty("_organicSandJitter").floatValue = 0.06f;
            soGrid.FindProperty("_grainScaleMultiplier").floatValue = 1.56f;
            soGrid.FindProperty("_autoFitToTrayBounds").boolValue = true;
            soGrid.ApplyModifiedPropertiesWithoutUndo();

            // Wire DockManager with the 4 fixed slot transforms
            SerializedObject soDock = new SerializedObject(dockManager);
            soDock.FindProperty("_dockRootAnchor").objectReferenceValue = dockAnchor.transform;
            SerializedProperty fixedSlotsProp = soDock.FindProperty("_fixedSlotTransforms");
            fixedSlotsProp.arraySize = slotCount;
            for (int i = 0; i < fixedSlotTransforms.Length; i++)
            {
                fixedSlotsProp.GetArrayElementAtIndex(i).objectReferenceValue = fixedSlotTransforms[i];
            }
            // Seat the bucket slightly inside the 3D DockSlot cavity so the slot walls wrap around it in 3D!
            soDock.FindProperty("_bucketWorldOffset").vector3Value = new Vector3(0f, 0f, -0.06f);
            soDock.ApplyModifiedPropertiesWithoutUndo();

            // Wire BucketQueueManager with the 4 fixed column roots
            SerializedObject soQueue = new SerializedObject(queueManager);
            soQueue.FindProperty("_queuesRootAnchor").objectReferenceValue = queuesAnchor.transform;
            SerializedProperty fixedColsProp = soQueue.FindProperty("_fixedColumnRoots");
            fixedColsProp.arraySize = slotCount;
            for (int c = 0; c < slotCount; c++)
            {
                fixedColsProp.GetArrayElementAtIndex(c).objectReferenceValue = fixedColumnRoots[c];
            }
            soQueue.FindProperty("_bucketPrefab").objectReferenceValue = bucketPrefab;
            soQueue.FindProperty("_gameplayCamera").objectReferenceValue = mainCam;
            soQueue.FindProperty("_columnHorizontalSpacing").floatValue = horizontalSpacing;
            soQueue.FindProperty("_rowVerticalSpacing").floatValue = 0.52f;
            soQueue.ApplyModifiedPropertiesWithoutUndo();

            // Wire SandTransferController
            SerializedObject soTransfer = new SerializedObject(transferController);
            soTransfer.FindProperty("_transferRootAnchor").objectReferenceValue = transferAnchor.transform;
            soTransfer.FindProperty("_sandGrainPrefab").objectReferenceValue = grainPrefab;
            soTransfer.ApplyModifiedPropertiesWithoutUndo();

            // Wire GameLoopController
            SerializedObject soLoop = new SerializedObject(gameLoop);
            SerializedProperty levelsProp = soLoop.FindProperty("_levels");
            levelsProp.arraySize = levels.Count;
            for (int i = 0; i < levels.Count; i++)
            {
                levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = levels[i];
            }
            soLoop.FindProperty("_gridManager").objectReferenceValue = gridManager;
            soLoop.FindProperty("_dockManager").objectReferenceValue = dockManager;
            soLoop.FindProperty("_bucketQueueManager").objectReferenceValue = queueManager;
            soLoop.FindProperty("_transferController").objectReferenceValue = transferController;
            soLoop.ApplyModifiedPropertiesWithoutUndo();

            // 7. Create UI Canvas & EventSystem
            BuildUICanvas(gameLoop);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = envRoot;

            Debug.Log(
                "[SandSort] Royal Indigo Tema + %100 Dolu Tepsi (28x28 = 784 Kum) + Hücresel Otomat Kum Fiziği kuruldu!");
        }

        private static void TintHierarchyRenderers(GameObject root, Color32 baseFloorColor, Color32 borderWallColor)
        {
            const string matFolder = "Assets/_Project/Materials";
            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            if (!AssetDatabase.IsValidFolder(matFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");

            Material floorMat = GetOrCreateTintedMaterial(
                $"{matFolder}/Mat_{root.name}_Floor.mat", baseFloorColor);
            Material wallMat = GetOrCreateTintedMaterial(
                $"{matFolder}/Mat_{root.name}_Wall.mat", borderWallColor);

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer r = renderers[i];
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = (r.gameObject == root) ? floorMat : wallMat;
                EditorUtility.SetDirty(r);
            }
        }

        private static Material GetOrCreateTintedMaterial(string assetPath, Color32 color)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, assetPath);
            }

            Color c = color;
            if (mat.HasProperty(GameConstants.BaseColorShaderId))
                mat.SetColor(GameConstants.BaseColorShaderId, c);
            if (mat.HasProperty(GameConstants.MainColorShaderId))
                mat.SetColor(GameConstants.MainColorShaderId, c);
            mat.color = c;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildUICanvas(GameLoopController gameLoop)
        {
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<InputSystemUIInputModule>();
            }

            // NEVER overwrite or recreate UI if the user already has a UIManager in the scene!
            UIManager existingUI = Object.FindFirstObjectByType<UIManager>();
            if (existingUI != null)
            {
                SerializedObject soExistingUI = new SerializedObject(existingUI);
                soExistingUI.FindProperty("_gameLoop").objectReferenceValue = gameLoop;
                soExistingUI.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(existingUI);
                return;
            }

            GameObject canvasObj = new GameObject("--- SANDSORT UI CANVAS ---");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            UIManager uiManager = canvasObj.AddComponent<UIManager>();

            // Top HUD Bar (Royal Purple-Blue header)
            GameObject topBar = CreateUIPanel(canvasObj.transform, "TopHUD", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -95f), new Vector2(-40f, 150f), new Color32(49, 38, 98, 240));

            TextMeshProUGUI levelTitle = CreateUIText(topBar.transform, "LevelTitleText", "LEVEL 1 — Pixel Heart", 30, new Vector2(0f, 34f), new Vector2(520f, 46f));
            TextMeshProUGUI progressText = CreateUIText(topBar.transform, "ProgressPercentText", "0%", 25, new Vector2(0f, -12f), new Vector2(380f, 38f));

            // Progress Bar Background & Fill
            GameObject progBg = CreateUIPanel(topBar.transform, "ProgressBarBG", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(460f, 20f), new Color32(22, 16, 48, 255));
            GameObject progFillObj = CreateUIPanel(progBg.transform, "ProgressBarFill", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, new Color32(46, 213, 115, 255));
            Image progFillImg = progFillObj.GetComponent<Image>();
            progFillImg.type = Image.Type.Filled;
            progFillImg.fillMethod = Image.FillMethod.Horizontal;
            progFillImg.fillAmount = 0f;

            Button restartBtn = CreateUIButton(topBar.transform, "RestartBtn", "RESTART", new Vector2(360f, 8f), new Vector2(145f, 60f), new Color32(245, 158, 11, 255));

            // Win Modal Panel
            GameObject winPanel = CreateUIPanel(canvasObj.transform, "WinPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 460f), new Color32(34, 25, 74, 250));
            CreateUIText(winPanel.transform, "WinTitle", "LEVEL COMPLETED!", 48, new Vector2(0f, 90f), new Vector2(680f, 80f));
            CreateUIText(winPanel.transform, "WinSubtitle", "All sand grains extracted cleanly!", 28, new Vector2(0f, 20f), new Vector2(640f, 50f));
            Button winNextBtn = CreateUIButton(winPanel.transform, "WinNextButton", "NEXT LEVEL", new Vector2(0f, -95f), new Vector2(340f, 95f), new Color32(46, 204, 113, 255));
            winPanel.SetActive(false);

            // Fail / Deadlock Modal Panel
            GameObject failPanel = CreateUIPanel(canvasObj.transform, "FailPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 460f), new Color32(58, 22, 48, 250));
            CreateUIText(failPanel.transform, "FailTitle", "OUT OF DOCK SLOTS!", 46, new Vector2(0f, 90f), new Vector2(680f, 80f));
            CreateUIText(failPanel.transform, "FailSubtitle", "No active bucket matches the exposed frontier.", 26, new Vector2(0f, 20f), new Vector2(680f, 50f));
            Button failRetryBtn = CreateUIButton(failPanel.transform, "FailRetryButton", "TRY AGAIN", new Vector2(0f, -95f), new Vector2(340f, 95f), new Color32(239, 68, 68, 255));
            failPanel.SetActive(false);

            // Wire UIManager fields
            SerializedObject soUI = new SerializedObject(uiManager);
            soUI.FindProperty("_levelTitleText").objectReferenceValue = levelTitle;
            soUI.FindProperty("_progressPercentText").objectReferenceValue = progressText;
            soUI.FindProperty("_progressFillBar").objectReferenceValue = progFillImg;
            soUI.FindProperty("_restartButton").objectReferenceValue = restartBtn;
            soUI.FindProperty("_winPanel").objectReferenceValue = winPanel;
            soUI.FindProperty("_winNextLevelButton").objectReferenceValue = winNextBtn;
            soUI.FindProperty("_failPanel").objectReferenceValue = failPanel;
            soUI.FindProperty("_failRetryButton").objectReferenceValue = failRetryBtn;
            soUI.FindProperty("_gameLoop").objectReferenceValue = gameLoop;
            soUI.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateUIPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color32 color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            Image img = panel.GetComponent<Image>();
            img.color = color;
            return panel;
        }

        private static TextMeshProUGUI CreateUIText(Transform parent, string name, string content, float fontSize, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return tmp;
        }

        private static Button CreateUIButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 sizeDelta, Color32 bgColor)
        {
            GameObject btnObj = CreateUIPanel(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, sizeDelta, bgColor);
            Button btn = btnObj.AddComponent<Button>();
            CreateUIText(btnObj.transform, "Label", label, 24, Vector2.zero, sizeDelta);
            return btn;
        }

        private static void DestroyExistingIfFound(string objName)
        {
            GameObject existing = GameObject.Find(objName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }
    }
}
