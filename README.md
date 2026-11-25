# TestRoom: flat gold companion cube

- `reported_in_world.png`: Cor's screenshot from the uploaded world: the cube flat tan/gold, no stars.
- `cube_batching_before_after.png`: four cubes drawn together in the editor, the old glow shader (left) and the fixed one (right).

Cause: the cube's glow shader (TestRoom/Glow) draws its stars and edge from the cube's own object-space positions. When several cube copies are on screen (the 104 cubes each have copies, and the parked or neighbouring ones are often in view), Unity's dynamic batching merges them into one draw and the shader then receives world positions. The pattern maths saturates and the whole face takes the edge colour (0.75, 0.55, 0.25), the exact tan in the screenshot. That is why it happens only sometimes.

Fix: `"DisableBatching" = "True"` on the glow shader, and on the room 50 well and halo shaders, which also use object space. Branch many-rooms, commit 6f54f29. It needs a re-upload.
