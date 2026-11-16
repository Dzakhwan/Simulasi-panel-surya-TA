using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controller utama untuk UI Inventory & Home Store bergaya The Sims FreePlay.
/// Mengelola Bottom Action Bar (docking bawah untuk akses jempol di layar sentuh mobile)
/// dan popup "THE HOME STORE" (3-row grid tile, badge merah, kategori Dapur/Elektronik/Solar/Baterai).
/// </summary>
public class SimsInventoryUI : MonoBehaviour
{
    public static SimsInventoryUI Instance { get; private set; }

    public enum StoreCategory
    {
        Semua,
        Dapur,
        Elektronik,
        Penerangan,
        PanelSurya,
        Baterai
    }

    [Header("Data Catalogs")]
    public List<FurnitureData> furnitureList = new();
    public List<SolarPanelData> solarPanelList = new();
    public List<BattData> batteryList = new();

    [Header("Scene Layout")]
    [Tooltip("Optional persistent layout handles. Move their RectTransforms in Scene View; runtime UI copies them.")]
    public RectTransform sceneLayoutRoot;

    [Header("Assets & Sprites")]
    public Sprite circleButtonSprite;
    public Sprite roundedBoxSprite;
    public Sprite sunIconSprite;
    public Sprite electricityIconSprite;
    public TMP_FontAsset fontAsset;

    [Header("Colors - The Sims FreePlay Palette")]
    public Color simsHeaderTop = new Color(0.0f, 0.65f, 0.95f, 1f);      // #00A6F3
    public Color simsHeaderBottom = new Color(0.0f, 0.44f, 0.72f, 1f);   // #0070B8
    public Color simsPanelBg = new Color(0.85f, 0.94f, 0.98f, 0.98f);    // #D9F0FA
    public Color tileCyanTop = new Color(0.0f, 0.72f, 0.96f, 1f);        // #00B8F5
    public Color tileCyanBottom = new Color(0.0f, 0.48f, 0.75f, 1f);     // #007ABF
    public Color tileOrangeTop = new Color(1.0f, 0.62f, 0.05f, 1f);      // #FF9E0D
    public Color tileOrangeBottom = new Color(0.88f, 0.40f, 0.02f, 1f);   // #E06605
    public Color tileGreenTop = new Color(0.20f, 0.80f, 0.35f, 1f);       // #33CC59
    public Color tileGreenBottom = new Color(0.12f, 0.60f, 0.22f, 1f);    // #1F9938
    public Color badgeRed = new Color(0.93f, 0.12f, 0.15f, 1f);          // #ED1F26

    // UI Roots
    private GameObject _simsRoot;
    private GameObject _bottomBarRoot;
    private GameObject _storeModalRoot;
    private GameObject _storeContentParent;
    private TextMeshProUGUI _cartBadgeText;
    private TextMeshProUGUI _storeTitleText;
    private readonly List<GameObject> _tabButtons = new();

    private StoreCategory _currentCategory = StoreCategory.Semua;
    private bool _isStoreOpen = false;
    private GameObject _modeBackButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        LoadResourcesIfNeeded();
        CollectDataFromExistingCatalogs();
        DeactivateLegacyUI();
        BuildSimsUIHierarchy();
        UpdateCartBadge();

        if (FurnitureManager.Instance != null)
            FurnitureManager.Instance.OnPowerChanged.AddListener(_ => UpdateCartBadge());
    }

    private void Update()
    {
        if (SimulationManager.Instance != null)
        {
            bool simRunning = SimulationManager.Instance.IsSimulationRunning;
            if (_bottomBarRoot != null && _bottomBarRoot.activeSelf == simRunning)
            {
                _bottomBarRoot.SetActive(!simRunning);
                if (simRunning && _isStoreOpen)
                    CloseStore();
            }
        }

        if (_isStoreOpen && GameInput.Player.Cancel.WasPressedThisFrame())
        {
            CloseStore();
        }
    }

    private void LoadResourcesIfNeeded()
    {
        if (circleButtonSprite == null)
        {
            circleButtonSprite = Resources.Load<Sprite>("Button Circle");
            if (circleButtonSprite == null)
            {
                var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
                foreach (var s in allSprites)
                {
                    if (s.name == "Button Circle") { circleButtonSprite = s; break; }
                }
            }
        }

        if (roundedBoxSprite == null)
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
            {
                if (s.name == "UI") { roundedBoxSprite = s; break; }
            }
        }

        if (sunIconSprite == null)
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
            {
                if (s.name == "SUN") { sunIconSprite = s; break; }
            }
        }

        if (electricityIconSprite == null)
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in allSprites)
            {
                if (s.name == "Electricity") { electricityIconSprite = s; break; }
            }
        }

        if (fontAsset == null)
        {
            var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (var f in allFonts)
            {
                if (f.name.Contains("Varela")) { fontAsset = f; break; }
            }
        }
    }

    private void CollectDataFromExistingCatalogs()
    {
        if (furnitureList.Count == 0)
        {
            var fCat = FindFirstObjectByType<FurnitureCatalogUI>(FindObjectsInactive.Include);
            if (fCat != null && fCat.furnitureList != null)
            {
                furnitureList.AddRange(fCat.furnitureList);
            }
        }

        if (solarPanelList.Count == 0)
        {
            var spCat = FindFirstObjectByType<SolarPanelCatalogUI>(FindObjectsInactive.Include);
            if (spCat != null && spCat.panelTypes != null)
            {
                solarPanelList.AddRange(spCat.panelTypes);
            }
        }

        if (batteryList.Count == 0)
        {
            var bCat = FindFirstObjectByType<BatteryCatalogUI>(FindObjectsInactive.Include);
            if (bCat != null && bCat.battTypes != null)
            {
                batteryList.AddRange(bCat.battTypes);
            }
        }
    }

    private void DeactivateLegacyUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        string[] legacyNames = new string[]
        {
            "UI Left",
            "UI Left SP",
            "UI Left Batt",
            "Instal buttons"
        };

        foreach (var n in legacyNames)
        {
            Transform t = canvas.transform.Find(n);
            if (t != null)
            {
                t.gameObject.SetActive(false);
            }
        }
    }
    private void BuildSimsUIHierarchy()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SimsInventoryUI] Tidak ditemukan Canvas di scene!");
            return;
        }

        if (sceneLayoutRoot != null)
        {
            _simsRoot = sceneLayoutRoot.gameObject;
            foreach (Transform child in _simsRoot.transform)
                Destroy(child.gameObject);
        }
        else
        {
            _simsRoot = new GameObject("SimsInventory_Root", typeof(RectTransform));
            _simsRoot.transform.SetParent(canvas.transform, false);
        }
        RectTransform rootRt = _simsRoot.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        BuildBottomBar(_simsRoot.transform);
        BuildModeBackButton(_simsRoot.transform);
        BuildStoreModal(_simsRoot.transform);
    }

    private void BuildBottomBar(Transform parent)
    {
        _bottomBarRoot = new GameObject("SimsBottomBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        _bottomBarRoot.transform.SetParent(parent, false);

        RectTransform rt = _bottomBarRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-28f, 22f);
        rt.sizeDelta = new Vector2(380f, 95f);

        var hlg = _bottomBarRoot.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        Sprite furnIcon = (furnitureList.Count > 0 && furnitureList[0].icon != null) ? furnitureList[0].icon : null;
        CreateDockButton(_bottomBarRoot.transform, "BtnAlatElektronik", "Elektronik", furnIcon, tileCyanTop, tileCyanBottom, () =>
        {
            OpenStore(StoreCategory.Semua);
        });

        CreateDockButton(_bottomBarRoot.transform, "BtnPanelSurya", "Panel Surya", sunIconSprite, tileOrangeTop, tileOrangeBottom, () =>
        {
            OpenStore(StoreCategory.PanelSurya);
        });

        CreateDockButton(_bottomBarRoot.transform, "BtnBaterai", "Baterai", electricityIconSprite, tileGreenTop, tileGreenBottom, () =>
        {
            OpenStore(StoreCategory.Baterai);
        });
    }

    private void BuildModeBackButton(Transform parent)
    {
        _modeBackButton = new GameObject("ModeBackButton", typeof(RectTransform), typeof(Image), typeof(Button));
        _modeBackButton.transform.SetParent(parent, false);
        var rect = _modeBackButton.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.02f, 0.86f);
        rect.anchorMax = new Vector2(0.22f, 0.94f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _modeBackButton.GetComponent<Image>().color = new Color(0.16f, 0.45f, 0.72f, 1f);
        _modeBackButton.GetComponent<Button>().onClick.AddListener(ExitInstallerMode);
        var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.transform.SetParent(_modeBackButton.transform, false);
        var tr = text.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
        var tmp = text.GetComponent<TextMeshProUGUI>();
        tmp.text = "← Kembali"; tmp.fontSize = 16f; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white; tmp.raycastTarget = false;
        _modeBackButton.SetActive(false);
    }

    private void ExitInstallerMode()
    {
        SolarPanelInstaller.Instance?.ForceExitMode();
        BatteryInstaller.Instance?.ForceExitMode();
        _modeBackButton?.SetActive(false);
        _bottomBarRoot?.SetActive(true);
        HideLegacyPlacementUI();
    }

    private void ShowInstallerBackButton()
    {
        _modeBackButton?.SetActive(true);
        _bottomBarRoot?.SetActive(false);
        HideLegacyPlacementUI();
    }

    private void CreateDockButton(Transform parent, string goName, string label, Sprite icon, Color topColor, Color bottomColor, Action onClick)
    {
        GameObject btnGO = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(UISoftShadow));
        btnGO.transform.SetParent(parent, false);

        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(85f, 85f);

        Image img = btnGO.GetComponent<Image>();
        img.sprite = circleButtonSprite != null ? circleButtonSprite : roundedBoxSprite;
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
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.text = label;
        tmp.fontSize = 11.5f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;

        Button btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());

        btnGO.AddComponent<UIButtonHoverScale>();
    }

    private void BuildStoreModal(Transform parent)
    {
        _storeModalRoot = new GameObject("TheHomeStore_Modal", typeof(RectTransform), typeof(Image));
        _storeModalRoot.transform.SetParent(parent, false);

        RectTransform modalRt = _storeModalRoot.GetComponent<RectTransform>();
        modalRt.anchorMin = Vector2.zero;
        modalRt.anchorMax = Vector2.one;
        modalRt.offsetMin = Vector2.zero;
        modalRt.offsetMax = Vector2.zero;

        Image scrim = _storeModalRoot.GetComponent<Image>();
        scrim.color = new Color(0f, 0f, 0f, 0.40f);
        var scrimBtn = _storeModalRoot.AddComponent<Button>();
        scrimBtn.targetGraphic = scrim;
        scrimBtn.onClick.AddListener(CloseStore);

        GameObject dialogGO = new GameObject("DialogPanel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(UISoftShadow));
        dialogGO.transform.SetParent(_storeModalRoot.transform, false);

        RectTransform dRt = dialogGO.GetComponent<RectTransform>();
        dRt.anchorMin = new Vector2(0.5f, 0.5f);
        dRt.anchorMax = new Vector2(0.5f, 0.5f);
        dRt.pivot = new Vector2(0.5f, 0.5f);
        dRt.anchoredPosition = new Vector2(0f, -15f);
        dRt.sizeDelta = new Vector2(1280f, 620f);

        Image dImg = dialogGO.GetComponent<Image>();
        dImg.sprite = roundedBoxSprite;
        dImg.type = Image.Type.Sliced;
        dImg.color = simsPanelBg;

        var dOutline = dialogGO.GetComponent<Outline>();
        dOutline.effectColor = Color.white;
        dOutline.effectDistance = new Vector2(4f, -4f);

        var dShadow = dialogGO.GetComponent<UISoftShadow>();
        dShadow.shadowColor = new Color(0f, 0.1f, 0.25f, 0.4f);
        dShadow.shadowOffset = new Vector2(4f, -6f);

        dialogGO.AddComponent<GraphicRaycaster>();

        BuildStoreHeader(dialogGO.transform);
        BuildCategoryTabs(dialogGO.transform);
        BuildStoreScrollView(dialogGO.transform);

        _storeModalRoot.SetActive(false);
    }

    private void BuildStoreHeader(Transform dialog)
    {
        GameObject headerGO = new GameObject("HeaderBanner", typeof(RectTransform), typeof(Image), typeof(Outline));
        headerGO.transform.SetParent(dialog, false);

        RectTransform hRt = headerGO.GetComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0.5f, 1f);
        hRt.anchorMax = new Vector2(0.5f, 1f);
        hRt.pivot = new Vector2(0.5f, 0.5f);
        hRt.anchoredPosition = new Vector2(0f, 4f);
        hRt.sizeDelta = new Vector2(580f, 56f);

        Image hImg = headerGO.GetComponent<Image>();
        hImg.sprite = roundedBoxSprite;
        hImg.type = Image.Type.Sliced;
        var hGrad = headerGO.AddComponent<UIGradient>();
        hGrad.colorTop = simsHeaderTop;
        hGrad.colorBottom = simsHeaderBottom;

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

        _storeTitleText = titleGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) _storeTitleText.font = fontAsset;
        _storeTitleText.text = "THE HOME STORE";
        _storeTitleText.fontSize = 22f;
        _storeTitleText.fontStyle = FontStyles.Bold;
        _storeTitleText.alignment = TextAlignmentOptions.Center;
        _storeTitleText.color = Color.white;

        GameObject backBtnGO = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        backBtnGO.transform.SetParent(dialog, false);
        RectTransform bRt = backBtnGO.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f);
        bRt.anchorMax = new Vector2(0f, 1f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = new Vector2(40f, 4f);
        bRt.sizeDelta = new Vector2(56f, 56f);

        Image bImg = backBtnGO.GetComponent<Image>();
        bImg.sprite = circleButtonSprite != null ? circleButtonSprite : roundedBoxSprite;
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
        if (fontAsset != null) btTmp.font = fontAsset;
        btTmp.text = "<";
        btTmp.fontSize = 28f;
        btTmp.fontStyle = FontStyles.Bold;
        btTmp.alignment = TextAlignmentOptions.Center;
        btTmp.color = Color.white;

        Button bBtn = backBtnGO.GetComponent<Button>();
        bBtn.onClick.AddListener(CloseStore);

        GameObject cartBtnGO = new GameObject("CartButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        cartBtnGO.transform.SetParent(dialog, false);
        RectTransform cRt = cartBtnGO.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(1f, 1f);
        cRt.anchorMax = new Vector2(1f, 1f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.anchoredPosition = new Vector2(-40f, 4f);
        cRt.sizeDelta = new Vector2(56f, 56f);

        Image cImg = cartBtnGO.GetComponent<Image>();
        cImg.sprite = circleButtonSprite != null ? circleButtonSprite : roundedBoxSprite;
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
        if (fontAsset != null) ciTmp.font = fontAsset;
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
        bgImg.sprite = circleButtonSprite != null ? circleButtonSprite : roundedBoxSprite;
        bgImg.color = badgeRed;

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

        _cartBadgeText = badgeTextGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) _cartBadgeText.font = fontAsset;
        _cartBadgeText.text = "0";
        _cartBadgeText.fontSize = 12f;
        _cartBadgeText.fontStyle = FontStyles.Bold;
        _cartBadgeText.alignment = TextAlignmentOptions.Center;
        _cartBadgeText.color = Color.white;
    }

    private void BuildCategoryTabs(Transform dialog)
    {
        GameObject tabsBar = new GameObject("CategoryTabsBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabsBar.transform.SetParent(dialog, false);

        RectTransform tRt = tabsBar.GetComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0f, 1f);
        tRt.anchorMax = new Vector2(1f, 1f);
        tRt.pivot = new Vector2(0.5f, 1f);
        tRt.anchoredPosition = new Vector2(0f, -40f);
        tRt.sizeDelta = new Vector2(-40f, 44f);

        var hlg = tabsBar.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        _tabButtons.Clear();

        CreateCategoryTab(tabsBar.transform, StoreCategory.Semua, "Semua");
        CreateCategoryTab(tabsBar.transform, StoreCategory.Dapur, "Dapur");
        CreateCategoryTab(tabsBar.transform, StoreCategory.Elektronik, "Elektronik");
        CreateCategoryTab(tabsBar.transform, StoreCategory.Penerangan, "Penerangan");
        CreateCategoryTab(tabsBar.transform, StoreCategory.PanelSurya, "Panel Surya");
        CreateCategoryTab(tabsBar.transform, StoreCategory.Baterai, "Baterai");
    }

    private void CreateCategoryTab(Transform parent, StoreCategory cat, string title)
    {
        GameObject tabGO = new GameObject($"Tab_{cat}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        tabGO.transform.SetParent(parent, false);

        RectTransform rt = tabGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(145f, 36f);

        Image img = tabGO.GetComponent<Image>();
        img.sprite = roundedBoxSprite;
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
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.text = title;
        tmp.fontSize = 13.5f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;

        Button btn = tabGO.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            SelectCategory(cat);
        });

        _tabButtons.Add(tabGO);
    }
    private void BuildStoreScrollView(Transform dialog)
    {
        GameObject scrollGO = new GameObject("StoreScrollView", typeof(RectTransform), typeof(ScrollRect));
        scrollGO.transform.SetParent(dialog, false);

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
        RectTransform cRt = contentGO.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0f, 0f);
        cRt.anchorMax = new Vector2(0f, 1f);
        cRt.pivot = new Vector2(0f, 0.5f);
        cRt.offsetMin = Vector2.zero;
        cRt.offsetMax = Vector2.zero;

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
        sr.content = cRt;
        _storeContentParent = contentGO;
    }

    public void OpenStore(StoreCategory initialCategory = StoreCategory.Semua)
    {
        if (_storeModalRoot == null) return;

        _isStoreOpen = true;
        _storeModalRoot.SetActive(true);
        SelectCategory(initialCategory);
        UpdateCartBadge();
    }

    public void CloseStore()
    {
        if (_storeModalRoot == null) return;

        _isStoreOpen = false;
        _storeModalRoot.SetActive(false);
    }

    public void SelectCategory(StoreCategory category)
    {
        _currentCategory = category;

        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var tabGO = _tabButtons[i];
            var img = tabGO.GetComponent<Image>();
            var txt = tabGO.GetComponentInChildren<TextMeshProUGUI>();

            bool isSelected = (i == (int)category);
            if (isSelected)
            {
                img.color = new Color(0.0f, 0.55f, 0.85f, 1f);
                txt.color = Color.white;
            }
            else
            {
                img.color = new Color(1f, 1f, 1f, 0.65f);
                txt.color = new Color(0.15f, 0.25f, 0.35f, 1f);
            }
        }

        if (_storeTitleText != null)
        {
            _storeTitleText.text = category == StoreCategory.Semua ? "THE HOME STORE" : $"THE HOME STORE - {category.ToString().ToUpper()}";
        }

        PopulateTiles();
    }

    private void PopulateTiles()
    {
        if (_storeContentParent == null) return;

        foreach (Transform child in _storeContentParent.transform)
        {
            Destroy(child.gameObject);
        }

        if (_currentCategory == StoreCategory.Semua || _currentCategory == StoreCategory.Dapur || _currentCategory == StoreCategory.Elektronik)
        {
            CreateItemTile(
                "Atur Jadwal",
                null,
                "CONFIG",
                "Jam & Daya",
                tileOrangeTop,
                tileOrangeBottom,
                () =>
                {
                    CloseStore();
                    var cfg = FurnitureConfigPanel.Instance ?? FindFirstObjectByType<FurnitureConfigPanel>(FindObjectsInactive.Include);
                    if (cfg != null && furnitureList.Count > 0)
                        cfg.Open(furnitureList[0]);
                },
                badgeRed
            );
        }

        if (_currentCategory == StoreCategory.Semua ||
            _currentCategory == StoreCategory.Dapur ||
            _currentCategory == StoreCategory.Elektronik ||
            _currentCategory == StoreCategory.Penerangan)
        {
            foreach (var fData in furnitureList)
            {
                if (fData == null) continue;

                StoreCategory itemCat = ClassifyFurniture(fData.furnitureName);
                if (_currentCategory != StoreCategory.Semua && itemCat != _currentCategory)
                    continue;

                string badge = $"{fData.wattConsumption}W";
                string subtitle = $"{fData.wattConsumption} Watt";

                FurnitureData localData = fData;
                CreateItemTile(
                    localData.furnitureName,
                    localData.icon,
                    badge,
                    subtitle,
                    tileCyanTop,
                    tileCyanBottom,
                    () => OnFurnitureSelected(localData),
                    badgeRed
                );
            }
        }

        if (_currentCategory == StoreCategory.Semua || _currentCategory == StoreCategory.PanelSurya)
        {
            foreach (var spData in solarPanelList)
            {
                if (spData == null) continue;

                string badge = $"{spData.wattOutput}W";
                string subtitle = $"Efisiensi {(spData.efficiency * 100f):F0}%";
                Sprite icon = spData.icon != null ? spData.icon : sunIconSprite;

                SolarPanelData localData = spData;
                CreateItemTile(
                    localData.panelName,
                    icon,
                    badge,
                    subtitle,
                    tileOrangeTop,
                    tileOrangeBottom,
                    () => OnSolarPanelSelected(localData),
                    badgeRed
                );
            }
        }

        if (_currentCategory == StoreCategory.Semua || _currentCategory == StoreCategory.Baterai)
        {
            foreach (var bData in batteryList)
            {
                if (bData == null) continue;

                string badge = $"{bData.capacity}kWh";
                string subtitle = $"Kapasitas {bData.capacity} kWh";
                Sprite icon = electricityIconSprite;

                BattData localData = bData;
                CreateItemTile(
                    localData.battName,
                    icon,
                    badge,
                    subtitle,
                    tileGreenTop,
                    tileGreenBottom,
                    () => OnBatterySelected(localData),
                    badgeRed
                );
            }
        }
    }

    private StoreCategory ClassifyFurniture(string name)
    {
        string lower = name.ToLower();
        if (lower.Contains("kulkas") || lower.Contains("kompor") || lower.Contains("microwave") ||
            lower.Contains("blender") || lower.Contains("kopi") || lower.Contains("roti") || lower.Contains("toaster"))
            return StoreCategory.Dapur;

        if (lower.Contains("lampu"))
            return StoreCategory.Penerangan;

        return StoreCategory.Elektronik;
    }

    private void CreateItemTile(
        string itemName,
        Sprite icon,
        string badgeValue,
        string subtitle,
        Color topColor,
        Color bottomColor,
        Action onClick,
        Color badgeColor)
    {
        GameObject tileGO = new GameObject($"Tile_{itemName}", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button), typeof(UISoftShadow));
        tileGO.transform.SetParent(_storeContentParent.transform, false);

        RectTransform rt = tileGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(150f, 140f);

        Image bgImg = tileGO.GetComponent<Image>();
        bgImg.sprite = roundedBoxSprite;
        bgImg.type = Image.Type.Sliced;

        var outline = tileGO.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        var shadow = tileGO.GetComponent<UISoftShadow>();
        shadow.shadowColor = new Color(0f, 0.15f, 0.35f, 0.35f);
        shadow.shadowOffset = new Vector2(2f, -3f);

        Button btn = tileGO.GetComponent<Button>();
        btn.targetGraphic = bgImg;

        GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(tileGO.transform, false);
        RectTransform iconRt = iconGO.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.58f);
        iconRt.anchorMax = new Vector2(0.5f, 0.58f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(68f, 68f);

        Image iconImg = iconGO.GetComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        GameObject nameGO = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(tileGO.transform, false);
        RectTransform nameRt = nameGO.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0.04f, 0.18f);
        nameRt.anchorMax = new Vector2(0.96f, 0.34f);
        nameRt.offsetMin = Vector2.zero;
        nameRt.offsetMax = Vector2.zero;

        var nameTmp = nameGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) nameTmp.font = fontAsset;
        nameTmp.fontSize = 11.5f;
        nameTmp.fontStyle = FontStyles.Bold;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color = Color.white;
        nameTmp.raycastTarget = false;
        nameTmp.enableWordWrapping = false;
        nameTmp.overflowMode = TextOverflowModes.Ellipsis;

        GameObject subGO = new GameObject("SubtitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
        subGO.transform.SetParent(tileGO.transform, false);
        RectTransform subRt = subGO.GetComponent<RectTransform>();
        subRt.anchorMin = new Vector2(0.04f, 0.04f);
        subRt.anchorMax = new Vector2(0.96f, 0.18f);
        subRt.offsetMin = Vector2.zero;
        subRt.offsetMax = Vector2.zero;

        var subTmp = subGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) subTmp.font = fontAsset;
        subTmp.fontSize = 9.5f;
        subTmp.alignment = TextAlignmentOptions.Center;
        subTmp.color = new Color(1f, 1f, 0.7f, 1f);
        subTmp.raycastTarget = false;

        GameObject badgeGO = new GameObject("Badge", typeof(RectTransform), typeof(Image), typeof(Outline));
        badgeGO.transform.SetParent(tileGO.transform, false);
        RectTransform badgeRt = badgeGO.GetComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(1f, 1f);
        badgeRt.anchorMax = new Vector2(1f, 1f);
        badgeRt.pivot = new Vector2(0.5f, 0.5f);
        badgeRt.anchoredPosition = new Vector2(-10f, -10f);
        badgeRt.sizeDelta = new Vector2(46f, 22f);

        Image badgeImg = badgeGO.GetComponent<Image>();
        badgeImg.sprite = roundedBoxSprite;
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = badgeColor;

        var badgeOutline = badgeGO.GetComponent<Outline>();
        badgeOutline.effectColor = Color.white;
        badgeOutline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject badgeTextGO = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        badgeTextGO.transform.SetParent(badgeGO.transform, false);
        RectTransform btRt = badgeTextGO.GetComponent<RectTransform>();
        btRt.anchorMin = Vector2.zero;
        btRt.anchorMax = Vector2.one;
        btRt.offsetMin = Vector2.zero;
        btRt.offsetMax = Vector2.zero;

        var btTmp = badgeTextGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) btTmp.font = fontAsset;
        btTmp.fontSize = 10f;
        btTmp.fontStyle = FontStyles.Bold;
        btTmp.alignment = TextAlignmentOptions.Center;
        btTmp.color = Color.white;
        btTmp.raycastTarget = false;

        var tile = tileGO.AddComponent<SimsItemTile>();
        tile.backgroundImage = bgImg;
        tile.outlineImage = outline;
        tile.iconImage = iconImg;
        tile.nameText = nameTmp;
        tile.subtitleText = subTmp;
        tile.badgeRoot = badgeGO;
        tile.badgeImage = badgeImg;
        tile.badgeText = btTmp;
        tile.button = btn;

        tile.Setup(
            itemName,
            icon,
            badgeValue,
            subtitle,
            topColor,
            bottomColor,
            onClick,
            badgeColor
        );
    }

    private void OnFurnitureSelected(FurnitureData data)
    {
        CloseStore();

        FurnitureInstaller installer = null;
        var installers = FindObjectsByType<FurnitureInstaller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var candidate in installers)
        {
            if (candidate == null) continue;
            if (candidate.IsFurnitureMode)
            {
                installer = candidate;
                break;
            }
            if (installer == null) installer = candidate;
        }

        if (installer == null)
        {
            Debug.LogWarning("[SimsInventoryUI] FurnitureInstaller tidak ditemukan.");
            return;
        }

        if (!installer.IsFurnitureMode)
            installer.EnterMode();

        foreach (var legacyUi in GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (legacyUi.name == "UI Left" || legacyUi.name == "UI Left SP" || legacyUi.name == "UI Left Batt" || legacyUi.name == "Instal buttons")
                legacyUi.SetActive(false);
        }

        HideLegacyPlacementUI();
        if (FurniturePlacementController.Instance != null)
        {
            FurniturePlacementController.Instance.BeginPlacementFromSims(data);
            Debug.Log($"[SimsInventoryUI] Memulai penempatan: {data.furnitureName}");
        }
    }

    private void HideLegacyPlacementUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;
        foreach (Transform child in canvas.transform)
        {
            if (child.name == "UI Left" || child.name == "UI Left SP" || child.name == "UI Left Batt" || child.name == "Instal buttons")
                child.gameObject.SetActive(false);
        }
    }

    private void OnSolarPanelSelected(SolarPanelData data)
    {
        CloseStore();

        if (SolarPanelInstaller.Instance != null)
        {
            SolarPanelInstaller.Instance.SetActivePanelType(data);
            if (!SolarPanelInstaller.Instance.IsRoofMode)
            {
                SolarPanelInstaller.Instance.ToggleMode();
            }
            HideLegacyPlacementUI();
            ShowInstallerBackButton();
            Debug.Log($"[SimsInventoryUI] Panel Surya terpilih: {data.panelName}");
        }
    }

    private void OnBatterySelected(BattData data)
    {
        CloseStore();

        if (BatteryInstaller.Instance != null)
        {
            BatteryInstaller.Instance.SetActiveBattType(data);
            if (!BatteryInstaller.Instance.IsInstallMode)
            {
                BatteryInstaller.Instance.ToggleMode();
            }
            HideLegacyPlacementUI();
            ShowInstallerBackButton();
            Debug.Log($"[SimsInventoryUI] Baterai terpilih: {data.battName}");
        }

        if (BatteryManager.Instance != null)
        {
            BatteryManager.Instance.SetBatteryData(data);
        }
    }

    private void UpdateCartBadge()
    {
        if (_cartBadgeText == null) return;

        int furnCount = FurnitureManager.Instance != null ? FurnitureManager.Instance.PlacedCount : 0;
        int panelCount = BatteryManager.Instance != null ? BatteryManager.Instance.PanelCount : 0;
        int battCount = BatteryManager.Instance != null ? BatteryManager.Instance.InstalledBatteryCount : 0;

        int total = furnCount + panelCount + battCount;
        _cartBadgeText.text = total.ToString();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        if (FindFirstObjectByType<SimsInventoryUI>() == null)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                GameObject go = new GameObject("SimsInventoryUI", typeof(SimsInventoryUI));
                go.transform.SetParent(canvas.transform, false);
                Debug.Log("[SimsInventoryUI] AutoBootstrap berhasil diinisialisasi.");
            }
        }
    }
}