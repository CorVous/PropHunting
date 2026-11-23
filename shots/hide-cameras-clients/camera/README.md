# Hider's VRChat camera turned on over OSC (2026-11-23, main f6d2658, 2 desktop clients)

Clients launched with their own OSC ports (--osc=9000:127.0.0.1:9001 and 9010:127.0.0.1:9011).
OSC was on in settings (UI.Settings.Osc=1). Commands were sent over UDP from a script: no keys or mouse input.

- /usercamera/Mode i 1, 2, 6 (photo, stream, drone) and /usercamera/Pose were sent to the hider. VRChat echoed
  /usercamera/Mode [n] on the out port each time, but the camera never actually opened: no viewfinder or drone
  view on the hider's own screen, and /usercamera/Capture saved no photo (it was also tried on a standing, unseated client).
  No /usercamera/Pose output was ever sent.
- So the hunter's screen (01, 03, 04, 06) shows no lens. That is because no camera existed, not because HIDE CAMERAS hid it.
  A and B are inconclusive.

01 A (switch OFF): hunter 2.5 m from the hider's pot plant, after Mode 1 was sent. No lens.
02 A: the hider's screen after Mode 1. No viewfinder.
03 A: hunter crop around the prop: before / photo / drone. Identical.
04 A: sheet; rows are photo, stream, stream with Pose set, drone. Left: hider. Right: hunter.
05 Lobby after round 1, hider's Mode still 6. Shows the other avatar with no drone near it.
06 B (switch ON): rows are base, Mode 1 sent to the hider (900 m east), then Mode 1 + Capture on the standing client.
   Left: hunter. Right: hider.
