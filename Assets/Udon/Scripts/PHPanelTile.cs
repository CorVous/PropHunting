using UdonSharp;

// One pressable tile of the host panel (a UI Button's OnClick calls Press). index -1 is the die (START), -2 Unlock/Lock host, -3 Claim host.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHPanelTile : UdonSharpBehaviour
{
    public PHPanel panel;
    public int index;

    public void Press()
    {
        if (panel == null) return;
        if (index == -1) panel.StartPress();
        else if (index == -2) panel.UnlockPress();
        else if (index == -3) panel.ClaimPress();
        else panel.TilePress(index);
    }
}
