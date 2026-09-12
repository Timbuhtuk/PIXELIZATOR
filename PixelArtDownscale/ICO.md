# ICO export

`PixelArtDownscale.IconExporter` is shared by the desktop application and CLI. It accepts a file or `Bitmap`, including the result of downscaling, sprite processing, grid alignment or cell reduction. No additional dependencies are required.

## Capabilities

- PNG, JPG/JPEG, BMP, GIF and TIF/TIFF use the standard `System.Drawing.Bitmap` decoder. GIF uses the first frame; TIFF uses the first page. File conversion honors EXIF orientation.
- One ICO contains multiple square frames. Defaults are **16, 24, 32, 48, 64, 128 and 256 px**. Any unique integer sizes from 1 to 256 are accepted and written in ascending order.
- Color and alpha are stored in 32-bit PNG frames inside the ICO. `Smooth` interpolates while resizing; `NearestNeighbor` selects source pixels without color blending. At 1:1 scale, RGBA values are copied exactly.
- Every size is generated directly from the supplied image. Small inputs are enlarged when necessary.
- `Contain` (the default) centers the image, preserves its aspect ratio and adds transparent padding. `Cover` crops a centered square to fill the frame; `Stretch` stretches the whole image into a square.
- Existing files are protected. `overwrite: true` permits replacement, but `Convert` rejects using the source path as the output.
- The complete ICO is encoded in memory, written to a temporary file beside the destination, then moved into place. Processing errors or cancellation before that move preserve an existing output. Missing destination directories are created.

## Solid background removal

Set `RemoveBackground = true` in `IconExportOptions`. `BackgroundTolerance` ranges from 0 to 100% (default 8). `BackgroundColor` accepts an explicit color or `null` to infer it from the edges. Removal runs once before resizing. All matching pixels, including enclosed areas, become transparent; other pixels retain their RGB and alpha. In automatic mode, a predominantly transparent border leaves the image unchanged. An explicit color can be supplied for an already transparent image.

`BackgroundRemover.Remove` returns a separate preview bitmap, leaves the source unchanged and supports cancellation. `CreateFrame` accepts a prepared image; `Encode`, `Save` and `Convert` apply the option themselves. This removes a solid color, without segmenting an object from a complex photographic background. Foreground pixels matching the background color will also be removed.

## Converting a file

```csharp
using PixelArtDownscale;

IconExporter.Convert("photo.jpg", "photo.ico");

IconExporter.Convert("sprite.png", "sprite.ico", new IconExportOptions
{
    Sizes = new[] { 16, 32, 48, 256 },
    ResizeMode = IconResizeMode.NearestNeighbor,
    FitMode = IconFitMode.Contain
});
```

## Exporting a processed result

```csharp
using System.Drawing;
using PixelArtDownscale;

using var source = new Bitmap("sprite.png");
var result = new PixelArtDownscaler().Process(source, new DownscaleOptions
{
    TargetWidth = 32,
    TargetHeight = 32,
    SpriteMode = true,
    Palette = PaletteKind.None,
    Quantization = QuantizationMethod.MedianCut,
    QuantizationColors = 256
});
using var cropped = result.CroppedSource;
using var processed = result.Downscaled;

IconExporter.Save(processed, "sprite.ico", new IconExportOptions
{
    ResizeMode = IconResizeMode.NearestNeighbor
});
```

This example needs an input at least 32 × 32 because of the downscaler's size constraint. ICO export itself supports enlargement. The exporter does not rerun quantization or sprite processing; palette and alpha-threshold options apply during the preceding processing stage. Use `Convert` directly for an ordinary photo or logo, and `PixelArtDownscaler` when a pixel-art effect is wanted.

## Integration API

| Method | Purpose |
| --- | --- |
| `Convert(inputPath, outputPath, options?, overwrite?, cancellationToken?)` | Load a file, apply EXIF orientation, create and save an ICO |
| `Save(source, outputPath, options?, overwrite?, cancellationToken?)` | Save an in-memory source or selected result |
| `Encode(source, options?, cancellationToken?)` | Return a complete ICO as `byte[]` without file operations |
| `CreateFrame(source, size, resizeMode?, fitMode?, cancellationToken?)` | Create one preview `Bitmap` |

`Save`, `Encode` and `CreateFrame` neither modify nor dispose the supplied bitmap and do not apply EXIF rotation; it is assumed to have the intended orientation already. The caller disposes the bitmap returned by `CreateFrame`. Do not modify the source during export; a UI can pass a copy for background work.

Methods are synchronous; use `Task.Run` for WPF background processing. Cancellation is checked between frames, within exact-resizing rows and before writing the output. Standard GDI+ decoding and smooth resizing cannot be interrupted midway through a call.

Invalid sizes, modes or output extensions raise `ArgumentException` / `ArgumentOutOfRangeException`; an occupied destination raises `IOException`; cancellation raises `OperationCanceledException`. Decoder and filesystem errors propagate to the caller, which should present a useful message.

## Format and compatibility

The ICO contains PNG frames and targets modern Windows. Older icon editors may not support these frames. Exporting 4/8-bit DIB-based ICO files with masks is not implemented. SVG, WebP, AVIF and HEIC inputs are outside the supported formats.

For WPF viewing of a saved ICO, use `IconBitmapDecoder`; for a preview before saving, use `CreateFrame`. In .NET 8, `System.Drawing.Icon` may select a smaller frame when asked for 256 px from a multi-size ICO because of the zero-valued width field. Checks decode every frame with WIC and load sizes 16–256 through Windows `LoadImageW`.

## Checks and interfaces

Run from the repository root:

```powershell
dotnet build Pixelizator.sln -c Release
dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --invariants
```

The eight `IconExportChecks` groups cover the ICO directory, PNG data, WIC and Windows loading, RGBA and source immutability, resizing modes, major input formats, TIFF/EXIF, existing downscale results, constraints, cancellation and file protection. They are also run by `Build-Standalone.ps1`.

The CLI exposes the shared library through `tessera ico`; see the [CLI reference](../docs/CLI.md) and `tessera ico --help`. In the desktop application, the second home banner opens the ICO workspace with file loading and dropping, editor source/result selection, sizes, fitting modes and previews. `IconWorkspaceChecks` run with `--ui` and also cover navigation, stale-preview cancellation, file protection and narrow layouts.
