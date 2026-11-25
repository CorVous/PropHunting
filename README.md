# TestRoom: world slide (local player never teleports)

Build: TestRoom local branch `world-slide` (5c5661b + regenerated assets 4ff9781). The previous version stays on `master`.

What changed
- Crossing a teleport zone no longer teleports the player. Each client moves the root of all four area copies by the right multiple of 1000 m, so the copy entered lands exactly under the player (the current copy always sits at world x = 0). The player keeps position, speed and facing and stays in their station.
- Other players are drawn by room distance along the loop, per viewer: 0, 1 or 2 rooms away at their area-local spot in the viewer's current copy (the two rooms 2 away share the diagonal footprint; walls block the view). 3 or 4 rooms away: parked far away, hidden and silent. Symmetric now: from room 3 you get rooms 1-5.
- Join and respawn: the spawn is a holding cell far below the level. A player waits there until seated in their station for 3 s, then enters room 1 of area 0. While in the cell everyone else keeps them parked.

Files
- `worldslide_watch.mp4` (2:40, real speed, A walker left, B watcher right):
  - 0:00-0:59, 2/3 hallway: A backs from room 3 to 2 and walks back, 3 times.
  - 0:59-1:55, 8/1 hallway: the same, 3 times; a third client (C) joins at about 1:00 and enters room 1 at about 1:12.
  - 1:55-2:40, "they move, you stay": B in room 3 looks into room 2 while A walks room 5 -> 6 -> 5 twice.
- `worldslide_before_after.mp4`: A's view at one 2/3 and one 8/1 crossing, old build (left) vs world slide (right), half speed.
- `worldslide_crossings_sheet.png`: all 12 crossings, 8 frames each from both views.
- `worldslide_tests_sheet.png`: the join and the "they move, you stay" checks.

Results
- No hair/cape whip on B at any of the 12 crossings (the old build whipped at every one).
- Every crossing logged a slide with the area before/after (1->0, 0->1 on 2/3; 0->3, 3->0 on 8/1); the walker never left their station on a crossing.
- Hitch: setting the root took 0.00-0.02 ms; the next 4 frames stayed at ~7 ms (one 10 ms frame while the third client was loading).
- Draw calls: unchanged. Nothing in TestRoom was ever batching-static (painting uses per-renderer property blocks) and there's no baked occlusion, so moving the root loses nothing.
- Join: C was seated in the hidden cell, VRChat's own extra re-seat happened there, then C entered room 1; both other clients first saw C at the room-1 entry spot.
- They move, you stay: A was parked for B on entering room 6 (local x 6.0) and shown again on entering room 5 (x 3.4-3.9); B's view of room 2 never showed A.
