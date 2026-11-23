# Hide cameras: real-input user camera test (2026-11-23 13:35-13:42)

World f6d2658 (main), 2 desktop clients, pilot camTest: hunter parked 2.5 m from the hider's prop. Round 1 switch OFF, round 2 switch ON; same roles both rounds.
Camera opened in the hider client with real clicks (Esc > Camera tab > Photo Camera) while seated in the station; Mode/Pose then by OSC.

Result: the hunter never saw the hider's lens or drone, OFF or ON, even with the lens posed 1.2 m in front of the hunter (the hider's camera view showed the hunter). So B passes but OFF gives no positive control.

- `01_hider_off_quickmenu.png`: HIDER | switch OFF | Esc quick menu opened while seated in the Immobilize station (13:37:25)
- `02_hider_off_camera_tab.png`: HIDER | switch OFF | quick menu > Camera tab: Photo / Stream / Drone available
- `03_hider_off_photo_open.png`: HIDER | switch OFF | PHOTO camera OPEN while seated in the station (real clicks)
- `04_hunter_off_photo_default.png`: HUNTER | switch OFF | PHOTO, lens default spot (ghost) | 2.5 m from hider's chair: no lens visible
- `05_hider_off_photo_posed.png`: HIDER | switch OFF | PHOTO lens posed by OSC to (9.8,1,19.4), 1.2 m in front of hunter: hider's camera sees the hunter
- `06_hunter_off_photo_posed.png`: HUNTER | switch OFF | same moment: lens should be ~1.2 m in front, centre of view: NOT visible
- `07_hider_off_lens_solid.png`: HIDER | switch OFF | Lens Visibility set Off/Ghost/SOLID -> Solid
- `08_hunter_off_lens_solid_roundend.png`: HUNTER | switch OFF | lens Solid, round-end reveal (hazard stripes): still no lens visible
- `09_hider_on_photo_open.png`: HIDER | switch ON | PHOTO camera reopened while seated (round 2, hiders' copy)
- `10_hunter_on_photo_default.png`: HUNTER | switch ON | PHOTO, lens Solid at default spot | 2.5 m from hider's chair: no lens
- `11_hider_on_photo_posed.png`: HIDER | switch ON | PHOTO lens posed to (8.54,1,21.0), between prop and hunter: sees the hunter
- `12_hunter_on_photo_posed.png`: HUNTER | switch ON | same moment: no lens between hunter and chair
- `13_hider_on_stream_posed.png`: HIDER | switch ON | STREAM mode (OSC Mode 2), lens posed between prop and hunter
- `14_hunter_on_stream_posed.png`: HUNTER | switch ON | STREAM: no lens visible
- `15_hider_on_drone.png`: HIDER | switch ON | DRONE mode (OSC Mode 6), drone in front of hunter
- `16_hunter_on_drone.png`: HUNTER | switch ON | DRONE: no drone visible
