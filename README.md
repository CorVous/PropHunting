# TestRoom: CORTAO vs PPv2 ambient occlusion

The 100-room TestRoom (branch `ao-compare`, off `hundred-rooms`). Every tile in a sheet comes from the same camera at 1920x1080, rendered in the editor.

- `sheets/<room>_sheet.png`: the whole frame, one tile per method.
- `sheets/<room>_closeup.png`: the centre of the frame at 1:1 pixels.
- Rooms: 1 First Sheet (plinths), 11 Dinner for Thirteen (chairs), 3 Pencil Forest, 12 Stair Knot, 48 Star Nursery, and 38 Pearl Bed painted (a dark room).

## Methods

| Tile | What it is |
|---|---|
| No AO | baseline |
| PPv2 MSVO | Post Processing v2 3.4.0, Multi-scale Volumetric Obscurance, intensity 1 |
| PPv2 SAO | PPv2 Scalable Ambient Obscurance, intensity 1, radius 1 m, quality High |
| CORTAO 8 / 16 rays | shader-only port; 8 samples with the 8-ray table, 16 samples with the 16-ray table; radius 1 m, max screen radius 0.2 |
| + filter | CORTAO's sparse spatial filter (large, symmetric), per pixel |
| 64 seeds averaged | CORTAO 16 averaged over 64 random seeds: roughly what CORTAO plus TAA would settle to. Editor only. |

## How CORTAO runs in a VRChat world

VRChat worlds can't use compute shaders, command buffers or custom post effects, so the port is shader-only:

- It's a full-screen quad, drawn after the opaque geometry. Its vertices are clip-space corners and its bounds are huge, so it is never culled.
- `CORTAOMainPass.h` from theagentd/CORTAO 420b168 (MIT) is included unchanged. Its input callbacks are fed from `_CameraDepthTexture`:
  - Packed depth and normal are built per sample. Normals are reconstructed from depth, taking the side nearer the centre on each axis.
  - There is no prepass mip chain, so every sample reads full-resolution depth.
  - The occluder table is a UNORM `Texture3D`, using the repo's SM 6.7 workaround.
- The filter is the repo's weights and pattern, run per pixel through GrabPass. The repo's version works on 2x2 quads with Gather.
- It uses single-pass-instanced macros: depth through `UNITY_DECLARE_DEPTH_TEXTURE` and the grabs through `UNITY_DECLARE_SCREENSPACE_TEXTURE`, with per-eye projection.
- **Depth texture:** the ink shader is unlit, so it gained a ShadowCaster pass to appear in the camera depth texture. PPv2's AO turns depth on itself. For CORTAO, a "depth light" (directional, hard shadows, culling only an empty layer) makes VRChat's camera render a depth texture.
  - Checked in the editor with the camera's depth mode off: with the light off CORTAO darkens 0 pixels; with it on, 19%.

## GPU time (RTX 4070 Ti, editor)

These are frame times for render plus readback, median of 5 interleaved rounds, rooms 11 and 3 (they agree within 0.1 ms). "Cost" is the method minus No AO. The VR-sized frame is 3840x2400, 9.2 MP: about both eyes of a Quest 3 or Index at 100%.

| Method | 1080p total | 1080p cost | VR-sized total | VR-sized cost |
|---|---|---|---|---|
| No AO | 1.11 | — | 2.80 | — |
| PPv2 MSVO | 1.37 | +0.26 | 3.65 | +0.85 |
| PPv2 SAO | 1.61 | +0.50 | 5.20 | +2.40 |
| CORTAO 8 | 1.71 | +0.60 | 6.07 | +3.27 |
| CORTAO 8 + filter | 2.02 | +0.91 | 7.50 | +4.70 |
| CORTAO 16 | 2.20 | +1.09 | 8.95 | +6.15 |
| CORTAO 16 + filter | 2.52 | +1.41 | 10.28 | +7.48 |

The raw numbers are in `timings.txt`. In VR, CORTAO 16 + filter takes about 7.5 ms of an 11.1 ms (90 Hz) frame on a 4070 Ti, which is too much as it stands. CORTAO 8 + filter (+4.7 ms) is the realistic setting.

Why the port is slower than the original:
- There is no packed mip chain, so cache behaviour is poor.
- Every sample reconstructs its normal from 4 extra depth reads.
- It runs at full resolution.

A half-resolution pass, or a CustomRenderTexture prepass, would cut this a lot.

## What it looks like

- **CORTAO** gives the tightest, most correct contact shadows: chair feet, pencil bases, stair risers, clams on the floor. It has no large halos.
  - The 64-seed average is clean and closest to "physically right".
  - A single frame is noisy: grain on flat areas and sparkle on thin geometry (chair legs, pencils), worst at 8 rays. VRChat has no TAA in VR, so this noise is what players see. The filter removes most of the grain but not the sparkle on thin edges.
- **PPv2 MSVO** is cheap and smooth, but broad: a soft grey in every room corner, and weak contact under small objects.
- **PPv2 SAO** draws big dark halos around door frames and walls at this radius. It's the most "dirty" look.

## Caveats

- Quest/Android: neither PPv2 nor GrabPass works there, and the port needs a PC GPU. Not handled.
- Any camera without a depth texture (mirrors, VRChat's photo and stream cameras) would read a stale depth texture with the CORTAO quad on. Not tested.
- Nothing was tested in VRChat clients (per Cor's recent preference). These are editor renders.
- No compute "real CORTAO" reference was built. The main pass algorithm is the repo's code, included unchanged, so the only differences are the inputs: depth-reconstructed normals, no mips, and no TAA.
