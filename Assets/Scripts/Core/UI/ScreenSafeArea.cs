using UnityEngine;

[DefaultExecutionOrder(-50)]
public class ScreenSafeArea : MonoBehaviour
{
    private Rect _lastSafeArea;
    private RectTransform _rt;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != _lastSafeArea)
            Apply();
    }

    void Apply()
    {
        _lastSafeArea = Screen.safeArea;

        if (!Application.isMobilePlatform) return;

        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        Rect sa = Screen.safeArea;

        Vector2 anchorMin = sa.position / screenSize;
        Vector2 anchorMax = (sa.position + sa.size) / screenSize;

        _rt.anchorMin = anchorMin;
        _rt.anchorMax = anchorMax;
        _rt.offsetMin = _rt.offsetMax = Vector2.zero;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isMobilePlatform) return;

        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        if (canvas.GetComponent<ScreenSafeArea>() == null)
            canvas.gameObject.AddComponent<ScreenSafeArea>();
    }
}
