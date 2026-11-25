# TestRoom thumbnail: where it was found

Picked: `testroom_thumbnail_cropped.png` (800x600), the VRChat SDK's cropped world thumbnail.
Also included: `testroom_thumbnail_captured_uncropped.png`, the SDK's raw capture it was cropped from.

The SDK writes the capture to the Windows temp folder, not into Assets. The TestRoom project's
SDK capture render texture (Packages/com.vrchat.base/.../Resources/ThumbnailCapture.renderTexture)
was written at 10:48:49 today, between the two files below, which ties them to the TestRoom editor.
No image under TestRoom/Assets changed in the last 4 hours; nothing named thumbnail/preview in Assets.

| Candidate | Size | Modified (local) | Notes |
|---|---|---|---|
| %TEMP%\11fee77d47a09094aa602e7952543362_cropped.png | 235,713 B | 2026-11-25 10:49:08 | SDK crop, the thumbnail (picked) |
| %TEMP%\793ce7f628ed53247ade847e29a1f7ec_captured.png | 350,856 B | 2026-11-25 10:48:41 | SDK raw capture of the same view |
| TestRoom\Packages\com.vrchat.base\Editor\VRCSDK\Dependencies\VRChat\Resources\ThumbnailCapture.renderTexture | 885 B | 2026-11-25 10:48:49 | render texture asset only, no pixels |
| %TEMP%\0393e4aafde25134a822e0ba9dbdd302_cropped.png | 91,459 B | 2026-11-24 04:40:49 | older SDK crop (yesterday, another project) |
| %TEMP%\cd68c6127951f75439a9fb76b23ffc9b_captured.png | 157,065 B | 2026-11-24 04:38:31 | older SDK capture |
| Downloads\AI Inside.png | - | last 3 h | unrelated download |

Originals were copied, not modified.
