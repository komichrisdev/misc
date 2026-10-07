# Beholder v3

A small, standalone Windows image-comparison app for artists and reviewers: compare references, check perspective and isometric construction, and export the whole canvas as one JPEG. Drop images onto an empty canvas; their tiles rebalance automatically around their proportions. No Hermes integration, accounts, uploads, telemetry, or network access.

![Beholder v3 image comparison canvas](docs/screenshot-color.png)

## Run

Extract `dist/Beholder-v3-Windows-x64.zip` and double-click **Beholder v3.exe**. No installer or administrator privileges are needed. Requires Windows 10/11 x64 with .NET Framework 4.8; there are no additional runtime packages.

The portable executable is also at `dist/Beholder v3.exe`, with its SHA-256 in `dist/Beholder v3.exe.sha256`. Older builds in this folder are not v3.

The executable is unsigned. Windows may display a SmartScreen warning for a newly downloaded build; only run a copy obtained from this repository or built from this source.

## Everyday controls

- **Drop files** anywhere, **Add images** / **Ctrl+O**, or **Ctrl+V** to paste an image or copied image files. PNG clipboard data takes precedence over a companion bitmap, so Flameshot screenshots paste with their visible pixels instead of appearing blank.
- **Auto tiles** follows image proportions. Two images stay side by side. **Equal tiles** uses equally sized comparison cells.
- Resize the window: every tile reflows to fit. Images keep their proportions and are never cropped at Fit.
- Mouse wheel over an image: zoom around the pointer. Drag the image: pan. **Fit all** / **Ctrl+0** resets the views.
- **B/W** / **Ctrl+B** toggles a black-and-white filter for all images, including newly added images. Turning it off restores the original colors; alpha and source files stay untouched.
- **Link views** / **Ctrl+L** synchronizes zoom and normalized image-relative pan. Turn it off for independent inspection.
- Drag a **filename bar** onto another tile to reorder.
- Double-click an image to focus it; double-click again or **Esc** to restore the grid.
- **F11** toggles fullscreen. **Esc** exits fullscreen.
- The tile's **x** or **Delete** removes the selected image. **Clear** / **Ctrl+Shift+X** empties the canvas. Original files are never deleted or written to.
- Dark-only canvas with a subtle checkerboard for transparency. No theme selector or bottom status bar; compact filename strips leave more room for the images.
- Import errors appear briefly over the canvas without reserving layout space. Click an error to dismiss it.
- Hover over a filename for its original pixel dimensions and full filename.

- **Save JPG** / **Ctrl+S** saves every image as a single composite JPEG with a canvas sized from the lowest-resolution source image rather than the window. With **Equal tiles** every cell uses that image's pixel dimensions; with **Auto tiles** the layout preserves cell ratios while the smallest image lands near native size. Tile zoom, pan, the B/W filter, and enabled perspective overlays are preserved. **Current limitation:** the export draws the display previews; source images wider or taller than 4096 pixels were decoded to bounded previews, so this is not yet a full-source-pixel-quality export for those files. All images are included even while one is focused; captions, menu chrome, and vanishing-point markers never appear.
- Click **Perspective ▾** to open a horizontal overlay row under the toolbar with every perspective control: checkboxes for **Vanishing points**, **Isometric grid**, **Pitch grid**, and **Wireframe cube**, the two angle sliders, and **Hide all overlays**, which clears them without closing the row. The cube follows the enabled vanishing-point guides, otherwise the pitch grid, otherwise the isometric grid; enabling the cube with no other mode selects isometric 2:1. Multiple guides can remain enabled together.
- **Vanishing points** / **Ctrl+P**: each click on an image places a point and draws a horizon and X/Y/Z construction lines rising from the image bottom (colors: horizon gold, X red, Y teal, Z blue — teal so the construction lines never match the green cube); additional points add converging sets. Drag to move; **Delete** removes the last-clicked point (falling back to the most recently placed one) even while the perspective row is open; hiding the guide keeps your points until you re-enable it.
- **Isometric grid** / **Ctrl+I**: a transparent checkerboard and three-family **black** isometric grid. The isometric slider follows the mouse continuously from 5° to 60° (no tick snapping) and still grabs the **2:1** (26.565°) and **30°** presets when you slide near them; Ctrl+I toggles on/off.
- **Pitch grid**: a **black** checkerboard plane that rotates around its horizontal axis with the continuous 15°–90° slider (follows the mouse, no tick snapping) — rows foreshorten toward edge-on and columns stay parallel and evenly spaced (a flat-plane rotation, not a perspective floor), and 90° is an exact top-down chessboard.
- **Wireframe cube**: translucent green edges (1.5x the grid stroke) with a transparent green face infill, projected to match the enabled guide. In pitch mode the cube stands on the rotated plane and foreshortens with it (at 90° it renders as a flat green square on the chessboard); in vanishing-point mode it follows the first three placed points (Z, X, Y); more points still draw their existing guides. **+ Cube** adds another cube.
- **Cube editing**: click a cube to select it; the selected cube shows grab handles — red **X**, teal **Y**, blue **Z** squares move it along that axis (drags track the mouse 1:1, including left/right in isometric); the white diamond at the far corner resizes it. Every cube keeps its own position and size while the projection, angle, and pitch still apply to all of them. Handles also work in vanishing-point mode; dragging an axis follows its projected direction without accumulating movement on repeated mouse events. Re-enabling **any** perspective checkbox resets it: vanishing points clear, isometric returns to 2:1, pitch returns to 35°, and the cube returns to its default position — so a cube dragged far away always comes back.
- **Link views** additionally copies the selected image's vanishing points to every image while linked; edits stay in sync. Isometric, pitch, and cube overlays draw on **every** image only while linked — otherwise they draw on the selected image alone, exactly like vanishing points.
## Supported inputs and boundaries

PNG, JPEG, BMP, GIF, TIFF and ICO use Windows' built-in image decoders. **WebP is built in**: lossy, lossless, transparency and the first frame of animated WebP are supported without installing a Windows codec. AVIF is optional and requires a compatible Windows imaging codec; unsupported files produce a visible error. SVG, RAW, PSD, PDF and video are not supported.

Animated and multi-page inputs show the first frame/page only. JPEG/TIFF EXIF orientation is applied. WebP ICC and EXIF metadata are not applied in this version; this is a comparison viewer, not a color-proofing tool. Unicode filenames and filenames containing spaces are supported. Dropped folders add supported files from that folder only (not recursively).

This first version is an in-memory workspace: closing it does not save the layout. Image previews are capped at 4096 pixels on their longest edge; the labels retain original dimensions. Zoom magnifies the preview, not a full-resolution pixel editor. There is a 64-image canvas limit, a 200 MB per-file limit and a 100-megapixel source-image limit. Memory usage scales with decoded image sizes.

## Build from source

No SDK download, NuGet restore, Node, browser bundle, or dependency installation is needed. The pinned native WebP decoder is already included as a compressed build resource. The build uses Windows' .NET Framework C# compiler and WPF assemblies.

In PowerShell, from this folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
& '.\dist\Beholder v3.exe'
```

Build and run the regression checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Tests use real WPF decoders, real generated image files and a routed FileDrop event through the live window. They write JUnit XML and an actual rendered comparison screenshot under `evidence/`. Generated fixture art is explicitly labelled as test imagery. A routed-event test is not a claim that an Explorer-to-window gesture was physically performed.

## Source map

- `src/Beholder.cs`: native window, image tiles and input interactions.
- `src/Layout.cs`: aspect-aware row partitioning and equal-cell layout.
- `src/Perspective.cs`: vanishing-point, pitch, isometric, and cube overlay drawing and geometry.
- `src/CompositeExporter.cs`: composite JPEG rendering and safe saving.
- `src/ImageLoader.cs`: image decoding, orientation, bounded previews, alpha-preserving grayscale and closed file streams.
- `src/WebPDecoder.cs`: bundled native WebP decoding and animated first-frame composition.
- `tests/Tests.cs`: regression and real-window integration harness.
- `beholder.ico`: original eye icon generated for this app.
- `app.manifest`: non-administrator launch and per-monitor DPI awareness.
- `build.ps1`: reproducible local build and test commands.

## Bundled WebP decoder

The official Google/libwebp **1.6.0 Windows x64** `dwebp.exe` is embedded in the app, compressed. On first WebP use it is unpacked to `%LOCALAPPDATA%/Beholder/codecs/libwebp-1.6.0/`. This is a versioned app cache, not an installer. Its SHA-256 is checked before use; it runs without a shell, console window, network access or administrator privileges. Source images are sent over stdin and decoded PNG pixels return over stdout.

Upstream BSD license, patent grant and author notices are in `third_party/libwebp/`. The portable ZIP includes them. The helper is pinned, not updated automatically. See `third_party/libwebp/NOTICE.md` for provenance and exact hashes.
