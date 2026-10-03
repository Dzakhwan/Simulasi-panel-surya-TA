using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the dynamic list of furniture cards inside the "electronic Content" container.
/// Listens to FurnitureManager.OnFurnitureCountChanged and rebuilds one card
/// per installed furniture whenever the count changes.
/// </summary>
public class FurnitureTopDownUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The parent transform where furniture cards are spawned (electronic Content).")]
    [SerializeField] private Transform furnitureContent;

    [Tooltip("Prefab representing a single furniture card (must have a FurnitureTopDownCardUI component).")]
    [SerializeField] private GameObject furnitureCardPrefab;

    private bool _hasStarted;

    private void Start()
    {
        Subscribe();
        _hasStarted = true;
        Rebuild();
    }

    private void OnEnable()
    {
        if (!_hasStarted) return;
        Subscribe();
        Rebuild();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (FurnitureManager.Instance == null) return;
        FurnitureManager.Instance.OnFurnitureCountChanged.RemoveListener(OnFurnitureCountChanged);
        FurnitureManager.Instance.OnFurnitureCountChanged.AddListener(OnFurnitureCountChanged);
    }

    private void Unsubscribe()
    {
        if (FurnitureManager.Instance != null)
            FurnitureManager.Instance.OnFurnitureCountChanged.RemoveListener(OnFurnitureCountChanged);
    }

    private void OnFurnitureCountChanged(int _) => Rebuild();

    /// <summary>
    /// Clears all existing furniture cards and spawns one per currently placed furniture.
    /// </summary>
    public void Rebuild()
    {
        if (furnitureContent == null || furnitureCardPrefab == null) return;

        // Destroy existing cards
        for (int i = furnitureContent.childCount - 1; i >= 0; i--)
            Destroy(furnitureContent.GetChild(i).gameObject);

        if (FurnitureManager.Instance == null) return;

        // Multi-instance: list berisi duplikat → satu kartu per instance terpasang.
        var placedData = new List<FurnitureData>(FurnitureManager.Instance.GetPlacedFurnitureData());
        if (placedData.Count == 0) return;

        int index = 1;
        foreach (FurnitureData data in placedData)
        {
            GameObject card = Instantiate(furnitureCardPrefab, furnitureContent);
            FurnitureTopDownCardUI ui = card.GetComponent<FurnitureTopDownCardUI>();
            ui?.Init(index, data);
            index++;
        }
    }
}
