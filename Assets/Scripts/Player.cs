using UnityEngine;

// The local player: a walking body, or a prop (the disguise is the body, G5). Mouse and keyboard.
//   walking body: 0.4 x 1.7 x 0.4 m box, eye at 1.6 m
//   prop: the prop type's box at its scale; eye at 0.9 x its height (0.25 to 2.0 m); drawn as a faint
//         see-through shell in first person and as the real prop in third person (G13)
// Player.cs: movement, camera, disguise, pin and lock. PlayerActions.cs: aiming, doors, the tagger, taunts.
public partial class Player : MonoBehaviour
{
    public const int ROLE_NONE = 0, ROLE_EXPLORE = 1, ROLE_HUNTER = 2, ROLE_HIDER = 3, ROLE_DEAD = 4;

    public int role;
    public bool active;
    public BoxBody body;
    public float camYaw, camPitch;
    public bool third;
    float thirdDist = 3.2f;

    public int def = -1;                // disguise def, -1 = walking body
    public int disguiseClass;           // 0 tiny, 1 medium, 2 large, 3 huge (G46)
    public float eye = Balance.HunterEye;
    public bool locked, settled;
    float stillTime;
    Vector3 lastPos;
    public int health, maxHealth;
    public int changesLeft;
    float lastChange = -99f;
    float noSettleUntil;
    public string notice = "";
    float noticeUntil;

    GameObject visual, figure;
    MeshFilter visualMesh;
    MeshRenderer visualRend;
    int captureClickFrame = -1;
    public bool CaptureClick { get { return Time.frameCount == captureClickFrame; } }   // this frame's click only captured the mouse

    // simulated input for the build checks (Game.Cmd)
    float testWalkUntil, testForward, testRight;
    bool testRun, testJump, testUse, testLock;

    void Awake()
    {
        body = new BoxBody("Player");
        visual = new GameObject("PlayerProp");
        visualMesh = visual.AddComponent<MeshFilter>();
        visualRend = visual.AddComponent<MeshRenderer>();
        visual.SetActive(false);
        figure = BuildFigure();
        figure.SetActive(false);
        InitActions();
    }

    GameObject BuildFigure()
    {
        return FigureMesh.Build("PlayerFigure", new Color32(72, 132, 196, 255));
    }

    public void Activate(int newRole, Vector3 at, float yaw)
    {
        role = newRole; active = true;
        BecomeWalker();
        body.pos = at; body.velY = 0f; body.grounded = false;
        camYaw = yaw; camPitch = 5f;
        locked = false; settled = false;
        Game.I.cam.enabled = !Game.I.topView;
    }

    public void Deactivate()
    {
        active = false;
        visual.SetActive(false); figure.SetActive(false);
        ClearAim();
    }

    public void Say(string text, float seconds = 2.5f)
    {
        notice = text; noticeUntil = Time.time + seconds;
    }

    public string Notice { get { return Time.time < noticeUntil ? notice : ""; } }

    // ---------------------------------------------------------------- shape
    public void BecomeWalker()
    {
        def = -1;
        body.SetShape(Balance.WalkBody);
        body.stepHeight = Balance.WalkStep;
        body.hopHeight = 0f; body.jumpSpeed = Balance.HunterJumpImpulse; body.ledge = 0f;
        body.yaw = 0f;
        eye = Balance.HunterEye;
        locked = false; settled = false;
        visual.SetActive(false);
    }

    public static Vector3 DefSize(int d)
    {
        return new Vector3(GenData.defSx[d], GenData.defSy[d], GenData.defSz[d]) / 1000f;
    }

    // Become a prop of this type. False (and nothing changes) if its box has no room here.
    public bool SetDisguise(int d)
    {
        Vector3 size = DefSize(d);
        Vector3 oldHalf = body.half;
        Vector3 oldPos = body.pos;
        float oldYaw = body.yaw;
        if (def < 0) body.yaw = Mathf.Round(camYaw / 15f) * 15f;
        body.SetShape(size);
        // a tight spot: nudge clear by up to 0.3 m, else refuse (05 section 3.7)
        for (int i = 0; i < 4 && !body.Fits(body.pos, body.yaw); i++) body.Depenetrate(0f);
        if (!body.Fits(body.pos, body.yaw) || (body.pos - oldPos).magnitude > 0.3f)
        {
            body.half = oldHalf; body.pos = oldPos; body.yaw = oldYaw;
            if (def < 0) body.SetShape(Balance.WalkBody); else body.SetShape(DefSize(def));
            return false;
        }
        int oldMax = maxHealth;
        def = d;
        disguiseClass = Mathf.Clamp(GenData.defDisguise[d] - 1, 0, 3);
        body.stepHeight = Balance.StepHeight(size.y);
        body.hopHeight = Balance.HopHeight[disguiseClass];
        body.jumpSpeed = 0f;
        body.ledge = disguiseClass < 2 ? Balance.LedgeAssist : 0f;
        eye = Balance.EyeHeight(size.y);
        // the first-person shell keeps clear of the eye by the prop's half diagonal, except low down (PropGhost.shader)
        ModelLibrary.ghost.SetFloat("_Clear", 0.5f * Mathf.Sqrt(size.x * size.x + size.z * size.z));
        ModelLibrary.ghost.SetFloat("_Below", size.y / 3f);
        // changing disguise keeps the same fraction of health (05 section 6.1)
        maxHealth = Balance.HiderHealth[disguiseClass];
        health = oldMax > 0 ? Mathf.Max(1, Mathf.RoundToInt((float)health / oldMax * maxHealth)) : maxHealth;
        visualMesh.sharedMesh = ModelLibrary.mesh[GenData.defModel[d]];
        float sc = GenData.defScale[d] / 1000f;
        visual.transform.localScale = new Vector3(sc, sc, sc);
        visual.SetActive(true);
        locked = false; settled = false; stillTime = 0f;
        body.grounded = false;
        return true;
    }

    // ---------------------------------------------------------------- frame
    void Update()
    {
        bool lobby = Game.I.state == Game.State.Lobby;
        if (!active || (Game.I.state != Game.State.Playing && !lobby)) return;
        bool paused = Game.I.menuOpen || Game.I.round.InputBlocked;
        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        if (!paused)
        {
            // a click captures the mouse, unless it is on the wrist panel or on the third-person screen (dragging it)
            Vector2 gp = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool onPanel = (GameUI.I.wristOpen && GameUI.I.WristRect.Contains(gp)) || PipScreen.I.Contains(gp) || (lobby && GameUI.I.LobbyAidsRect.Contains(gp));
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && !onPanel)
            {
                GameUI.I.wristOpen = false;
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                captureClickFrame = Time.frameCount;   // this click only captures the mouse
            }
            Look();
        }

        // input
        float f = 0f, r = 0f;
        bool run = false, jump = false;
        if (!paused)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) f += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) f -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) r += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) r -= 1f;
            run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            jump = Input.GetKeyDown(KeyCode.Space);
        }
        if (Time.time < testWalkUntil) { f = testForward; r = testRight; run = testRun; }
        if (testJump) { jump = true; testJump = false; }

        bool moveInput = f != 0f || r != 0f || jump;
        if (locked || Game.I.round.MovementBlocked(this)) { f = 0f; r = 0f; jump = false; }

        Quaternion yawRot = Quaternion.Euler(0f, camYaw, 0f);
        Vector3 wish = yawRot * new Vector3(r, 0f, f);
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        float speed;
        if (def < 0) speed = Balance.HunterRun;                               // VRChat's desktop default is running
        else speed = Balance.HiderRun[disguiseClass] * (run ? 1f : Balance.DesktopWalkFactor);
        wish *= speed;

        // the prop faces the way the hider turned it by looking while moving; pinned, it keeps its facing and the
        // view looks around freely (G38: no turn keys, no rotation lock, no facing rounding)
        if (def >= 0 && !settled && !locked) body.TryYaw(camYaw);

        // sub-steps keep sweeps short on slow frames
        UpdateMask();
        float left = dt;
        while (left > 1e-5f)
        {
            float h = Mathf.Min(left, 0.034f);
            body.Move(wish, jump, h);
            jump = false;
            left -= h;
        }
        body.Depenetrate(dt);

        if ((body.pos - lastPos).sqrMagnitude > 2.5e-5f) lastMoveTime = Time.time;
        UpdatePin(moveInput, dt);
        // no movement sounds (G41): no footsteps, no shuffle of a running prop. What a moving prop should sound like
        // to hunters is an open design question (README, revision 6).
        UpdateVisual();
        UpdateCamera();
        if (!paused && !lobby) UpdateActions(dt);   // in the lobby a click is for the start stand
        else ClearAim();
        lastPos = body.pos;
    }

    // What the body collides with (G45): a walking body (hunter, the dead, explore, the lobby) passes through props
    // and stops only at the architecture; a prop body (a disguised hider) and a hider still on foot collide with
    // every prop (G26), and step onto low ones.
    void UpdateMask()
    {
        body.mask = (def >= 0 || role == ROLE_HIDER) ? Layers.MoveMask : Layers.WalkerMask;
    }

    void Look()
    {
        float mx = Input.GetAxisRaw("Mouse X"), my = Input.GetAxisRaw("Mouse Y");
        // without pointer lock (before the first click) only turn while the right button is held
        if (Cursor.lockState != CursorLockMode.Locked && !Input.GetMouseButton(1)) { mx = 0f; my = 0f; }
        float s = 2.2f * Game.I.mouseSensitivity;
        camYaw = Mathf.Repeat(camYaw + mx * s, 360f);
        camPitch = Mathf.Clamp(camPitch - my * s, -85f, 85f);
    }

    // moving, settled, locked (05 section 3.6.5)
    void UpdatePin(bool moveInput, float dt)
    {
        if (def < 0) { settled = false; locked = false; return; }
        float moved = (body.pos - lastPos).magnitude;
        if (moveInput && !locked) { settled = false; stillTime = 0f; }
        if (!body.grounded || moved > 0.01f * Mathf.Max(1f, dt / 0.016f)) { stillTime = 0f; if (!locked) settled = false; }
        else stillTime += dt;
        // settling only pins the prop where the hider left it: no snapping into a slot and no rounding of its
        // position or facing (owner decisions G37, G38); it rests on whatever is under it
        if (!settled && !locked && body.grounded && stillTime >= Balance.SettleTime && Time.time >= noSettleUntil) settled = true;
    }

    public void Unpin(float noSettleFor)
    {
        locked = false; settled = false; stillTime = 0f;
        noSettleUntil = Time.time + noSettleFor;
    }

    void UpdateVisual()
    {
        bool showProp = def >= 0;
        if (visual.activeSelf != showProp) visual.SetActive(showProp);
        if (showProp)
        {
            visual.transform.position = body.pos;
            visual.transform.rotation = body.Rot;
            // the hider's own prop: a faint shell from inside, the real prop from outside
            visualRend.sharedMaterial = (third || Game.I.topView) ? ModelLibrary.lit : ModelLibrary.ghost;
        }
        bool showFigure = def < 0 && (third || Game.I.topView) && role != ROLE_NONE;
        if (figure.activeSelf != showFigure) figure.SetActive(showFigure);
        if (showFigure)
        {
            figure.transform.position = body.pos;
            figure.transform.rotation = Quaternion.Euler(0f, camYaw, 0f);
        }
    }

    // The third-person screen draws the player from outside: the real prop instead of the faint shell, the figure
    // instead of nothing. PipScreen renders between the two calls; afterwards the first-person state is back.
    public void PipRender(bool begin)
    {
        bool outside = begin || third || Game.I.topView;
        if (def >= 0) visualRend.sharedMaterial = outside ? ModelLibrary.lit : ModelLibrary.ghost;
        else if (role != ROLE_NONE)
        {
            if (figure.activeSelf != outside) figure.SetActive(outside);
            if (outside)
            {
                figure.transform.position = body.pos;
                figure.transform.rotation = Quaternion.Euler(0f, camYaw, 0f);
            }
        }
    }

    void UpdateCamera()
    {
        Camera cam = Game.I.cam;
        Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
        Vector3 head = body.pos + Vector3.up * eye;
        if (!third)
        {
            cam.transform.position = head;
            cam.transform.rotation = rot;
            cam.nearClipPlane = def < 0 ? 0.05f : Mathf.Clamp(0.05f * eye, 0.01f, 0.05f);
        }
        else
        {
            Vector3 pivot = body.pos + Vector3.up * Mathf.Max(eye + 0.15f, 0.6f);
            Vector3 back = rot * Vector3.back;
            float dist = thirdDist;
            RaycastHit hit;
            if (Physics.SphereCast(pivot, 0.18f, back, out hit, thirdDist, Layers.CameraMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0.3f, hit.distance);
            cam.transform.position = pivot + back * dist;
            cam.transform.rotation = rot;
            cam.nearClipPlane = 0.05f;
        }
    }

    // ---------------------------------------------------------------- helpers for the game and the checks
    public Vector3 EyePos { get { return body.pos + Vector3.up * eye; } }

    float lastMoveTime = -9f;
    public bool MovedRecently { get { return Time.time - lastMoveTime < 0.4f; } }

    // The disguise as a target for bot hunters' darts: the def's own collider set, switched on only while a
    // shot is being resolved (the player's own casts must never meet it).
    GameObject hitbox;
    int hitboxDef = -2;

    public void SetHitbox(bool on)
    {
        if (!on) { if (hitbox != null) hitbox.SetActive(false); return; }
        if (hitboxDef != def)
        {
            if (hitbox != null) Destroy(hitbox);
            hitbox = new GameObject("PlayerHitbox");
            hitbox.AddComponent<PlayerHitbox>();
            hitbox.layer = Layers.Hider;
            PropColliders.Build(hitbox, def);
            hitboxDef = def;
        }
        float sc = GenData.defScale[def] / 1000f;
        hitbox.transform.position = body.pos;
        hitbox.transform.rotation = body.Rot;
        hitbox.transform.localScale = new Vector3(sc, sc, sc);
        hitbox.SetActive(true);
    }

    public void Teleport(Vector3 p)
    {
        body.pos = p; body.velY = 0f; body.grounded = false; body.lifting = false;
        lastPos = p; settled = false; locked = false;
    }

    public void SetLook(float yaw, float pitch) { camYaw = yaw; camPitch = pitch; }

    public void SetThird(bool on) { third = on; }

    public void TestWalk(float forward, float seconds, bool run)
    {
        testForward = forward; testRight = 0f; testRun = run; testWalkUntil = Time.time + seconds;
    }

    public void TestKey(string k)
    {
        if (k == "jump") testJump = true;
        else if (k == "use") testUse = true;
        else if (k == "lock") testLock = true;
        else if (k == "take") TryTake();
        else if (k == "taunt") Taunt(false);
    }

    public string StatusLine()
    {
        return "player role " + role + " def " + (def < 0 ? "walker" : GenData.defId[def]) + " pos " + body.pos.x.ToString("0.00") + "," + body.pos.y.ToString("0.00") + "," + body.pos.z.ToString("0.00")
            + " yaw " + body.yaw.ToString("0") + " cam " + camYaw.ToString("0") + "/" + camPitch.ToString("0") + " eye " + eye.ToString("0.00")
            + " grounded " + body.grounded + " settled " + settled + " locked " + locked + " hp " + health + "/" + maxHealth + " hunterHp " + hunterHealth
            + " aim " + AimText() + " notice '" + Notice + "'";
    }
}
