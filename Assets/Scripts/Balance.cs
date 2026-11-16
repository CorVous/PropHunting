using UnityEngine;

// Every gameplay number in one place. Values come from the balance table of 05-gameplay-design.md
// (section 9.1, revision 5) unless marked "prototype": those are this prototype's own choices.
public static class Balance
{
    // ---- phases, by map size S, M, L, XL (9.1) ----
    public static readonly float[] HidePhase = { 35f, 40f, 45f, 50f };
    public static readonly float[] HuntPhase = { 180f, 210f, 240f, 270f };
    // the host's round length preset (G14, G44): the hunt phase above times the factor; the hide phase is unchanged
    public static readonly string[] RoundLengthName = { "Short", "Normal", "Long" };
    public static readonly float[] RoundLengthFactor = { 0.75f, 1f, 1.5f };
    public const int RunCapDefault = 10;             // runs per map before it leaves the rotation (Q4, C1); not active here
    public const int MaxPlayers = 32;
    // Auto size (G44, D5): S up to 8 players, M up to 16, L up to 24, XL up to 32
    public static int SizeFor(int players) { return players <= 8 ? 0 : (players <= 16 ? 1 : (players <= 24 ? 2 : 3)); }
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
    public const int WrongPropPenalty = 10;          // the host's default; the host panel sets 5 / 10 / 15 (G14, G17, G44)
    public static readonly int[] WrongPropChoices = { 5, 10, 15 };
    public const float WrongPropSameWindow = 5f;     // the same real prop again within 5 s costs nothing
    public const int HitReward = 5;
    public const float HunterWalk = 2.0f;            // VRChat's default walk speed (prototype: Shift runs)
    public const float HunterRun = 4.0f;
    public const float HunterJumpImpulse = 3.0f;     // about 0.45 m
    public const float HunterEye = 1.6f;
    public static readonly Vector3 WalkBody = new Vector3(0.4f, 1.7f, 0.4f);   // the walking body of 7.1
    public const float WalkStep = 0.35f;

    // ---- hider, by disguise class tiny, medium, large (6.1, 3.6.2) ----
    // tiny, medium, large, and huge (G46: over 2.5 m, trees): huge is slow, cannot hop and takes the most darts
    public static readonly int[] HiderHealth = { 50, 100, 150, 200 };
    public static readonly float[] HiderRun = { 3.2f, 3.6f, 2.8f, 2.2f };
    public static readonly float[] HopHeight = { 1.0f, 0.8f, 0f, 0f };
    public static readonly string[] ClassName = { "tiny", "medium", "large", "huge" };
    public const float DesktopWalkFactor = 0.55f;    // desktop walks at 55% without Shift
    public const float LedgeAssist = 0.35f;
    public const float LedgeLiftTime = 0.15f;
    public const float Gravity = 9.81f;
    public const float MaxFall = 12f;
    public const float BodySkin = 0.02f;             // the prop box is shrunk by a 2 cm skin
    public const float SettleTime = 0.5f;
    public const float HitSlide = 0.3f;
    public const float HitNoSettle = 1.5f;

    public static float StepHeight(float boxHeight) { return Mathf.Clamp(0.4f * boxHeight, 0.04f, 0.30f); }

    // eye height: 0.9 x the prop's top height, 0.25 m to 2.0 m, in 0.05 m steps (3.6.4)
    // 0.9 of the prop's height (G5), 0.25 to 2.0 m; a prop taller than 2.5 m (a tree, G46) puts the eye at a standing
    // person's 1.6 m, under its crown, where the view is clear and a VR player stands as they are
    public static float EyeHeight(float propHeight) { return propHeight > 2.5f ? HunterEye : Mathf.Clamp(Mathf.Round(0.9f * propHeight / 0.05f) * 0.05f, 0.25f, 2.0f); }

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
    public const float ForcedTaunt = 40f;            // the host's default; the host panel sets 30 / 40 / 60 s (G14, G44)
    public static readonly int[] ForcedTauntChoices = { 30, 40, 60 };
    // fewer than 6 players (9.4): three quarters of the host's interval; the endgame: half of it (Round.cs)
    public const float ForcedTauntWarning = 3f;
    public const float ForcedTauntMoved = 4f;        // G47: a hider this far (straight line) from where they last taunted skips the forced taunt
    public const int SmallGroup = 6;

    // ---- knocks (04 section 4.8; values are the prototype's) ----
    public const float KnockSpeedLight = 5.5f;       // prototype: m/s a dart gives a LIGHT prop (less when heavy)
    public const float KnockSpeed = 3.2f;            // prototype: m/s for a KNOCKABLE prop
    public const float KnockSettleTime = 4f;         // "IsSleeping or 4 s"
    public const int KnockMaxChildren = 6;
    public const int KnockMaxSimulating = 24;

    // ---- bots (prototype) ----
    // A run is one round (G34). The players in the lobby's zone take part; about 1 in 4 of them hunt (at least 1),
    // drawn at random each round, and everyone else hides. In the prototype bots stand in for the other players.
    public static readonly int[] LobbyBotsInZone = { 5, 7, 9, 11 };   // bots in the zone by map size: 6, 8, 10, 12 taking part with the player
    public const int LobbyWatchers = 3;                               // bots in the lobby outside the zone (they do not play)
    public static int Hunters(int participants) { int h = (participants + 2) / 4; return h < 1 ? 1 : h; }   // 1 in 4, rounded, at least 1
    public const float BotHunterSight = 9f;          // how far a bot hunter notices a moving prop
    public const float BotHunterGuessInterval = 30f;  // seconds between a bot hunter's idle guesses at still props on patrol
    public const int BotHunterIdleGuessHealth = 60;  // no idle guesses at or below this health
    public const float BotHunterFollowUp = 1.8f;     // seconds before a bot hunter's next dart after it landed a hit
    public const float BotHunterMoverScatter = 3f;   // aim scatter multiplier when shooting at a moving prop
    public const float BotHunterNoiseHit = 0.35f;    // chance that a guess after a taunt picks the hider, if it is in view
    public const float BotHunterAimError = 0.25f;    // metres of aim scatter at 10 m
    public const float BotHiderMoveChance = 0.35f;   // chance that a bot hider relocates after a forced taunt

    // ---- audio ----
    public const float MusicDefault = 0.4f;          // per-player slider, default 40% (11.1); the prototype has no music
    public const float MasterDefault = 0.8f;         // prototype
}
