using UnityEngine;

// Every gameplay number in one place. Values come from the balance table of 05-gameplay-design.md
// (section 9.1, revision 5) unless marked "prototype": those are this prototype's own choices.
public static class Balance
{
    // ---- phases, by map size S, M, L, XL (9.1) ----
    public static readonly float[] HidePhase = { 35f, 40f, 45f, 50f };
    public static readonly float[] HuntPhase = { 180f, 210f, 240f, 270f };
    public const float Brief = 8f;
    public const float RoundEnd = 12f;
    public const float EndgameAt = 45f;              // endgame: one hider left, one hunter left, or 45 s left (6.6)

    // ---- tagger (5.2) ----
    public const float DartInterval = 0.5f;
    public const float DartRange = 25f;
    public const float DartRayRadius = 0.04f;        // desktop
    public const float DartSpeed = 40f;              // the flying dart is only a picture
    public const int DartDamage = 25;

    // ---- hunter (5.5) ----
    public const int HunterHealth = 100;
    public const int WrongPropPenalty = 10;          // host setting 5 / 10 / 15 (G14, G17)
    public const float WrongPropSameWindow = 5f;     // the same real prop again within 5 s costs nothing
    public const int HitReward = 5;
    public const float HunterWalk = 2.0f;            // VRChat's default walk speed (prototype: Shift runs)
    public const float HunterRun = 4.0f;
    public const float HunterJumpImpulse = 3.0f;     // about 0.45 m
    public const float HunterEye = 1.6f;
    public static readonly Vector3 WalkBody = new Vector3(0.4f, 1.7f, 0.4f);   // the walking body of 7.1
    public const float WalkStep = 0.35f;

    // ---- hider, by disguise class tiny, medium, large (6.1, 3.6.2) ----
    public static readonly int[] HiderHealth = { 50, 100, 150 };
    public static readonly float[] HiderRun = { 3.2f, 3.6f, 2.8f };
    public static readonly float[] HopHeight = { 1.0f, 0.8f, 0f };
    public const float DesktopWalkFactor = 0.55f;    // desktop walks at 55% without Shift
    public const float LedgeAssist = 0.35f;
    public const float LedgeLiftTime = 0.15f;
    public const float Gravity = 9.81f;
    public const float MaxFall = 12f;
    public const float BodySkin = 0.02f;             // the prop box is shrunk by a 2 cm skin
    public const float SettleTime = 0.5f;
    public const float HitSlide = 0.3f;
    public const float HitNoSettle = 1.5f;
    public const float TurnStepDeg = 15f;

    public static float StepHeight(float boxHeight) { return Mathf.Clamp(0.4f * boxHeight, 0.04f, 0.30f); }

    // eye height: 0.9 x the prop's top height, 0.25 m to 2.0 m, in 0.05 m steps (3.6.4)
    public static float EyeHeight(float propHeight) { return Mathf.Clamp(Mathf.Round(0.9f * propHeight / 0.05f) * 0.05f, 0.25f, 2.0f); }

    // ---- disguise (3.8) ----
    public const float TakeRange = 4f;
    public const float HandleRange = 1.5f;           // a hider works a door by aim within 1.5 m of the prop
    public const float HunterHandleRange = 2.5f;     // prototype: a hunter's reach for a handle
    public const int HuntChanges = 3;
    public const float HuntChangeCooldown = 5f;
    public const float ChangePopRange = 8f;
    public const float DoorSwingTime = 0.3f;
    public const float DoorRecloseDelay = 3f;
    public const float DoorOpenAngle = 100f;         // prototype

    // ---- taunts (6.3, 6.4) ----
    public const float TauntCooldown = 8f;
    public const float TauntRange = 20f;
    public const float ForcedTaunt = 40f;
    public const float ForcedTauntSmallGroup = 30f;  // fewer than 6 players (9.4)
    public const float ForcedTauntEndgame = 20f;
    public const float ForcedTauntWarning = 3f;
    public const int SmallGroup = 6;

    // ---- knocks (04 section 4.8; values are the prototype's) ----
    public const float KnockSpeedLight = 5.5f;       // prototype: m/s a dart gives a LIGHT prop (less when heavy)
    public const float KnockSpeed = 3.2f;            // prototype: m/s for a KNOCKABLE prop
    public const float KnockSettleTime = 4f;         // "IsSleeping or 4 s"
    public const int KnockMaxChildren = 6;
    public const int KnockMaxSimulating = 24;

    // ---- bots (prototype) ----
    public static readonly int[] BotHiders = { 3, 4, 5, 6 };     // hunt mode: bot hiders by map size
    public static readonly int[] BotHunters = { 1, 2, 2, 3 };    // hide mode: bot hunters by map size
    public const float BotHunterSight = 9f;          // how far a bot hunter notices a moving prop
    public const float BotHunterGuessInterval = 30f;  // seconds between a bot hunter's idle guesses at still props on patrol
    public const int BotHunterIdleGuessHealth = 60;  // no idle guesses at or below this health
    public const float BotHunterNoiseHit = 0.35f;    // chance that a guess after a taunt picks the hider, if it is in view
    public const float BotHunterAimError = 0.25f;    // metres of aim scatter at 10 m
    public const float BotHiderMoveChance = 0.35f;   // chance that a bot hider relocates after a forced taunt

    // ---- audio ----
    public const float MusicDefault = 0.4f;          // per-player slider, default 40% (11.1); the prototype has no music
    public const float MasterDefault = 0.8f;         // prototype
}
