using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using SolarEdu;
using UnityEngine.EventSystems;

public class LoginController : MonoBehaviour
{
    [Header("Scene Target")]
    [SerializeField] private string nextSceneName = "Menu";

    [Header("Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject signupPanel;

    [Header("Login Fields")]
    [SerializeField] private TMP_InputField loginUsername;
    [SerializeField] private TMP_InputField loginPassword;
    [SerializeField] private TMP_Text loginError;

    [Header("Signup Fields")]
    [SerializeField] private TMP_InputField signupName;
    [SerializeField] private TMP_InputField signupEmail;
    [SerializeField] private TMP_InputField signupPassword;
    [SerializeField] private TMP_InputField signupConfirm;
    [SerializeField] private TMP_InputField signupClassCode;
    [SerializeField] private TMP_Text signupError;

    // --- BAGIAN YANG DIPERBARUI: Eye Icon dipisah menjadi 3 ---
    [Header("Password Toggle Icons (Raw Image)")]
    [SerializeField] private RawImage loginEyeIcon;
    [SerializeField] private RawImage signupPasswordEyeIcon; // Untuk kolom Password Sign-Up
    [SerializeField] private RawImage signupConfirmEyeIcon;  // Untuk kolom Confirm Sign-Up
    [SerializeField] private Texture iconEyeHidden;
    [SerializeField] private Texture iconEyeVisible;

    private SolarEduAuth auth;
    private bool isLoginPasswordVisible = false;
    private bool isSignupPasswordVisible = false;
    private bool isSignupConfirmVisible = false; // Status untuk Confirm Password

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        auth = FindFirstObjectByType<SolarEduAuth>();
        if (auth == null)
            Debug.LogError("[LoginController] SolarEduAuth tidak ditemukan!");

        if (loginPanel == null || signupPanel == null)
            AutoWire();

        if (auth != null && auth.TryAutoLogin())
        {
            SceneManager.LoadScene(nextSceneName);
            return;
        }

        ShowLogin();
    }

    private void Update()
    {
        if (GameInput.Player.Tab.WasPressedThisFrame())
        {
            EventSystem system = EventSystem.current;
            if (system == null) return;

            // 1. JIKA ADA UI YANG SEDANG DIPILIH
            if (system.currentSelectedGameObject != null)
            {
                Selectable currentSelectable = system.currentSelectedGameObject.GetComponent<Selectable>();
                if (currentSelectable != null)
                {
                    bool isShiftDown = GameInput.Player.Shift.IsPressed();

                    Selectable next = isShiftDown ?
                        currentSelectable.FindSelectableOnUp() :
                        currentSelectable.FindSelectableOnDown();

                    if (next != null)
                    {
                        next.Select();

                        // Ekstra: Pastikan kursor langsung berkedip di kolom selanjutnya
                        TMP_InputField nextInput = next.GetComponent<TMP_InputField>();
                        if (nextInput != null) nextInput.ActivateInputField();
                    }
                }
            }
            // 2. JIKA TIDAK ADA UI YANG DIPILIH
            else
            {
                if (loginPanel != null && loginPanel.activeSelf)
                {
                    if (loginUsername != null)
                    {
                        loginUsername.Select();
                        loginUsername.ActivateInputField();
                    }
                }
                else if (signupPanel != null && signupPanel.activeSelf)
                {
                    if (signupName != null)
                    {
                        signupName.Select();
                        signupName.ActivateInputField();
                    }
                }
            }
        }
    }

    // ── Auto-wire fallback ─────────────────────────────────────────────────
    private void AutoWire()
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        if (loginPanel == null) loginPanel = FindChildRecursive(canvas.gameObject, "LoginPanel");
        if (signupPanel == null) signupPanel = FindChildRecursive(canvas.gameObject, "SignupPanel");

        if (loginPanel != null)
        {
            if (loginUsername == null) loginUsername = FindInputRecursive(loginPanel, "UsernameField");
            if (loginPassword == null) loginPassword = FindInputRecursive(loginPanel, "PasswordField");
            if (loginError == null) loginError = FindTextRecursive(loginPanel, "ErrorText");
        }
        if (signupPanel != null)
        {
            if (signupName == null) signupName = FindInputRecursive(signupPanel, "NameField");
            if (signupEmail == null) signupEmail = FindInputRecursive(signupPanel, "EmailField");
            if (signupPassword == null) signupPassword = FindInputRecursive(signupPanel, "PasswordField");
            if (signupConfirm == null) signupConfirm = FindInputRecursive(signupPanel, "ConfirmField");
            if (signupClassCode == null) signupClassCode = FindInputRecursive(signupPanel, "ClassCodeField");
            if (signupError == null) signupError = FindTextRecursive(signupPanel, "ErrorText");
        }
    }

    // ── Panel switching ────────────────────────────────────────────────────

    public void ShowLogin()
    {
        SetActive(loginPanel, true);
        SetActive(signupPanel, false);
        ClearInputs();
        ClearErrors();

        isLoginPasswordVisible = false;
        SetPasswordVisibility(loginPassword, isLoginPasswordVisible, loginEyeIcon);

        if (loginUsername != null) loginUsername.Select();
    }

    public void ShowSignup()
    {
        SetActive(loginPanel, false);
        SetActive(signupPanel, true);
        ClearInputs();
        ClearErrors();

        // Mengatur ulang kedua status password di Sign-Up ke Hidden
        isSignupPasswordVisible = false;
        isSignupConfirmVisible = false;

        SetPasswordVisibility(signupPassword, isSignupPasswordVisible, signupPasswordEyeIcon);
        SetPasswordVisibility(signupConfirm, isSignupConfirmVisible, signupConfirmEyeIcon);

        if (signupName != null) signupName.Select();
    }

    // ── Show/Hide Password Toggle ──────────────────────────────────────────

    public void ToggleLoginPasswordVisibility()
    {
        isLoginPasswordVisible = !isLoginPasswordVisible;
        SetPasswordVisibility(loginPassword, isLoginPasswordVisible, loginEyeIcon);
    }

    // Fungsi Toggle untuk Password Sign-Up
    public void ToggleSignupPasswordVisibility()
    {
        isSignupPasswordVisible = !isSignupPasswordVisible;
        SetPasswordVisibility(signupPassword, isSignupPasswordVisible, signupPasswordEyeIcon);
    }

    // Fungsi Toggle untuk Confirm Password Sign-Up
    public void ToggleSignupConfirmVisibility()
    {
        isSignupConfirmVisible = !isSignupConfirmVisible;
        SetPasswordVisibility(signupConfirm, isSignupConfirmVisible, signupConfirmEyeIcon);
    }

    private void SetPasswordVisibility(TMP_InputField inputField, bool isVisible, RawImage targetEyeIcon)
    {
        if (inputField == null) return;

        inputField.contentType = isVisible ?
            TMP_InputField.ContentType.Standard :
            TMP_InputField.ContentType.Password;

        inputField.ForceLabelUpdate();

        if (targetEyeIcon != null && iconEyeHidden != null && iconEyeVisible != null)
        {
            targetEyeIcon.texture = isVisible ? iconEyeVisible : iconEyeHidden;
        }
    }

    // ── Login & Sign-Up ────────────────────────────────────────────────────

    public void OnLoginClicked()
    {
        string user = loginUsername != null ? loginUsername.text.Trim() : "";
        string pass = loginPassword != null ? loginPassword.text : "";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) { ShowError(loginError, "Isi email dan password."); return; }
        if (auth == null || auth.IsLoading) return;

        ShowError(loginError, "Masuk...");
        auth.Login(user, pass,
            onSuccess: (userData) => { SceneManager.LoadScene(nextSceneName); },
            onError: (msg) => { ShowError(loginError, msg); }
        );
    }

    public void OnSignupClicked()
    {
        string name = signupName != null ? signupName.text.Trim() : "";
        string email = signupEmail != null ? signupEmail.text.Trim() : "";
        string pass = signupPassword != null ? signupPassword.text : "";
        string confirm = signupConfirm != null ? signupConfirm.text : "";
        string classCode = signupClassCode != null ? signupClassCode.text.Trim() : "";

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(classCode))
        { ShowError(signupError, "Semua field harus diisi."); return; }
        if (pass != confirm) { ShowError(signupError, "Konfirmasi password tidak cocok."); return; }
        if (auth == null || auth.IsLoading) return;

        ShowError(signupError, "Mendaftar...");
        auth.Register(name, email, pass, classCode,
            onSuccess: (userData) => { SceneManager.LoadScene(nextSceneName); },
            onError: (msg) => { ShowError(signupError, msg); }
        );
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static GameObject FindChildRecursive(GameObject root, string targetName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == targetName) return t.gameObject;
        return null;
    }

    private static TMP_InputField FindInputRecursive(GameObject panel, string fieldName)
    {
        foreach (Transform t in panel.GetComponentsInChildren<Transform>(true))
            if (t.name == fieldName)
            {
                var input = t.GetComponentInChildren<TMP_InputField>(true);
                if (input != null) return input;
            }
        return null;
    }

    private static TMP_Text FindTextRecursive(GameObject panel, string textName)
    {
        foreach (Transform t in panel.GetComponentsInChildren<Transform>(true))
            if (t.name == textName)
            {
                var text = t.GetComponent<TMP_Text>();
                if (text != null) return text;
            }
        return null;
    }

    private static void SetActive(GameObject go, bool active) { if (go != null) go.SetActive(active); }
    private static void ShowError(TMP_Text label, string msg) { if (label == null) return; label.text = msg; label.gameObject.SetActive(true); }

    private void ClearErrors()
    {
        if (loginError != null) { loginError.text = ""; loginError.gameObject.SetActive(false); }
        if (signupError != null) { signupError.text = ""; signupError.gameObject.SetActive(false); }
    }

    private void ClearInputs()
    {
        if (loginUsername != null) loginUsername.text = "";
        if (loginPassword != null) loginPassword.text = "";
        if (signupName != null) signupName.text = "";
        if (signupEmail != null) signupEmail.text = "";
        if (signupPassword != null) signupPassword.text = "";
        if (signupConfirm != null) signupConfirm.text = "";
        if (signupClassCode != null) signupClassCode.text = "";
    }
}