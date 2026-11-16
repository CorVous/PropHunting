using UnityEngine;

// A plain stand-in figure for a walking body (avatars do not exist in the browser build): the player in
// third person, and bot hunters.
public static class FigureMesh
{
    public static GameObject Build(string name, Color32 shirt)
    {
        var mb = new MeshBuf();
        Color32 skin = new Color32(238, 196, 160, 255), legs = new Color32(70, 74, 92, 255), dark = new Color32(60, 60, 66, 255);
        mb.Box(new Vector3(-0.17f, 0f, -0.09f), new Vector3(-0.02f, 0.85f, 0.09f), legs);
        mb.Box(new Vector3(0.02f, 0f, -0.09f), new Vector3(0.17f, 0.85f, 0.09f), legs);
        mb.Box(new Vector3(-0.2f, 0.85f, -0.11f), new Vector3(0.2f, 1.42f, 0.11f), shirt);
        mb.Box(new Vector3(-0.12f, 1.44f, -0.11f), new Vector3(0.12f, 1.7f, 0.11f), skin);
        // eyes on the front (+Z), so the facing direction can be read
        mb.Box(new Vector3(-0.08f, 1.58f, 0.11f), new Vector3(-0.03f, 1.63f, 0.115f), dark);
        mb.Box(new Vector3(0.03f, 1.58f, 0.11f), new Vector3(0.08f, 1.63f, 0.115f), dark);
        return mb.ToObject(name, null, ModelLibrary.lit);
    }
}
