// Deterministic integer random numbers for the map generator (04 section 4.6.4).
// xorshift32 on uint, with substreams derived by hashing (seed, genVersion, stage, a, b).
// No UnityEngine, no System.Random: the same seed gives the same map on every machine.
public struct GenRng
{
    uint s;

    public GenRng(uint seed)
    {
        s = seed != 0 ? seed : 0x9E3779B9u;
    }

    public static uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x7FEB352Du;
        h ^= h >> 15; h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    public static uint Hash32(uint seed, uint version, uint stage, uint a, uint b)
    {
        uint h = 0x811C9DC5u;
        h = Mix(h ^ seed);
        h = Mix(h + 0x9E3779B9u ^ version);
        h = Mix(h + 0x85EBCA6Bu ^ stage);
        h = Mix(h + 0xC2B2AE35u ^ a);
        h = Mix(h + 0x27D4EB2Fu ^ b);
        return h;
    }

    public static GenRng Sub(uint seed, uint version, int stage, int a, int b)
    {
        return new GenRng(Hash32(seed, version, (uint)stage, (uint)a, (uint)b));
    }

    public uint NextU()
    {
        uint x = s;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        s = x;
        return x;
    }

    // 0 .. n-1
    public int Range(int n)
    {
        if (n <= 1) return 0;
        return (int)((NextU() >> 4) % (uint)n);
    }

    // lo .. hi inclusive
    public int Between(int lo, int hi)
    {
        if (hi <= lo) return lo;
        return lo + Range(hi - lo + 1);
    }

    public bool Chance(int permille)
    {
        return Range(1000) < permille;
    }
}
