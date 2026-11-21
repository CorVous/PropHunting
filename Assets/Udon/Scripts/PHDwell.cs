using UdonSharp;
using UnityEngine;

// Where each hider spent the most time (Cor's playtest: the END reveal showed only where they ended up or were caught,
// "show where they spent the most time hiding"). Every client keeps its own tally from the hiders' synced positions:
// once a second during the hunt, the 1.5 m floor square each hider's prop is in gets a second more. At the END reveal
// an amber beam stands on each hider's busiest square, beside the green / red beacon of where they ended (PHLocal),
// when the two are more than 2 m apart. Nothing is synced: everyone sees much the same, from the same positions.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHDwell : UdonSharpBehaviour
{
    public PHGame game;
    public GameObject[] beams;     // one per slot (WorldSceneBuilder)

    const int K = 24;              // squares remembered per hider
    const float CELL = 1.5f;
    int[] key;                     // slot * K + n: the square (packed x, z), 0 empty
    float[] secs;
    int lastPhase = -1, lastRound = -1;
    bool shown;

    void Start()
    {
        int n = game.slots.Length * K;
        key = new int[n]; secs = new float[n];
        SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
    }

    public void _Tick()
    {
        SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
        int ph = game.phase;
        if (game.round != lastRound && ph != PHGame.PH_END) { lastRound = game.round; Clear(); }
        if (ph == PHGame.PH_HUNT)
        {
            for (int i = 0; i < game.slots.Length; i++)
            {
                if (game.slotPlayer[i] == 0 || game.slotRole[i] != PHGame.ROLE_HIDER) continue;
                PHSlot s = game.slots[i];
                if (s.caught) continue;
                Add(i, s.propPos);
            }
        }
        if (ph == PHGame.PH_END && !shown) Show();
        else if (ph != PHGame.PH_END && shown) Hide();
        lastPhase = ph;
    }

    void Clear()
    {
        for (int k = 0; k < key.Length; k++) { key[k] = 0; secs[k] = 0f; }
    }

    static int Pack(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x / CELL) + 20000, z = Mathf.FloorToInt(p.z / CELL) + 20000;
        return x * 40000 + z + 1;
    }

    void Add(int slot, Vector3 p)
    {
        int c = Pack(p), b = slot * K, low = b;
        for (int n = 0; n < K; n++)
        {
            int k = b + n;
            if (key[k] == c) { secs[k] += 1f; return; }
            if (key[k] == 0) { key[k] = c; secs[k] = 1f; return; }
            if (secs[k] < secs[low]) low = k;
        }
        key[low] = c; secs[low] = 1f;   // full: the least-used square gives way
    }

    void Show()
    {
        shown = true;
        for (int i = 0; i < game.slots.Length && i < beams.Length; i++)
        {
            if (beams[i] == null) continue;
            bool on = false;
            if (game.slotPlayer[i] != 0 && game.slotRole[i] == PHGame.ROLE_HIDER)
            {
                int b = i * K, best = -1;
                for (int n = 0; n < K; n++) if (key[b + n] != 0 && (best < 0 || secs[b + n] > secs[best])) best = b + n;
                if (best >= 0 && secs[best] >= 5f)
                {
                    int c = key[best] - 1;
                    float x = (c / 40000 - 20000 + 0.5f) * CELL, z = (c % 40000 - 20000 + 0.5f) * CELL;
                    Vector3 end = game.slots[i].propPos;
                    if ((new Vector2(x - end.x, z - end.z)).sqrMagnitude > 4f)
                    {
                        beams[i].transform.position = game.builder.ToWorld(new Vector3(x, 0f, z));   // (the squares are map-local, as propPos)
                        on = true;
                    }
                }
            }
            beams[i].SetActive(on);
        }
    }

    void Hide()
    {
        shown = false;
        for (int i = 0; i < beams.Length; i++) if (beams[i] != null) beams[i].SetActive(false);
    }
}
