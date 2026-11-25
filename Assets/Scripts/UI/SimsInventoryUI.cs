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

    [Header("Scene UI References (Dibuat di Scene Mode)")]
    [Tooltip("Root objek Bottom Bar yang bisa diatur posisinya di Scene View")]
    public GameObject bottomBarRoot;
    [Tooltip("Tombol Tutup / Kembali floating di Scene View")]
    public GameObject closeInventoryButton;
    [Tooltip("Modal popup Home Store")]
    public GameObject storeModalRoot;
    [Tooltip("Content parent di dalam Store ScrollView")]
    public GameObject storeContentParent;
    public TextMeshProUGUI cartBadgeText;
    public TextMeshProUGUI storeTitleText;
    public List<GameObject> tabButtons = new();

    // Private UI runtime handles
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
    private GameObject _closeInventoryButton;
    
    // Cached legacy UI transforms for continuous suppression
    private Transform[] _cachedLegacyTransforms;
    private bool _legacyCacheReady = false;

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
        // Continuously suppress legacy UI panels (they get re-activated by installer ToggleMode/EnterMode)
        SuppressLegacyPanels();

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

        // Show/hide close button based on active mode
        UpdateCloseButtonVisibility();
    }

    /// <summary>
    /// Suppress legacy UI panels setiap frame. Installer ToggleMode() dan EnterMode() 
    /// memanggil SlideIn() pada panel lama, jadi kita perlu menyembunyikannya terus-menerus.
    /// </summary>
    private void SuppressLegacyPanels()
    {
        if (!_legacyCacheReady)
        {
            CacheLegacyTransforms();
            _legacyCacheReady = true;
        }

        if (_cachedLegacyTransforms == null) return;

        for (int i = 0; i < _cachedLegacyTransforms.Length; i++)
        {
            Transform t = _cachedLegacyTransforms[i];
            if (t == null) continue;

            // Untuk 'Instal buttons', jangan di-deactivate karena mengandung komponen FurnitureInstaller.
            // Sebagai gantinya, pasang CanvasGroup agar 100% transparan dan tidak merespon klik/touch.
            if (t.name == "Instal buttons")
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);
            }
            else
            {
                // UI Left, UI Left SP, UI Left Batt
                var cg = t.GetComponent<CanvasGroup>();
                if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;

                if (t.gameObject.activeSelf)
                    t.gameObject.SetActive(false);
            }
        }
    }

    private void CacheLegacyTransforms()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) { _cachedLegacyTransforms = new Transform[0]; return; }

        string[] names = { "UI Left", "UI Left SP", "UI Left Batt", "Instal buttons" };
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var n in names)
        {
            Transform t = canvas.transform.Find(n);
            if (t != null) list.Add(t);
        }
        _cachedLegacyTransforms = list.ToArray();
    }

    /// <summary>
    /// Update visibilitas close button berdasarkan apakah ada mode installer aktif.
    /// </summary>
    private void UpdateCloseButtonVisibility()
    {
        bool anyModeActive = IsAnyInstallerModeActive();

        if (_closeInventoryButton != null)
            _closeInventoryButton.SetActive(anyModeActive && !_isStoreOpen);

        // Tampilkan bottom bar hanya saat tidak ada mode aktif dan store tidak terbuka
        if (_bottomBarRoot != null && !anyModeActive && !_isStoreOpen)
        {
            bool simRunning = SimulationManager.Instance != null && SimulationManager.Instance.IsSimulationRunning;
            if (!simRunning)
                _bottomBarRoot.SetActive(true);
        }
    }

    /// <summary>
    /// Cek apakah ada mode installer yang sedang aktif.
    /// </summary>
    private bool IsAnyInstallerModeActive()
    {
        // Check furniture mode
        var fInstallers = FindObjectsByType<FurnitureInstaller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var fi in fInstallers)
            if (fi != null && fi.IsFurnitureMode) return true;

        // Check solar panel mode
        if (SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode)
            return true;

        // Check battery mode
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            return true;

        // Check furniture placement actively placing
        if (FurniturePlacementController.Instance != null && FurniturePlacementController.Instance.IsPlacing)
            return true;

        return false;
    }

    /// <summary>
    /// Keluar dari SEMUA mode installer dan kembali ke tampilan awal.
    /// </summary>
    public void ExitAllModes()
    {
        // Exit furniture mode
        var furnitureInstallers = FindObjectsByType<FurnitureInstaller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var fi in furnitureInstallers)
        {
            if (fi != null && fi.IsFurnitureMode)
                fi.ForceExitMode();
        }

        // Exit solar panel mode 
        if (SolarPanelInstaller.Instance != null && SolarPanelInstaller.Instance.IsRoofMode)
            SolarPanelInstaller.Instance.ToggleMode(); // Use ToggleMode for proper camera transition back

        // Exit battery mode
        if (BatteryInstaller.Instance != null && BatteryInstaller.Instance.IsInstallMode)
            BatteryInstaller.Instance.ToggleMode(); // Use ToggleMode for proper camera transition back

        // Cancel any active furniture placement
        if (FurniturePlacementController.Instance != null)
            FurniturePlacementController.Instance.CancelPlacement();

        // Close store if open
        if (_isStoreOpen)
            CloseStore();

        // Reset close button dan tampilkan bottom bar
        if (_closeInventoryButton != null) _closeInventoryButton.SetActive(false);
        if (_modeBackButton != null) _modeBackButton.SetActive(false);
        if (_bottomBarRoot != null) _bottomBarRoot.SetActive(true);

        // Suppress legacy UI immediately
        SuppressLegacyPanels();

        Debug.Log("[SimsInventoryUI] Semua mode keluar - kembali ke tampilan awal.");
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

        // Pastikan Inverter terdaftar di katalog furnitur
        bool hasInverter = false;
        foreach (var f in furnitureList)
        {
            if (f != null && f.furnitureName.Contains("Inverter")) { hasInverter = true; break; }
        }

        if (!hasInverter)
        {
            var allData = Resources.FindObjectsOfTypeAll<FurnitureData>();
            foreach (var d in allData)
            {
                if (d != null && d.furnitureName.Contains("Inverter"))
                {
                    furnitureList.Add(d);
                    hasInverter = true;
                    break;
                }
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
        SuppressLegacyPanels();
    }
    private void BuildSimsUIHierarchy()
    {
        Canvas canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SimsInventoryUI] Tidak ditemukan Canvas di scene!");
            return;
        }

        // 1. Cek apakah hierarchy UI SUDAH ADA di Scene (dibuat di Scene Mode / Editor)
        if (TryBindExistingSceneUI(canvas))
        {
            Debug.Log("[SimsInventoryUI] Berhasil mengikat UI dari Scene Mode. Posisi dipertahankan 100%.");
            return;
        }

        // 2. Fallback: buat otomatis saat runtime jika scene belum memiliki hierarki UI
        Debug.Log("[SimsInventoryUI] UI belum ada di scene, membuat fallback runtime UI.");
        _simsRoot = new GameObject("SimsInventory_Root", typeof(RectTransform));
        _simsRoot.transform.SetParent(canvas.transform, false);
        RectTransform rootRt = _simsRoot.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        BuildBottomBar(_simsRoot.transform);
        BuildCloseInventoryButton(_simsRoot.transform);
        BuildModeBackButton(_simsRoot.transform);
        BuildStoreModal(_simsRoot.transform);
    }

    /// <summary>
    /// Mencari dan mengikat elemen UI yang sudah ada di Scene View tanpa menghancurkan
    /// atau mereset posisi yang sudah diatur oleh pengguna di Editor.
    /// </summary>
    public bool TryBindExistingSceneUI(Canvas canvas)
    {
        Transform root = sceneLayoutRoot != null ? sceneLayoutRoot : canvas.transform.Find("SimsInventory_Root");
        if (root == null)
        {
            Transform bb = canvas.transform.Find("SimsBottomBar");
            if (bb == null && bottomBarRoot == null) return false;
            root = canvas.transform;
        }

        _simsRoot = root.gameObject;

        // 1. Bottom Bar
        _bottomBarRoot = bottomBarRoot != null ? bottomBarRoot : root.Find("SimsBottomBar")?.gameObject;
        if (_bottomBarRoot != null)
        {
            Transform btnEl = _bottomBarRoot.transform.Find("BtnAlatElektronik");
            if (btnEl != null)
            {
                var b = btnEl.GetComponent<Button>();
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => OpenStore(StoreCategory.Semua)); }
            }

            Transform btnSP = _bottomBarRoot.transform.Find("BtnPanelSurya");
            if (btnSP != null)
            {
                var b = btnSP.GetComponent<Button>();
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => OpenStore(StoreCategory.PanelSurya)); }
            }

            Transform btnBat = _bottomBarRoot.transform.Find("BtnBaterai");
            if (btnBat != null)
            {
                var b = btnBat.GetComponent<Button>();
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => OpenStore(StoreCategory.Baterai)); }
            }
        }

        // 2. Close Button
        _closeInventoryButton = closeInventoryButton != null ? closeInventoryButton : root.Find("CloseInventoryBtn")?.gameObject;
        if (_closeInventoryButton != null)
        {
            var b = _closeInventoryButton.GetComponent<Button>();
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(ExitAllModes); }
            _closeInventoryButton.SetActive(false);
        }

        // 3. Store Modal
        _storeModalRoot = storeModalRoot != null ? storeModalRoot : root.Find("TheHomeStore_Modal")?.gameObject;
        if (_storeModalRoot != null)
        {
            var scrimBtn = _storeModalRoot.GetComponent<Button>();
            if (scrimBtn != null) { scrimBtn.onClick.RemoveAllListeners(); scrimBtn.onClick.AddListener(CloseStore); }

            Transform dialog = _storeModalRoot.transform.Find("DialogPanel");
            if (dialog != null)
            {
                Transform header = dialog.Find("HeaderBanner");
                if (header != null)
                {
                    Transform title = header.Find("TitleText");
                    if (title != null) _storeTitleText = title.GetComponent<TextMeshProUGUI>();
                }

                Transform backBtn = dialog.Find("BackButton");
                if (backBtn != null)
                {
                    var b = backBtn.GetComponent<Button>();
                    if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(CloseStore); }
                }

                Transform cartBtn = dialog.Find("CartButton");
                if (cartBtn != null)
                {
                    Transform badge = cartBtn.Find("CartBadge");
                    if (badge != null)
                    {
                        Transform bgt = badge.Find("BadgeText");
                        if (bgt != null) _cartBadgeText = bgt.GetComponent<TextMeshProUGUI>();
                    }
                }

                Transform tabsBar = dialog.Find("CategoryTabsBar");
                _tabButtons.Clear();
                if (tabsBar != null)
                {
                    for (int i = 0; i < tabsBar.childCount; i++)
                    {
                        Transform child = tabsBar.GetChild(i);
                        _tabButtons.Add(child.gameObject);
                        int catIdx = i;
                        var btn = child.GetComponent<Button>();
                        if (btn != null)
                        {
                            btn.onClick.RemoveAllListeners();
                            btn.onClick.AddListener(() => SelectCategory((StoreCategory)catIdx));
                        }
                    }
                }

                Transform scrollView = dialog.Find("StoreScrollView");
                if (scrollView != null)
                {
                    Transform view = scrollView.Find("Viewport");
                    if (view != null)
                    {
                        Transform content = view.Find("Content");
                        if (content != null) _storeContentParent = content.gameObject;
                    }
                }
            }

            _storeModalRoot.SetActive(false);
        }

        if (bottomBarRoot == null) bottomBarRoot = _bottomBarRoot;
        if (closeInventoryButton == null) closeInventoryButton = _closeInventoryButton;
        if (storeModalRoot == null) storeModalRoot = _storeModalRoot;
        if (storeContentParent == null) storeContentParent = _storeContentParent;
        if (cartBadgeText == null) cartBadgeText = _cartBadgeText;
        if (storeTitleText == null) storeTitleText = _storeTitleText;

        return (_bottomBarRoot != null || _storeModalRoot != null);
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
        ExitAllModes();
    }

    private void ShowInstallerBackButton()
    {
        // Now handled by UpdateCloseButtonVisibility in Update()
        // Just hide bottom bar immediately
        _bottomBarRoot?.SetActive(false);
        SuppressLegacyPanels();
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

    /// <summary>
    /// Tombol close inventory floating di pojok kanan bawah. Tampil saat mode installer aktif.
    /// Menutup mode instalasi dan mengembalikan tampilan awal ke rumah.
    /// </summary>
    private void BuildCloseInventoryButton(Transform parent)
    {
        _closeInventoryButton = new GameObject("CloseInventoryBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline), typeof(UISoftShadow));
        _closeInventoryButton.transform.SetParent(parent, false);

        RectTransform rt = _closeInventoryButton.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-28f, 22f);
        rt.sizeDelta = new Vector2(185f, 65f);

        Image img = _closeInventoryButton.GetComponent<Image>();
        img.sprite = roundedBoxSprite;
        img.type = Image.Type.Sliced;
        img.color = badgeRed;

        var outline = _closeInventoryButton.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        var shadow = _closeInventoryButton.GetComponent<UISoftShadow>();
        shadow.shadowColor = new Color(0.4f, 0f, 0f, 0.45f);
        shadow.shadowOffset = new Vector2(2f, -3f);

        GameObject iconGO = new GameObject("XIcon", typeof(RectTransform), typeof(TextMeshProUGUI));
        iconGO.transform.SetParent(_closeInventoryButton.transform, false);
        RectTransform iconRt = iconGO.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0f);
        iconRt.anchorMax = new Vector2(0.28f, 1f);
        iconRt.offsetMin = Vector2.zero;
        iconRt.offsetMax = Vector2.zero;
        var iconTmp = iconGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) iconTmp.font = fontAsset;
        iconTmp.text = "✕";
        iconTmp.fontSize = 24f;
        iconTmp.fontStyle = FontStyles.Bold;
        iconTmp.alignment = TextAlignmentOptions.Center;
        iconTmp.color = Color.white;
        iconTmp.raycastTarget = false;

        GameObject lblGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lblGO.transform.SetParent(_closeInventoryButton.transform, false);
        RectTransform lblRt = lblGO.GetComponent<RectTransform>();
        lblRt.anchorMin = new Vector2(0.25f, 0f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;
        var lblTmp = lblGO.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) lblTmp.font = fontAsset;
        lblTmp.text = "Tutup Mode";
        lblTmp.fontSize = 15f;
        lblTmp.fontStyle = FontStyles.Bold;
        lblTmp.alignment = TextAlignmentOptions.Center;
        lblTmp.color = Color.white;
        lblTmp.raycastTarget = false;

        Button btn = _closeInventoryButton.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(ExitAllModes);

        _closeInventoryButton.AddComponent<UIButtonHoverScale>();
        _closeInventoryButton.SetActive(false);
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

        // Jika tidak sedang menempatkan ghost furnitur (tidak ada item aktif), pastikan keluar dari installer mode
        // agar kamera dan UI kembali ke tampilan awal permainan
        if (FurniturePlacementController.Instance != null && !FurniturePlacementController.Instance.IsPlacing)
        {
            var furnitureInstallers = FindObjectsByType<FurnitureInstaller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var fi in furnitureInstallers)
            {
                if (fi != null && fi.IsFurnitureMode)
                    fi.ExitMode();
            }
        }
    }

    /// <summary>
    /// Membuka kembali The Home Store pada kategori terakhir setelah penempatan furnitur selesai.
    /// Memberikan jeda 1 frame agar EventSystem dan state tombol penempatan selesai dengan bersih.
    /// </summary>
    public void ReopenStore()
    {
        if (_storeModalRoot == null) return;
        StopCoroutine(nameof(ReopenStoreRoutine));
        StartCoroutine(nameof(ReopenStoreRoutine));
    }

    private IEnumerator ReopenStoreRoutine()
    {
        yield return null;
        if (_storeModalRoot == null) yield break;

        _isStoreOpen = true;
        _storeModalRoot.SetActive(true);
        SelectCategory(_currentCategory);
        UpdateCartBadge();
        Debug.Log($"[SimsInventoryUI] ReopenStore otomatis terbuka kembali pada kategori {_currentCategory}.");
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

                bool isInv = fData.furnitureName.Contains("Inverter");
                string badge = isInv ? "5000W" : $"{fData.wattConsumption}W";
                string subtitle = isInv ? "Konverter DC-AC" : $"{fData.wattConsumption} Watt";
                Sprite tileIcon = isInv && fData.icon == null ? electricityIconSprite : fData.icon;

                FurnitureData localData = fData;
                CreateItemTile(
                    localData.furnitureName,
                    tileIcon,
                    badge,
                    subtitle,
                    isInv ? tileOrangeTop : tileCyanTop,
                    isInv ? tileOrangeBottom : tileCyanBottom,
                    () => OnFurnitureSelected(localData),
                    badgeRed
                );
            }
        }

        if (_currentCategory == StoreCategory.PanelSurya)
        {
            // Tampilkan Inverter juga di tab Panel Surya sebagai komponen sistem surya
            foreach (var fData in furnitureList)
            {
                if (fData != null && fData.furnitureName.Contains("Inverter"))
                {
                    FurnitureData localData = fData;
                    Sprite tileIcon = localData.icon != null ? localData.icon : electricityIconSprite;
                    CreateItemTile(
                        localData.furnitureName,
                        tileIcon,
                        "5000W",
                        "Konverter DC-AC",
                        tileOrangeTop,
                        tileOrangeBottom,
                        () => OnFurnitureSelected(localData),
                        badgeRed
                    );
                }
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

        SuppressLegacyPanels();

        if (FurniturePlacementController.Instance != null)
        {
            FurniturePlacementController.Instance.BeginPlacementFromSims(data);
            CloseStore();
            Debug.Log($"[SimsInventoryUI] Memulai penempatan: {data.furnitureName}");
        }

        UpdateCloseButtonVisibility();
    }

    private void HideLegacyPlacementUI()
    {
        SuppressLegacyPanels();
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
            SuppressLegacyPanels();
            UpdateCloseButtonVisibility();
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
            SuppressLegacyPanels();
            UpdateCloseButtonVisibility();
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