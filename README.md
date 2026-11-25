# TestRoom: ripple contrast + iridescence

Code: branch many-rooms in D:\VRChat Worlds\TestRoom, shader Assets/Shaders/TRInk.shader.

- `before_after_closeup.mp4` (9 s): left the previous shader, right the new one, gliding along a wall corner in room 1 (white), room 30 (mid grey) and room 52 (black), painted. Editor renders of the same scene, not a client recording.
- `before_after_frames.png`: the middle frame of each pair.
- `rooms_contrast_sheet.png`: rooms 1, 13, 26, 39, 52, 65, 78, 104 painted, then room 30 unpainted and painted.

Numbers (all in sRGB grey, 0 black .. 1 white):
- Lines run opposite to the base: dark ink on bases at or above 0.45 luminance, pale lines below. The ink always stays at least `_MinContrast` = 0.32 away from the base (pale side +0.40), so lines never fade into a mid-grey surface. The old shader cross-faded the ink between 0.3 and 0.5 luminance, so around rooms 26-40 and 65-80 the lines were nearly the base colour.
- The switch is a clean flip at 0.45, not a blend: any blend from dark ink to pale ink has to pass through the base colour on the way, which is exactly the fade you saw. A wall is one tone, so the flip never shows as an edge on a surface, only between a room and the next (or a wall and a darker object).
- Line strength: 0.60 unpainted, 0.85 painted (was 0.30 / 0.62).
- `_Iridescence` = 0.4 (0 turns it off): a zero-mean thin-film hue swing of up to about 0.06 on the lines and stars, a fifth of that on the base, stronger at grazing angles. The hue phase comes from view angle, the 3D pattern phase and the shared `_Flow` clock, so it slides slowly as you move your head and stays seamless. The flat-colour toggle is unaffected.
- Cost: a few extra ALU ops per pixel, no textures.
