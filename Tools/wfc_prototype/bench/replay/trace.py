"""Writes what the prototype's collapses did, so the same work can be replayed and timed in C#."""
import sys
sys.path.insert(0, __import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), ".."))
import wfc, sitegen as S, items as I

OUT = None
class Traced(wfc.Wave):
    def __init__(self, w, h, nt, compat, border, walls=None):
        self._quiet = True
        OUT.write("W %d %d %d %d\n" % (w, h, nt, border))
        OUT.write("L " + (" ".join(str(x) for x in walls) if walls is not None else "-") + "\n")
        OUT.write("C " + " ".join("%x" % compat[d][t] for d in range(4) for t in range(nt)) + "\n")
        super().__init__(w, h, nt, compat, border, walls)
        self._quiet = False
    def restrict(self, c, mask):
        if not self._quiet:
            OUT.write("r %d %x\n" % (c, mask & self.full))
        super().restrict(c, mask)
    def propagate(self):
        if self._quiet:
            return super().propagate()
        OUT.write("p\n")
        self._quiet = True
        r = super().propagate()
        self._quiet = False
        return r
    def collapse(self, c, t):
        OUT.write("c %d %d\n" % (c, t))
        self._quiet = True
        self.restrict(c, 1 << t)
        r = super().propagate()
        self._quiet = False
        return r
S.Wave = Traced
I.Wave = Traced
N = int(sys.argv[1])
for size in range(4):
    fl = open("layout_%d.txt" % size, "w"); fi = open("items_%d.txt" % size, "w")
    for seed in range(1, N + 1):
        OUT = fl; OUT.write("M\n")
        s = S.generate(seed, size)
        OUT = fi; OUT.write("M\n")
        I.generate(s)
    fl.close(); fi.close()
    print(size, "done")
