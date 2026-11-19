using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Data;
using VRC.SDK3.Persistence;

// The world's text in the local player's language (Cor: "full localization options", 2026-11-19).
//
// The strings live in Assets/Udon/Text/*.tsv (one file per area; columns key, en, then VRChat's language codes: ja, ko,
// zh-CN, zh-HK, es, fr, de, pt-BR, ru, it, pl, id, nl, sv, uk, da, no, fi, hu, cs, tr, ro, vi). "PropHunt/Bake text table" (TextBaker.cs) reads them into the arrays below, fills every empty cell
// with the English, finds the static labels in the scene (a TMP text whose whole text equals some row's English)
// and gives the Kenney fonts a fallback that holds the CJK and Cyrillic glyphs used by the table.
//
// Language: the one the player picked on the lobby's language board (saved in PlayerData), else VRChat's own
// client language when it is one of ours, else English. Purely local: nothing here is synced.
//
// Use from any script with a PHGame: game.text.Get("key"), game.text.F1("key", a) (fills {0}), F2, F3,
// game.text.Prop(def). Text set at runtime that must follow a language switch: game.text.Register(this) once,
// and a public void _OnTextLanguage() that sets it again.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHText : UdonSharpBehaviour
{
    public const string SAVE_KEY = "ph.lang";      // PlayerData int: picked language + 1 (0 or missing = follow VRChat)

    // ---- baked by TextBaker
    public string[] langCode;          // "en", "ja", ... (VRChat's codes)
    public string[] langName;          // each language's name in itself ("日本語")
    public string[] keys;              // n keys
    public string[] table;             // n * nLang, row-major: table[k * nLang + lang]; empty cells hold the English
    public string[] propNames;         // nDefs * nLang, the readable prop names (PHLocal.defNames in English)
    public TMP_Text[] labels;          // static labels baked in English ...
    public int[] labelKey;             // ... and the row each one shows

    // ---- state
    [System.NonSerialized] public int lang;            // index into langCode
    [System.NonSerialized] public bool picked;         // the player chose it on the board (else it follows VRChat)
    int nLang = 1;
    DataDictionary index, enIndex;
    UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[0];
    bool started;

    void Start()
    {
        nLang = langCode != null && langCode.Length > 0 ? langCode.Length : 1;
        index = new DataDictionary();
        if (keys != null) for (int k = 0; k < keys.Length; k++) index.SetValue(keys[k], k);
        started = true;
        int v = FromVRChat(VRCPlayerApi.GetCurrentLanguage());
        if (!picked) lang = v;
        Apply();
    }

    // ------------------------------------------------------------------ lookups
    public string Get(string key)
    {
        if (index == null) return key;
        DataToken t;
        if (!index.TryGetValue(key, out t)) return key;
        int i = t.Int * nLang + lang;
        return i < table.Length ? table[i] : key;
    }

    public string F1(string key, string a) { return Get(key).Replace("{0}", a); }
    public string F2(string key, string a, string b) { return Get(key).Replace("{0}", a).Replace("{1}", b); }
    public string F3(string key, string a, string b, string c) { return Get(key).Replace("{0}", a).Replace("{1}", b).Replace("{2}", c); }

    // a prop's readable name ("Chair cushion"); its lower-case form is for the middle of a sentence in English
    public string Prop(int def)
    {
        if (propNames == null || def < 0) return "";
        int i = def * nLang + lang;
        if (i >= propNames.Length) { i = def * nLang; if (i >= propNames.Length) return ""; }
        return propNames[i];
    }

    // a synced English string (PHGame.endReason) in the local language: the row whose English it is, else as it is
    public string En(string english)
    {
        if (english == null || english == "" || keys == null) return english;
        if (enIndex == null)
        {
            enIndex = new DataDictionary();
            for (int k = 0; k < keys.Length; k++) { string e = table[k * nLang]; if (!enIndex.ContainsKey(e)) enIndex.SetValue(e, k); }
        }
        DataToken t;
        if (!enIndex.TryGetValue(english, out t)) return english;
        return table[t.Int * nLang + lang];
    }

    public string Code() { return langCode != null && lang < langCode.Length ? langCode[lang] : "en"; }

    // ------------------------------------------------------------------ language
    int FromVRChat(string code)
    {
        if (langCode == null || code == null) return 0;
        if (code == "pt" || code == "pt-PT") code = "pt-BR";
        if (code == "nb" || code == "nn") code = "no";
        if (code == "zh-TW" || code == "zh-Hant") code = "zh-HK";
        if (code == "zh" || code == "zh-Hans") code = "zh-CN";
        for (int i = 0; i < langCode.Length; i++) if (langCode[i] == code) return i;
        return 0;
    }

    public override void OnLanguageChanged(string language)
    {
        if (picked) return;
        int v = FromVRChat(language);
        if (v == lang) return;
        lang = v;
        Apply();
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        int saved;
        if (!PlayerData.TryGetInt(player, SAVE_KEY, out saved) || saved <= 0 || saved > nLang) return;
        picked = true;
        if (lang == saved - 1) return;
        lang = saved - 1;
        if (started) Apply();
    }

    // the language board: a tile per language; index -1 = follow VRChat again
    public void Pick(int i)
    {
        if (i < 0)
        {
            picked = false;
            lang = FromVRChat(VRCPlayerApi.GetCurrentLanguage());
            PlayerData.SetInt(SAVE_KEY, 0);
        }
        else
        {
            if (i >= nLang) return;
            picked = true;
            lang = i;
            PlayerData.SetInt(SAVE_KEY, i + 1);
        }
        Apply();
    }

    public void Register(UdonSharpBehaviour b)
    {
        if (b == null) return;
        for (int i = 0; i < listeners.Length; i++) if (listeners[i] == b) return;
        var n = new UdonSharpBehaviour[listeners.Length + 1];
        for (int i = 0; i < listeners.Length; i++) n[i] = listeners[i];
        n[listeners.Length] = b;
        listeners = n;
        if (started) b.SendCustomEvent("_OnTextLanguage");
    }

    void Apply()
    {
        if (labels != null && labelKey != null)
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null) continue;
                int j = labelKey[i] * nLang + lang;
                if (j < table.Length) labels[i].text = table[j];
            }
        for (int i = 0; i < listeners.Length; i++) if (listeners[i] != null) listeners[i].SendCustomEvent("_OnTextLanguage");
    }
}
