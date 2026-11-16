// Hand-written part of MapGenUdon: the generator's PRNG (GenRng.cs) without a struct. An rng is a uint[1] holding
// the xorshift32 state; the arithmetic is GenRng's, so a seed gives the same draws as the prototype.
public partial class MapGenUdon
{
    uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x7FEB352Du;
        h ^= h >> 15; h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    uint Hash32(uint seedValue, uint version, uint stageId, uint a, uint b)
    {
        uint h = 0x811C9DC5u;
        h = Mix(h ^ seedValue);
        h = Mix(h + 0x9E3779B9u ^ version);
        h = Mix(h + 0x85EBCA6Bu ^ stageId);
        h = Mix(h + 0xC2B2AE35u ^ a);
        h = Mix(h + 0x27D4EB2Fu ^ b);
        return h;
    }

    uint[] RngNew(uint s)
    {
        uint[] r = new uint[1];
        r[0] = s != 0 ? s : 0x9E3779B9u;
        return r;
    }

    uint[] RngSub(uint seedValue, uint version, int stageId, int a, int b)
    {
        return RngNew(Hash32(seedValue, version, (uint)stageId, (uint)a, (uint)b));
    }

    uint RngNextU(uint[] r)
    {
        uint x = r[0];
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        r[0] = x;
        return x;
    }

    // 0 .. n-1
    int RngRange(uint[] r, int n)
    {
        if (n <= 1) return 0;
        return (int)((RngNextU(r) >> 4) % (uint)n);
    }

    // lo .. hi inclusive
    int RngBetween(uint[] r, int lo, int hi)
    {
        if (hi <= lo) return lo;
        return lo + RngRange(r, hi - lo + 1);
    }

    bool RngChance(uint[] r, int permille)
    {
        return RngRange(r, 1000) < permille;
    }
}
