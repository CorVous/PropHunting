using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Small pictographic plaques (Cor, onboarding: icons only, no words) in the lobby's style: a cream card on a darker
// lip with a row of icons, over the Trophies board's two halves. Icons are Assets/Lobby/Icons sprites (trophy from
// Tools/make_plaque_icons.py) or cells of Assets/Lobby/WeaponIcons.png. Static world-space canvases with no raycaster or collider: they never take a press.
public static partial class LobbyBuilder
{
    const float PlaqueIcon = 96f, PlaquePad = 14f, PlaqueLip = 9f;

    // one icon: a sprite, or (sprite null) a weapon index into WeaponIcons.png
    static void PlaqueIconAt(Transform card, Vector2 pos, Sprite s, int weapon, Texture2D weaponIcons)
    {
        if (s != null) { Img(card, s.name, pos, new Vector2(PlaqueIcon, PlaqueIcon), s, Color.white); return; }
        var ri = Rect(card, "Weapon" + weapon, pos, new Vector2(PlaqueIcon, PlaqueIcon)).gameObject.AddComponent<RawImage>();
        ri.texture = weaponIcons; ri.raycastTarget = false;
        int cols = PHStoreDialog.ICON_COLS, rows = PHStoreDialog.ICON_ROWS;
        ri.uvRect = new Rect((weapon % cols) / (float)cols, 1f - (weapon / cols + 1) / (float)rows, 1f / cols, 1f / rows);
    }

    // a plaque heightM tall with the icons in a row; sprites[i] null means weapons[i]
    public static Transform Plaque(Transform parent, string name, Vector3 localPos, Quaternion rot, float heightM, Sprite[] sprites, int[] weapons)
    {
        int n = sprites.Length;
        float w = n * PlaqueIcon + (n - 1) * PlaquePad * 0.6f + 2f * PlaquePad, h = PlaqueIcon + 2f * PlaquePad;
        var cv = Canvas(parent, name, localPos, rot, new Vector2(w, h + PlaqueLip), heightM / (h + PlaqueLip), false);
        Img(cv, "Lip", new Vector2(0f, -PlaqueLip / 2f), new Vector2(w, h), sRound, C("c4ad7e"), 3f);
        var card = Img(cv, "Card", new Vector2(0f, PlaqueLip / 2f), new Vector2(w, h), sRound, Cream, 3f).transform;
        var weaponIcons = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "WeaponIcons.png");
        float x = -w / 2f + PlaquePad + PlaqueIcon / 2f;
        for (int i = 0; i < n; i++)
        {
            PlaqueIconAt(card, new Vector2(x, 0f), sprites[i], weapons != null && i < weapons.Length ? weapons[i] : -1, weaponIcons);
            x += PlaqueIcon + PlaquePad * 0.6f;
        }
        return cv;
    }

    // the plaques, in lobby space (centre of the room at 0, north +z); a canvas faces away from its forward. Only the
    // Trophies board's (Cor: the weapon wall's halves carry word labels instead, LobbyBuilder.Store.cs)
    public static void BuildPlaques(Transform lobby, float roomW, float roomD)
    {
        Sprite trophy = Icon("trophy"), hunt = Icon("meeple_hunt"), hide = Icon("meeple_hide");
        var root = new GameObject("Plaques").transform;
        root.SetParent(lobby, false);
        // the Trophies board (west wall): over each half, the side and a cup (hunters' half to the south, the
        // viewer's left)
        // (Cor, 2026-11-22: the board is half size, 5.4 x 2.2 m centred 1.7 m up (close to the ground): the plaques sit as far over its
        // top, 2.8 m, as they did over the big one, over each half's middle)
        float tx = -roomW / 2f + 0.06f;
        Plaque(root, "PlaqueTrophiesHunt", new Vector3(tx, 3.13f, -1.34f), Quaternion.Euler(0f, -90f, 0f), 0.56f, new[] { hunt, trophy }, null);
        Plaque(root, "PlaqueTrophiesHide", new Vector3(tx, 3.13f, 1.34f), Quaternion.Euler(0f, -90f, 0f), 0.56f, new[] { hide, trophy }, null);
    }
}
