using UnityEngine;
using UnityEngine.EventSystems;

public class UIDeselectManager : MonoBehaviour
{
    void Update()
    {
        if (GameInput.Player.Click.WasReleasedThisFrame())
        {
            if (EventSystem.current.currentSelectedGameObject != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
