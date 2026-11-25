# TestRoom: dark rooms, room 50 climax, two-step lag

Code: branch many-rooms in D:\VRChat Worlds\TestRoom.

- `before_after_45_52.png`: rooms 45-52, columns before/after unpainted, before/after painted (editor renders).
- `client_rooms_check.png`: frames from a real 2-client run, rooms 3, 5, 45, 47, 49, 51, 52, 53.
- `room50_climax.mp4` (43 s, client recording): the doorway view as you walk in, the slow 20 s look-around (the other player shows as a dark shape against the star), and the other player's view.
- `room50_frames.png`: frames from the look-around, walker (right) and the other player (left).
- `tones_1_6.png`: rooms 1-6, unpainted (top) and painted (bottom).

Changes:
1. Near-black dark rooms. Grey value from t (0 white .. 1 black) is now 0.03 + 0.93 (1-t)^1.5 (was a straight line from 0.96 to 0.04). Rooms with t 0.86-1 come out between about 0.08 and 0.03, and the darkest lean to ink-black teal. The fog now takes the room's own tone (unpainted, moving to painted as the room gets painted), so far walls no longer haze pale in dark rooms. Pale lines carry the shapes (the minimum line contrast still applies). Rooms 52 and 53 are both the darkest tone; the light level there is 0.
2. Room 50: the great star is about twice as thick: a 6.4 m vertical spike through floor and ceiling, arms 4.4 m across, four short diagonal rays, near-white material with pale lines and rim. A soft additive halo card sits behind it (TestRoom/Halo, camera-facing). The constellation net is thicker and brighter, the outgoing ring is thicker and near-white, and the well's dots are bigger and brighter. All motion is unchanged and slow.
3. The unpainted tone lags two rooms: default(n) = t(n-2), with wrap (default(1) = t(103), default(2) = t(104)). Rooms 1-3 start white, room 4 at the first grey step. On the climb each unpainted room starts two steps darker than its painted tone; 53 and 54 start black.
4. Fix: room 9's black puddle had picked up room 104's tone (white) after the move to 104 rooms; it is black again.

Client test (2 desktop clients): rooms 1-6, jump to 40, walk to 56; 10 crossings, 537 flips, no Udon errors; test-camera look-aways in 5, 45, 50, 55: 0 flips each.
