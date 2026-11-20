using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

// The weapon store and the trophies (Cor, 2026-11-19), all on the local player's own client:
//   coins       earned at the end of every real round (not a debug round, and only with hunters and hiders both
//               playing), spent on the weapon wall's shop weapons (PHShelf)
//   weapons     the catalog of Tools/weapon_catalog.json, baked by the scene builder (StoreBuilder): starters and
//               the 10 free weapons a side (Tools/store_free.json) are free; a new player carries a random free one; cool and goofy ones come from trophies, shop ones are bought. What the player carries is
//               written to their PHSlot (meleeChoice / gunChoice, the melee work's synced picks)
//   trophies    80: 40 hunter ones unlock melee weapons, 40 hider ones guns (Cor: half the weapons; the hardest
//               give the most interesting). Watched from the player's own PHSlot
//               (a light 0.25 s check during the round, 1 s otherwise) and settled when the round ends
// Everything is kept in VRChat's player data (PlayerData: per player, per world), read when it is restored.
// The catalog may only grow at the end: owned weapons are a bit per catalog position.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHStore : UdonSharpBehaviour
{
    public PHGame game;
    public PHAudio sound;
    public PHShelf shelfMelee, shelfGun;
    public PHTrophyBoard board;
    public PHStoreDialog dialog;

    // ---- the catalog (baked)
    public string[] wId;
    public int[] wKind;          // 0 melee, 1 gun
    public int[] wCat;           // CAT_*
    public int[] wPrice;         // shop weapons only
    public int[] wTrophy;        // the trophy that unlocks it, -1 none
    public int[] wGameIdx;       // its index in PHWeapons' melee or gun list (what PHSlot.meleeChoice / gunChoice hold), -1 unknown
    public const int CAT_STARTER = 0, CAT_COOL = 1, CAT_GOOFY = 2, CAT_SHOP = 3;

    // ---- the trophies (baked; texts from Assets/Udon/Text/strings_store.tsv)
    public string[] tKey, tName, tGoal;
    public int[] tLo, tHi;       // a rough range of rounds to earn it
    public int[] tWeapon;        // the catalog weapon it unlocks
    public const int TROPHIES = 80, HUNTER_TROPHIES = 40;

    // ---- the player's own (saved)
    [System.NonSerialized] public int coins;
    [System.NonSerialized] public bool[] own;
    [System.NonSerialized] public bool[] got;
    [System.NonSerialized] public int pickMelee = -1, pickGun = -1;   // catalog index (a random free one until chosen)
    [System.NonSerialized] public int[] st;
    [System.NonSerialized] public bool loaded;
    byte[] defs;   // the props this player has been (a bit per disguise def), for Chameleon

    // the counters in st
    public const int S_CATCH = 0, S_HROUNDS = 1, S_PROUNDS = 2, S_SURV = 3, S_HWINS = 4, S_PWINS = 5, S_WRONG = 6,
        S_EMPTY = 7, S_STREAK = 8, S_FIRST = 9, S_THEME_H = 10, S_THEME_P = 11, S_PROPS = 12, S_HOPS = 13, S_CLEAN = 14, S_TAUNTS = 15, STATS = 24;

    // the trophies by number (strings_store.tsv lists them in this order)
    const int T_FIRST_CATCH = 0, T_HAT_TRICK = 1, T_SHARP_EYE = 2, T_CLEAN_SWEEP = 3, T_QUICK_DRAW = 4, T_BUZZER = 5,
        T_TEN = 6, T_HUNDRED = 7, T_GOOD_AS_NEW = 8, T_WEATHERED = 9, T_PACK_LEADER = 10, T_GLOBE_H = 11, T_OLD_H = 12,
        T_SNAPPED = 13, T_FURNITURE = 14, T_BONK = 15, T_SWING = 16, T_SPLINTERS = 17, T_CLOSE_SHAVE = 18, T_SHOT_DOWN = 19,
        T_EMPTY = 20, T_SCRAPPER = 21,
        T_FIRST_HUNT = 22, T_DOUBLE = 23, T_FIVE = 24, T_TWENTY_FIVE = 25, T_FIFTY = 26, T_TEN_HUNTS = 27,
        T_REG_HUNTER = 28, T_WIN1_H = 29, T_WIN3_H = 30, T_TOP_DOG = 31, T_TOWN_H = 32, T_VILLAGE_H = 33, T_STATION_H = 34,
        T_FIFTY_BONKS = 35, T_STEADY = 36, T_LAST_STAND = 37, T_QUICK_PAIR = 38, T_THREE_CATCHES = 39,
        T_SURVIVOR = 40, T_UNTOUCHED = 41, T_LAST_PROP = 42, T_TEN_SURV = 43, T_FIFTY_SURV = 44, T_ON_A_ROLL = 45,
        T_FIRST_SHOT = 46, T_SHARPSHOOTER = 47, T_GIANT_SLAYER = 48, T_PROPS_WIN = 49, T_GLOBE_P = 50, T_OLD_P = 51,
        T_LONG_HAUL = 52, T_FOUND = 53, T_CLOSE_CALL = 54, T_QUICK_CHANGE = 55, T_BOUNCY = 56, T_CHAMELEON = 57,
        T_SPRAY = 58, T_FULL_HOUSE = 59, T_USUAL = 60, T_WALLFLOWER = 61,
        T_FIRST_HIDE = 62, T_THREE_SURV = 63, T_TWENTY_FIVE_SURV = 64, T_TEN_HIDES = 65, T_REG_HIDER = 66, T_WIN1_P = 67,
        T_WIN3_P = 68, T_ROYALTY = 69, T_MINUTE = 70, T_TWO_MINUTES = 71, T_TOWN_P = 72, T_VILLAGE_P = 73, T_STATION_P = 74,
        T_TEN_PROPS = 75, T_HOPPY = 76, T_BACK_TO_BACK = 77, T_STAND = 78, T_TAUNTS = 79;

    // coins (Cor approved): a round, a catch, surviving, every 20 s hidden before being caught, winning
    public const int COIN_ROUND = 10, COIN_CATCH = 15, COIN_SURVIVE = 25, COIN_WIN = 10, COIN_HIDDEN_MS = 20000;

    // ---- this round, as watched
    int lastPhase = -1;
    bool inRound;
    int rRole, rCatches, rHiders, rPeople, rTheme, rSize;
    bool rQuick, rBuzzer, rShave, rSplinter, rAlone, rMoved, rStand, rStandCatch, rPair, rStandHide;
    int rBonus;   // the round's map bonus (PHGame.roundBonus): 1 = 1.5x, 2 = 2x
    int rEarly;
    Vector3 rStart;
    bool rStartSet;
    int lastDef = -1;
    // the melee work's counters (PHLocal, PHHiderBody: per session, never reset), read by name so this compiles
    // without that work; a missing one reads -1 and its trophy waits. Kept as they were at the hunt's start.
    int c0Swings, c0Shots, c0ShotHits, c0Broke, c0Changes, c0MyBroken, lastHops = -1;

    void Start()
    {
        Init();
        SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
    }

    // (also from OnPlayerRestored and the shelves, whichever comes first)
    public void Init()
    {
        if (own != null) return;
        own = new bool[wId != null ? wId.Length : 0];
        got = new bool[TROPHIES];
        st = new int[STATS];
        defs = new byte[128];
        GiveStarters();
        // until the save is read (and for a new player): a random free weapon a side (Cor), never a theme's default
        pickMelee = RandomFree(0); pickGun = RandomFree(1);
    }

    // a random free weapon of a kind (melee 0, gun 1)
    int RandomFree(int kind)
    {
        int n = 0;
        for (int i = 0; i < wId.Length; i++) if (wKind[i] == kind && wCat[i] == CAT_STARTER) n++;
        if (n == 0) return -1;
        int k = Random.Range(0, n);
        for (int i = 0; i < wId.Length; i++) if (wKind[i] == kind && wCat[i] == CAT_STARTER) { if (k == 0) return i; k--; }
        return -1;
    }

    void GiveStarters() { for (int i = 0; i < own.Length; i++) if (wCat[i] == CAT_STARTER) own[i] = true; }

    // ------------------------------------------------------------------ saving
    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        Init();
        int v;
        if (PlayerData.TryGetInt(player, "ph.coins", out v)) coins = v;
        byte[] b;
        if (PlayerData.TryGetBytes(player, "ph.own", out b)) for (int i = 0; i < own.Length; i++) own[i] = own[i] || GetBit(b, i);
        if (PlayerData.TryGetBytes(player, "ph.tro", out b)) for (int i = 0; i < TROPHIES; i++) got[i] = GetBit(b, i);
        if (PlayerData.TryGetBytes(player, "ph.st", out b)) for (int i = 0; i < STATS && i * 4 + 3 < b.Length; i++)
            st[i] = b[i * 4] | (b[i * 4 + 1] << 8) | (b[i * 4 + 2] << 16) | (b[i * 4 + 3] << 24);
        if (PlayerData.TryGetBytes(player, "ph.defs", out b)) for (int i = 0; i < defs.Length && i < b.Length; i++) defs[i] = b[i];
        string s;
        // a saved pick wins; a first visit keeps the random free one from Init
        if (PlayerData.TryGetString(player, "ph.melee", out s) && Find(s) >= 0) pickMelee = Find(s);
        if (PlayerData.TryGetString(player, "ph.gun", out s) && Find(s) >= 0) pickGun = Find(s);
        // a trophy earned before its weapon was in the catalog, or a weapon lost: what a trophy gives is always owned
        for (int t = 0; t < TROPHIES; t++) if (got[t] && tWeapon[t] >= 0) own[tWeapon[t]] = true;
        if (pickMelee < 0 || !own[pickMelee] || wKind[pickMelee] != 0) pickMelee = RandomFree(0);
        if (pickGun < 0 || !own[pickGun] || wKind[pickGun] != 1) pickGun = RandomFree(1);
        loaded = true;
        Save();   // (a first visit's random picks stay theirs)
        ApplyPicks();
        RefreshAll();
    }

    void Save()
    {
        if (!loaded) return;   // never over a save not read yet
        PlayerData.SetInt("ph.coins", coins);
        PlayerData.SetBytes("ph.own", Bits(own));
        PlayerData.SetBytes("ph.tro", Bits(got));
        byte[] b = new byte[STATS * 4];
        for (int i = 0; i < STATS; i++) { int x = st[i]; b[i * 4] = (byte)(x & 255); b[i * 4 + 1] = (byte)((x >> 8) & 255); b[i * 4 + 2] = (byte)((x >> 16) & 255); b[i * 4 + 3] = (byte)((x >> 24) & 255); }
        PlayerData.SetBytes("ph.st", b);
        PlayerData.SetBytes("ph.defs", defs);
        PlayerData.SetString("ph.melee", pickMelee >= 0 ? wId[pickMelee] : "");
        PlayerData.SetString("ph.gun", pickGun >= 0 ? wId[pickGun] : "");
    }

    byte[] Bits(bool[] a)
    {
        byte[] b = new byte[(a.Length + 7) / 8];
        for (int i = 0; i < a.Length; i++) if (a[i]) b[i >> 3] |= (byte)(1 << (i & 7));
        return b;
    }

    bool GetBit(byte[] b, int i) { return b != null && (i >> 3) < b.Length && (b[i >> 3] & (1 << (i & 7))) != 0; }

    int Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < wId.Length; i++) if (wId[i] == id) return i;
        return -1;
    }

    // ------------------------------------------------------------------ the wall's clicks (PHShelf, PHStoreDialog)
    public void Clicked(int c, Vector3 at, Quaternion facing)
    {
        if (c < 0 || c >= wId.Length) return;
        if (own[c]) { Pick(c); return; }
        if (dialog == null) return;
        if (wTrophy[c] >= 0) dialog.ShowTrophy(c, at, facing);
        else dialog.ShowBuy(c, at, facing);
        Play("click_001", 0.6f);
    }

    public void Pick(int c)
    {
        if (!own[c]) return;
        if (wKind[c] == 0) pickMelee = c; else pickGun = c;
        Play("select_001", 0.7f);
        ApplyPicks();
        Save();
        RefreshShelves();
    }

    public bool Buy(int c)
    {
        if (c < 0 || own[c] || wCat[c] != CAT_SHOP || wTrophy[c] >= 0 || coins < wPrice[c] || !loaded) { Play("error_004", 0.5f); return false; }
        coins -= wPrice[c];
        own[c] = true;
        Play("jingles_PIZZI07", 0.7f);
        Pick(c);   // carry it at once (saves)
        return true;
    }

    // the picks onto the player's slot (the melee work's synced meleeChoice / gunChoice), by name so this compiles
    // without that work; a slot without them is left alone
    void ApplyPicks()
    {
        PHSlot s = game != null ? game.LocalSlot() : null;
        if (s == null) return;
        SetChoice(s, "meleeChoice", pickMelee >= 0 ? wGameIdx[pickMelee] : -1);
        SetChoice(s, "gunChoice", pickGun >= 0 ? wGameIdx[pickGun] : -1);
    }

    void SetChoice(PHSlot s, string field, int v)
    {
        object o = s.GetProgramVariable(field);
        if (o == null) return;
        if ((int)o == v) return;
        s.SetProgramVariable(field, v);
        s.Send();
    }

    // ------------------------------------------------------------------ watching the round
    public void _Tick()
    {
        int phase = game != null ? game.phase : -1;
        bool live = phase == PHGame.PH_HIDE || phase == PHGame.PH_HUNT;
        SendCustomEventDelayedSeconds(nameof(_Tick), live ? 0.25f : 1f);
        PHSlot me = game != null ? game.LocalSlot() : null;
        if (me == null) { lastPhase = phase; return; }
        if (loaded) ApplyPicks();

        // Scrapper: a scrap vote cast (any time in the lobby)
        if (loaded && !got[T_SCRAPPER] && me.scrapVote != 0 && me.scrapVote == game.voteId) Earn(T_SCRAPPER);

        // the props this player has been (Chameleon)
        if (live && me.def >= 0 && me.def != lastDef)
        {
            lastDef = me.def;
            if ((me.def >> 3) < defs.Length && (defs[me.def >> 3] & (1 << (me.def & 7))) == 0)
            {
                defs[me.def >> 3] |= (byte)(1 << (me.def & 7));
                st[S_PROPS]++;
                if (!got[T_TEN_PROPS] && st[S_PROPS] >= 10) Earn(T_TEN_PROPS);
                if (!got[T_CHAMELEON] && st[S_PROPS] >= 25) Earn(T_CHAMELEON); else Save();
            }
        }

        // Bouncy: hops as a prop, added up across rounds
        if (live && game.local != null)
        {
            int hops = Counter(game.local.body, "hops");
            if (hops >= 0)
            {
                if (lastHops >= 0 && hops > lastHops)
                {
                    st[S_HOPS] += hops - lastHops;
                    if (!got[T_HOPPY] && st[S_HOPS] >= 50) Earn(T_HOPPY);
                    if (!got[T_BOUNCY] && st[S_HOPS] >= 200) Earn(T_BOUNCY);
                }
                lastHops = hops;
            }
        }

        if (phase == PHGame.PH_HUNT && lastPhase != PHGame.PH_HUNT) BeginRound(me);
        if (phase == PHGame.PH_HUNT && inRound) Watch(me);
        if (lastPhase == PHGame.PH_HUNT && phase != PHGame.PH_HUNT && inRound)
        {
            inRound = false;
            if (phase == PHGame.PH_END) EndRound(me);   // (a hunt that was scrapped or reset pays nothing)
        }
        lastPhase = phase;
    }

    void BeginRound(PHSlot me)
    {
        int idx = game.SlotOf(Networking.LocalPlayer.playerId);
        rRole = idx >= 0 ? game.slotRole[idx] : PHGame.ROLE_NONE;
        rHiders = 0; int hunters = 0; rPeople = 0;
        for (int i = 0; i < game.slotRole.Length; i++)
        {
            if (game.slotRole[i] == PHGame.ROLE_HIDER) rHiders++;
            else if (game.slotRole[i] == PHGame.ROLE_HUNTER) hunters++;
            if (game.slotPlayer[i] != 0) rPeople++;
        }
        // a real round: not the debug buttons', and both sides playing (one person hiding alone earns nothing)
        inRound = rRole != PHGame.ROLE_NONE && !game.debugRound && rHiders > 0 && hunters > 0;
        rCatches = me.catches;
        rTheme = game.gen != null ? game.gen.mapTheme : 0;
        rSize = game.mapSize;
        rBonus = game.roundBonus;   // (a map played once or twice, or one on offer: 1.5x; a new map on offer: 2x)
        if (rBonus > 0 && inRound && game.local != null)
        {
            game.local.SetProgramVariable("notice", game.text.Get(rBonus == 2 ? "newmap.banner" : "replay.banner"));
            game.local.SetProgramVariable("noticeUntil", Time.time + 10f);
        }
        rQuick = false; rBuzzer = false; rShave = false; rSplinter = false; rAlone = false; rMoved = false; rStand = false;
        rStandCatch = false; rPair = false; rStandHide = false; rEarly = 0;
        rStartSet = false;
        c0Swings = Counter(game.local, "statSwings"); c0Shots = Counter(game.local, "statShots");
        c0ShotHits = Counter(game.local, "statShotHits"); c0Broke = Counter(game.local, "statWeaponsBroken");
        c0Changes = Counter(game.local, "statPropChanges"); c0MyBroken = Counter(game.local, "myWeaponBroken");
    }

    int Counter(UdonSharpBehaviour b, string field)
    {
        if (b == null) return -1;
        object o = b.GetProgramVariable(field);
        if (o == null) return -1;
        return (int)o;
    }

    // how much a counter grew since the hunt began (-1: the counter isn't there)
    int Grew(UdonSharpBehaviour b, string field, int at0)
    {
        int now = Counter(b, field);
        return now < 0 || at0 < 0 ? -1 : now - at0;
    }

    void Watch(PHSlot me)
    {
        float into = (game.Now - game.huntStartMs) / 1000f;
        float left = game.Left;
        if (rRole == PHGame.ROLE_HUNTER)
        {
            if (me.catches > rCatches)
            {
                rCatches = me.catches;
                if (into <= 15f) rQuick = true;
                if (left <= 10f) rBuzzer = true;
                if (me.hunterHealth <= 10) rShave = true;
                if (game.Endgame) rStandCatch = true;
                if (into <= 60f) { rEarly++; if (rEarly >= 2) rPair = true; }
            }
            if (me.hunterOut && into <= 30f) rSplinter = true;
            if (game.Endgame && !me.hunterOut) rStand = true;
        }
        else
        {
            if (!rStartSet) { rStart = me.propPos; rStartSet = true; }
            if (game.Endgame && !me.caught) rStandHide = true;
            else if (!me.caught && (me.propPos - rStart).sqrMagnitude > 0.36f) rMoved = true;
            if (!me.caught && rHiders >= 2)
            {
                int left2 = 0;
                for (int i = 0; i < game.slots.Length; i++)
                    if (game.slotRole[i] == PHGame.ROLE_HIDER && game.slotPlayer[i] != 0 && !game.slots[i].caught) left2++;
                if (left2 == 1) rAlone = true;
            }
        }
    }

    void EndRound(PHSlot me)
    {
        bool hunter = rRole == PHGame.ROLE_HUNTER;
        bool won = hunter != game.hidersWin;
        int pay = COIN_ROUND + (won ? COIN_WIN : 0);
        PHText tx = game.text;
        string parts = tx.F1("pay.round", COIN_ROUND.ToString());
        if (won) parts += "\n" + tx.F1("pay.win", COIN_WIN.ToString());
        int themeBit = 1 << Mathf.Clamp(rTheme, 0, 2);
        if (hunter)
        {
            int catches = me.catches, wrong = me.wrongProps;
            pay += COIN_CATCH * catches;
            if (catches > 0) parts += "\n" + tx.F2("pay.catch", catches.ToString(), (COIN_CATCH * catches).ToString());
            st[S_HROUNDS]++; st[S_CATCH] += catches; st[S_WRONG] += wrong;
            if (won) st[S_HWINS]++;
            if (catches == 0) st[S_EMPTY]++;
            if (catches > 0) st[S_THEME_H] |= themeBit;
            if (catches >= 1) Earn(T_FIRST_CATCH);
            if (catches >= 3) Earn(T_HAT_TRICK);
            if (catches >= 3 && wrong == 0) Earn(T_SHARP_EYE);
            if (rHiders >= 3 && catches >= rHiders) Earn(T_CLEAN_SWEEP);
            if (rQuick) Earn(T_QUICK_DRAW);
            if (rBuzzer && !game.hidersWin) Earn(T_BUZZER);
            if (wrong == 0 && !me.hunterOut) Earn(T_GOOD_AS_NEW);
            if (rStand && !me.hunterOut) Earn(T_WEATHERED);
            if (me.hunterOut) Earn(T_SNAPPED);
            if (wrong >= 9 && !me.hunterOut) Earn(T_FURNITURE);
            if (rSplinter) Earn(T_SPLINTERS);
            if (rShave) Earn(T_CLOSE_SHAVE);
            if (wrong == 0 && !me.hunterOut) st[S_CLEAN]++;
            Earn(T_FIRST_HUNT);
            if (catches >= 2) Earn(T_DOUBLE);
            if (won) Earn(T_WIN1_H);
            if (catches > 0 && rTheme == 0) Earn(T_TOWN_H);
            if (catches > 0 && rTheme == 2) Earn(T_VILLAGE_H);
            if (catches > 0 && rTheme == 1) Earn(T_STATION_H);
            if (rStandCatch) Earn(T_LAST_STAND);
            if (rPair) Earn(T_QUICK_PAIR);
            if (Grew(game.local, "statSwings", c0Swings) >= 100) Earn(T_SWING);
            if (Grew(game.local, "myWeaponBroken", c0MyBroken) > 0 && Counter(game.local, "lastBrokenBy") > 0) Earn(T_SHOT_DOWN);
        }
        else
        {
            bool survived = !me.caught;
            if (survived) { pay += COIN_SURVIVE; parts += "\n" + tx.F1("pay.survive", COIN_SURVIVE.ToString()); }
            else
            {
                int hid = Mathf.Max(0, me.survivedMs) / COIN_HIDDEN_MS;
                pay += hid;
                if (hid > 0) parts += "\n" + tx.F1("pay.hidden", hid.ToString());
            }
            st[S_PROUNDS]++;
            if (won) st[S_PWINS]++;
            if (survived) { st[S_SURV]++; st[S_STREAK]++; st[S_THEME_P] |= themeBit; } else st[S_STREAK] = 0;
            // the first hider caught: the one caught soonest after the release
            if (!survived && rHiders >= 2)
            {
                bool first = true;
                for (int i = 0; i < game.slots.Length; i++)
                {
                    PHSlot o = game.slots[i];
                    if (o == me || game.slotRole[i] != PHGame.ROLE_HIDER || !o.caught) continue;
                    if (o.survivedMs < me.survivedMs) { first = false; break; }
                }
                if (first) st[S_FIRST]++;
            }
            if (survived) Earn(T_SURVIVOR);
            if (survived && me.health >= me.maxHealth && me.maxHealth > 0) Earn(T_UNTOUCHED);
            if (survived && rAlone) Earn(T_LAST_PROP);
            if (survived && rSize >= 2) Earn(T_LONG_HAUL);
            if (!survived && me.survivedMs <= 10000) Earn(T_FOUND);
            if (survived && me.health > 0 && me.health <= 25) Earn(T_CLOSE_CALL);
            if (survived && !rMoved && rStartSet) Earn(T_WALLFLOWER);
            Earn(T_FIRST_HIDE);
            if (won) Earn(T_WIN1_P);
            if (survived || me.survivedMs >= 60000) Earn(T_MINUTE);
            if ((survived && game.HuntLength >= 120f) || me.survivedMs >= 120000) Earn(T_TWO_MINUTES);
            if (survived && rTheme == 0) Earn(T_TOWN_P);
            if (survived && rTheme == 2) Earn(T_VILLAGE_P);
            if (survived && rTheme == 1) Earn(T_STATION_P);
            if (rStandHide) Earn(T_STAND);
            int hitsNow = Grew(game.local, "statShotHits", c0ShotHits);
            if (hitsNow >= 1) Earn(T_FIRST_SHOT);
            if (hitsNow >= 10) Earn(T_SHARPSHOOTER);
            if (Grew(game.local, "statWeaponsBroken", c0Broke) >= 1) Earn(T_GIANT_SLAYER);
            if (Grew(game.local, "statShots", c0Shots) >= 20) Earn(T_SPRAY);
            if (survived && Grew(game.local, "statPropChanges", c0Changes) >= 3) Earn(T_QUICK_CHANGE);
        }
        if (rPeople >= 10) Earn(T_FULL_HOUSE);
        CheckCounts();
        // the map's bonus (Cor): 1.5x on a map played once or twice or one on offer, 2x on a new map on offer
        if (rBonus == 2) { pay *= 2; parts += "\n" + tx.Get("pay.newmap"); }
        else if (rBonus == 1) { pay = (pay * 3 + 1) / 2; parts += "\n" + tx.Get("pay.replay"); }
        coins += pay;
        // what earned them, under the round's results for a while (Cor's playtest: feedback on what got you points)
        if (game.local != null)
        {
            game.local.SetProgramVariable("notice", tx.F1("store.coins_earned", pay.ToString()) + "\n" + parts);
            game.local.SetProgramVariable("noticeUntil", Time.time + 20f);
        }
        Play("jingles_PIZZI00", 0.5f);
        Save();
        RefreshAll();
    }

    // a taunt from this player's own prop (PHSlot.PlayTaunt, on its owner): counted for Showboat (Cor's playtest)
    public void Taunted()
    {
        if (!loaded) return;
        st[S_TAUNTS]++;
        if (st[S_TAUNTS] >= 100) Earn(T_TAUNTS);
        else if (st[S_TAUNTS] % 10 == 0) Save();
    }

    // the trophies that count up across rounds
    void CheckCounts()
    {
        if (st[S_CATCH] >= 10) Earn(T_TEN);
        if (st[S_CATCH] >= 100) Earn(T_HUNDRED);
        if (st[S_HWINS] >= 10) Earn(T_PACK_LEADER);
        if (st[S_THEME_H] == 7) Earn(T_GLOBE_H);
        if (st[S_HROUNDS] >= 50) Earn(T_OLD_H);
        if (st[S_WRONG] >= 250) Earn(T_BONK);
        if (st[S_EMPTY] >= 10) Earn(T_EMPTY);
        if (st[S_SURV] >= 10) Earn(T_TEN_SURV);
        if (st[S_SURV] >= 50) Earn(T_FIFTY_SURV);
        if (st[S_STREAK] >= 5) Earn(T_ON_A_ROLL);
        if (st[S_PWINS] >= 10) Earn(T_PROPS_WIN);
        if (st[S_THEME_P] == 7) Earn(T_GLOBE_P);
        if (st[S_PROUNDS] >= 50) Earn(T_OLD_P);
        if (st[S_PROPS] >= 25) Earn(T_CHAMELEON);
        if (st[S_FIRST] >= 5) Earn(T_USUAL);
        if (st[S_CATCH] >= 5) Earn(T_FIVE);
        if (st[S_CATCH] >= 25) Earn(T_TWENTY_FIVE);
        if (st[S_CATCH] >= 50) Earn(T_FIFTY);
        if (st[S_HROUNDS] >= 10) Earn(T_TEN_HUNTS);
        if (st[S_HROUNDS] >= 25) Earn(T_REG_HUNTER);
        if (st[S_HWINS] >= 3) Earn(T_WIN3_H);
        if (st[S_HWINS] >= 25) Earn(T_TOP_DOG);
        if (st[S_WRONG] >= 50) Earn(T_FIFTY_BONKS);
        if (st[S_CLEAN] >= 5) Earn(T_STEADY);
        if (st[S_SURV] >= 3) Earn(T_THREE_SURV);
        if (st[S_SURV] >= 25) Earn(T_TWENTY_FIVE_SURV);
        if (st[S_PROUNDS] >= 10) Earn(T_TEN_HIDES);
        if (st[S_PROUNDS] >= 25) Earn(T_REG_HIDER);
        if (st[S_PWINS] >= 3) Earn(T_WIN3_P);
        if (st[S_PWINS] >= 25) Earn(T_ROYALTY);
        if (st[S_PROPS] >= 10) Earn(T_TEN_PROPS);
        if (st[S_STREAK] >= 2) Earn(T_BACK_TO_BACK);
        if (st[S_HOPS] >= 50) Earn(T_HOPPY);
        if (st[S_CATCH] >= 3) Earn(T_THREE_CATCHES);
        if (st[S_TAUNTS] >= 100) Earn(T_TAUNTS);
    }

    // how far along a counted trophy is: x of n (n 0 for one done in one go)
    public int ProgressOf(int t)
    {
        switch (t)
        {
            case T_TEN: case T_HUNDRED: case T_FIVE: case T_TWENTY_FIVE: case T_FIFTY: case T_THREE_CATCHES: return st[S_CATCH];
            case T_PACK_LEADER: case T_WIN3_H: case T_TOP_DOG: return st[S_HWINS];
            case T_TEN_HUNTS: case T_REG_HUNTER: return st[S_HROUNDS];
            case T_FIFTY_BONKS: return st[S_WRONG];
            case T_STEADY: return st[S_CLEAN];
            case T_THREE_SURV: case T_TWENTY_FIVE_SURV: return st[S_SURV];
            case T_TAUNTS: return st[S_TAUNTS];
            case T_TEN_HIDES: case T_REG_HIDER: return st[S_PROUNDS];
            case T_WIN3_P: case T_ROYALTY: return st[S_PWINS];
            case T_TEN_PROPS: return st[S_PROPS];
            case T_BACK_TO_BACK: return st[S_STREAK];
            case T_HOPPY: return st[S_HOPS];
            case T_GLOBE_H: return Count3(st[S_THEME_H]);
            case T_OLD_H: return st[S_HROUNDS];
            case T_BONK: return st[S_WRONG];
            case T_EMPTY: return st[S_EMPTY];
            case T_TEN_SURV: case T_FIFTY_SURV: return st[S_SURV];
            case T_ON_A_ROLL: return st[S_STREAK];
            case T_PROPS_WIN: return st[S_PWINS];
            case T_GLOBE_P: return Count3(st[S_THEME_P]);
            case T_OLD_P: return st[S_PROUNDS];
            case T_CHAMELEON: return st[S_PROPS];
            case T_USUAL: return st[S_FIRST];
            case T_BOUNCY: return st[S_HOPS];
        }
        return 0;
    }

    public int TargetOf(int t)
    {
        switch (t)
        {
            case T_TEN: return 10; case T_HUNDRED: return 100; case T_PACK_LEADER: return 10;
            case T_GLOBE_H: return 3; case T_OLD_H: return 50; case T_BONK: return 250; case T_EMPTY: return 10;
            case T_TEN_SURV: return 10; case T_FIFTY_SURV: return 50; case T_ON_A_ROLL: return 5; case T_PROPS_WIN: return 10;
            case T_GLOBE_P: return 3; case T_OLD_P: return 50; case T_CHAMELEON: return 25; case T_USUAL: return 5; case T_BOUNCY: return 200;
            case T_FIVE: return 5; case T_TWENTY_FIVE: return 25; case T_FIFTY: return 50; case T_TEN_HUNTS: return 10;
            case T_REG_HUNTER: return 25; case T_WIN3_H: return 3; case T_TOP_DOG: return 25; case T_FIFTY_BONKS: return 50;
            case T_STEADY: return 5; case T_THREE_SURV: return 3; case T_TWENTY_FIVE_SURV: return 25; case T_TEN_HIDES: return 10;
            case T_REG_HIDER: return 25; case T_WIN3_P: return 3; case T_ROYALTY: return 25; case T_TEN_PROPS: return 10;
            case T_BACK_TO_BACK: return 2; case T_HOPPY: return 50; case T_THREE_CATCHES: return 3; case T_TAUNTS: return 100;
        }
        return 0;
    }

    int Count3(int bits) { return (bits & 1) + ((bits >> 1) & 1) + ((bits >> 2) & 1); }

    void Earn(int t)
    {
        if (t < 0 || t >= TROPHIES || got[t] || !loaded) return;
        got[t] = true;
        if (tWeapon[t] >= 0) own[tWeapon[t]] = true;
        Play("jingles_STEEL04", 0.8f);
        Save();
        RefreshAll();
    }

    // ------------------------------------------------------------------ showing it
    void RefreshAll() { RefreshShelves(); if (board != null) board.Refresh(); }

    void RefreshShelves()
    {
        if (shelfMelee != null) shelfMelee.Refresh();
        if (shelfGun != null) shelfGun.Refresh();
    }

    public bool Picked(int c) { return c >= 0 && (c == pickMelee || c == pickGun); }

    void Play(string n, float v) { if (sound != null) sound.Play(n, v); }
}
