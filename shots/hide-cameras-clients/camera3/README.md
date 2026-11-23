# Camera test 3: World anchor (2026-11-23, 13:54 to 14:08)

Two desktop clients on the built world from hco2 (pilot camTest, layerPlan "01"). The hunter parks 2.5 m from the hider's prop, facing it. The hider's Photo Camera was set by real clicks to Anchor World and Lens Solid. The lens was placed with OSC /usercamera/Pose.

Settings found: Anchor Default, Lens Ghost, Stream resolution HD 720p. All three were set back afterwards.

## A: HIDE CAMERAS off (round 1, hider = client 0)
- Lens at (22.65, 0.9, 7.28), halfway between the prop (21.40, 7.28) and the hunter (23.90, 7.28), facing the hunter: **the hunter sees the lens** (05, 06).
- First pose (08): the lens was placed for the hunter's first parking spot. The pilot then parked the hunter somewhere else, so the lens was off its line of sight and was not seen. Camera test 2 probably missed it the same way.
- Anchor Default at the same spot (09, 10, taken during END): **also visible**. The anchor setting makes no difference to whether the lens can be seen.

## B: HIDE CAMERAS on (round 2, hider = client 1)
- With the switch on, the hider's own position reads x+900: the hider plays in a copy of the map (head at (923.70, 0.99, 16.72)).
- Lens placed in the hider's copy, between the prop and where the hunter is (922.76, 0.9, 17.47): the hunter sees **no lens** (13).
- Lens placed at the same spot in the real map (22.76, 0.9, 17.47): the hunter sees **no lens**, and the hider's viewfinder shows only fog (15, 16).
- The only mark at the centre of the hunter's view is the reticle, which is there before any pose too (17).
- Stream camera, both spots: no lens seen (19). The pilot hunter may have shifted by then.
- World anchor holds still: the hider walked 1.7 m (z 16.72 to 15.02) and no new Pose came out on OSC (18).

Drone: not tested; the round ran out.
