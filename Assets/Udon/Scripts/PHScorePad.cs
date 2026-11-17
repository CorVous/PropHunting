using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// The last round's results on the lobby's west wall, drawn as a spiral score pad (built by LobbyBuilder): who
// won and why, the survival share, every hider with the prop they wore and when they were caught, every hunter's
// hits, catches and wrong props. Filled when a round reaches its reveal (PH_END).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHScorePad : UdonSharpBehaviour
{
    public PHGame game;
    public PHLocal local;               // defNames

    public TextMeshProUGUI title, reason, shareText;
    public Image shareFill;
    public GameObject shareRoot, winHiders, winHunters, columns;
    public Color hidersColour, huntersColour, idleColour;

    public GameObject[] hiderRow;       // 24: 3 columns of 8
    public TextMeshProUGUI[] hiderName, hiderProp, hiderResult;
    public GameObject[] hunterRow;      // 8
    public TextMeshProUGUI[] hunterName, hunterHits, hunterCatches, hunterWrong;

    int shownRound = -1;

    void Start() { Empty(); }

    void Update()
    {
        if (game == null) return;
        if (game.phase == PHGame.PH_END && game.round != shownRound) { shownRound = game.round; Fill(); }
    }

    void Empty()
    {
        title.text = "NO ROUNDS YET"; title.color = idleColour;
        reason.text = "";
        shareRoot.SetActive(false); winHiders.SetActive(false); winHunters.SetActive(false); columns.SetActive(false);
    }

    string Clock(int ms) { int t = Mathf.Max(0, Mathf.RoundToInt(ms / 1000f)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    string Name(int playerId)
    {
        VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
        return Utilities.IsValid(p) ? p.displayName : "Player " + playerId;
    }

    void Fill()
    {
        bool hw = game.hidersWin;
        title.text = hw ? "HIDERS WIN" : "HUNTERS WIN";
        title.color = hw ? hidersColour : huntersColour;
        reason.text = game.endReason;
        winHiders.SetActive(hw); winHunters.SetActive(!hw);
        columns.SetActive(true);

        float huntMs = game.HuntLength * 1000f, sum = 0f;
        int n = 0, hu = 0;
        for (int i = 0; i < game.slots.Length; i++)
        {
            if (game.slotPlayer[i] == 0) continue;
            PHSlot s = game.slots[i];
            if (game.slotRole[i] == PHGame.ROLE_HIDER && n < hiderRow.Length)
            {
                hiderName[n].text = Name(game.slotPlayer[i]);
                hiderProp[n].text = s.lastDef >= 0 && local != null && s.lastDef < local.defNames.Length ? local.defNames[s.lastDef] : "";
                hiderResult[n].text = s.caught ? "caught " + Clock(s.survivedMs) : "survived";
                hiderResult[n].color = s.caught ? new Color(0.54f, 0.23f, 0.17f) : new Color(0.18f, 0.48f, 0.23f);
                sum += s.caught ? s.survivedMs : huntMs;
                n++;
            }
            else if (game.slotRole[i] == PHGame.ROLE_HUNTER && hu < hunterRow.Length)
            {
                hunterName[hu].text = Name(game.slotPlayer[i]);
                hunterHits[hu].text = s.hits.ToString();
                hunterCatches[hu].text = s.catches.ToString();
                hunterWrong[hu].text = s.wrongProps.ToString();
                hu++;
            }
        }
        for (int i = 0; i < hiderRow.Length; i++) hiderRow[i].SetActive(i < n);
        for (int i = 0; i < hunterRow.Length; i++) hunterRow[i].SetActive(i < hu);
        // survival share (05 section 8.1)
        shareRoot.SetActive(n > 0);
        if (n > 0)
        {
            float share = sum / (n * huntMs);
            shareText.text = Mathf.RoundToInt(100f * share) + "%";
            shareFill.fillAmount = Mathf.Clamp01(share);
        }
    }
}
