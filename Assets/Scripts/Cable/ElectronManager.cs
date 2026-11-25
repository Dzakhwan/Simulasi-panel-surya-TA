using UnityEngine;

public enum ElectronFlowType
{
    InverterToAppliance,
    BatteryToInverter
}

public class ElectronManager : MonoBehaviour
{
    public static ElectronManager Instance { get; private set; }

    [Header("Global Electron Settings")]
    [Min(0f)] public float defaultSpeed = 20f;
    [Min(1)] public int defaultCount = 15;
    [Min(0.001f)] public float defaultRadius = 4f;
    public Color inverterToApplianceColor = new Color(0.1f, 0.45f, 1f);
    public Color batteryToInverterColor = new Color(1f, 0.15f, 0.1f);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public Color GetColor(ElectronFlowType flowType)
    {
        return flowType == ElectronFlowType.BatteryToInverter
            ? batteryToInverterColor
            : inverterToApplianceColor;
    }
}
