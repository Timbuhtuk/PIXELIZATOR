# Pixel grid alignment

Grid alignment is independent of downscaling and palette selection.

The standalone Windows x64 desktop build is `artifacts/standalone/Tessera.exe`; the CLI is `artifacts/cli/tessera.exe`. Download packaged builds from [GitHub Releases](https://github.com/Timbuhtuk/PIXELIZATOR/releases/latest).

In the editor, open an image, set a cell size in the grid alignment section and run alignment. Automatic size detection is optional. Save the result as PNG. Downscaling and palette settings do not affect alignment; the Process action still downscales the original source. To compress the aligned grid, select the result and reduce each grid cell to one pixel. Drag a result into the source filmstrip for further processing.

```powershell
.\artifacts\cli\tessera.exe align input.png -o aligned.png --cell-size 4
.\artifacts\cli\tessera.exe align input.png -o aligned-auto.png --json
.\artifacts\cli\tessera.exe align --help
```

The canvas retains its original width and height. Output uses equal square cells, with partial cells retained at the right and bottom edges. Each cell receives the ARGB value of an original pixel; the algorithm does not reduce the palette or compute new visible colors. Hidden RGB values in fully transparent pixels may become zero. Already aligned images are preserved exactly. PNG is required to avoid compression damage to the grid.

## Algorithm

1. Build an alpha-aware transition map, suppressing isolated contamination only in that auxiliary map. Original pixels remain unchanged and available for color selection.
2. Use an explicit cell size or estimate it from recurring transitions. For regular patterns, check the global lattice to avoid confusing a two-pixel pattern with one pixel.
3. For sharp boundaries, use dynamic programming to select ordered grid lines. Penalize transitions inside cells, excessive displacement and size violations. A weak global-shift estimate is not forced onto the artwork.
4. For blurred or locally shifted boundaries, estimate the local grid phase, keeping it consistent with the previous strip and bounded relative to the global grid. Sampling regions retain positive width.
5. Vote among original ARGB values in each region, weighted by overlap area and a preference for the center. Fill the corresponding regular output cell with the winning original color.

## API

```csharp
using var result = new PixelGridAligner().Align(source,
    new GridAlignmentOptions { CellSize = 4 });
result.Aligned.Save("aligned.png", ImageFormat.Png);
```

`CellSize = null` enables automatic detection. `Adaptive = false` disables adaptation to local shifts. `GridAlignmentResult` contains the image, cell size, diagnostic detection confidence and timing. Dispose the result; the input `Bitmap` remains owned by the caller.

The method cannot guarantee recovery of details smaller than the chosen cell, or infer a uniquely correct artistic interpretation of an ambiguous image. See the [evaluation protocol](../PixelArtAlignment.Tests/README.md) for the tested range and reproducible measurements.
