# autophase: how the generator's phases were made

The map generator (`Assets/Scripts/Gen/MapGen*.cs`, ported to Udon by `Tools/port_mapgen.py`) runs a small piece of
work per `Step()` so a map build never holds a frame for long (the target: every step under about 20 ms in Udon on
a PC, which leaves room for a Quest). The pieces were not written by hand: these scripts rewrote the generator's
room plans, its search helpers and its stage functions into phases, from the hand-written source at commit
`cb48efd`:

1. `prepatch.py`, `prepatch2.py`: small hand rewrites into shapes the splitter knows, and the markers `// @step`,
   `// @step N` (a loop: one round, or N rounds, a step) and `// @cut` (a step ends before this statement).
2. `autophase4.py` (with `ap_norm.py`, `costs2.txt`): the splitter. A function becomes `X_Body()` (its phases) and
   `X(...)` (runs them to the end, for callers that are not in phases); a loop body with several units becomes
   `X_L1()` and so on. Locals that live across phases are the `hq_X_name` fields at the end of each file. The
   runtime is in `MapGenPlace.cs` (Ph, PhLoop, PhBreak, PhEnd, PhAdvance, PhPush, PhPop, PhSub...).
3. `postpatch.py`: the hand-made steppers (MapGen.Step, FillToMinimumStep, PlaceLeaksStep) drive the phases.

The scripts are kept for reference; they need not be run again. The generator's source is now the phase form: edit
it directly. A new piece of code inside a phase is fine as long as a single phase stays short; a new loop over
placements should be a phase of its own (copy the shape of a neighbouring `if (planPhase == k && PhLoop(k, 100000))`
block, or of a plain `if (planPhase == k)` one, and keep the numbers in order: `planMax` at the function's top is
the last phase's number).

Checks after any change to the generator: the map hashes must not change unless meant to (bump GEN_VERSION when they
do), `python Tools/port_mapgen.py`, and in Unity the slow-step log (`PHGame.logSlowSteps`, `slowStepMs`).
