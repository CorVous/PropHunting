# TestRoom: backwards walk through the teleport hallways

Two local VRChat clients on the decorated TestRoom build (2026-11-25), recorded side by side at real speed.
Left: A (walker) backs through a hallway while facing B, then walks forward back. Right: B (watcher) stays in the next room facing A.

- 0:05-0:56: 2/3 hallway. B in room 3; A goes room 3 -> 2 backwards and room 2 -> 3 forwards, 3 times (the last at run speed).
- 0:56-1:52: 8/1 hallway. B in room 1; A goes room 1 -> 8 backwards and room 8 -> 1 forwards, 3 times.

All 12 crossings fired a real area teleport (walker log: area 1->0->1 on 2/3, area 0->3->0 on 8/1; each landed in 1 frame at the same local spot).

`backwards_watch_sheet.png`: one row per crossing, 8 frames 0.2 s apart from each view.

Review: B's view of A stays smooth through every crossing (no pop, slide, vanish or wrong turn). In A's view, B's hair and cape flick sideways for about 0.3 s at every crossing, because B is redrawn 1000 m over in A's new area copy and the avatar's physics sees that jump.

## Mockup: world slides instead of the player teleporting

`world_slide_mockup.mp4` (27 s, top-down, not to scale) compares the current teleport with the proposed approach, where each client moves the root of all four area copies by 1000 m at a crossing so the local player never teleports and other players' stations never jump. It ends with a card listing the costs and risks.

`world_slide_mockup_8.mp4`: the same mockup with one person in every room, all drawn as A's client sees them (people are placed in A's current copy, or parked if A's copy lacks their room).

`distance_mockup.mp4`: who A's client should show, by how many rooms away each person is (everyone a different colour). 0 and 1 away: see and hear; 2 away: drawn normally too (the walls block the view); 3 away: the open question (they would stand on a footprint you can see into); 4 away: would stand in your own room. A walks 3 -> 2 -> 1 so the distances shift. Ends with the options for 3 away.
