using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Tile item bergaya The Sims FreePlay untuk catalog / home store.
/// Dilengkapi gradient, border putih, badge merah, label teks, dan efek sentuh tactile (mobile-first).
/// </summary>
public class SimsItemTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Visual Components")]
    public Image backgroundImage;
    public Outline outlineImage;
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI subtitleText;

    [Header("Badge")]
    public GameObject badgeRoot;
    public Image badgeImage;
    public TextMeshProUGUI badgeText;

    [Header("Button")]
    public Button button;

    private Action _onClick;
    private Vector3 _baseScale = Vector3.one;
    private bool _isPressed;
    private AudioSource _audioSource;
    private AudioClip _clickClip;

    public void Setup(
        string itemName,
        Sprite icon,
        string badgeValue,
        string subtitle,
        Color topColor,
        Color bottomColor,
        Action onClick,
        Color? badgeColor = null,
        AudioClip clickSound = null)
    {
        _onClick = onClick;
        _baseScale = Vector3.one;
        transform.localScale = _baseScale;
        _clickClip = clickSound;

        if (_audioSource == null)
        {
            var soundManager = FindFirstObjectByType<ButtonSound>();
            if (soundManager != null)
            {
                _audioSource = soundManager.audioSource;
                if (_clickClip == null) _clickClip = soundManager.clickSound;
            }
        }

        if (nameText != null)
        {
            nameText.text = itemName;
        }

        if (iconImage != null)
        {
            if (icon != null)
            {
                iconImage.sprite = icon;
                iconImage.gameObject.SetActive(true);
            }
            else
            {
                iconImage.gameObject.SetActive(false);
            }
        }

        if (subtitleText != null)
        {
            subtitleText.text = subtitle ?? "";
            subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
        }

        if (badgeRoot != null)
        {
            bool hasBadge = !string.IsNullOrEmpty(badgeValue);
            badgeRoot.SetActive(hasBadge);
            if (hasBadge && badgeText != null)
            {
                badgeText.text = badgeValue;
            }
            if (hasBadge && badgeColor.HasValue && badgeImage != null)
            {
                badgeImage.color = badgeColor.Value;
            }
        }

        if (backgroundImage != null)
        {
            var gradient = backgroundImage.GetComponent<UIGradient>();
            if (gradient == null)
                gradient = backgroundImage.gameObject.AddComponent<UIGradient>();
            gradient.colorTop = topColor;
            gradient.colorBottom = bottomColor;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
    }

    private void HandleClick()
    {
        if (_audioSource != null && _clickClip != null)
        {
            _audioSource.PlayOneShot(_clickClip);
        }
        _onClick?.Invoke();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        transform.localScale = _baseScale * 0.93f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        transform.localScale = _baseScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isPressed)
            transform.localScale = _baseScale * 1.05f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isPressed)
            transform.localScale = _baseScale;
    }
}