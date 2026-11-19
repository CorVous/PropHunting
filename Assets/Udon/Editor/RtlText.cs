using System.Collections.Generic;
using System.Text;

// Arabic and Hebrew for TextMeshPro, which lays every line out left to right and does not join Arabic letters:
// the bake turns each right-to-left string into what TMP should draw. Arabic letters are swapped for their joined
// presentation forms (initial, medial, final, isolated; lam-alef ligatures); long strings are cut into lines at
// spaces (so a wrapped line can never put the end of a sentence first); each line is put in visual order: reversed,
// with runs of left-to-right text (Latin, digits, {0} placeholders, times like 2:15) kept the right way round and
// brackets mirrored. PHText's {0} arguments drop into those runs at runtime and read correctly.
public static class RtlText
{
    public const int LineChars = 44;

    // letter -> isolated, final, initial, medial (0 = that form does not exist: the letter does not join forward)
    static readonly Dictionary<char, int[]> Forms = new Dictionary<char, int[]>
    {
        { 'ء', new[] { 0xFE80, 0, 0, 0 } },
        { 'آ', new[] { 0xFE81, 0xFE82, 0, 0 } }, { 'أ', new[] { 0xFE83, 0xFE84, 0, 0 } },
        { 'ؤ', new[] { 0xFE85, 0xFE86, 0, 0 } }, { 'إ', new[] { 0xFE87, 0xFE88, 0, 0 } },
        { 'ئ', new[] { 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C } }, { 'ا', new[] { 0xFE8D, 0xFE8E, 0, 0 } },
        { 'ب', new[] { 0xFE8F, 0xFE90, 0xFE91, 0xFE92 } }, { 'ة', new[] { 0xFE93, 0xFE94, 0, 0 } },
        { 'ت', new[] { 0xFE95, 0xFE96, 0xFE97, 0xFE98 } }, { 'ث', new[] { 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C } },
        { 'ج', new[] { 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0 } }, { 'ح', new[] { 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4 } },
        { 'خ', new[] { 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8 } }, { 'د', new[] { 0xFEA9, 0xFEAA, 0, 0 } },
        { 'ذ', new[] { 0xFEAB, 0xFEAC, 0, 0 } }, { 'ر', new[] { 0xFEAD, 0xFEAE, 0, 0 } },
        { 'ز', new[] { 0xFEAF, 0xFEB0, 0, 0 } }, { 'س', new[] { 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4 } },
        { 'ش', new[] { 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8 } }, { 'ص', new[] { 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC } },
        { 'ض', new[] { 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0 } }, { 'ط', new[] { 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4 } },
        { 'ظ', new[] { 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8 } }, { 'ع', new[] { 0xFEC9, 0xFECA, 0xFECB, 0xFECC } },
        { 'غ', new[] { 0xFECD, 0xFECE, 0xFECF, 0xFED0 } }, { 'ف', new[] { 0xFED1, 0xFED2, 0xFED3, 0xFED4 } },
        { 'ق', new[] { 0xFED5, 0xFED6, 0xFED7, 0xFED8 } }, { 'ك', new[] { 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC } },
        { 'ل', new[] { 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0 } }, { 'م', new[] { 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4 } },
        { 'ن', new[] { 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8 } }, { 'ه', new[] { 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC } },
        { 'و', new[] { 0xFEED, 0xFEEE, 0, 0 } }, { 'ى', new[] { 0xFEEF, 0xFEF0, 0, 0 } },
        { 'ي', new[] { 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4 } },
    };
    // lam + alef variant -> isolated, final ligature
    static readonly Dictionary<char, int[]> LamAlef = new Dictionary<char, int[]>
    {
        { 'آ', new[] { 0xFEF5, 0xFEF6 } }, { 'أ', new[] { 0xFEF7, 0xFEF8 } },
        { 'إ', new[] { 0xFEF9, 0xFEFA } }, { 'ا', new[] { 0xFEFB, 0xFEFC } },
    };

    static bool Transparent(char c) { return c >= 'ً' && c <= 'ٟ' || c == 'ٰ'; }
    static bool JoinsForward(char c) { return c == 'ـ' || Forms.TryGetValue(c, out var f) && f[2] != 0; }
    static bool Joins(char c) { return c == 'ـ' || Forms.ContainsKey(c); }

    public static string ShapeArabic(string s)
    {
        var src = new List<char>();
        foreach (char c in s) if (!Transparent(c)) src.Add(c);   // tashkeel dropped (the font has no mark placement in TMP)
        var sb = new StringBuilder();
        for (int i = 0; i < src.Count; i++)
        {
            char c = src[i];
            if (!Forms.TryGetValue(c, out var f)) { sb.Append(c); continue; }
            bool fromPrev = i > 0 && JoinsForward(src[i - 1]);
            if (c == 'ل' && i + 1 < src.Count && LamAlef.TryGetValue(src[i + 1], out var la))
            {
                sb.Append((char)la[fromPrev ? 1 : 0]);
                i++;
                continue;
            }
            bool toNext = f[2] != 0 && i + 1 < src.Count && Joins(src[i + 1]);
            int form = fromPrev ? (toNext ? 3 : 1) : (toNext ? 2 : 0);
            int cp = f[form] != 0 ? f[form] : f[form == 3 ? 1 : 0];
            sb.Append((char)cp);
        }
        return sb.ToString();
    }

    public static bool IsRtl(char c) { return c >= '֐' && c <= 'ࣿ' || c >= 'יִ' && c <= '﷿' || c >= 'ﹰ' && c <= '﻿'; }
    static bool IsLtr(char c) { return c < 0x0590 && char.IsLetterOrDigit(c) || c == '{' || c == '}'; }
    static char Mirror(char c)
    {
        switch (c) { case '(': return ')'; case ')': return '('; case '[': return ']'; case ']': return '['; case '«': return '»'; case '»': return '«'; case '<': return '>'; case '>': return '<'; }
        return c;
    }

    // one line in visual order
    public static string VisualLine(string s)
    {
        var units = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (IsLtr(s[i]))
            {
                int j = i, last = i;
                while (j < s.Length && !IsRtl(s[j])) { if (IsLtr(s[j])) last = j; j++; }
                // a closing bracket right after the run belongs to it ("(T)")
                if (last + 1 < s.Length && (s[last + 1] == ')' || s[last + 1] == ']') && i > 0 && (s[i - 1] == '(' || s[i - 1] == '[')) { units.RemoveAt(units.Count - 1); units.Add(s.Substring(i - 1, last + 2 - (i - 1))); i = last + 2; continue; }
                units.Add(s.Substring(i, last + 1 - i));
                i = last + 1;
            }
            else { units.Add(Mirror(s[i]).ToString()); i++; }
        }
        units.Reverse();
        return string.Concat(units);
    }

    // a whole string: shaped (Arabic), cut into lines at spaces, each line in visual order
    public static string Visual(string s, bool arabic)
    {
        if (arabic) s = ShapeArabic(s);
        var lines = new List<string>();
        foreach (var para in s.Split('\n'))
        {
            string rest = para;
            while (rest.Length > LineChars)
            {
                int cut = rest.LastIndexOf(' ', LineChars);
                if (cut <= LineChars / 3) cut = rest.IndexOf(' ', LineChars);
                if (cut < 0) break;
                lines.Add(rest.Substring(0, cut)); rest = rest.Substring(cut + 1);
            }
            lines.Add(rest);
        }
        for (int k = 0; k < lines.Count; k++) lines[k] = VisualLine(lines[k]);
        return string.Join("\n", lines);
    }
}
