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

        // Cari atau load resources
        LoadAssetsForBuilder(simsUI);

        // 2. Cari atau buat SimsInventory_Root
        Transform existingRoot = canvas.transform.Find("SimsInventory_Root");
        GameObject rootGO;
        if (existingRoot != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Sims Inventory UI Sudah Ada",
                "Objek 'SimsInventory_Root' sudah ada di Canvas.\nApakah Anda ingin membuat ulang (rebuild) hierarki UI di scene?",
                "Ya, Rebuild",
                "Batal"
            );
            if (!overwrite) return;

            Undo.DestroyObjectImmediate(existingRoot.gameObject);
        }

        rootGO = new GameObject("SimsInventory_Root", typeof(RectTransform));
        rootGO.transform.SetParent(canvas.transform, false);
        var rootRt = rootGO.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        Undo.RegisterCreatedObjectUndo(rootGO, "Create SimsInventory_Root");
        simsUI.sceneLayoutRoot = rootRt;

        // 3. Bangun SimsBottomBar di Scene
        GameObject bottomBar = BuildBottomBarInScene(rootGO.transform, simsUI);
        simsUI.bottomBarRoot = bottomBar;

        // 4. Bangun CloseInventoryBtn di Scene
        GameObject closeBtn = BuildCloseButtonInScene(rootGO.transform, simsUI);
        simsUI.closeInventoryButton = closeBtn;

        // 5. Bangun TheHomeStore_Modal di Scene
        GameObject storeModal = BuildStoreModalInScene(rootGO.transform, simsUI);
        simsUI.storeModalRoot = storeModal;

        // 6. Nonaktifkan Legacy UI Panels
        string[] legacyNames = new string[] { "UI Left", "UI Left SP", "UI Left Batt", "Instal buttons" };
        foreach (var n in legacyNames)
        {
            Transform t = canvas.transform.Find(n);
            if (t != null)
            {
                if (n == "Instal buttons")
                {
                    var cg = t.GetComponent<CanvasGroup>();
                    if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
                else
                {
                    t.gameObject.SetActive(false);
                }
            }
        }

        EditorUtility.SetDirty(simsUI);
        EditorUtility.SetDirty(simsUI.gameObject);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        EditorUtility.DisplayDialog(
            "Sukses",
            "Sims Inventory UI berhasil dibangun di Scene!\n\n" +
            "Sekarang Anda dapat melihat dan mengatur posisi objek secara bebas di Scene View:\n" +
            "- SimsInventory_Root > SimsBottomBar (Action bar kanan bawah)\n" +
            "- SimsInventory_Root > CloseInventoryBtn (Tombol tutup mode)\n" +
            "- SimsInventory_Root > TheHomeStore_Modal (Dialog store Sims)\n\n" +
            "Posisi yang Anda ubah di Scene View tidak akan hilang saat Play Mode.",
            "OK"
        );
    }

    private static void LoadAssetsForBuilder(SimsInventoryUI ui)
    {
        if (ui.fontAsset == null)
        {
            var fontGUIDs = AssetDatabase.FindAssets("VarelaRound-Regular SDF t:TMP_FontAsset");
            if (fontGUIDs.Length > 0)
            {
                string fontPath = AssetDatabase.GUIDToAssetPath(fontGUIDs[0]);
                ui.fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            }
        }

        if (ui.circleButtonSprite == null)
        {
            var circleGUIDs = AssetDatabase.FindAssets("Button Circle t:Sprite");
            if (circleGUIDs.Length > 0)
            {
                string p = AssetDatabase.GUIDToAssetPath(circleGUIDs[0]);
                ui.circleButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
        }

        if (ui.roundedBoxSprite == null)
        {
            var uiGUIDs = AssetDatabase.FindAssets("UI t:Sprite");
            foreach (var guid in uiGUIDs)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("UI.png"))
                {
                    ui.roundedBoxSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                    break;
                }
            }
        }

        if (ui.sunIconSprite == null)
        {
            var sunGUIDs = AssetDatabase.FindAssets("SUN t:Sprite");
            if (sunGUIDs.Length > 0)
            {
                string p = AssetDatabase.GUIDToAssetPath(sunGUIDs[0]);
                ui.sunIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
        }

        if (ui.electricityIconSprite == null)
        {
            var elecGUIDs = AssetDatabase.FindAssets("Electricity t:Sprite");
            if (elecGUIDs.Length > 0)
            {
                string p = AssetDatabase.GUIDToAssetPath(elecGUIDs[0]);
                ui.electricityIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
        }

        // Data Catalogs
        if (ui.furnitureList.Count == 0)
        {
            var fGUIDs = AssetDatabase.FindAssets("t:FurnitureData");
            foreach (var guid in fGUIDs)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var f = AssetDatabase.LoadAssetAtPath<FurnitureData>(p);
                if (f != null && !ui.furnitureList.Contains(f)) ui.furnitureList.Add(f);
            }
        }

        if (ui.solarPanelList.Count == 0)
        {
            var spGUIDs = AssetDatabase.FindAssets("t:SolarPanelData");
            foreach (var guid in spGUIDs)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var sp = AssetDatabase.LoadAssetAtPath<SolarPanelData>(p);
                if (sp != null && !ui.solarPanelList.Contains(sp)) ui.solarPanelList.Add(sp);
            }
        }

        if (ui.batteryList.Count == 0)
        {
            var bGUIDs = AssetDatabase.FindAssets("t:BattData");
            foreach (var guid in bGUIDs)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var b = AssetDatabase.LoadAssetAtPath<BattData>(p);
                if (b != null && !ui.batteryList.Contains(b)) ui.batteryList.Add(b);
            }
        }
    }

    private static GameObject BuildBottomBarInScene(Transform parent, SimsInventoryUI ui)
    {
        GameObject bbGO = new GameObject("SimsBottomBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        bbGO.transform.SetParent(parent, false);

        RectTransform rt = bbGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-28f, 22f);
        rt.sizeDelta = new Vector2(380f, 95f);

        var hlg = bbGO.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        Sprite furnIcon = (ui.furnitureList.Count > 0 && ui.furnitureList[0].icon != null) ? ui.furnitureList[0].icon : null;
        CreateDockButtonInScene(bbGO.transform, "BtnAlatElektronik", "Elektronik", furnIcon, ui.tileCyanTop, ui.tileCyanBottom, ui);
        CreateDockButtonInScene(bbGO.transform, "BtnPanelSurya", "Panel Surya", ui.sunIconSprite, ui.tileOrangeTop, ui.tileOrangeBottom, ui);
        CreateDockButtonInScene(bbGO.transform, "BtnBaterai", "Baterai", ui.electricityIconSprite, ui.tileGreenTop, ui.tileGreenBottom, ui);

        return bbGO;
    }

    private static GameObject CreateDockButtonInScene(Transform parent, string goName, string label, Sprite icon, Color topColor, Color bottomColor, SimsInventoryUI ui)
    {
        GameObject btnGO = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(UISoftShadow));
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(85f, 85f);

        Image img = btnGO.GetComponent<Image>();
        img.sprite = ui.circleButtonSprite != null ? ui.circleButtonSprite : ui.roundedBoxSprite;
        img.type = Image.Type.Simple;

        var grad = btnGO.AddComponent<UIGradient>();
        grad.colorTop = topColor;
        grad.colorBottom = bottomColor;

        var outline = btnGO.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        var shadow = btnGO.GetComponent<UISoftShadow>();
        shadow.shadowColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.shadowOffset = new Vector2(2f, -3f);

        if (icon != null)
        {
            GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(btnGO.transform, false);
            RectTransform iconRt = iconGO.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.55f);
            iconRt.anchorMax = new Vector2(0.5f, 0.55f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(46f, 46f);

            Image iconImg = iconGO.GetComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
        }

        GameObject lblGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lblGO.transform.SetParent(btnGO.transform, false);
        RectTransform lblRt = lblGO.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0f, 0.05f);
        lblRt.anchorMax = new Vector2(1f, 0.35f);
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;

        var tmp = lblGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) tmp.font = ui.fontAsset;
        tmp.text = label;
        tmp.fontSize = 11.5f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;

        btnGO.AddComponent<UIButtonHoverScale>();
        return btnGO;
    }

    private static GameObject BuildCloseButtonInScene(Transform parent, SimsInventoryUI ui)
    {
        GameObject closeBtnGO = new GameObject("CloseInventoryBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(UISoftShadow));
        closeBtnGO.transform.SetParent(parent, false);

        RectTransform rt = closeBtnGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-28f, 22f);
        rt.sizeDelta = new Vector2(185f, 65f);

        Image img = closeBtnGO.GetComponent<Image>();
        img.sprite = ui.roundedBoxSprite;
        img.type = Image.Type.Sliced;
        img.color = ui.badgeRed;

        var outline = closeBtnGO.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        var shadow = closeBtnGO.GetComponent<UISoftShadow>();
        shadow.shadowColor = new Color(0.4f, 0f, 0f, 0.45f);
        shadow.shadowOffset = new Vector2(2f, -3f);

        GameObject iconGO = new GameObject("XIcon", typeof(RectTransform), typeof(TextMeshProUGUI));
        iconGO.transform.SetParent(closeBtnGO.transform, false);
        RectTransform iconRt = iconGO.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0f);
        iconRt.anchorMax = new Vector2(0.28f, 1f);
        iconRt.offsetMin = Vector2.zero;
        iconRt.offsetMax = Vector2.zero;
        var iconTmp = iconGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) iconTmp.font = ui.fontAsset;
        iconTmp.text = "✕";
        iconTmp.fontSize = 24f;
        iconTmp.fontStyle = FontStyles.Bold;
        iconTmp.alignment = TextAlignmentOptions.Center;
        iconTmp.color = Color.white;
        iconTmp.raycastTarget = false;

        GameObject lblGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lblGO.transform.SetParent(closeBtnGO.transform, false);
        RectTransform lblRt = lblGO.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0.25f, 0f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;
        var lblTmp = lblGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) lblTmp.font = ui.fontAsset;
        lblTmp.text = "Tutup Mode";
        lblTmp.fontSize = 15f;
        lblTmp.fontStyle = FontStyles.Bold;
        lblTmp.alignment = TextAlignmentOptions.Center;
        lblTmp.color = Color.white;
        lblTmp.raycastTarget = false;

        Button btn = closeBtnGO.GetComponent<Button>();
        btn.targetGraphic = img;

        closeBtnGO.AddComponent<UIButtonHoverScale>();
        closeBtnGO.SetActive(false); // Inactive until mode active
        return closeBtnGO;
    }

    private static GameObject BuildStoreModalInScene(Transform parent, SimsInventoryUI ui)
    {
        GameObject modalGO = new GameObject("TheHomeStore_Modal", typeof(RectTransform), typeof(Image));
        modalGO.transform.SetParent(parent, false);

        RectTransform modalRt = modalGO.GetComponent<RectTransform>();
        modalRt.anchorMin = Vector2.zero;
        modalRt.anchorMax = Vector2.one;
        modalRt.offsetMin = Vector2.zero;
        modalRt.offsetMax = Vector2.zero;

        Image scrim = modalGO.GetComponent<Image>();
        scrim.color = new Color(0f, 0f, 0f, 0.40f);
        var scrimBtn = modalGO.AddComponent<Button>();
        scrimBtn.targetGraphic = scrim;

        // Dialog Panel
        GameObject dialogGO = new GameObject("DialogPanel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(UISoftShadow));
        dialogGO.transform.SetParent(modalGO.transform, false);

        RectTransform dRt = dialogGO.GetComponent<RectTransform>();
        dRt.anchorMin = new Vector2(0.5f, 0.5f);
        dRt.anchorMax = new Vector2(0.5f, 0.5f);
        dRt.pivot = new Vector2(0.5f, 0.5f);
        dRt.anchoredPosition = new Vector2(0f, -15f);
        dRt.sizeDelta = new Vector2(1280f, 620f);

        Image dImg = dialogGO.GetComponent<Image>();
        dImg.sprite = ui.roundedBoxSprite;
        dImg.type = Image.Type.Sliced;
        dImg.color = ui.simsPanelBg;

        var dOutline = dialogGO.GetComponent<Outline>();
        dOutline.effectColor = Color.white;
        dOutline.effectDistance = new Vector2(4f, -4f);

        var dShadow = dialogGO.GetComponent<UISoftShadow>();
        dShadow.shadowColor = new Color(0f, 0.1f, 0.25f, 0.4f);
        dShadow.shadowOffset = new Vector2(4f, -6f);

        dialogGO.AddComponent<GraphicRaycaster>();

        // Header Banner
        GameObject headerGO = new GameObject("HeaderBanner", typeof(RectTransform), typeof(Image), typeof(Outline));
        headerGO.transform.SetParent(dialogGO.transform, false);

        RectTransform hRt = headerGO.GetComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0.5f, 1f);
        hRt.anchorMax = new Vector2(0.5f, 1f);
        hRt.pivot = new Vector2(0.5f, 0.5f);
        hRt.anchoredPosition = new Vector2(0f, 4f);
        hRt.sizeDelta = new Vector2(580f, 56f);

        Image hImg = headerGO.GetComponent<Image>();
        hImg.sprite = ui.roundedBoxSprite;
        hImg.type = Image.Type.Sliced;
        var hGrad = headerGO.AddComponent<UIGradient>();
        hGrad.colorTop = ui.simsHeaderTop;
        hGrad.colorBottom = ui.simsHeaderBottom;

        var hOutline = headerGO.GetComponent<Outline>();
        hOutline.effectColor = Color.white;
        hOutline.effectDistance = new Vector2(3f, -3f);

        GameObject titleGO = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(headerGO.transform, false);
        RectTransform tRt = titleGO.GetComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero;
        tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(60f, 0f);
        tRt.offsetMax = new Vector2(-60f, 0f);

        var titleTmp = titleGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) titleTmp.font = ui.fontAsset;
        titleTmp.text = "THE HOME STORE";
        titleTmp.fontSize = 22f;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;
        ui.storeTitleText = titleTmp;

        // Back Button
        GameObject backBtnGO = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        backBtnGO.transform.SetParent(dialogGO.transform, false);
        RectTransform bRt = backBtnGO.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f);
        bRt.anchorMax = new Vector2(0f, 1f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = new Vector2(40f, 4f);
        bRt.sizeDelta = new Vector2(56f, 56f);

        Image bImg = backBtnGO.GetComponent<Image>();
        bImg.sprite = ui.circleButtonSprite != null ? ui.circleButtonSprite : ui.roundedBoxSprite;
        bImg.color = new Color(0.25f, 0.78f, 0.32f, 1f);

        var bOutline = backBtnGO.GetComponent<Outline>();
        bOutline.effectColor = Color.white;
        bOutline.effectDistance = new Vector2(3f, -3f);

        GameObject bTextGO = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        bTextGO.transform.SetParent(backBtnGO.transform, false);
        RectTransform btRt = bTextGO.GetComponent<RectTransform>();
        btRt.anchorMin = Vector2.zero;
        btRt.anchorMax = Vector2.one;
        btRt.offsetMin = Vector2.zero;
        btRt.offsetMax = Vector2.zero;

        var btTmp = bTextGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) btTmp.font = ui.fontAsset;
        btTmp.text = "<";
        btTmp.fontSize = 28f;
        btTmp.fontStyle = FontStyles.Bold;
        btTmp.alignment = TextAlignmentOptions.Center;
        btTmp.color = Color.white;

        // Cart Button
        GameObject cartBtnGO = new GameObject("CartButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        cartBtnGO.transform.SetParent(dialogGO.transform, false);
        RectTransform cRt = cartBtnGO.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(1f, 1f);
        cRt.anchorMax = new Vector2(1f, 1f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.anchoredPosition = new Vector2(-40f, 4f);
        cRt.sizeDelta = new Vector2(56f, 56f);

        Image cImg = cartBtnGO.GetComponent<Image>();
        cImg.sprite = ui.circleButtonSprite != null ? ui.circleButtonSprite : ui.roundedBoxSprite;
        cImg.color = new Color(0.60f, 0.20f, 0.75f, 1f);

        var cOutline = cartBtnGO.GetComponent<Outline>();
        cOutline.effectColor = Color.white;
        cOutline.effectDistance = new Vector2(3f, -3f);

        GameObject cartIconGO = new GameObject("CartIcon", typeof(RectTransform), typeof(TextMeshProUGUI));
        cartIconGO.transform.SetParent(cartBtnGO.transform, false);
        RectTransform ciRt = cartIconGO.GetComponent<RectTransform>();
        ciRt.anchorMin = Vector2.zero;
        ciRt.anchorMax = Vector2.one;
        ciRt.offsetMin = Vector2.zero;
        ciRt.offsetMax = Vector2.zero;

        var ciTmp = cartIconGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) ciTmp.font = ui.fontAsset;
        ciTmp.text = "⚡";
        ciTmp.fontSize = 24f;
        ciTmp.alignment = TextAlignmentOptions.Center;
        ciTmp.color = Color.white;

        GameObject badgeGO = new GameObject("CartBadge", typeof(RectTransform), typeof(Image), typeof(Outline));
        badgeGO.transform.SetParent(cartBtnGO.transform, false);
        RectTransform bgRt = badgeGO.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(1f, 1f);
        bgRt.anchorMax = new Vector2(1f, 1f);
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.anchoredPosition = new Vector2(2f, 2f);
        bgRt.sizeDelta = new Vector2(26f, 26f);

        Image bgImg = badgeGO.GetComponent<Image>();
        bgImg.sprite = ui.circleButtonSprite != null ? ui.circleButtonSprite : ui.roundedBoxSprite;
        bgImg.color = ui.badgeRed;

        var bgOutline = badgeGO.GetComponent<Outline>();
        bgOutline.effectColor = Color.white;
        bgOutline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject badgeTextGO = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        badgeTextGO.transform.SetParent(badgeGO.transform, false);
        RectTransform bgtRt = badgeTextGO.GetComponent<RectTransform>();
        bgtRt.anchorMin = Vector2.zero;
        bgtRt.anchorMax = Vector2.one;
        bgtRt.offsetMin = Vector2.zero;
        bgtRt.offsetMax = Vector2.zero;

        var bgtTmp = badgeTextGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) bgtTmp.font = ui.fontAsset;
        bgtTmp.text = "0";
        bgtTmp.fontSize = 12f;
        bgtTmp.fontStyle = FontStyles.Bold;
        bgtTmp.alignment = TextAlignmentOptions.Center;
        bgtTmp.color = Color.white;
        ui.cartBadgeText = bgtTmp;

        // Tabs Bar
        GameObject tabsBar = new GameObject("CategoryTabsBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabsBar.transform.SetParent(dialogGO.transform, false);

        RectTransform tbRt = tabsBar.GetComponent<RectTransform>();
        tbRt.anchorMin = new Vector2(0f, 1f);
        tbRt.anchorMax = new Vector2(1f, 1f);
        tbRt.pivot = new Vector2(0.5f, 1f);
        tbRt.anchoredPosition = new Vector2(0f, -40f);
        tbRt.sizeDelta = new Vector2(-40f, 44f);

        var hlg = tabsBar.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        ui.tabButtons.Clear();
        CreateCategoryTabInScene(tabsBar.transform, SimsInventoryUI.StoreCategory.Semua, "Semua", ui);
        CreateCategoryTabInScene(tabsBar.transform, SimsInventoryUI.StoreCategory.Dapur, "Dapur", ui);
        CreateCategoryTabInScene(tabsBar.transform, SimsInventoryUI.StoreCategory.Elektronik, "Elektronik", ui);
        CreateCategoryTabInScene(tabsBar.transform, SimsInventoryUI.StoreCategory.Penerangan, "Penerangan", ui);
        CreateCategoryTabInScene(tabsBar.transform, SimsInventoryUI.StoreCategory.PanelSurya, "Panel Surya", ui);
        CreateCategoryTabInScene(tabsBar.transform, SimsInventoryUI.StoreCategory.Baterai, "Baterai", ui);

        // Scroll View
        GameObject scrollGO = new GameObject("StoreScrollView", typeof(RectTransform), typeof(ScrollRect));
        scrollGO.transform.SetParent(dialogGO.transform, false);

        RectTransform sRt = scrollGO.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 0f);
        sRt.anchorMax = new Vector2(1f, 1f);
        sRt.offsetMin = new Vector2(30f, 30f);
        sRt.offsetMax = new Vector2(-30f, -95f);

        ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
        sr.horizontal = true;
        sr.vertical = false;
        sr.movementType = ScrollRect.MovementType.Elastic;
        sr.elasticity = 0.1f;
        sr.inertia = true;
        sr.decelerationRate = 0.135f;
        sr.scrollSensitivity = 25f;

        GameObject viewGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewGO.transform.SetParent(scrollGO.transform, false);
        RectTransform vRt = viewGO.GetComponent<RectTransform>();
        vRt.anchorMin = Vector2.zero;
        vRt.anchorMax = Vector2.one;
        vRt.offsetMin = Vector2.zero;
        vRt.offsetMax = Vector2.zero;

        GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentGO.transform.SetParent(viewGO.transform, false);
        RectTransform cRt2 = contentGO.GetComponent<RectTransform>();
        cRt2.anchorMin = new Vector2(0f, 0f);
        cRt2.anchorMax = new Vector2(0f, 1f);
        cRt2.pivot = new Vector2(0f, 0.5f);
        cRt2.offsetMin = Vector2.zero;
        cRt2.offsetMax = Vector2.zero;

        var grid = contentGO.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150f, 140f);
        grid.spacing = new Vector2(14f, 12f);
        grid.startAxis = GridLayoutGroup.Axis.Vertical;
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;

        var csf = contentGO.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        sr.viewport = vRt;
        sr.content = cRt2;
        ui.storeContentParent = contentGO;

        modalGO.SetActive(false); // Sembunyikan modal di scene awal
        return modalGO;
    }

    private static void CreateCategoryTabInScene(Transform parent, SimsInventoryUI.StoreCategory cat, string title, SimsInventoryUI ui)
    {
        GameObject tabGO = new GameObject($"Tab_{cat}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        tabGO.transform.SetParent(parent, false);

        RectTransform rt = tabGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(145f, 36f);

        Image img = tabGO.GetComponent<Image>();
        img.sprite = ui.roundedBoxSprite;
        img.type = Image.Type.Sliced;

        var outline = tabGO.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(tabGO.transform, false);
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        if (ui.fontAsset != null) tmp.font = ui.fontAsset;
        tmp.text = title;
        tmp.fontSize = 13.5f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;

        Button btn = tabGO.GetComponent<Button>();
        btn.targetGraphic = img;

        ui.tabButtons.Add(tabGO);
    }
}
