using UnityEngine;

// Player, part 2: what the player aims at and does: take a disguise, lock, turn, taunt, open doors, fire the tagger.
public partial class Player
{
    public PropInstance aimProp;
    public BotHider aimHider;
    public Openable aimDoor;
    public Vector3 aimPoint;
    public float aimDist;
    public bool aimValid;
    PropInstance highlighted;
    MaterialPropertyBlock block;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    // hunter
    public int hunterHealth;
    public int wrongProps, hitsLanded, catches;
    float lastShot = -9f;
    public float hitMarkUntil, hurtUntil;
    GameObject tagger;
    float recoil;
    float lastTaunt = -99f;

    void InitActions()
    {
        block = new MaterialPropertyBlock();
        // the tagger: Kenney Blaster Kit's blaster-a, drawn at the lower right of the view (05 section 5.1)
        int m = ModelLibrary.Find("blaster-a");
        tagger = new GameObject("Tagger");
        tagger.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.mesh[m];
        tagger.AddComponent<MeshRenderer>().sharedMaterial = ModelLibrary.lit;
        tagger.SetActive(false);
    }

    void ClearAim()
    {
        aimProp = null; aimHider = null; aimDoor = null; aimValid = false; insideNow = null;
        Highlight(null);
    }

    void Highlight(PropInstance p)
    {
        if (p == highlighted) return;
        if (highlighted != null)
        {
            var r = highlighted.GetComponent<MeshRenderer>();
            if (r != null) r.SetPropertyBlock(null);
        }
        highlighted = p;
        if (p != null)
        {
            block.SetColor(ColorId, new Color(1.5f, 1.45f, 1.1f, 1f));
            p.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }
    }

    bool CanDisguise { get { return role == ROLE_EXPLORE || role == ROLE_HIDER; } }

    void UpdateActions(float dt)
    {
        Camera cam = Game.I.cam;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit hit;
        aimProp = null; aimHider = null; aimDoor = null; aimValid = false;
        if (Physics.Raycast(ray, out hit, 40f, Layers.AimMask, QueryTriggerInteraction.Ignore))
        {
            aimValid = true;
            aimPoint = hit.point;
            aimDist = Vector3.Distance(EyePos, hit.point);
            if (hit.collider.gameObject.layer == Layers.Door) aimDoor = hit.collider.GetComponentInParent<Openable>();
            aimProp = hit.collider.GetComponentInParent<PropInstance>();
            aimHider = hit.collider.GetComponentInParent<BotHider>();
        }
        // a soft highlight on the prop a hider could take, on the hider's own screen only (05 section 3.8)
        Highlight(CanDisguise && aimDoor == null && aimProp != null && aimDist <= Balance.TakeRange ? aimProp : null);

        insideNow = def >= 0 ? InsideOpenable() : null;
        bool use = Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked && Time.frameCount != captureClickFrame;
        if (testUse) { use = true; testUse = false; }
        if (use)
        {
            // a click that operates a handle does not also fire or take; a prop shut inside furniture works
            // its door from inside with a click, wherever the view points (the inner handle, 05 section 3.11)
            if (insideNow != null) { aimDoor = insideNow; aimPoint = body.Centre; UseDoor(); }
            else if (aimDoor != null && DoorInReach()) UseDoor();
            else if (role == ROLE_HUNTER) Fire();
            else if (CanDisguise) TryTake();
        }
        if (Input.GetKeyDown(KeyCode.E) && CanDisguise) TryTake();
        if ((Input.GetKeyDown(KeyCode.Q) || testLock) && def >= 0) ToggleLock();
        testLock = false;
        if (Input.GetKeyDown(KeyCode.R) && def >= 0) { rotationLock = !rotationLock; Say(rotationLock ? "Rotation lock on: the prop keeps its direction while pinned" : "Rotation lock off: the prop turns with your view"); }
        if (Input.GetKeyDown(KeyCode.Z)) TurnPinned(-Balance.TurnStepDeg);
        if (Input.GetKeyDown(KeyCode.C)) TurnPinned(Balance.TurnStepDeg);
        if (Input.GetKeyDown(KeyCode.T)) Taunt(false);
        if (Input.GetKeyDown(KeyCode.G) && role == ROLE_EXPLORE && def >= 0) { ToWalker(); Say("Back to the walking body"); }
        if (Input.GetKeyDown(KeyCode.U)) Unstick();

        // tagger view model
        bool showTagger = role == ROLE_HUNTER && !third && !Game.I.topView;
        if (tagger.activeSelf != showTagger) tagger.SetActive(showTagger);
        if (showTagger)
        {
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 6f);
            Transform ct = cam.transform;
            tagger.transform.position = ct.position + ct.right * 0.21f - ct.up * (0.21f - recoil * 0.03f) + ct.forward * (0.5f - recoil * 0.08f);
            tagger.transform.rotation = ct.rotation * Quaternion.Euler(-recoil * 12f, 0f, 0f);
            tagger.transform.localScale = Vector3.one * 0.32f;
        }
    }

    public Vector3 MuzzlePos()
    {
        Transform ct = Game.I.cam.transform;
        if (third || Game.I.topView) return EyePos - Vector3.up * 0.25f;
        return ct.position + ct.right * 0.2f - ct.up * 0.12f + ct.forward * 0.7f;
    }

    public string AimText()
    {
        if (insideNow != null) return insideNow.open ? "Inside: click to close the door" : "Inside: click to open the door";
        if (!aimValid) return "";
        if (aimDoor != null) return DoorInReach() ? (aimDoor.open ? "Click: close" : "Click: open") : "";
        if (!CanDisguise || aimProp == null || aimDist > Balance.TakeRange) return "";
        int d = aimProp.def;
        int cls = GenData.defDisguise[d];
        if (cls == 0) return GameUI.DefName(d) + " · not a disguise";
        bool onMenu = Game.I.map.gen.menu[d];
        string cn = cls == 1 ? "tiny" : (cls == 2 ? "medium" : "large");
        string t = GameUI.DefName(d) + " · " + cn + " · " + Balance.HiderHealth[cls - 1] + " HP";
        if (!onMenu && role == ROLE_HIDER) return t + " · not a disguise this round";
        return t + " · E: disguise";
    }

    // ---------------------------------------------------------------- disguise
    public void TryTake()
    {
        if (!CanDisguise) return;
        if (aimProp == null || aimDoor != null) return;
        if (aimDist > Balance.TakeRange) { Say("Too far: within " + Balance.TakeRange.ToString("0") + " m"); return; }
        int d = aimProp.def;
        if (GenData.defDisguise[d] == 0) { Say("Not a disguise"); AudioHub.I.Play("error_004", 0.4f); return; }
        if (role == ROLE_HIDER && !Game.I.map.gen.menu[d]) { Say("Not a disguise this round"); AudioHub.I.Play("error_004", 0.4f); return; }
        if (role == ROLE_HIDER && Game.I.round.phase == Round.PH_HUNT)
        {
            if (changesLeft <= 0) { Say("No disguise changes left this round"); return; }
            if (Time.time - lastChange < Balance.HuntChangeCooldown) { Say("Wait " + (Balance.HuntChangeCooldown - (Time.time - lastChange)).ToString("0") + " s to change again"); return; }
        }
        if (!SetDisguise(d)) { Say("No room here"); AudioHub.I.Play("error_004", 0.4f); return; }
        if (role == ROLE_HIDER && Game.I.round.phase == Round.PH_HUNT)
        {
            changesLeft--; lastChange = Time.time;
            // a change in the hunt pops, heard to 8 m
            AudioHub.I.PlayAt("drop_002", body.pos, Balance.ChangePopRange, 0.9f);
            Game.I.round.NoiseAt(body.pos, Balance.ChangePopRange, this);
        }
        else AudioHub.I.Play("drop_002", 0.5f);
        Say("You are a " + GameUI.DefName(d).ToLowerInvariant());
    }

    public string TakeDisguiseByName(string id)
    {
        int d = GenData.FindDef(id);
        return SetDisguise(d) ? "disguised as " + id + " eye " + eye.ToString("0.00") : "no room";
    }

    void ToggleLock()
    {
        if (locked) { locked = false; Say("Unlocked"); AudioHub.I.Play("toggle_001", 0.5f); return; }
        if (!settled) { Say("Stand still for a moment to lock"); return; }
        locked = true;
        Say("Locked in place (Q to unlock, Z / C to turn)");
        AudioHub.I.Play("toggle_002", 0.5f);
    }

    // turn the prop in 15 degree steps while pinned (05 section 3.6.5)
    public void TurnPinned(float deg)
    {
        if (def < 0 || !(settled || locked)) return;
        if (!body.TryYaw(Mathf.Round((body.yaw + deg) / Balance.TurnStepDeg) * Balance.TurnStepDeg)) Say("No room to turn");
        else AudioHub.I.Play("tick_001", 0.4f);
    }

    public void Taunt(bool forced)
    {
        if (def < 0 || role == ROLE_DEAD || role == ROLE_HUNTER) return;
        if (!forced && Time.time - lastTaunt < Balance.TauntCooldown) { Say("Taunt again in " + (Balance.TauntCooldown - (Time.time - lastTaunt)).ToString("0") + " s"); return; }
        lastTaunt = Time.time;
        AudioHub.I.PlayAt(AudioHub.Pick(AudioHub.Taunts), body.pos + Vector3.up * 0.2f, Balance.TauntRange, 1f, Random.Range(0.8f, 1.25f));
        Game.I.round.NoiseAt(body.pos, Balance.TauntRange, this);
        Game.I.round.PlayerTaunted(forced);
    }

    // back to the walking body where the prop stood; if the taller box has no room there, the nearest free spot
    public void ToWalker()
    {
        BecomeWalker();
        for (int i = 0; i < 4 && !body.Fits(body.pos, 0f); i++) body.Depenetrate(0f);
        if (!body.Fits(body.pos, 0f)) Unstick();
    }

    public void Unstick()
    {
        // to the nearest free reachable cell (05 N5)
        var map = Game.I.map;
        int c = map.CellAt(body.pos);
        if (c < 0) { Teleport(map.ReleasePoint() + Vector3.up * 0.05f); return; }
        var g = map.gen;
        int best = -1, bestD = int.MaxValue;
        int cx = c % g.IW, cz = c / g.IW;
        for (int i = 0; i < g.IW * g.IH; i++)
        {
            if (g.reach[i] == 0 || g.walk[i] != 1) continue;
            int dx = i % g.IW - cx, dz = i / g.IW - cz;
            int dd = dx * dx + dz * dz;
            if (dd < bestD) { bestD = dd; best = i; }
        }
        if (best >= 0) { Teleport(map.CellCentre(best) + Vector3.up * 0.05f); Say("Unstuck"); }
    }

    static readonly Collider[] nearSolid = new Collider[12];
    Openable insideNow;   // the furniture the prop is inside this frame, if any

    // the openable whose hollow the prop body is in, or null
    public Openable InsideOpenable()
    {
        int n = Physics.OverlapSphereNonAlloc(body.Centre, 0.6f, nearSolid, 1 << Layers.Solid, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var op = nearSolid[i].GetComponentInParent<Openable>();
            if (op != null && op.Contains(body.Centre)) return op;
        }
        return null;
    }

    // ---------------------------------------------------------------- doors (C7: click on desktop)
    bool DoorInReach()
    {
        if (aimDoor == null) return false;
        if (def >= 0)
        {
            // a hider works a handle by aim within 1.5 m of the prop
            float d = Vector3.Distance(body.Centre, aimPoint) - Mathf.Max(body.half.x, body.half.z);
            return d <= Balance.HandleRange;
        }
        return aimDist <= Balance.HunterHandleRange;
    }

    void UseDoor()
    {
        if (role == ROLE_DEAD) { Say("The dead cannot use handles"); return; }
        bool hunter = role == ROLE_HUNTER;
        if (!hunter && aimDoor.open && Time.time - aimDoor.lastOpenedByHunter < Balance.DoorRecloseDelay)
        {
            Say("A hunter just opened it"); return;
        }
        aimDoor.Toggle();
        if (hunter && aimDoor.open) aimDoor.lastOpenedByHunter = Time.time;
        AudioHub.I.PlayAt(aimDoor.open ? "doorOpen_1" : "doorClose_1", aimPoint, 12f, 0.8f);
    }

    // ---------------------------------------------------------------- the tagger (05 sections 5.2, 5.3)
    void Fire()
    {
        if (Time.time - lastShot < Balance.DartInterval) return;
        if (Game.I.round.phase != Round.PH_HUNT && Game.I.round.phase != Round.PH_NONE) return;
        lastShot = Time.time;
        recoil = 1f;
        Camera cam = Game.I.cam;
        Vector3 origin = EyePos;
        Vector3 dir = aimValid ? (aimPoint - origin).normalized : cam.transform.forward;
        AudioHub.I.Play("pluck_001", 0.7f, Random.Range(0.95f, 1.1f));
        Game.I.round.ResolveShot(this, origin, dir, MuzzlePos());
    }

    // ---------------------------------------------------------------- checks
    public string FindProp(string id)
    {
        int d = GenData.FindDef(id);
        var map = Game.I.map;
        PropInstance best = null;
        float bestD = float.MaxValue;
        foreach (var p in map.props)
        {
            if (p == null || p.def != d) continue;
            float dd = (p.transform.position - body.pos).sqrMagnitude;
            if (dd < bestD) { bestD = dd; best = p; }
        }
        if (best == null) return "none";
        Vector3 pp = best.transform.position;
        // stand in front of it, looking at it
        Vector3 fwd = best.transform.forward;
        float back = Mathf.Max(GenData.defSz[d], GenData.defSx[d]) / 2000f + 1.3f;
        Vector3 mid = pp + Vector3.up * (GenData.defSy[d] / 2000f);
        Vector3 at = pp + fwd * back;
        Vector3[] dirs = { fwd, -fwd, best.transform.right, -best.transform.right };
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 c = pp + dirs[i] * back;
            int cell = map.CellAt(c);
            if (cell < 0 || map.gen.walk[cell] != 1) continue;
            if (Physics.Linecast(new Vector3(c.x, pp.y + Balance.HunterEye, c.z), mid, 1 << Layers.Arch, QueryTriggerInteraction.Ignore)) continue;
            at = c; break;
        }
        Teleport(new Vector3(at.x, pp.y + 0.05f, at.z));
        Vector3 to = pp + Vector3.up * (GenData.defSy[d] / 2000f) - (new Vector3(at.x, pp.y, at.z) + Vector3.up * eye);
        SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
        return "emit " + best.emit + " at " + pp.x.ToString("0.00") + " " + pp.y.ToString("0.00") + " " + pp.z.ToString("0.00");
    }
}
