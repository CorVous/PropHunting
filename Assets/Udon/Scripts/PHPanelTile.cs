using UdonSharp;

// One pressable tile of the host panel (a UI Button's OnClick calls Press). index -1 is the die (START).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHPanelTile : UdonSharpBehaviour
{
    public PHPanel panel;
    public int index;

    public void Press()
    {
        if (panel == null) return;
        if (index < 0) panel.StartPress(); else panel.TilePress(index);
    }
}
