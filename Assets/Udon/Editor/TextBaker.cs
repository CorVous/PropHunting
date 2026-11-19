using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// "PropHunt/Bake text table": the world's text in every language (PHText).
//  1. reads Assets/Udon/Text/*.tsv (one file per area; key, en, then one column per language; # lines are comments;
//     an empty cell means "use the English"). props.tsv holds the prop names ("prop:<english name>" keys).
//  2. fills PHText (World/Game/Text, made if missing): languages, keys, the table, the prop names per def, and the
//     static labels: every TMP text in the scene whose whole text equals a row's English.
//  3. makes the fallback fonts: static SDF atlases holding exactly the characters the table needs that the Kenney
//     fonts lack (accents from the Kenney fonts themselves, Cyrillic from Rubik, CJK from Noto Sans JP/KR/SC/TC; the
//     TTFs in Assets/Lobby/Fonts/L10n come from Tools/make_l10n_fonts.py), hung under the Kenney fonts and
//     LiberationSans as fallbacks. Re-run after any TSV edit.
public static class TextBaker
{
    public const string TextDir = "Assets/Udon/Text";
    const string FontDir = "Assets/Lobby/Fonts/L10n/";
    const string KenneyFuture = "Assets/Lobby/Fonts/KenneyFuture SDF.asset", KenneyRocket = "Assets/Lobby/Fonts/KenneyRocket SDF.asset";
    const string FutureTtf = "Assets/Resources/PropHunt/Fonts/KenneyFuture.ttf", RocketTtf = "Assets/Lobby/Fonts/KenneyRocket.ttf";
    const string Liberation = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    // the languages, in the board's order: VRChat's codes and each one's name in itself
    public static readonly string[] Codes = { "en", "ja", "ko", "zh-CN", "zh-HK", "es", "fr", "de", "pt-BR", "ru", "it", "pl",
        "id", "nl", "sv", "uk", "da", "no", "fi", "hu", "cs", "tr", "ro", "vi", "ar", "he", "th", "tok" };
    public static readonly string[] Names = { "English", "日本語", "한국어", "简体中文", "繁體中文", "Español", "Français", "Deutsch", "Português", "Русский", "Italiano", "Polski",
        "Indonesia", "Nederlands", "Svenska", "Українська", "Dansk", "Norsk", "Suomi", "Magyar", "Čeština", "Türkçe", "Română", "Tiếng Việt", "العربية", "עברית", "ไทย", "toki pona" };
    // which column's characters go to which fallback font first (a Han character used by Japanese and Chinese takes the
    // Japanese shape: one fallback chain serves every language)
    // (Latin letters the Kenney fonts hold come from them; Cyrillic and Latin Extended-A from Rubik; Vietnamese from Noto Sans)
    static readonly string[] FontFor = { null, "NotoSansJP", "NotoSansKR", "NotoSansSC", "NotoSansTC", null, null, null, null, "Rubik", null, "Rubik",
        null, null, null, "Rubik", null, null, null, "Rubik", "Rubik", "Rubik", "Rubik", "NotoSans", "NotoSansArabic", "Rubik", "NotoSansThai", null };
    static readonly string[] FontOrder = { "NotoSansJP", "NotoSansKR", "NotoSansSC", "NotoSansTC", "Rubik", "NotoSans", "NotoSansArabic", "NotoSansThai" };
    // right to left: shaped (Arabic) and put in visual order at bake time (RtlText)
    static readonly string[] Rtl = { "ar", "he" };

    // a language's own name as the board draws it (right-to-left ones shaped and in visual order)
    public static string DisplayName(int i) { string c = Codes[i]; return c == "ar" || c == "he" ? RtlText.Visual(Names[i], c == "ar") : Names[i]; }

    public class Table
    {
        public List<string> keys = new List<string>();
        public List<string[]> rows = new List<string[]>();             // one cell per language, English filled in
        public Dictionary<string, string[]> props = new Dictionary<string, string[]>();
        public List<string> warnings = new List<string>();
        public List<string> missing = new List<string>();               // "key (codes)" rows with untranslated cells
    }

    public static Table Read()
    {
        var t = new Table();
        var at = new Dictionary<string, int>();
        foreach (var path in Directory.GetFiles(TextDir, "*.tsv").OrderBy(p => Path.GetFileName(p) == "strings_core.tsv" ? "" : Path.GetFileName(p)))
        {
            string[] head = null;
            int n = 0;
            foreach (var raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                n++;
                var ln = raw.TrimEnd('\r');
                if (ln.Trim() == "" || ln.StartsWith("#")) continue;
                var cells = ln.Split('\t');
                if (head == null) { head = cells; if (head[0] != "key" || head.Length < 2 || head[1] != "en") t.warnings.Add(Path.GetFileName(path) + ": the first line must be key<TAB>en<TAB>..."); continue; }
                string key = cells[0].Trim();
                if (key == "") continue;
                var row = new string[Codes.Length];
                for (int c = 1; c < head.Length && c < cells.Length; c++)
                {
                    int l = System.Array.IndexOf(Codes, head[c].Trim());
                    if (l >= 0) row[l] = cells[c].Trim().Replace("\\n", "\n");
                }
                if (string.IsNullOrEmpty(row[0])) { t.warnings.Add(Path.GetFileName(path) + ":" + n + " " + key + " has no English"); row[0] = key; }
                foreach (var code in Rtl)
                {
                    int l = System.Array.IndexOf(Codes, code);
                    if (!string.IsNullOrEmpty(row[l])) row[l] = RtlText.Visual(row[l], code == "ar");
                }
                var gaps = new List<string>();
                for (int l = 1; l < row.Length; l++) if (string.IsNullOrEmpty(row[l])) { row[l] = row[0]; gaps.Add(Codes[l]); }
                if (gaps.Count > 0) t.missing.Add(key + " (" + (gaps.Count == Codes.Length - 1 ? "all" : string.Join(" ", gaps)) + ")");
                if (key.StartsWith("prop:")) { t.props[key.Substring(5)] = row; continue; }
                if (at.TryGetValue(key, out int was)) { t.warnings.Add(Path.GetFileName(path) + ":" + n + " " + key + " repeats an earlier key (this one wins)"); t.rows[was] = row; continue; }
                at[key] = t.keys.Count; t.keys.Add(key); t.rows.Add(row);
            }
        }
        return t;
    }

    [MenuItem("PropHunt/Bake text table")]
    public static void Bake()
    {
        var t = Read();
        foreach (var w in t.warnings) Debug.LogWarning("[prophunt] text: " + w);
        var text = Object.FindObjectOfType<PHText>(true);
        if (text == null)
        {
            var game = GameObject.Find("World/Game");
            if (game == null) { Debug.LogError("[prophunt] text: no World/Game in the open scene"); return; }
            var go = new GameObject("Text");
            go.transform.SetParent(game.transform, false);
            text = go.AddUdonSharpComponent<PHText>();
        }
        int L = Codes.Length;
        text.langCode = (string[])Codes.Clone();
        text.langName = Enumerable.Range(0, Names.Length).Select(DisplayName).ToArray();
        text.keys = t.keys.ToArray();
        text.table = new string[t.keys.Count * L];
        for (int k = 0; k < t.keys.Count; k++) for (int l = 0; l < L; l++) text.table[k * L + l] = t.rows[k][l];

        // prop names: the sentence form (English lower case, as PHLocal always showed them)
        GenData.Build();
        text.propNames = new string[GenData.nDefs * L];
        var untranslated = new HashSet<string>();
        for (int d = 0; d < GenData.nDefs; d++)
        {
            string en = DefName(GenData.defId[d]).ToLowerInvariant();
            string[] row;
            if (!t.props.TryGetValue(en, out row)) { row = null; untranslated.Add(en); }
            for (int l = 0; l < L; l++) text.propNames[d * L + l] = row != null ? row[l] : en;
        }
        if (untranslated.Count > 0) Debug.Log("[prophunt] text: " + untranslated.Count + " prop names stay English: " + string.Join(", ", untranslated.Take(40)));

        // static labels: the first row whose English is the label's whole text
        var byEnglish = new Dictionary<string, int>();
        for (int k = 0; k < t.keys.Count; k++) if (!byEnglish.ContainsKey(t.rows[k][0])) byEnglish[t.rows[k][0]] = k;
        // labels a builder baked in capitals ("FIRST CATCH" for the row "First Catch"): the Kenney fonts draw lower case
        // as small capitals, so the row's own text is shown
        var byUpper = new Dictionary<string, int>();
        for (int k = 0; k < t.keys.Count; k++) { string u = t.rows[k][0].ToUpperInvariant(); if (u != t.rows[k][0] && !byUpper.ContainsKey(u)) byUpper[u] = k; }
        var labels = new List<TMP_Text>(); var labelKey = new List<int>();
        foreach (var tmp in Object.FindObjectsOfType<TMP_Text>(true))
        {
            if (tmp.GetComponentInParent<TMP_InputField>(true) != null && tmp.GetComponentInParent<TMP_InputField>(true).textComponent == tmp) continue;
            if (byEnglish.TryGetValue(tmp.text.Trim(), out int k) || byUpper.TryGetValue(tmp.text.Trim(), out k)) { labels.Add(tmp); labelKey.Add(k); }
        }
        text.labels = labels.ToArray(); text.labelKey = labelKey.ToArray();
        // longer languages shrink to fit their label instead of spilling out (Cor: "make sure all the text stays
        // formatted well"): auto size from the baked size down to 55% of it
        foreach (var lab in labels) FitToBox(lab);
        // and the texts the lobby boards set at runtime (not player names or map codes)
        foreach (var u in Object.FindObjectsOfType<UdonSharp.UdonSharpBehaviour>(true))
        {
            if (!(u is PHPanel) && !(u is PHScorePad)) continue;
            foreach (var f in u.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (f.Name.ToLowerInvariant().Contains("name") || f.Name.Contains("seedCode")) continue;
                if (typeof(TMP_Text).IsAssignableFrom(f.FieldType)) FitToBox(f.GetValue(u) as TMP_Text);
                else if (f.FieldType.IsArray && typeof(TMP_Text).IsAssignableFrom(f.FieldType.GetElementType()) && f.GetValue(u) is System.Array arr)
                    foreach (var o in arr) FitToBox(o as TMP_Text);
            }
        }
        UdonSharpEditorUtility.CopyProxyToUdon(text);

        var game2 = Object.FindObjectOfType<PHGame>(true);
        if (game2 != null && game2.text != text) { game2.text = text; UdonSharpEditorUtility.CopyProxyToUdon(game2); }
        foreach (var b in Object.FindObjectsOfType<PHLangBoard>(true)) if (b.text != text) { b.text = text; UdonSharpEditorUtility.CopyProxyToUdon(b); }
        foreach (var b in Object.FindObjectsOfType<PHLangTile>(true)) if (b.text != text) { b.text = text; UdonSharpEditorUtility.CopyProxyToUdon(b); }

        BakeFonts(t);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(text.gameObject.scene);
        Debug.Log("[prophunt] text: " + t.keys.Count + " strings x " + L + " languages, " + t.props.Count + " prop names, " + labels.Count + " static labels");
    }

    public static void FitToBox(TMP_Text t)
    {
        if (t == null) return;
        if (!t.enableAutoSizing) { t.fontSizeMax = t.fontSize; t.enableAutoSizing = true; }
        t.fontSizeMin = Mathf.Max(5f, t.fontSizeMax * 0.55f);
        EditorUtility.SetDirty(t);
    }

    // "PropHunt/Check text fit": every static label in every language, laid out by TMP; lists the ones that still
    // spill out of their box at the smallest auto size, and the runtime texts of the panels (PHPanel, PHScorePad)
    // with the longest translation of their rows. Restores the English afterwards.
    [MenuItem("PropHunt/Check text fit")]
    public static void CheckFit()
    {
        var text = Object.FindObjectOfType<PHText>(true);
        if (text == null || text.labels == null) { Debug.LogWarning("[prophunt] text fit: bake the text table first"); return; }
        int L = text.langCode.Length, bad = 0;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.labels.Length; i++)
        {
            var t = text.labels[i];
            if (t == null) continue;
            string was = t.text;
            var box = t.rectTransform.rect;
            for (int l = 0; l < L; l++)
            {
                t.text = text.table[text.labelKey[i] * L + l];
                t.ForceMeshUpdate(true, true);
                var b = t.textBounds.size;
                bool over = t.isTextOverflowing || b.x > box.width * 1.02f + 1f || (!t.enableWordWrapping && b.y > box.height * 1.3f + 2f);
                if (over) { bad++; sb.AppendLine(text.keys[text.labelKey[i]] + " [" + text.langCode[l] + "] " + TPath(t.transform) + "  " + b.x.ToString("0") + " > " + box.width.ToString("0") + "  \"" + t.text + "\""); }
            }
            t.text = was; t.ForceMeshUpdate(true, true);
        }
        Debug.Log("[prophunt] text fit: " + bad + " label/language pairs overflow" + (bad > 0 ? ":" + System.Environment.NewLine + sb : ""));
    }

    static string TPath(Transform t) { string p = t.name; while (t.parent != null && p.Length < 80) { t = t.parent; p = t.name + "/" + p; } return p; }

    // ------------------------------------------------------------------ fonts
    static void BakeFonts(Table t)
    {
        var future = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KenneyFuture);
        var rocket = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KenneyRocket);
        var lib = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Liberation);
        var kenneyTtf = AssetDatabase.LoadAssetAtPath<Font>(FutureTtf);
        var rocketTtf = AssetDatabase.LoadAssetAtPath<Font>(RocketTtf);

        // every character the table (and the board's language names) needs, by the column that uses it
        var perFont = FontOrder.ToDictionary(f => f, f => new SortedSet<char>());
        var latin = new SortedSet<char>();
        var all = new SortedSet<char>();
        var rows = t.rows.Concat(t.props.Values).ToList();
        rows.Add(Enumerable.Range(0, Names.Length).Select(DisplayName).ToArray());
        foreach (var row in rows)
            for (int l = 0; l < row.Length; l++)
                foreach (char c in row[l]) all.Add(c);
        var fontFile = FontOrder.ToDictionary(f => f, f => AssetDatabase.LoadAssetAtPath<Font>(FontDir + f + "-Bold.ttf"));
        var cover = FontOrder.ToDictionary(f => f, f => Cmap(FontDir + f + "-Bold.ttf"));
        var kenneyCover = Cmap(FutureTtf);
        var assigned = new HashSet<char>();
        foreach (char c in all)
        {
            if (c < 0x20 || char.IsWhiteSpace(c)) { assigned.Add(c); continue; }
            if (future != null && future.HasCharacter(c, false)) { assigned.Add(c); continue; }
            if (kenneyCover.Contains(c)) { latin.Add(c); assigned.Add(c); }
        }
        foreach (var row in rows)
            for (int l = 0; l < row.Length; l++)
            {
                string f = l < FontFor.Length ? FontFor[l] : null;
                if (f == null) continue;
                foreach (char c in row[l]) if (!assigned.Contains(c) && cover[f].Contains(c)) { perFont[f].Add(c); assigned.Add(c); }
            }
        foreach (char c in all)
            if (!assigned.Contains(c))
            {
                var f = FontOrder.FirstOrDefault(x => cover[x].Contains(c));
                if (f != null) { perFont[f].Add(c); assigned.Add(c); }
                else Debug.LogWarning("[prophunt] text: no font has U+" + ((int)c).ToString("X4") + " '" + c + "'");
            }

        var chain = new List<TMP_FontAsset>();
        foreach (var f in FontOrder)
        {
            if (perFont[f].Count == 0 || fontFile[f] == null) { if (fontFile[f] == null && perFont[f].Count > 0) Debug.LogError("[prophunt] text: missing " + FontDir + f + "-Bold.ttf (Tools/make_l10n_fonts.py)"); continue; }
            var fa = Atlas(fontFile[f], FontDir + f + " SDF.asset", new string(perFont[f].ToArray()));
            if (fa != null) chain.Add(fa);
        }
        var latinChars = new string(latin.ToArray());
        var futureExtra = latin.Count > 0 && kenneyTtf != null ? Atlas(kenneyTtf, FontDir + "KenneyFuture Extra SDF.asset", latinChars) : null;
        var rocketExtra = latin.Count > 0 && rocketTtf != null ? Atlas(rocketTtf, FontDir + "KenneyRocket Extra SDF.asset", new string(latin.Where(Cmap(RocketTtf).Contains).ToArray())) : null;

        SetFallbacks(future, futureExtra, chain);
        SetFallbacks(rocket, rocketExtra != null ? rocketExtra : futureExtra, chain);
        SetFallbacks(lib, null, chain);
        AssetDatabase.SaveAssets();
        Debug.Log("[prophunt] text fonts: " + string.Join(", ", FontOrder.Select(f => f + " " + perFont[f].Count)) + ", Latin extras " + latin.Count);
    }

    static void SetFallbacks(TMP_FontAsset fa, TMP_FontAsset first, List<TMP_FontAsset> chain)
    {
        if (fa == null) return;
        var list = new List<TMP_FontAsset>();
        if (first != null) list.Add(first);
        list.AddRange(chain);
        fa.fallbackFontAssetTable = list;
        EditorUtility.SetDirty(fa);
    }

    // a static SDF atlas holding exactly these characters: the smallest square that fits (512 .. 4096)
    static TMP_FontAsset Atlas(Font font, string path, string chars)
    {
        TMP_FontAsset fa = null;
        foreach (int size in new[] { 512, 1024, 2048, 4096 })
        {
            fa = TMP_FontAsset.CreateFontAsset(font, 56, 7, GlyphRenderMode.SDFAA, size, size, AtlasPopulationMode.Dynamic, false);
            if (fa.TryAddCharacters(chars, out string missing)) break;
            if (size == 4096) Debug.LogWarning("[prophunt] text: " + Path.GetFileName(path) + " could not fit: " + missing);
            else Object.DestroyImmediate(fa);
        }
        fa.atlasPopulationMode = AtlasPopulationMode.Static;
        string name = Path.GetFileNameWithoutExtension(path);
        // a fresh asset every bake (the fallback lists are set again right after)
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = name + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = name + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        EditorUtility.SetDirty(fa);
        return fa;
    }

    // the characters a TTF holds, from the list Tools/make_l10n_fonts.py writes next to it (<ttf name>.chars.txt in
    // Fonts/L10n; KenneyFuture.chars.txt / KenneyRocket.chars.txt for the Kenney fonts)
    static HashSet<char> Cmap(string ttfPath)
    {
        string name = Path.GetFileNameWithoutExtension(ttfPath);
        string list = FontDir + name + ".chars.txt";
        var set = new HashSet<char>();
        if (!File.Exists(list)) { Debug.LogWarning("[prophunt] text: no " + list + " (run Tools/make_l10n_fonts.py)"); return set; }
        foreach (char c in File.ReadAllText(list, System.Text.Encoding.UTF8)) set.Add(c);
        return set;
    }

    // the prototype's GameUI.DefName: "k_chairCushion" -> "Chair cushion" (WorldSceneBuilder has the same)
    public static string DefName(string id)
    {
        int us = id.IndexOf('_');
        if (us >= 0 && us <= 4) id = id.Substring(us + 1);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

// Before a build or upload (Cor: "make sure localizations stay up to date as we add more text"): the scene's text
// table is baked again from the TSVs if it is stale, and rows still missing translations are listed, with the choice
// to stop and translate them first.
public class TextPublishCheck : VRC.SDKBase.Editor.BuildPipeline.IVRCSDKBuildRequestedCallback
{
    public int callbackOrder => 1;

    public bool OnBuildRequested(VRC.SDKBase.Editor.BuildPipeline.VRCSDKRequestedBuildType type)
    {
        if (type != VRC.SDKBase.Editor.BuildPipeline.VRCSDKRequestedBuildType.Scene || PrePublishCheck.Exporting) return true;
        var text = Object.FindObjectOfType<PHText>(true);
        if (text == null) return true;
        var t = TextBaker.Read();
        bool stale = text.keys == null || text.keys.Length != t.keys.Count;
        int L = TextBaker.Codes.Length;
        for (int k = 0; !stale && k < t.keys.Count; k++)
            for (int l = 0; l < L; l++)
                if (text.keys[k] != t.keys[k] || text.table[k * L + l] != t.rows[k][l]) { stale = true; break; }
        if (stale) { Debug.Log("[prophunt] text table was stale: baking it again before the build"); TextBaker.Bake(); }
        if (t.missing.Count == 0) return true;
        string nl = "\n";
        string list = string.Join(nl, t.missing.Take(15)) + (t.missing.Count > 15 ? nl + "... and " + (t.missing.Count - 15) + " more" : "");
        Debug.LogWarning("[prophunt] text rows missing translations (English shows instead):" + nl + string.Join(nl, t.missing));
        if (Application.isBatchMode || PrePublishCheck.Quiet) return true;
        return EditorUtility.DisplayDialog("PropHunt: untranslated text", t.missing.Count + " text rows are missing translations; those languages show English for them:" + nl + nl + list,
            "Build anyway", "Cancel");
    }
}
