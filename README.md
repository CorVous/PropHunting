# TestRoom: the CRT TV (branch crt-tv)

The companion cube is now a small CRT TV: one per room, with the cube's pickup, sync, beam and gold glow. Its screen shows one shared YouTube or other URL video through libretro's crt-guest-advanced. That's the full 12-pass preset, ported to Unity CustomRenderTextures (GPL v2+, by guest(r) and Dr. Venom).

- `tv_sheet.png`: room 1 close-up, room 48 close-up, the TV in white room 3, the TV in black room 50.
- `crt_compare.png`: the chain's 640x480 input (a still room shot standing in for the video) next to its 1280x960 output.
- `crt_zoom.png`: the output at 3x, showing scanlines, the mask, glow and curvature.

The pictures are editor renders. The screen shows a frame the chain produced in Play mode from a still image, because a video URL can't play in the editor.

## GPU cost of the chain

About 0.37 ms per frame for all 12 passes, on an RTX 4070 Ti. It runs once per frame however many TVs are on screen.

Method: in Play mode, frame time with the chain idle against the chain updated 8 times per frame, divided by 8.

## Not done yet

VRC Light Volumes (screen light on avatars and walls) needs Cor's OK to add the third-party package. It wasn't installed.
