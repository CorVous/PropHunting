using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

// Toki Pona in sitelen pona (Cor): the "tok" column written in the script, as UCSUR codepoints (U+F1900..), drawn by
// nasin sitelen pu mono (CC0, jan Ke Tami and kili pan Juli, Fonts/L10n/NasinSitelenPuMono.otf). Each known word becomes
// its glyph, with no space between glyphs; anything else (names, numbers, units, {0} placeholders, punctuation) stays
// in Latin letters. The table is the font's own (its glyph names), so every glyph used exists in the font.
public static class SitelenPona
{
    public const int CartoucheStart = 0xF1990, CartoucheEnd = 0xF1991, MiddleDot = 0xF199C, Colon = 0xF199D;

    static readonly Dictionary<string, int> Words = new Dictionary<string, int>
    {
        { "a", 0xF1900 },
        { "akesi", 0xF1901 },
        { "ala", 0xF1902 },
        { "alasa", 0xF1903 },
        { "ale", 0xF1904 },
        { "anpa", 0xF1905 },
        { "ante", 0xF1906 },
        { "anu", 0xF1907 },
        { "awen", 0xF1908 },
        { "e", 0xF1909 },
        { "en", 0xF190A },
        { "esun", 0xF190B },
        { "ijo", 0xF190C },
        { "ike", 0xF190D },
        { "ilo", 0xF190E },
        { "insa", 0xF190F },
        { "jaki", 0xF1910 },
        { "jan", 0xF1911 },
        { "jelo", 0xF1912 },
        { "jo", 0xF1913 },
        { "kala", 0xF1914 },
        { "kalama", 0xF1915 },
        { "kama", 0xF1916 },
        { "kasi", 0xF1917 },
        { "ken", 0xF1918 },
        { "kepeken", 0xF1919 },
        { "kili", 0xF191A },
        { "kiwen", 0xF191B },
        { "ko", 0xF191C },
        { "kon", 0xF191D },
        { "kule", 0xF191E },
        { "kulupu", 0xF191F },
        { "kute", 0xF1920 },
        { "la", 0xF1921 },
        { "lape", 0xF1922 },
        { "laso", 0xF1923 },
        { "lawa", 0xF1924 },
        { "len", 0xF1925 },
        { "lete", 0xF1926 },
        { "li", 0xF1927 },
        { "lili", 0xF1928 },
        { "linja", 0xF1929 },
        { "lipu", 0xF192A },
        { "loje", 0xF192B },
        { "lon", 0xF192C },
        { "luka", 0xF192D },
        { "lukin", 0xF192E },
        { "lupa", 0xF192F },
        { "ma", 0xF1930 },
        { "mama", 0xF1931 },
        { "mani", 0xF1932 },
        { "meli", 0xF1933 },
        { "mi", 0xF1934 },
        { "mije", 0xF1935 },
        { "moku", 0xF1936 },
        { "moli", 0xF1937 },
        { "monsi", 0xF1938 },
        { "mu", 0xF1939 },
        { "mun", 0xF193A },
        { "musi", 0xF193B },
        { "mute", 0xF193C },
        { "nanpa", 0xF193D },
        { "nasa", 0xF193E },
        { "nasin", 0xF193F },
        { "nena", 0xF1940 },
        { "ni", 0xF1941 },
        { "nimi", 0xF1942 },
        { "noka", 0xF1943 },
        { "o", 0xF1944 },
        { "olin", 0xF1945 },
        { "ona", 0xF1946 },
        { "open", 0xF1947 },
        { "pakala", 0xF1948 },
        { "pali", 0xF1949 },
        { "palisa", 0xF194A },
        { "pan", 0xF194B },
        { "pana", 0xF194C },
        { "pi", 0xF194D },
        { "pilin", 0xF194E },
        { "pimeja", 0xF194F },
        { "pini", 0xF1950 },
        { "pipi", 0xF1951 },
        { "poka", 0xF1952 },
        { "poki", 0xF1953 },
        { "pona", 0xF1954 },
        { "pu", 0xF1955 },
        { "sama", 0xF1956 },
        { "seli", 0xF1957 },
        { "selo", 0xF1958 },
        { "seme", 0xF1959 },
        { "sewi", 0xF195A },
        { "sijelo", 0xF195B },
        { "sike", 0xF195C },
        { "sin", 0xF195D },
        { "sina", 0xF195E },
        { "sinpin", 0xF195F },
        { "sitelen", 0xF1960 },
        { "sona", 0xF1961 },
        { "soweli", 0xF1962 },
        { "suli", 0xF1963 },
        { "suno", 0xF1964 },
        { "supa", 0xF1965 },
        { "suwi", 0xF1966 },
        { "tan", 0xF1967 },
        { "taso", 0xF1968 },
        { "tawa", 0xF1969 },
        { "telo", 0xF196A },
        { "tenpo", 0xF196B },
        { "toki", 0xF196C },
        { "tomo", 0xF196D },
        { "tu", 0xF196E },
        { "unpa", 0xF196F },
        { "uta", 0xF1970 },
        { "utala", 0xF1971 },
        { "walo", 0xF1972 },
        { "wan", 0xF1973 },
        { "waso", 0xF1974 },
        { "wawa", 0xF1975 },
        { "weka", 0xF1976 },
        { "wile", 0xF1977 },
        { "namako", 0xF1978 },
        { "kin", 0xF1979 },
        { "oko", 0xF197A },
        { "kipisi", 0xF197B },
        { "leko", 0xF197C },
        { "monsuta", 0xF197D },
        { "tonsi", 0xF197E },
        { "jasima", 0xF197F },
        { "kijetesantakalu", 0xF1980 },
        { "soko", 0xF1981 },
        { "meso", 0xF1982 },
        { "epiku", 0xF1983 },
        { "kokosila", 0xF1984 },
        { "lanpan", 0xF1985 },
        { "n", 0xF1986 },
        { "misikeke", 0xF1987 },
        { "ku", 0xF1988 },
        { "cartstart", 0xF1990 },
        { "cartend", 0xF1991 },
        { "cart", 0xF1992 },
        { "extpistart", 0xF1993 },
        { "extpi", 0xF1994 },
        { "combstack", 0xF1995 },
        { "combscale", 0xF1996 },
        { "extstart", 0xF1997 },
        { "extend", 0xF1998 },
        { "ext", 0xF1999 },
        { "revextstart", 0xF199A },
        { "revextend", 0xF199B },
        { "middot", 0xF199C },
        { "colon", 0xF199D },
        { "pake", 0xF19A0 },
        { "apeja", 0xF19A1 },
        { "majuna", 0xF19A2 },
        { "powe", 0xF19A3 },
    };

    public static bool Has(string word) { return Words.ContainsKey(word.ToLowerInvariant()); }

    public static string Write(string tok)
    {
        if (string.IsNullOrEmpty(tok)) return tok;
        var sb = new StringBuilder();
        bool lastGlyph = false;
        foreach (Match m in Regex.Matches(tok, @"\{\d\}|[A-Za-z]+|\s+|."))
        {
            string s = m.Value;
            int cp;
            if (char.IsLetter(s[0]) && Words.TryGetValue(s.ToLowerInvariant(), out cp))
            {
                sb.Append(char.ConvertFromUtf32(cp));
                lastGlyph = true;
                continue;
            }
            if (char.IsWhiteSpace(s[0]))
            {
                // spaces between two glyphs are dropped (sitelen pona runs words together); others stay
                int next = m.Index + m.Length;
                var rest = next < tok.Length ? Regex.Match(tok.Substring(next), @"^[A-Za-z]+") : Match.Empty;
                if (lastGlyph && rest.Success && Has(rest.Value)) continue;
                sb.Append(s); lastGlyph = false; continue;
            }
            if (s == ":" && lastGlyph) { sb.Append(char.ConvertFromUtf32(Colon)); continue; }
            sb.Append(s);
            lastGlyph = false;
        }
        return sb.ToString();
    }
}
