# TestRoom: backwards walk through the teleport hallways

Two local VRChat clients on the decorated TestRoom build (2026-11-25), recorded side by side at real speed.
Left: A (walker) backs through a hallway while facing B, then walks forward back. Right: B (watcher) stays in the next room facing A.

- 0:05-0:56: 2/3 hallway. B in room 3; A goes room 3 -> 2 backwards and room 2 -> 3 forwards, 3 times (the last at run speed).
- 0:56-1:52: 8/1 hallway. B in room 1; A goes room 1 -> 8 backwards and room 8 -> 1 forwards, 3 times.

All 12 crossings fired a real area teleport (walker log: area 1->0->1 on 2/3, area 0->3->0 on 8/1; each landed in 1 frame at the same local spot).

`backwards_watch_sheet.png`: one row per crossing, 8 frames 0.2 s apart from each view.

Review: B's view of A stays smooth through every crossing (no pop, slide, vanish or wrong turn). In A's view, B's hair and cape flick sideways for about 0.3 s at every crossing, because B is redrawn 1000 m over in A's new area copy and the avatar's physics sees that jump.
