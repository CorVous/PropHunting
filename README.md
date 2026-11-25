# TestRoom: 104 rooms (shots)

Branch in D:\VRChat Worlds\TestRoom: `many-rooms` (local only), latest commit b34928e.

- `testroom_104_demo.mp4` (2:15): arrival, pattern close-up, the walk, lighting at rooms 13/26/39/65/78 seen by the other player, room 50 (climax), 52 black and the climb back (8x), 104 -> 1.
- `pattern_closeup.mp4`: 11 s at a door jamb, the seamless 3D pattern with its slow ring creep.
- `room50_climax.mp4`: room 50 from the walker, a slow 20 s look-around.
- `rooms_all_painted.png` / `rooms_all_unpainted.png`: every room 1-104 from its walkway corner (editor render).
- `seam_*.png`: pattern across wall, floor, ceiling, doorway, hallway and objects.
- `review_*.png`: close-ups of rooms, including 45-51 (the rewrite), 53-104 (ascent), 54/57/59.
- `perf_editor_estimate.txt`: renderers in view per room (upper bound on draw calls) and triangles.

Test runs (two desktop clients, local test instance), final run:
- Full loop 1 -> 104 -> 1, then 5 rooms back: 55 crossings, 2652 flips, 1078 s, no Udon halts.
- Crossing (pool slide): 0.04 ms average, 0.12 ms max; first frame after a crossing 9.5 ms average (was 30-50 ms before the fixes).
- Frame time per room: 7.0 ms average (desktop client). Spikes over 50 ms in 6 of 109 rooms (481 ms on join in room 1).
- Look-away with a test camera rendering the room (stand-in for the VRChat photo camera): 0 flips in 21 look-aways, against 12.9 per normal look-away.
- Forced station exit (room 4): watchdog reseated at its next check. Manual respawn (room 8): holding cell, then back to room 8's corner. Avatar change via pedestal (room 12): seat kept.
- Join: the room-1 cell copy swapped for the real room with no teleport (log), under VRChat's join fade.
- Observer followed the walker every 13 rooms: the remote avatar darkens toward the black rooms and lightens again.
