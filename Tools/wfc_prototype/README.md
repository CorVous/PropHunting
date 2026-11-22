# Wave function collapse site layout (prototype)

A Python prototype of a replacement for `MapGenSite.LayoutSite`: the lanes, the plots and the wood of a map
come from a wave function collapse over the outer grid of 4 m cells, instead of one of seven fixed lane shapes.
The buildings are still the plan library's (`GenPlans.cs`, read by `plans.py`).

Nothing here is used by the world. It is the reference for a C# port (`MapGenSiteWfc.cs`).

    python sitegen.py            # retry rates and lane variety over 200 seeds a size
    python render.py 2 l.png     # twelve L maps on one sheet (0 S, 1 M, 2 L, 3 XL)
    python make_out.py out       # the sheets of every size and the collapse as a film
    python items.py 25           # the item pass over 25 seeds a size
    python items_render.py out   # maps with their items

- `wfc.py`: the core, written as the Udon port has to be: flat arrays, bit masks, integer maths, GenRng,
  no backtracking (a contradiction is the seed's next attempt).
- `sitegen.py`: the tiles and what may stand beside what (`allowed`), the lanes grown first along their open
  ends, then every other cell, then the plots cut from the frontage and a plan seated on each.
- `items.py`: a second collapse on 1 m cells for the items of every room, garden, the wood and the green: one
  tile set, and per kind of place the tiles it may hold, their weights and the pieces it must have.
- `render.py`, `items_render.py`, `make_out.py`: pictures.

Needs Pillow.

## Speed against the current generator

`bench/current` times `MapGen` per size and stage outside Unity (`dotnet run -c Release -- 200`).
`bench/replay` replays the prototype's collapses in C#: `python trace.py 60` writes what each collapse did,
`dotnet run -c Release` times the same work (propagation, the scan for the next cell, a weight per option).
