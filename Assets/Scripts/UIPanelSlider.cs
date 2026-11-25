using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slides a UI panel in/out from either the left or right edge of the screen.
/// Attach this to the panel's GameObject, then assign the toggle button.
/// </summary>
public class UIPanelSlider : MonoBehaviour
{
    public enum SlideDirection { Left, Right }

    [Header("References")]
    [Tooltip("Button that triggers show/hide.")]
    [SerializeField] private Button toggleButton;

    [Tooltip("Optional: Legacy Text component on the button to update its label.")]
    [SerializeField] private Text buttonLabel;

    [Tooltip("GameObjects to deactivate while this panel is visible (e.g. room button container).")]
    [SerializeField] private GameObject[] objectsToHideWhenVisible;

    [Header("Settings")]
    [Tooltip("Which side this panel originates from.")]
    [SerializeField] private SlideDirection panelSide = SlideDirection.Left;

    [Tooltip("Distance in pixels the panel travels when hiding. Set to 0 to auto-use the panel's width.")]
    [SerializeField] private float slideDistance = 0f;

    [Tooltip("Duration of the slide animation in seconds.")]
    [SerializeField] private float slideDuration = 0.3f;

    [Tooltip("Text shown on the button when the panel is hidden.")]
    [SerializeField] private string showText = "Show UI";

    [Tooltip("Text shown on the button when the panel is visible.")]
    [SerializeField] private string hideText = "Hide UI";

    private RectTransform _rectTransform;
    private bool _isVisible = true;
    private bool _positionsCached = false;
    private Vector2 _visiblePosition;
    private Vector2 _hiddenPosition;

    private Coroutine _slideCoroutine;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();

        // Pastikan kalkulasi posisi awal HANYA dilakukan sekali di sini
        EnsurePositionsCached();

        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);

        UpdateButtonLabel();
    }

    private void OnEnable()
    {
        _isVisible = true;
        UpdateDependentObjects();
    }

    private void OnDisable()
    {
        _isVisible = false;
        UpdateDependentObjects();
    }

    /// <summary>
    /// Menghitung posisi visible & hidden sekali saja berdasarkan RectTransform awal.
    /// </summary>
    private void EnsurePositionsCached()
    {
        if (_positionsCached) return;

        if (_rectTransform == null)
            _rectTransform = GetComponent<RectTransform>();

        // Ambil posisi awal dari Layout Editor sebagai posisi 'Visible'
        _visiblePosition = _rectTransform.anchoredPosition;

        float distance = slideDistance > 0f ? slideDistance : _rectTransform.rect.width;
        float hiddenOffset = panelSide == SlideDirection.Left ? -distance : distance;

        _hiddenPosition = new Vector2(_visiblePosition.x + hiddenOffset, _visiblePosition.y);
        _positionsCached = true;
    }

    /// <summary>
    /// Toggles the panel between visible and hidden state.
    /// Safe to spam-click.
    /// </summary>
    public void Toggle()
    {
        _isVisible = !_isVisible;
        UpdateButtonLabel();

        if (_isVisible)
        {
            UpdateDependentObjects();
        }

        // Hentikan coroutine yang sedang berjalan jika tombol di-spam
        if (_slideCoroutine != null)
            StopCoroutine(_slideCoroutine);

        _slideCoroutine = StartCoroutine(SlideCoroutine(_isVisible ? _visiblePosition : _hiddenPosition));
    }

    public void SetVisible(bool visible)
    {
        if (_slideCoroutine != null)
            StopCoroutine(_slideCoroutine);

        EnsurePositionsCached();

        _isVisible = visible;
        _rectTransform.anchoredPosition = _isVisible ? _visiblePosition : _hiddenPosition;
        UpdateButtonLabel();
        UpdateDependentObjects();
    }

    public void ResetToVisible()
    {
        if (_slideCoroutine != null)
            StopCoroutine(_slideCoroutine);

        EnsurePositionsCached();

        _isVisible = true;
        _rectTransform.anchoredPosition = _visiblePosition;
        UpdateButtonLabel();
    }

    public void SlideIn()
    {
        EnsurePositionsCached();

        if (!gameObject.activeSelf)
        {
            _rectTransform.anchoredPosition = _hiddenPosition;
            gameObject.SetActive(true);
        }

        _isVisible = true;
        UpdateButtonLabel();

        if (_slideCoroutine != null)
            StopCoroutine(_slideCoroutine);

        _slideCoroutine = StartCoroutine(SlideCoroutine(_visiblePosition));
        UpdateDependentObjects();
    }

    public void SlideOut(System.Action onComplete = null)
    {
        if (!gameObject.activeInHierarchy)
        {
            onComplete?.Invoke();
            return;
        }

        EnsurePositionsCached();

        _isVisible = false;
        UpdateButtonLabel();

        if (_slideCoroutine != null)
            StopCoroutine(_slideCoroutine);

        _slideCoroutine = StartCoroutine(SlideOutCoroutine(onComplete));
    }

    private IEnumerator SlideOutCoroutine(System.Action onComplete)
    {
        yield return SlideCoroutine(_hiddenPosition);
        gameObject.SetActive(false);
        onComplete?.Invoke();
    }

    private IEnumerator SlideCoroutine(Vector2 targetPosition)
    {
        Vector2 startPosition = _rectTransform.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);

            // Smooth-step easing
            float eased = t * t * (3f - 2f * t);
            _rectTransform.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, eased);

            yield return null;
        }

        _rectTransform.anchoredPosition = targetPosition;

        if (!_isVisible)
        {
            UpdateDependentObjects();
        }

        _slideCoroutine = null;
    }

    private void UpdateButtonLabel()
    {
        if (buttonLabel == null) return;
        buttonLabel.text = _isVisible ? hideText : showText;
    }

    private void UpdateDependentObjects()
    {
        if (objectsToHideWhenVisible == null) return;

        foreach (GameObject obj in objectsToHideWhenVisible)
        {
            if (obj != null)
                obj.SetActive(!_isVisible);
        }
    }
}