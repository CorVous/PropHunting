using UnityEngine;
using UnityEngine.EventSystems;

// The browser's stand-in for pointing at a world-space canvas (G55; in VR the controller's laser, on desktop VRChat
// the centre of the view): while the mouse is captured, the pointer is the centre of the screen, and a click presses
// the button under it. Only in the lobby and only for the host (G51): for a guest nothing is hovered and nothing is
// pressed. The click that captures the mouse presses nothing.
public class CentrePointerModule : BaseInputModule
{
    PointerEventData pointer;

    // the button the centre of the screen is on, or null
    public static GameObject Hovered;

    public override void Process()
    {
        if (pointer == null) pointer = new PointerEventData(eventSystem);
        var g = Game.I;
        bool active = g != null && g.state == Game.State.Lobby && g.isHost && !g.menuOpen && Cursor.lockState == CursorLockMode.Locked;
        GameObject over = null;
        if (active)
        {
            pointer.Reset();
            pointer.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            pointer.delta = Vector2.zero;
            pointer.button = PointerEventData.InputButton.Left;
            eventSystem.RaycastAll(pointer, m_RaycastResultCache);
            pointer.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResultCache);
            m_RaycastResultCache.Clear();
            over = pointer.pointerCurrentRaycast.gameObject;
        }
        HandlePointerExitAndEnter(pointer, over);
        Hovered = over != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(over) : null;
        if (Hovered != null && Input.GetMouseButtonDown(0) && !g.player.CaptureClick)
        {
            pointer.pointerPress = Hovered;
            ExecuteEvents.Execute(Hovered, pointer, ExecuteEvents.pointerClickHandler);
        }
    }
}
