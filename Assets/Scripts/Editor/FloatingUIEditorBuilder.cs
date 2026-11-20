using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor Builder untuk membangun seluruh hierarki Floating UI (Placement & Selection)
/// langsung di Scene Mode / Canvas agar user dapat melihat, mengatur posisi, ukuran, warna,
/// dan styling di Scene View secara visual.
/// </summary>
[InitializeOnLoad]
public static class FloatingUIEditorBuilder
{
    static FloatingUIEditorBuilder()
    {
        EditorApplication.delayCall += CheckAndAutoBuild;
    }

    private static void CheckAndAutoBuild()
    {
        if (Application.isPlaying) return;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;
        Transform root = canvas.transform.Find("FloatingUI_Root");
        if (root == null)
        {
            BuildFloatingUIInternal(false);
        }
        else
        {
            Transform sUI = root.Find("FloatingSelectionUI");
            if (sUI != null && sUI.Find("BtnRotate") == null)
            {
                BuildFloatingUIInternal(false);
            }
        }
    }

    [MenuItem("Simulasi/Build Floating UI in Scene", false, 101)]
    public static void BuildFloatingUI()
    {
        BuildFloatingUIInternal(true);
    }

    public static void BuildFloatingUIInternal(bool interactive)
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            if (interactive) EditorUtility.DisplayDialog("Error", "Canvas tidak ditemukan di scene saat ini!", "OK");
            return;
        }

        // 1. Cari atau buat FurniturePlacementController
        var fpc = Object.FindFirstObjectByType<FurniturePlacementController>(FindObjectsInactive.Include);
        if (fpc == null)
        {
            GameObject fpcGO = new GameObject("[FurniturePlacementController]", typeof(FurniturePlacementController));
            fpc = fpcGO.GetComponent<FurniturePlacementController>();
            Undo.RegisterCreatedObjectUndo(fpcGO, "Create FurniturePlacementController");
        }

        // 2. Cek apakah FloatingUI_Root sudah ada
        Transform existingRoot = canvas.transform.Find("FloatingUI_Root");
        if (existingRoot != null)
        {
            if (interactive)
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Floating UI Sudah Ada",
                    "Objek 'FloatingUI_Root' sudah ada di Canvas.\nApakah Anda ingin membuat ulang? (Posisi custom yang belum disimpan akan di-reset)",
                    "Buat Ulang", "Batal"
                );
                if (!overwrite) return;
            }
            Undo.DestroyObjectImmediate(existingRoot.gameObject);
        }

        // 3. Buat FloatingUI_Root container
        GameObject rootGO = new GameObject("FloatingUI_Root", typeof(RectTransform));
        rootGO.transform.SetParent(canvas.transform, false);
        Undo.RegisterCreatedObjectUndo(rootGO, "Build Floating UI Hierarchy");

        RectTransform rootRt = rootGO.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        // Ambil resource sprite & font
        Sprite circleSprite = FindCircleSprite();
        Sprite roundedSprite = FindRoundedSprite();
        TMP_FontAsset font = FindFontAsset();

        // 4. Bangun FloatingPlacementUI (Preview di sisi kiri: X = -220)
        GameObject placementUI = BuildPlacementUINode(rootGO.transform, circleSprite, roundedSprite, font);

        // 5. Bangun FloatingSelectionUI (Preview di sisi kanan: X = 220)
        GameObject selectionUI = BuildSelectionUINode(rootGO.transform, circleSprite, roundedSprite, font);

        // 6. Hubungkan ke FurniturePlacementController
        fpc.floatingUIRoot = rootGO;
        fpc.scenePlacementUI = placementUI;
        fpc.sceneSelectionUI = selectionUI;

        fpc.sceneBtnConfirm = placementUI.transform.Find("BtnConfirm")?.GetComponent<Button>();
        fpc.sceneBtnRotate = placementUI.transform.Find("BtnRotate")?.GetComponent<Button>();
        fpc.sceneBtnCancel = placementUI.transform.Find("BtnCancel")?.GetComponent<Button>();
        fpc.sceneMoveGizmoPlacement = placementUI.transform.Find("MoveGizmo")?.gameObject;
        fpc.sceneNameLabelPlacement = placementUI.transform.Find("NameLabel")?.gameObject;

        fpc.sceneBtnDelete = selectionUI.transform.Find("BtnHapus")?.GetComponent<Button>();
        fpc.sceneBtnCable = selectionUI.transform.Find("BtnKabel")?.GetComponent<Button>();
        fpc.sceneBtnRotateSelection = selectionUI.transform.Find("BtnRotate")?.GetComponent<Button>();
        fpc.sceneBtnMove = selectionUI.transform.Find("BtnPindah")?.GetComponent<Button>();
        fpc.sceneBtnClock = selectionUI.transform.Find("BtnJam")?.GetComponent<Button>();
        fpc.sceneMoveGizmoSelection = selectionUI.transform.Find("MoveGizmo")?.gameObject;
        fpc.sceneNameLabelSelection = selectionUI.transform.Find("NameLabel")?.gameObject;

        EditorUtility.SetDirty(fpc);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        Selection.activeGameObject = rootGO;
        Debug.Log("[FloatingUIEditorBuilder] Berhasil membangun Floating UI di Scene Mode!");
        if (interactive)
        {
            EditorUtility.DisplayDialog(
                "Floating UI Selesai Dibuat",
                "Hierarki 'FloatingUI_Root' berhasil dibuat di Canvas!\n\n" +
                "Anda dapat melihat dan mengedit tampilan tombol Placement (kiri) dan Selection (kanan) di Scene View.",
                "OK"
            );
        }
    }

    private static GameObject BuildPlacementUINode(Transform parent, Sprite circleSprite, Sprite roundedSprite, TMP_FontAsset font)
    {
        GameObject pUI = new GameObject("FloatingPlacementUI", typeof(RectTransform));
        pUI.transform.SetParent(parent, false);

        RectTransform rt = pUI.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-220f, 0f); // Tampil di sisi kiri untuk preview
        rt.sizeDelta = new Vector2(320f, 320f);

        // Header Title Preview
        CreatePreviewHeader(pUI.transform, "PLACEMENT MODE (PREVIEW)", font);

        // Confirm (✓ hijau) di kiri atas (-75, 75)
        CreateSceneCircleButton(pUI.transform, "BtnConfirm", "✓",
            new Color(0.20f, 0.78f, 0.35f, 1f), 64f, new Vector2(-75f, 75f), circleSprite, font);

        // Rotate (↻ teal) di atas (0, 105)
        CreateSceneCircleButton(pUI.transform, "BtnRotate", "↻",
            new Color(0.12f, 0.72f, 0.50f, 1f), 64f, new Vector2(0f, 105f), circleSprite, font);

        // Cancel (✕ merah) di kiri (-105, 0)
        CreateSceneCircleButton(pUI.transform, "BtnCancel", "✕",
            new Color(0.90f, 0.18f, 0.15f, 1f), 64f, new Vector2(-105f, 0f), circleSprite, font);

        // Move Gizmo di kanan bawah (75, -55)
        CreateSceneMoveGizmo(pUI.transform, 56f, new Vector2(75f, -55f), circleSprite);

        // Name Label di bawah (0, -85)
        CreateSceneLabel(pUI.transform, "NameLabel", "Preview Furnitur", 14f, new Vector2(0f, -85f), roundedSprite, font);

        return pUI;
    }

    private static GameObject BuildSelectionUINode(Transform parent, Sprite circleSprite, Sprite roundedSprite, TMP_FontAsset font)
    {
        GameObject sUI = new GameObject("FloatingSelectionUI", typeof(RectTransform));
        sUI.transform.SetParent(parent, false);

        RectTransform rt = sUI.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(220f, 0f); // Tampil di sisi kanan untuk preview
        rt.sizeDelta = new Vector2(320f, 320f);

        // Header Title Preview
        CreatePreviewHeader(sUI.transform, "SELECTION MODE (PREVIEW)", font);

        // Hapus (✕ merah) di kiri (-110, 0)
        CreateSceneCircleButton(sUI.transform, "BtnHapus", "✕",
            new Color(0.90f, 0.18f, 0.15f, 1f), 64f, new Vector2(-110f, 0f), circleSprite, font);

        // Sambung Kabel (⚡ cyan) di kiri atas (-75, 75)
        GameObject cableBtn = CreateSceneCircleButton(sUI.transform, "BtnKabel", "⚡",
            new Color(0.00f, 0.72f, 0.96f, 1f), 64f, new Vector2(-75f, 75f), circleSprite, font);
        
        // Pasang UIElectricParticleEffect pada tombol kabel
        if (cableBtn.GetComponent<UIElectricParticleEffect>() == null)
            cableBtn.AddComponent<UIElectricParticleEffect>();

        // Rotasi 90° (↻ teal) di atas (0, 105)
        CreateSceneCircleButton(sUI.transform, "BtnRotate", "↻",
            new Color(0.12f, 0.72f, 0.50f, 1f), 64f, new Vector2(0f, 105f), circleSprite, font);

        // Atur Jam (⏰ oranye) di kanan atas (75, 75)
        CreateSceneCircleButton(sUI.transform, "BtnJam", "⏰",
            new Color(0.95f, 0.6f, 0.1f, 1f), 64f, new Vector2(75f, 75f), circleSprite, font);

        // Pindahkan (✥ biru) di kanan (110, 0)
        CreateSceneCircleButton(sUI.transform, "BtnPindah", "✥",
            new Color(0.25f, 0.45f, 0.85f, 1f), 64f, new Vector2(110f, 0f), circleSprite, font);

        // Move Gizmo di kanan bawah (75, -55)
        CreateSceneMoveGizmo(sUI.transform, 56f, new Vector2(75f, -55f), circleSprite);

        // Name Label di bawah (0, -85)
        CreateSceneLabel(sUI.transform, "NameLabel", "#1 Preview Furnitur • 100W", 14f, new Vector2(0f, -85f), roundedSprite, font);

        return sUI;
    }

    private static void CreatePreviewHeader(Transform parent, string title, TMP_FontAsset font)
    {
        GameObject header = new GameObject("PreviewHeader", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        header.transform.SetParent(parent, false);
        RectTransform rt = header.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 25f);
        rt.sizeDelta = new Vector2(260f, 30f);

        var tmp = header.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = title;
        tmp.fontSize = 12f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.6f);
        tmp.raycastTarget = false;
    }

    private static GameObject CreateSceneCircleButton(Transform parent, string goName, string iconText,
        Color bgColor, float size, Vector2 pos, Sprite circleSprite, TMP_FontAsset font)
    {
        GameObject btnGO = new GameObject(goName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(Outline), typeof(Shadow));
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        Image img = btnGO.GetComponent<Image>();
        img.sprite = circleSprite;
        img.type = Image.Type.Simple;
        img.color = bgColor;

        var outline = btnGO.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        var shadow = btnGO.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(2f, -3f);

        GameObject textGO = new GameObject("IconText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(btnGO.transform, false);
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = iconText;
        tmp.fontSize = size * 0.44f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (font != null) tmp.font = font;

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;

        btnGO.AddComponent<UIButtonHoverScale>();
        return btnGO;
    }

    private static GameObject CreateSceneMoveGizmo(Transform parent, float gizmoSize, Vector2 pos, Sprite circleSprite)
    {
        GameObject gizmo = new GameObject("MoveGizmo", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(Outline), typeof(Shadow));
        gizmo.transform.SetParent(parent, false);

        RectTransform rt = gizmo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(gizmoSize, gizmoSize);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        Image img = gizmo.GetComponent<Image>();
        img.sprite = circleSprite;
        img.type = Image.Type.Simple;
        img.color = new Color(1f, 1f, 1f, 0.85f);

        var outline = gizmo.GetComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.2f, 0.2f, 0.4f);
        outline.effectDistance = new Vector2(2f, -2f);

        var shadow = gizmo.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(2f, -3f);

        // Center cross ✥
        GameObject cross = new GameObject("CrossIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        cross.transform.SetParent(gizmo.transform, false);
        RectTransform cRt = cross.GetComponent<RectTransform>();
        cRt.anchorMin = Vector2.zero;
        cRt.anchorMax = Vector2.one;
        cRt.offsetMin = Vector2.zero;
        cRt.offsetMax = Vector2.zero;

        TextMeshProUGUI cTmp = cross.GetComponent<TextMeshProUGUI>();
        cTmp.text = "✥";
        cTmp.fontSize = gizmoSize * 0.48f;
        cTmp.alignment = TextAlignmentOptions.Center;
        cTmp.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
        cTmp.raycastTarget = false;

        float offset = gizmoSize * 0.52f;
        float chevronSize = gizmoSize * 0.30f;
        CreateChevronInScene(gizmo.transform, "▲", new Vector2(0f, offset), chevronSize);
        CreateChevronInScene(gizmo.transform, "▼", new Vector2(0f, -offset), chevronSize);
        CreateChevronInScene(gizmo.transform, "◀", new Vector2(-offset, 0f), chevronSize);
        CreateChevronInScene(gizmo.transform, "▶", new Vector2(offset, 0f), chevronSize);

        Button btn = gizmo.GetComponent<Button>();
        btn.targetGraphic = img;
        gizmo.AddComponent<UIButtonHoverScale>();

        return gizmo;
    }

    private static void CreateChevronInScene(Transform parent, string symbol, Vector2 offset, float size)
    {
        GameObject chevron = new GameObject("Chevron", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        chevron.transform.SetParent(parent, false);
        RectTransform rt = chevron.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = offset;

        TextMeshProUGUI tmp = chevron.GetComponent<TextMeshProUGUI>();
        tmp.text = symbol;
        tmp.fontSize = size * 0.72f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.35f, 0.35f, 0.35f, 0.9f);
        tmp.raycastTarget = false;
    }

    private static GameObject CreateSceneLabel(Transform parent, string goName, string text, float fontSize,
        Vector2 pos, Sprite roundedSprite, TMP_FontAsset font)
    {
        GameObject labelGO = new GameObject(goName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Outline));
        labelGO.transform.SetParent(parent, false);

        RectTransform rt = labelGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        Image bg = labelGO.GetComponent<Image>();
        bg.sprite = roundedSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.1f, 0.12f, 0.18f, 0.88f);
        bg.raycastTarget = false;

        var outline = labelGO.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.5f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(labelGO.transform, false);
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(14f, 4f);
        textRt.offsetMax = new Vector2(-14f, -4f);

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        if (font != null) tmp.font = font;

        float width = text.Length * fontSize * 0.65f + 32f;
        rt.sizeDelta = new Vector2(Mathf.Max(120f, width), fontSize + 18f);

        return labelGO;
    }

    private static Sprite FindCircleSprite()
    {
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.circleButtonSprite != null)
            return SimsInventoryUI.Instance.circleButtonSprite;

        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in sprites)
            if (s.name == "Button Circle") return s;
        return null;
    }

    private static Sprite FindRoundedSprite()
    {
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.roundedBoxSprite != null)
            return SimsInventoryUI.Instance.roundedBoxSprite;

        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        foreach (var s in sprites)
            if (s.name == "UI") return s;
        return null;
    }

    private static TMP_FontAsset FindFontAsset()
    {
        if (SimsInventoryUI.Instance != null && SimsInventoryUI.Instance.fontAsset != null)
            return SimsInventoryUI.Instance.fontAsset;

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (var f in fonts)
            if (f.name.Contains("Varela")) return f;
        return null;
    }
}
