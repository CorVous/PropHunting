using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// One knocked prop at a time (04 section 4.8, D12): a pool of these carries a dart's knock to everyone.
// The hunter whose dart knocked the prop owns the knock and runs its physics; everyone else moves their copy of
// the prop to the synced pose. Updates go out only while the prop moves (about 8 a second); once it rests one
// last update carries the final pose and nothing more is sent. The knock keeps that pose (late joiners get it)
// until the pool reuses it for another prop or a new round starts (props go home, D11).
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PHKnock : UdonSharpBehaviour
{
    public PHGame game;
    public const float SETTLE_TIME = 4f, SEND_INTERVAL = 0.12f;
    public const float KNOCK_SPEED_LIGHT = 5.5f, KNOCK_SPEED = 3.2f, KNOCK_SPEED_HEAVY = 2.2f;

    [UdonSynced] public int emit = -1;
    [UdonSynced] public int round = -1;
    [UdonSynced] public Vector3 pos;
    [UdonSynced] public Quaternion rot;
    [UdonSynced] public bool moving;

    [HideInInspector] public float lastUsed;
    Rigidbody body;          // owner: the prop's rigidbody while it flies
    float knockedAt, nextSend;
    int shownEmit = -1;

    public bool Busy { get { return moving && round == game.round; } }

    // the owner starts a knock: the prop flies from the dart's hit
    public void Knock(int e, Vector3 dir, Vector3 point)
    {
        GameObject go = game.builder.objects[e];
        if (go == null) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (emit != e && emit >= 0 && round == game.round) Release();   // the pool reuses the oldest knock
        emit = e; round = game.round; shownEmit = e;
        lastUsed = Time.time;
        body = go.GetComponent<Rigidbody>();
        if (body == null) return;
        body.isKinematic = false;
        int fl = game.gen.defFlags[game.gen.emitDef[e]];
        bool light = (fl & GenData.FL_LIGHT) != 0;
        // heavy furniture and trees (Cor: most things knockable) are shoved, not thrown: slower, low, no spin
        bool heavy = (fl & (GenData.FL_HEAVY | GenData.FL_SCENERY)) != 0;
        float mass = body.mass;
        float speed = heavy ? KNOCK_SPEED_HEAVY : (light ? KNOCK_SPEED_LIGHT : KNOCK_SPEED) * Mathf.Clamp(8f / (mass + 4f), 0.5f, 1.2f);
        Vector3 push = (dir.normalized + Vector3.up * (heavy ? 0.12f : 0.45f)).normalized * speed;
        body.AddForceAtPosition(push * mass, point, ForceMode.Impulse);
        if (!heavy) body.AddTorque(Random.insideUnitSphere * mass * 0.4f, ForceMode.Impulse);
        knockedAt = Time.time;
        moving = true;
        KnockSound(point);
        Capture();
        nextSend = Time.time + SEND_INTERVAL;
        RequestSerialization();
    }

    void Capture()
    {
        Transform t = game.builder.objects[emit].transform;
        pos = t.position; rot = t.rotation;
    }

    // stop simulating the old prop where it lies (only on the owner's client, which was running it)
    void Release()
    {
        if (body != null) { body.isKinematic = true; body = null; }
    }

    // profiling (PHProfiler): while it runs, the time this script's updates take adds up in profMs
    [System.NonSerialized] public System.Diagnostics.Stopwatch profWatch;
    [System.NonSerialized] public double profMs;

    void Update()
    {
        if (profWatch == null) { UpdateBody(); return; }
        double p0 = profWatch.Elapsed.TotalMilliseconds;
        UpdateBody();
        profMs += profWatch.Elapsed.TotalMilliseconds - p0;
    }

    void UpdateBody()
    {
        if (game == null || emit < 0 || game.builder.objects == null || emit >= game.builder.objects.Length) return;
        if (round != game.round) return;
        bool owner = Networking.IsOwner(gameObject);
        if (owner && body != null && moving)
        {
            float t = Time.time - knockedAt;
            Transform tr = game.builder.objects[emit].transform;
            bool done = t > SETTLE_TIME || (t > 0.4f && body.IsSleeping()) || tr.position.y < -3f;
            if (done)
            {
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                body.isKinematic = true; body = null;
                moving = false;
                Capture();
                RequestSerialization();   // the last update: the pose it rests in
                return;
            }
            if (Time.time >= nextSend)
            {
                nextSend = Time.time + SEND_INTERVAL;
                Capture();
                RequestSerialization();
            }
            return;
        }
        if (owner && body == null && !moving) return;
        // everyone else: follow the synced pose
        GameObject go = game.builder.objects[emit];
        if (go == null) return;
        Transform g = go.transform;
        // (at rest: put once where it lies, not every frame; OnDeserialization puts it there too. Perf)
        if (!moving) { if (restPut != go) { g.SetPositionAndRotation(pos, rot); restPut = go; } return; }
        restPut = null;
        float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
        g.SetPositionAndRotation(Vector3.Lerp(g.position, pos, k), Quaternion.Slerp(g.rotation, rot, k));
    }

    bool wasMoving;
    int heardEmit = -1;

    // the knock's sound, where the prop was hit: wood, soft, or a thud
    void KnockSound(Vector3 at)
    {
        PHAudio a = game.local != null ? game.local.sound : null;
        if (a == null) return;
        int d = game.gen.emitDef[emit];
        string id = game.gen.defId[d];
        bool soft = id.Contains("cushion") || id.Contains("pillow") || id.Contains("bed") || id.Contains("rug") || id.Contains("plush") || id.Contains("bag");
        a.PlayAt(a.Pick(soft ? a.softKnock : a.woodKnock, Random.Range(0, 100)), at, 16f, 0.9f, Random.Range(0.9f, 1.1f));
    }

    GameObject restPut;   // the prop last put at its resting pose (UpdateBody); a new sync puts it again

    // The hunter running a knock left while it flew: the new owner has no rigidbody for it, so 'moving' would stay
    // set for good (the knock Busy all round, never reused; everyone easing the prop toward a pose that never comes).
    // The new owner settles it where it shows it now and sends that. The old owner, if still here, stops its physics.
    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // (a prop still in the air is taken over and lands here: settled where it shows, it would hang in mid-air)
        restPut = null;
        if (!Networking.IsOwner(gameObject)) { Release(); return; }
        if (body != null || !moving) return;
        GameObject go = (emit >= 0 && game != null && game.builder.objects != null && emit < game.builder.objects.Length) ? game.builder.objects[emit] : null;
        if (go == null || round != game.round) { moving = false; RequestSerialization(); return; }
        body = go.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = false;
            knockedAt = Time.time;   // the normal settle path ends it and sends the pose it rests in
            nextSend = Time.time;
            return;
        }
        moving = false;
        Capture();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        restPut = null;
        // a knock that has just started: everyone hears it
        // (only on a built map: a late joiner still generating has no prop table yet, and a bad index halts this script)
        if (moving && (!wasMoving || heardEmit != emit) && round == game.round && emit >= 0 && game.MapReady && emit < game.gen.emitDef.Length) KnockSound(pos);
        wasMoving = moving; heardEmit = emit;
        // a knock of another prop took this one over: the old prop stays where it lay
        shownEmit = emit;
        if (!moving && emit >= 0 && round == game.round && game.builder.objects != null && emit < game.builder.objects.Length && game.builder.objects[emit] != null)
            game.builder.objects[emit].transform.SetPositionAndRotation(pos, rot);
    }
}
