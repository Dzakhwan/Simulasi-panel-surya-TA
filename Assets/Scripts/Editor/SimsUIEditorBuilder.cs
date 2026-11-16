#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public static class SimsUIEditorBuilder
{
    [MenuItem("Simulasi/Build Sims Inventory UI in Scene", false, 100)]
    public static void BuildSimsUI()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Error", "Canvas tidak ditemukan di scene saat ini!", "OK");
            return;
        }

        // 1. Cari atau buat SimsInventoryUI GameObject
        SimsInventoryUI simsUI = Object.FindFirstObjectByType<SimsInventoryUI>(FindObjectsInactive.Include);
        if (simsUI == null)
        {
            GameObject simsGO = new GameObject("SimsInventoryUI", typeof(SimsInventoryUI));
            simsGO.transform.SetParent(canvas.transform, false);
            simsUI = simsGO.GetComponent<SimsInventoryUI>();
            Undo.RegisterCreatedObjectUndo(simsGO, "Create SimsInventoryUI");
        }

        // 2. Create persistent editable layout root
        Transform layoutRoot = canvas.transform.Find("SimsInventory_Root");
        if (layoutRoot == null)
        {
            var layoutGO = new GameObject("SimsInventory_Root", typeof(RectTransform));
            layoutGO.transform.SetParent(canvas.transform, false);
            var layoutRect = layoutGO.GetComponent<RectTransform>();
            layoutRect.anchorMin = Vector2.zero;
            layoutRect.anchorMax = Vector2.one;
            layoutRect.offsetMin = Vector2.zero;
            layoutRect.offsetMax = Vector2.zero;
            layoutRoot = layoutGO.transform;
            Undo.RegisterCreatedObjectUndo(layoutGO, "Create SimsInventory layout root");
        }
        simsUI.sceneLayoutRoot = layoutRoot as RectTransform;

        // 3. Assign Font
        var fontGUIDs = AssetDatabase.FindAssets("VarelaRound-Regular SDF t:TMP_FontAsset");
        if (fontGUIDs.Length > 0)
        {
            string fontPath = AssetDatabase.GUIDToAssetPath(fontGUIDs[0]);
            simsUI.fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        }

        // 3. Assign Sprites
        var circleGUIDs = AssetDatabase.FindAssets("Button Circle t:Sprite");
        if (circleGUIDs.Length > 0)
        {
            string p = AssetDatabase.GUIDToAssetPath(circleGUIDs[0]);
            simsUI.circleButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        var uiGUIDs = AssetDatabase.FindAssets("UI t:Sprite");
        foreach (var guid in uiGUIDs)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith("UI.png"))
            {
                simsUI.roundedBoxSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                break;
            }
        }

        var sunGUIDs = AssetDatabase.FindAssets("SUN t:Sprite");
        if (sunGUIDs.Length > 0)
        {
            string p = AssetDatabase.GUIDToAssetPath(sunGUIDs[0]);
            simsUI.sunIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        var elecGUIDs = AssetDatabase.FindAssets("Electricity t:Sprite");
        if (elecGUIDs.Length > 0)
        {
            string p = AssetDatabase.GUIDToAssetPath(elecGUIDs[0]);
            simsUI.electricityIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        // 4. Assign Furniture Data
        simsUI.furnitureList.Clear();
        var fGUIDs = AssetDatabase.FindAssets("t:FurnitureData");
        foreach (var guid in fGUIDs)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var f = AssetDatabase.LoadAssetAtPath<FurnitureData>(p);
            if (f != null && !simsUI.furnitureList.Contains(f))
                simsUI.furnitureList.Add(f);
        }

        // 5. Assign Solar Panel Data
        simsUI.solarPanelList.Clear();
        var spGUIDs = AssetDatabase.FindAssets("t:SolarPanelData");
        foreach (var guid in spGUIDs)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var sp = AssetDatabase.LoadAssetAtPath<SolarPanelData>(p);
            if (sp != null && !simsUI.solarPanelList.Contains(sp))
                simsUI.solarPanelList.Add(sp);
        }

        // 6. Assign Battery Data
        simsUI.batteryList.Clear();
        var bGUIDs = AssetDatabase.FindAssets("t:BattData");
        foreach (var guid in bGUIDs)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var b = AssetDatabase.LoadAssetAtPath<BattData>(p);
            if (b != null && !simsUI.batteryList.Contains(b))
                simsUI.batteryList.Add(b);
        }

        // 7. Sembunyikan Legacy Panels
        string[] legacyNames = new string[] { "UI Left", "UI Left SP", "UI Left Batt", "Instal buttons" };
        foreach (var n in legacyNames)
        {
            Transform t = canvas.transform.Find(n);
            if (t != null)
            {
                Undo.RecordObject(t.gameObject, "Deactivate Legacy UI");
                t.gameObject.SetActive(false);
            }
        }

        EditorUtility.SetDirty(simsUI.gameObject);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        EditorUtility.DisplayDialog(
            "Sukses",
            $"Sims Inventory UI berhasil disetup di scene!\n\n" +
            $"- Furnitur terdeteksi: {simsUI.furnitureList.Count}\n" +
            $"- Panel Surya: {simsUI.solarPanelList.Count}\n" +
            $"- Baterai: {simsUI.batteryList.Count}\n\n" +
            "Legacy UI Left telah dinonaktifkan.",
            "OK"
        );
    }
}
#endif