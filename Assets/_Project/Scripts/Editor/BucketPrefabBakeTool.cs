using SandSort.Gameplay.Containers;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SandSort.Editor
{
    [InitializeOnLoad]
    public static class BucketPrefabBakeTool
    {
        private const string PrefabPath = "Assets/Prefabs/Bucket 1.prefab";
        private const string FbxPath = "Assets/Prefabs/Downloadedfbx/plastic+bucket.fbx";

        static BucketPrefabBakeTool()
        {
            EditorApplication.delayCall += EnsureBucketPrefabBaked;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EnsureBucketPrefabBaked();
        }

        [MenuItem("Tools/SandSort/Rebake Bucket 1 Prefab (Visual Edit Ready)")]
        public static void ForceRebakeBucketPrefab()
        {
            BakePrefabInternal(true);
        }

        private static void EnsureBucketPrefabBaked()
        {
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existingPrefab == null)
            {
                BakePrefabInternal(false);
                return;
            }

            Transform pivot = existingPrefab.transform.Find("ModelPivot_3D");
            Transform label = existingPrefab.transform.Find("CapacityLabel");
            // If scale is smaller than full-size 42.56x, rebake so size is 100% accurate!
            if (pivot == null || label == null || pivot.localScale.x < 10f)
            {
                BakePrefabInternal(false);
            }
        }

        private static void BakePrefabInternal(bool forceLog)
        {
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null)
            {
                Debug.LogWarning($"[BucketPrefabBakeTool] FBX not found at {FbxPath}");
                return;
            }

            // 1. Create root instance
            GameObject root = new GameObject("Bucket 1");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 2. Create ModelPivot_3D container with true 42.56x scale (0.54m physical bucket size)
            GameObject pivotObj = new GameObject("ModelPivot_3D");
            Transform modelPivot = pivotObj.transform;
            modelPivot.SetParent(root.transform, false);
            modelPivot.localPosition = Vector3.zero;
            modelPivot.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            modelPivot.localScale = Vector3.one * 42.56f;

            // 3. Instantiate and unpack FBX children under ModelPivot_3D
            GameObject fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(fbxInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            int childCount = fbxInstance.transform.childCount;
            Transform[] children = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                children[i] = fbxInstance.transform.GetChild(i);
            }

            for (int i = 0; i < children.Length; i++)
            {
                children[i].SetParent(modelPivot, false);
            }
            Object.DestroyImmediate(fbxInstance);

            // 4. Align handle X with body X center
            Transform bodyTr = modelPivot.Find("plastic_bucket_001");
            if (bodyTr != null)
            {
                MeshRenderer bodyRenderer = bodyTr.GetComponent<MeshRenderer>();
                if (bodyRenderer != null)
                {
                    float bodyCenterX = bodyRenderer.bounds.center.x;
                    MeshRenderer[] allR = modelPivot.GetComponentsInChildren<MeshRenderer>();
                    for (int i = 0; i < allR.Length; i++)
                    {
                        if (allR[i] != bodyRenderer)
                        {
                            float dx = bodyCenterX - allR[i].bounds.center.x;
                            if (Mathf.Abs(dx) > 0.0001f)
                            {
                                allR[i].transform.position += new Vector3(dx, 0f, 0f);
                            }
                        }
                    }
                }
            }

            // Center the model's world bounds onto root (0,0,0)
            MeshRenderer[] renderers = modelPivot.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);
                Vector3 centerDiff = root.transform.position - combined.center;
                modelPivot.position += centerDiff;
            }

            // 5. Add BoxCollider
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, 0.02f);
            col.size = new Vector3(0.55f, 0.55f, 0.55f);

            // 6. Create CapacityLabel TextMeshPro as an explicit prefab child
            GameObject labelObj = new GameObject("CapacityLabel");
            labelObj.transform.SetParent(root.transform, false);
            labelObj.transform.localPosition = new Vector3(0f, -0.09f, -0.28f);
            labelObj.transform.localRotation = Quaternion.identity;
            labelObj.transform.localScale = Vector3.one;

            TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
            tmp.text = "?";
            tmp.fontSize = 2.4f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.outlineWidth = 0.28f;
            tmp.outlineColor = new Color32(14, 12, 28, 255);
            tmp.rectTransform.sizeDelta = new Vector2(2.5f, 1.2f);

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (fontAsset != null)
            {
                tmp.font = fontAsset;
            }

            // 7. Add BucketView component and wire references
            BucketView view = root.AddComponent<BucketView>();
            SerializedObject so = new SerializedObject(view);
            so.FindProperty("_capacityText").objectReferenceValue = tmp;
            so.FindProperty("_respectPrefabLabelTransform").boolValue = true;
            so.FindProperty("_respectPrefabModelTransform").boolValue = true;
            so.FindProperty("_autoNormalizeWorldSize").boolValue = false;
            so.FindProperty("_mysteryLabelText").stringValue = "?";
            so.FindProperty("_cameraForwardTiltDegrees").floatValue = -38f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 8. Save as Prefab Asset
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=#55FF55><b>[SandSort]</b> Bucket 1.prefab başarıyla oluşturuldu/güncellendi! Prefab içine girip 'CapacityLabel' ve kovayı elle dilediğiniz gibi düzenleyebilirsiniz.</color>");
        }
    }
}
