using UdonSharp;
using UnityEngine;

// Shareable seed codes (the prototype's SeedCode.cs, 04 section 6.1) without 64-bit arithmetic, which Udon only
// partly exposes: the 60 bits are kept one per array element.
// 60 bits: genVersion 10 | size 3 | seed 32 | flags 7 | crc8 8, written as 12 Crockford base32 characters with a
// readable copy of the version in front: v8-K7QM-4XT9-2BRA. Flags bits 0 to 2 hold the prop density.
// The same codes as the browser prototype, so a seed shared from either plays the same map.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PHSeed : UdonSharpBehaviour
{
    public const int OK = 0, TYPO = 1, OLDER = 2, NEWER = 3;
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // the results of the last Decode
    [HideInInspector] public int genVersion, size, flags;
    [HideInInspector] public uint seed;

    int[] bits = new int[60];

    void Put(int start, int len, uint v)
    {
        for (int i = 0; i < len; i++) bits[start + i] = (int)((v >> (len - 1 - i)) & 1u);
    }

    uint Get(int start, int len)
    {
        uint v = 0;
        for (int i = 0; i < len; i++) v = (v << 1) | (uint)bits[start + i];
        return v;
    }

    // CRC-8 (poly 0x07) over the 52-bit body as 7 bytes, most significant first (the top byte holds 4 bits)
    uint Crc8()
    {
        uint crc = 0;
        for (int byteIndex = 0; byteIndex < 7; byteIndex++)
        {
            // byte 0 is the body's top 4 bits (52 = 4 + 6 x 8)
            uint b = byteIndex == 0 ? Get(0, 4) : Get(4 + (byteIndex - 1) * 8, 8);
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = ((crc & 0x80u) != 0) ? ((crc << 1) ^ 0x07u) & 0xFFu : (crc << 1) & 0xFFu;
        }
        return crc;
    }

    public string Encode(int version, int sizeBucket, uint seedValue, int flagBits)
    {
        Put(0, 10, (uint)(version & 0x3FF));
        Put(10, 3, (uint)(sizeBucket & 7));
        Put(13, 32, seedValue);
        Put(45, 7, (uint)(flagBits & 0x7F));
        Put(52, 8, Crc8());
        string s = "";
        for (int i = 0; i < 12; i++)
        {
            if (i == 4 || i == 8) s += "-";
            s += Alphabet.Substring((int)Get(i * 5, 5), 1);
        }
        return "v" + version + "-" + s;
    }

    // OK / TYPO / OLDER / NEWER; on OK and OLDER genVersion, size, seed and flags are set
    public int Decode(string text, int currentVersion)
    {
        genVersion = 0; size = 0; seed = 0; flags = 0;
        if (text == null) return TYPO;
        string t = text.Trim().ToUpper();
        // (V is also a code letter: a bare code like V123-4567-89AB keeps its first group)
        int symbols = 0;
        for (int i = 0; i < t.Length; i++) if (t.Substring(i, 1) != "-" && t.Substring(i, 1) != " ") symbols++;
        if (symbols != 12 && t.Length > 1 && t.Substring(0, 1) == "V")
        {
            int end = 1;
            while (end < t.Length && "0123456789".IndexOf(t.Substring(end, 1)) >= 0) end++;
            if (end > 1 && end < t.Length && (t.Substring(end, 1) == "-" || t.Substring(end, 1) == " ")) t = t.Substring(end + 1);
        }
        int n = 0;
        for (int i = 0; i < t.Length; i++)
        {
            string ch = t.Substring(i, 1);
            if (ch == "-" || ch == " ") continue;
            if (ch == "I" || ch == "L") ch = "1";
            if (ch == "O") ch = "0";
            int v = Alphabet.IndexOf(ch);
            if (v < 0 || n >= 12) return TYPO;
            Put(n * 5, 5, (uint)v);
            n++;
        }
        if (n != 12) return TYPO;
        uint crc = Get(52, 8);
        if (Crc8() != crc) return TYPO;
        genVersion = (int)Get(0, 10);
        size = (int)Get(10, 3);
        seed = Get(13, 32);
        flags = (int)Get(45, 7);
        if (size > 4) return TYPO;   // 0 to 3: S to XL, 4: XS (Cor, 2026-11-20)
        if (genVersion > currentVersion) return NEWER;
        if (genVersion < currentVersion) return OLDER;
        return OK;
    }
}
