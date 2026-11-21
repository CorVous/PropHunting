// Shareable, versioned seed codes (04 section 6.1).
// 60 bits: genVersion 10 | size 3 | seed 32 | flags 7 | crc8 8, written as 12 Crockford base32
// characters with a readable copy of the version in front: v1-K7QM-4XT9-2BRA.
// Flags: bits 0 to 2 hold the prop density setting (GenData.DensityName index, 0 = Normal), the rest is reserved.
// A code therefore reproduces a map with its density.
public static class SeedCode
{
    public const int OK = 0;         // same version: exact replay
    public const int TYPO = 1;       // length or checksum wrong
    public const int OLDER = 2;      // made with an older generator: can be played as a remix
    public const int NEWER = 3;      // needs a newer generator: refused

    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    static uint Crc8(ulong v52)
    {
        uint crc = 0;
        for (int i = 6; i >= 0; i--)
        {
            crc ^= (uint)((v52 >> (i * 8)) & 0xFF);
            for (int b = 0; b < 8; b++)
                crc = ((crc & 0x80) != 0) ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
        }
        return crc;
    }

    public const int DENSITY_MASK = 7;

    public static string Encode(int genVersion, int size, uint seed) { return Encode(genVersion, size, seed, 0); }

    public static string Encode(int genVersion, int size, uint seed, int flags)
    {
        ulong body = ((ulong)(uint)(genVersion & 0x3FF) << 42) | ((ulong)(uint)(size & 7) << 39) | ((ulong)seed << 7) | (ulong)(uint)(flags & 0x7F);
        ulong bits = (body << 8) | Crc8(body);
        char[] c = new char[12];
        for (int i = 0; i < 12; i++)
            c[i] = Alphabet[(int)((bits >> ((11 - i) * 5)) & 31)];
        string s = new string(c);
        return "v" + genVersion + "-" + s.Substring(0, 4) + "-" + s.Substring(4, 4) + "-" + s.Substring(8, 4);
    }

    public static int Decode(string text, int currentVersion, out int genVersion, out int size, out uint seed)
    {
        int flags;
        return Decode(text, currentVersion, out genVersion, out size, out seed, out flags);
    }

    // Returns OK / TYPO / OLDER / NEWER. On OK and OLDER the out values are set.
    public static int Decode(string text, int currentVersion, out int genVersion, out int size, out uint seed, out int flags)
    {
        genVersion = 0; size = 0; seed = 0; flags = 0;
        if (text == null) return TYPO;
        string t = text.Trim().ToUpperInvariant();
        // drop the readable "vN-" prefix: the version inside the code is what counts
        if (t.Length > 1 && t[0] == 'V')
        {
            int end = 1;
            while (end < t.Length && t[end] >= '0' && t[end] <= '9') end++;
            if (end > 1 && end < t.Length && (t[end] == '-' || t[end] == ' ')) t = t.Substring(end + 1);
        }
        ulong bits = 0;
        int n = 0;
        for (int i = 0; i < t.Length; i++)
        {
            char ch = t[i];
            if (ch == '-' || ch == ' ') continue;
            if (ch == 'I' || ch == 'L') ch = '1';
            if (ch == 'O') ch = '0';
            int v = Alphabet.IndexOf(ch);
            if (v < 0) return TYPO;
            bits = (bits << 5) | (uint)v;
            n++;
            if (n > 12) return TYPO;
        }
        if (n != 12) return TYPO;
        ulong body = bits >> 8;
        if (Crc8(body) != (uint)(bits & 0xFF)) return TYPO;
        genVersion = (int)((body >> 42) & 0x3FF);
        size = (int)((body >> 39) & 7);
        seed = (uint)((body >> 7) & 0xFFFFFFFFu);
        flags = (int)(body & 0x7F);
        if (size > 4) return TYPO;   // 0 to 3: S to XL, 4: XS (Cor, 2026-11-20)
        if (genVersion > currentVersion) return NEWER;
        if (genVersion < currentVersion) return OLDER;
        return OK;
    }
}
