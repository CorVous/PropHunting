# Standard Triplanar (ours) vs Synty Triplanar Basic: feature by feature

Black-box test: Synty used only through the Material API and renders. Batch-mode Unity 2022.3.22f1, built-in pipeline, 2026-11-28.
Generated textures: Top = green hex cells + round-bump normal; Sides = grey brick + bevelled brick normal; marker = "R" + up arrow (yellow top / blue sides).
"mean diff" = mean absolute pixel difference between the Synty and Ours renders (0% = identical).

| # | Feature | Verdict |
|---|---------|---------|
| 1 | Top vs Sides albedo split (cube, sphere, cube from below, 45° ramp) | **Same**. Up faces use Top; side and down faces use Sides on both. The 45° ramp blends both textures the same way. With a flat normal map assigned (1b) the diff is 0.1–0.3%. |
| 1/6 | Normal-map slots left **empty** | **Different: Synty bug.** With no normal map assigned, Synty's shading normal is bent 90° on every face, so a face lit head-on renders black (probe: lit-head-on brightness 0.00 on all 6 faces). Ours with empty slots matches Unity Standard exactly. See feature_1 (dark Synty faces) and feature_6 (4 of 6 faces dark). |
| 2 | Top vs Sides normal maps | **Same direction.** The probe estimates the shading normal within 1–3° of the face normal on all 6 faces for both shaders, with near-identical per-light brightness. The 0.5–2.6% diff left over comes from smoothness (see 9): on faces turned away from the light, ours still shows bricks through reflections and Synty is flat dark. |
| 3 | FallOff | **Same for 1, 5, 10, 20** (0.5% diff). Synty goes up to 100 and its **default is 100**. At 50 and 100 Synty renders a **black band** on curved surfaces wherever no projection dominates (sphere goes dark around the 45° zone). Ours caps the slider at 20 and defaults to 10. |
| 4 | Tiling | **Same.** Same scale and direction (larger value = more repeats) at 0.5, 1, 3 and 2.5. Pattern position matches exactly on all 6 faces (4b: 0.0% diff, cube at an odd offset). |
| 5 | World space | **Same.** On both the texture stays put in the world when the object moves, rotates or scales. |
| 6 | Orientation / mirroring | **Same.** On both shaders all 6 faces read the right way round ("R" not mirrored, arrow pointing up / +Z) under these camera conventions. |
| 7 | Every other Synty property | Synty exposes **only 6 properties**: FallOff (0–100, default 100), Tiling (0–10, default 1), Sides, SidesNormal, Top, TopNormal, plus a hidden `__dirty`. Each has an equivalent in ours. **Per-texture Tiling/Offset fields** in the texture slots: both ignore them (0.0% change, 7b). |
| 9 | Lighting model | **Same, apart from the smoothness default.** Synty matches ours at metallic 0 / smoothness 0 (0.0% diff). Our default smoothness is 0.2, slightly shinier. Fog, shadow casting/receiving and point lights (forward-add) work on both. Both shaders: same pass list (Forward base/add, Deferred, ShadowCaster, Meta), GPU instancing, fog variants, lightmap variants. One unexplained detail: the ball's shadow on the top face looks a bit larger/darker on Synty. |
| 8 | Ours only | Emission (top/side maps + HDR colour), top/side metallic + smoothness, top/side tints, Normal Strength, Occlusion map, Object Space toggle. **Synty Basic has none of these.** |

Not tested: light baking with each shader (lightmap/Meta pass), VR single-pass stereo, mobile/Quest.
