using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// The Trophies board on the spawn wall (Cor, 2026-11-19): hunter trophies on the left, hider trophies on the
// right, 40 each, every card with its name, its goal written out and, for the ones that count up, a
// bar (no rounds estimate: Cor). Each player sees their own progress, but every card keeps its own fixed place (Cor,
// 2026-11-22: no sorting by progress). The cards are built and placed by the scene builder; this only colours them.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHTrophyBoard : UdonSharpBehaviour
{
    public PHStore store;
    public RectTransform[] card;         // 44, by trophy number
    public Image[] cardBg;
    public RawImage[] medal;
    public GameObject[] tick, bar;
    public RectTransform[] barFill;
    public TextMeshProUGUI[] progText;
    public TextMeshProUGUI countHunter, countHider;
    public Color plain = new Color(0.914f, 0.855f, 0.714f), cool = new Color(0.969f, 0.863f, 0.576f), goofy = new Color(0.965f, 0.753f, 0.710f);
    public Color medalOff = new Color(1f, 1f, 1f, 0.4f);
    public string progressFormat = "{0} / {1}";   // strings_store.tsv store.progress

    void Start() { Refresh(); }

    public void Refresh()
    {
        if (store == null || card == null) return;
        store.Init();
        int n = PHStore.TROPHIES, half = PHStore.HUNTER_TROPHIES;
        int gotH = 0, gotP = 0;
        for (int t = 0; t < n; t++)
        {
            bool got = store.got[t];
            int target = store.TargetOf(t), prog = Mathf.Min(store.ProgressOf(t), target);
            float f = got ? 2f : (target > 0 ? (float)prog / target : 0f);
            if (got) { if (t < half) gotH++; else gotP++; }
            int w = store.tWeapon[t];
            bool goofyCard = w >= 0 && store.wCat[w] == PHStore.CAT_GOOFY;
            cardBg[t].color = got ? (goofyCard ? goofy : cool) : plain;
            medal[t].color = got ? Color.white : medalOff;
            tick[t].SetActive(got);
            bool counted = !got && target > 0;
            bar[t].SetActive(counted);
            if (counted)
            {
                Vector3 s = barFill[t].localScale; s.x = Mathf.Clamp01(f); barFill[t].localScale = s;
                progText[t].text = progressFormat.Replace("{0}", prog.ToString()).Replace("{1}", target.ToString());
            }
        }
        string tot = half.ToString();
        if (countHunter != null) countHunter.text = progressFormat.Replace("{0}", gotH.ToString()).Replace("{1}", tot);
        if (countHider != null) countHider.text = progressFormat.Replace("{0}", gotP.ToString()).Replace("{1}", tot);
    }
}
