using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// The doors and lids of openable props (04 section 4.9, the prototype's Openable): a toggle swings them open or
// shut over 0.3 s (eased, 100 degrees). Whether each one is open is synced: whoever toggles one takes the
// object and sends the table. A hider can shut themselves inside a wardrobe; a closed door stops darts.
// Desktop: click on a door within reach (hiders 1.5 m, hunters 2.5 m from the prop). VR: grip with a hand on
// or close to a door. (PHLocal decides; this only keeps the state and moves the parts.)
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PHDoors : UdonSharpBehaviour
{
    public PHGame game;
    public const int MAX = 256, MAX_PARTS = 4;
    public const float SWING_TIME = 0.3f, OPEN_ANGLE = 100f;

    [UdonSynced] public bool[] open = new bool[MAX];
    [UdonSynced] public int round = -1;

    // per openable: its emit index, its parts (doors/lids) and which way each swings
    [HideInInspector] public int count;
    int[] emitOf = new int[MAX];
    Transform[] parts = new Transform[MAX * MAX_PARTS];
    float[] sign = new float[MAX * MAX_PARTS];
    int[] partCount = new int[MAX];
    float[] t = new float[MAX];
    int listedFor = -1;

    // the openables of the map that was just built (called on every client)
    public void ListDoors()
    {
        count = 0;
        GameObject[] objs = game.builder.objects;
        if (objs == null) return;
        for (int e = 0; e < objs.Length && count < MAX; e++)
        {
            if (objs[e] == null) continue;
            Transform root = objs[e].transform;
            int k = 0;
            for (int c = 0; c < root.childCount && k < MAX_PARTS; c++)
            {
                Transform ch = root.GetChild(c);
                if (!ch.name.StartsWith("door")) continue;
                parts[count * MAX_PARTS + k] = ch;
                // a door hinged on its +x side swings out with a positive turn, on its -x side with a negative one
                sign[count * MAX_PARTS + k] = ch.localPosition.x > 0f ? 1f : -1f;
                k++;
            }
            if (k == 0) continue;
            emitOf[count] = e; partCount[count] = k; t[count] = 0f;
            count++;
        }
        listedFor = game.round;
        Apply();
    }

    // the openable a collider belongs to (a door part, or the prop itself), or -1
    public int IndexOf(Transform hitPart)
    {
        Transform tr = hitPart;
        while (tr != null && tr.parent != game.builder.propRoot) tr = tr.parent;
        if (tr == null) return -1;
        for (int i = 0; i < count; i++)
        {
            GameObject o = game.builder.objects[emitOf[i]];
            if (o != null && o.transform == tr) return i;
        }
        return -1;
    }

    public int EmitOf(int i) { return emitOf[i]; }

    // the nearest openable with a part within `reach` of a point, or -1 (VR: a hand)
    public int NearPart(Vector3 p, float reach)
    {
        int best = -1;
        float bestD = reach * reach;
        for (int i = 0; i < count; i++)
            for (int k = 0; k < partCount[i]; k++)
            {
                Transform part = parts[i * MAX_PARTS + k];
                if (part == null) continue;
                Collider c = part.GetComponent<Collider>();
                Vector3 q = c != null ? c.ClosestPoint(p) : part.position;
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
        return best;
    }

    public void Toggle(int i)
    {
        if (i < 0 || i >= count) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (round != game.round) { for (int k = 0; k < MAX; k++) open[k] = false; round = game.round; }
        open[i] = !open[i];
        RequestSerialization();
        if (game.local != null && game.local.sound != null)
        {
            GameObject o = game.builder.objects[emitOf[i]];
            if (o != null) game.local.sound.PlayAt(open[i] ? "doorOpen_1" : "doorClose_1", o.transform.position, 10f, 0.8f, 1f);
        }
    }

    // test aid (editor, MCP)
    public void TestToggleFirst() { Toggle(0); }

    public override void OnDeserialization()
    {
        // others hear a door that changed (a hider shutting themselves in is a clue)
        for (int i = 0; i < count; i++)
        {
            bool want = open[i] && round == game.round;
            if ((t[i] > 0.5f) != want && game.local != null && game.local.sound != null)
            {
                GameObject o = game.builder.objects[emitOf[i]];
                if (o != null) game.local.sound.PlayAt(want ? "doorOpen_1" : "doorClose_1", o.transform.position, 10f, 0.8f, 1f);
            }
        }
    }

    void Update()
    {
        if (game == null || game.builder == null) return;
        if (game.builder.built && listedFor != game.round && game.MapReady) ListDoors();
        Apply();
    }

    void Apply()
    {
        float step = Time.deltaTime / SWING_TIME;
        for (int i = 0; i < count; i++)
        {
            float target = (open[i] && round == game.round) ? 1f : 0f;
            if (Mathf.Approximately(t[i], target)) continue;
            t[i] = Mathf.MoveTowards(t[i], target, step);
            float e = t[i] * t[i] * (3f - 2f * t[i]);
            for (int k = 0; k < partCount[i]; k++)
            {
                Transform part = parts[i * MAX_PARTS + k];
                if (part != null) part.localRotation = Quaternion.Euler(0f, sign[i * MAX_PARTS + k] * OPEN_ANGLE * e, 0f);
            }
        }
    }
}
