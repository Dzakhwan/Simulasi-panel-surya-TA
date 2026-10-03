using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Assertions.Must;
using UnityEngine.EventSystems;


/// <summary>
/// Mengatur tombol navigasi ruangan.
/// Saat tombol ruangan diklik, kamera fokus ke ruangan tersebut.
/// </summary>
public class RoomFocusManager : MonoBehaviour
{
    [System.Serializable]
    public class RoomData
    {
        public string roomName;          // Nama ruangan (e.g., "Kamar Mandi")
        public Transform focusPoint;     // Empty object di tengah ruangan
    }

    [Header("Rooms")]
    public List<RoomData> rooms = new();

    [Header("UI References")]
    public Transform roomButtonContainer;   // Parent untuk tombol-tombol ruangan
    public GameObject roomButtonPrefab;     // Prefab tombol ruangan
    public Image freeModeButton;           // Tombol Free Mode
    public TextMeshProUGUI freeModeText;    // Text tombol Free Mode
    public Button resetViewButton;          // Tombol Reset View (opsional)

    [Header("Settings")]
    public float transitionDuration = 1.2f;

    private OrbitCamera orbitCamera;
    private bool isFreeMode = false;
    private int currentRoomIndex = -1; // -1 = overview

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        orbitCamera = Camera.main.GetComponent<OrbitCamera>();

        GenerateRoomButtons();
        UpdateFreeModeUI();
    }

    // ── Button Generation ──────────────────────────────────────────────────

    private void GenerateRoomButtons()
    {
        if (roomButtonContainer == null || roomButtonPrefab == null) return;

        for (int i = 0; i < rooms.Count; i++)
        {
            int index = i; // Capture for closure
            GameObject btnObj = Instantiate(roomButtonPrefab, roomButtonContainer);

            var text = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.text = rooms[i].roomName;

            var button = btnObj.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(() => FocusRoom(index));
        }
    }

    // ── Room Navigation ────────────────────────────────────────────────────

    /// <summary>Fokuskan kamera ke ruangan yang dipilih.</summary>
    public void FocusRoom(int index)
    {
        if (index < 0 || index >= rooms.Count) return;
        if (orbitCamera == null) return;
        if (rooms[index].focusPoint == null) return;

        currentRoomIndex = index;
        orbitCamera.FocusOnPosition(rooms[index].focusPoint.position, transitionDuration);

        Debug.Log($"[Room] Fokus ke: {rooms[index].roomName}");
    }

    /// <summary>Reset kamera ke tampilan overview.</summary>
    public void ResetView()
    {
        if (orbitCamera == null) return;

        currentRoomIndex = -1;
        orbitCamera.ResetToOrigin(transitionDuration);

        Debug.Log("[Room] Reset ke tampilan overview");
    }

    // ── Free Mode ──────────────────────────────────────────────────────────

    public void ToggleFreeMode()
    {
        isFreeMode = !isFreeMode;

        if (orbitCamera != null)
            orbitCamera.freeMode = isFreeMode;

        if (Application.isMobilePlatform)
            VirtualJoystick.SetVisible(isFreeMode);

        UpdateFreeModeUI();
        Debug.Log($"[Room] Free Mode: {(isFreeMode ? "AKTIF" : "NONAKTIF")}");

        EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Paksa free mode OFF — dipanggil saat simulasi dimulai.</summary>
    public void ForceExitFreeMode()
    {
        isFreeMode = false;
        if (orbitCamera != null)
            orbitCamera.freeMode = false;
        if (Application.isMobilePlatform)
            VirtualJoystick.SetVisible(false);
        UpdateFreeModeUI();
    }

    public void ForceEnterFreeMode()
    {
        isFreeMode = true;
        if (orbitCamera != null)
            orbitCamera.freeMode = true;
        if (Application.isMobilePlatform)
            VirtualJoystick.SetVisible(true);
        UpdateFreeModeUI();
    }

    private void UpdateFreeModeUI()
    {
        if (freeModeText != null)
            freeModeText.text = isFreeMode ? "Free Mode: ON" : "Free Mode: OFF";

        if (freeModeButton != null)
        {
            freeModeButton.color = isFreeMode ? new Color(0.1355286f, 0.2025502f, 0.3056603f, 1f) : new Color(0.19f, 0.19f, 0.19f, 0.8313726f);
            //freeModeText.color = isFreeMode ? new Color(1f, 1f, 1f, 1f) : new Color(0.1960784f, 0.1960784f, 0.1960784f, 1f);
        }
    }
}
