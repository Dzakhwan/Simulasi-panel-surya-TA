using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;

public static class GameInput
{
    private static InputSystem_Actions _actions;

    public static InputSystem_Actions Actions
    {
        get
        {
            if (_actions == null)
            {
                _actions = new InputSystem_Actions();
                _actions.Player.Enable();
            }
            return _actions;
        }
    }

    public static InputSystem_Actions.PlayerActions Player => Actions.Player;

    public static Vector2 PointerPosition
    {
        get
        {
            // 1. Prioritaskan Touchscreen jika ada sentuhan aktif (EnhancedTouch lebih andal di mobile)
            if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
            {
                return UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0].screenPosition;
            }

            // 2. Fallback baca langsung dari hardware Touchscreen standard
            if (Touchscreen.current != null && Touchscreen.current.touches.Count > 0 && Touchscreen.current.touches[0].isInProgress)
            {
                return Touchscreen.current.touches[0].position.ReadValue();
            }

            // 3. Fallback ke action (Mouse/Pen)
            var pos = Player.Point.ReadValue<Vector2>();
            if (pos == Vector2.zero)
                pos = Player.Touch0Position.ReadValue<Vector2>();
            
            return pos;
        }
    }

    public static bool PrimaryPressed()
    {
        return Player.Click.WasPressedThisFrame();
    }

    public static bool IsPointerOverGameObject(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;

        var results = new List<RaycastResult>();
        var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    /// <summary>
    /// Apakah pointer berada di atas elemen UI yang interaktif (Button, InputField,
    /// Slider, Toggle, ScrollRect, dst.) — bukan background Image/Text fullscreen.
    /// Dipakai untuk sentuhan/pan agar tidak terblokir elemen UI dekoratif.
    /// </summary>
    public static bool IsPointerOverInteractiveUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;

        var results = new List<RaycastResult>();
        var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
        EventSystem.current.RaycastAll(eventData, results);

        foreach (var result in results)
        {
            if (result.gameObject == null) continue;

            var selectable = result.gameObject.GetComponentInParent<Selectable>(true);
            if (selectable != null) return true;

            if (result.gameObject.GetComponentInParent<ScrollRect>(true) != null) return true;
        }

        return false;
    }

    public static void Dispose()
    {
        if (_actions != null)
        {
            _actions.Player.Disable();
            _actions.Dispose();
            _actions = null;
        }
    }
}
