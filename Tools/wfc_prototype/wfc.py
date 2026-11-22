"""Wave function collapse core for the PropHunting site layout prototype.

Written the way the Udon port would have to be: flat arrays, integer maths only, bit masks for the
options of a cell, the generator's own xorshift PRNG (GenRng), no recursion, no backtracking (a
contradiction is a re-roll of the seed's attempt, like the generator's Retry).
"""

M32 = 0xFFFFFFFF


class Rng:
    """GenRng.cs, line for line."""

    def __init__(self, seed):
        self.s = seed & M32 or 0x9E3779B9

    @staticmethod
    def mix(h):
        h ^= h >> 16; h = (h * 0x7FEB352D) & M32
        h ^= h >> 15; h = (h * 0x846CA68B) & M32
        h ^= h >> 16
        return h

    @staticmethod
    def hash32(seed, version, stage, a, b):
        h = 0x811C9DC5
        h = Rng.mix(h ^ (seed & M32))
        h = Rng.mix(((h + 0x9E3779B9) & M32) ^ (version & M32))
        h = Rng.mix(((h + 0x85EBCA6B) & M32) ^ (stage & M32))
        h = Rng.mix(((h + 0xC2B2AE35) & M32) ^ (a & M32))
        h = Rng.mix(((h + 0x27D4EB2F) & M32) ^ (b & M32))
        return h

    @staticmethod
    def sub(seed, version, stage, a, b):
        return Rng(Rng.hash32(seed, version, stage, a, b))

    def next(self):
        x = self.s
        x ^= (x << 13) & M32
        x ^= x >> 17
        x ^= (x << 5) & M32
        self.s = x
        return x

    def range(self, n):
        if n <= 1:
            return 0
        return (self.next() >> 4) % n

    def chance(self, permille):
        return self.range(1000) < permille

    def shuffle(self, a):
        for i in range(len(a) - 1, 0, -1):
            j = self.range(i + 1)
            a[i], a[j] = a[j], a[i]


DX = (0, 1, 0, -1)   # N, E, S, W (the generator's order)
DZ = (1, 0, -1, 0)


def popcount(m):
    return bin(m).count("1")


class Wave:
    """A grid of cells, each a bit mask of the tiles it can still be.

    compat[d][t] is the mask of tiles allowed in the neighbour on side d of a cell holding t.
    border is the tile every cell outside the grid counts as.
    """

    def __init__(self, w, h, ntiles, compat, border):
        self.W, self.H, self.N = w, h, ntiles
        self.compat = compat
        self.full = (1 << ntiles) - 1
        self.wave = [self.full] * (w * h)
        self.failed = False
        self.stack = []
        self.propagations = 0
        # the map's edge: what may stand beside the outside
        for z in range(h):
            for x in range(w):
                for d in range(4):
                    nx, nz = x + DX[d], z + DZ[d]
                    if nx < 0 or nz < 0 or nx >= w or nz >= h:
                        self.restrict(z * w + x, compat[(d + 2) & 3][border])
        self.propagate()

    def restrict(self, c, mask):
        old = self.wave[c]
        new = old & mask
        if new == old:
            return
        self.wave[c] = new
        if new == 0:
            self.failed = True
            return
        self.stack.append(c)

    def allowed_from(self, mask, d):
        out = 0
        t = 0
        comp = self.compat[d]
        while mask:
            if mask & 1:
                out |= comp[t]
            mask >>= 1
            t += 1
        return out

    def propagate(self):
        W, H = self.W, self.H
        while self.stack and not self.failed:
            c = self.stack.pop()
            x, z = c % W, c // W
            m = self.wave[c]
            for d in range(4):
                nx, nz = x + DX[d], z + DZ[d]
                if nx < 0 or nz < 0 or nx >= W or nz >= H:
                    continue
                self.propagations += 1
                self.restrict(nz * W + nx, self.allowed_from(m, d))
        self.stack.clear()
        return not self.failed

    def collapse(self, c, t):
        self.restrict(c, 1 << t)
        return self.propagate()

    def decided(self, c):
        m = self.wave[c]
        return m != 0 and m & (m - 1) == 0

    def tile(self, c):
        return self.wave[c].bit_length() - 1
