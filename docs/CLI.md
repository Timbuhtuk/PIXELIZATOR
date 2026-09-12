# Tessera CLI reference

The standalone Windows x64 CLI is `artifacts/cli/tessera.exe` after publication, or inside `Tessera-cli-win-x64.zip` in [GitHub Releases](https://github.com/Timbuhtuk/PIXELIZATOR/releases/latest). It downscales images, aligns pixel grids and exports ICO files. Supported inputs are PNG, JPEG, BMP, GIF and TIFF.

Run these examples from the repository root after building; replace the sample input paths with your own:

```powershell
.\artifacts\cli\tessera.exe --help
.\artifacts\cli\tessera.exe input.png -o result.png --width 64 --height 64 --palette db32
.\artifacts\cli\tessera.exe align input.png -o aligned.png --cell-size 4
.\artifacts\cli\tessera.exe ico input.png -o application.ico --sizes 16,32,48,256 --resize nearest-neighbor
```

The application interface and CLI messages currently use Russian. This reference describes the commands in English.

## Downscaling options

Without `--output`, the result is placed next to the source with the suffix `_downscaled.png`. Existing files require `--overwrite`; the source itself is protected.

| Option | Values and default |
| --- | --- |
| `<image>`, `-i`, `--input` | One input file; quote paths containing spaces |
| `-o`, `--output` | PNG, JPG/JPEG or BMP |
| `--width` | 1–3840; **48** |
| `--height` | 1–2160; **48** |
| `--sprite` | Full image without cropping, transparent background, output alpha 0/255; PNG output only |
| `--alpha-threshold` | 1–100%; **50**; requires `--sprite` |
| `--crop-horizontal` | **center**, left, right |
| `--crop-vertical` | **center**, top, bottom |
| `--palette` | none, **db16**, db32, nes, gameboy, step |
| `--palette-step` | 1–255; **32**; requires `--palette step` |
| `--quantization` | median-cut, **kmeans-lab**, kmeans-linear |
| `--quantization-colors` | 1–4096; **64**; upper bound for the initial palette |
| `--color-weights` | Off by default; weight each color by its pixel count |
| `--dithering` | Off by default; available for DB16, DB32, NES and GameBoy |
| `--threads` | 1–logical processor count; defaults to all |
| `--block-mode` | **automatic**, manual |
| `--brightness` | 0–100; **50**; manual mode only |
| `--contrast` | 0–100; **50**; manual mode only |
| `--saturation` | 0–100; **50**; manual mode only |
| `--edge` | 0–100; **50**; manual mode only |
| `--cropped-output` | Export the cropped source |
| `--preview-output` | Export a nearest-neighbor enlarged preview |
| `--preview-scale` | 1–100; **8**; requires `--preview-output` |
| `--overwrite` | Allow replacing an existing output |
| `--json` | Write the processing report as JSON |

Numbers are integers. Mode names are case-insensitive and options accept `--width=48` syntax. For an input path starting with a hyphen, use `--input=PATH` or place it after `--`. Help is `--help` / `-h`; the version is `--version`. Running without arguments prints help.

The output must not be larger than the input. Cropping removes the remainder along each axis so that the source dimensions are divisible by the target dimensions; crop options select its position.

`--palette none` disables the final fixed-palette mapping, but initial quantization still runs. `--quantization-colors` limits that stage; the actual number of colors can be smaller. DB16, DB32, NES, GameBoy and RGB step mapping run later and may reduce it further.

Without `--color-weights`, unique colors have equal weight. With it, K-Means averages use pixel counts, and Median Cut uses counts both when splitting groups and when averaging them. K-Means initialization and iteration counts remain unchanged. Sprite weights only include pixels with alpha at least 128. Weighting can help large color regions but lose rare accents; it is separate from block representative selection.

Manual-mode values describe the desired properties of the **color selected within each block**, rather than applying global brightness or contrast filters. The CLI divides 0–100 values by 100, matching the application's sliders.

## Transparent sprites

`--sprite` divides the entire image into a target grid with fractional block boundaries and no cropping. Coverage includes source alpha. Color selection and quantization use pixels with alpha at least 128 without mixing in a black background. A result pixel becomes opaque when coverage meets `--alpha-threshold` and a source color is available.

A 25% threshold keeps more of the edge; 75% narrows the silhouette. Sprite mode cannot be combined with cropping options or dithering. `--cropped-output` exports the full source. Set the target size manually; sprite downscaling does not automatically find the original pixel size or repack animation frames.

```powershell
.\artifacts\cli\tessera.exe sprite.png -o artifacts/sprite.png --sprite --width 256 --height 128 --alpha-threshold 50 --palette none --preview-output artifacts/sprite-preview.png --preview-scale 4
```

The image decoder is shared with the desktop application. Corrupt or unsupported inputs fail. PNG and BMP preserve output pixels losslessly; JPEG compression can change colors.

## Grid alignment

`tessera align` preserves the canvas and chooses colors and alpha from the source. Output must be PNG and defaults to `<name>_aligned.png`.

| Option | Meaning |
| --- | --- |
| `--cell-size N` | Explicit square cell size, at least 2 and no larger than the smaller image dimension |
| `--max-cell-size N` | Upper bound for automatic detection: 2–256; default 32 |
| `--rigid` | Disable adaptation to local grid shifts |
| `--overwrite`, `--json` | Shared output replacement and reporting options |

Omit `--cell-size` for automatic detection. Set it explicitly for ambiguous grids. See the [algorithm and API](../PixelArtAlignment/README.md).

## ICO export

The `ico` command (alias `icon`) creates a multi-size icon directly from an image, without reapplying a palette or quantization.

```powershell
.\artifacts\cli\tessera.exe ico result.png -o application.ico --sizes 16,24,32,48,64,128,256 --resize nearest-neighbor --fit contain
```

`--sizes` accepts unique integer sizes from 1 to 256 separated by commas. The default set is 16, 24, 32, 48, 64, 128 and 256. `--resize` accepts `smooth` or `nearest-neighbor`; the latter avoids color blending for pixel art. `--fit contain` preserves aspect ratio with transparent padding, `cover` fills the frame with a centered crop, and `stretch` fills it by stretching.

`--overwrite` and `--json` are supported. Without `--output`, the result is `<name>.ico` beside the input. Full command help is `tessera ico --help`. See the [ICO format and shared API](../PixelArtDownscale/ICO.md) for details, including background removal in the desktop workspace and library API.

## More examples

Quantize to at most 128 colors with frequency weighting and no fixed palette:

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/weighted.png --quantization-colors 128 --color-weights --palette none
```

Omit `--color-weights` to disable weighting. Omit the color count or use 64 to restore its default.

Manual color selection with bottom-right cropping and an RGB step palette:

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/manual.png --width 96 --height 64 --crop-horizontal right --crop-vertical bottom --palette step --palette-step 32 --quantization median-cut --block-mode manual --brightness 60 --contrast 70 --saturation 80 --edge 90 --threads 1
```

Export the crop and an 8x preview:

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/result.png --cropped-output artifacts/crop.png --preview-output artifacts/preview.png --preview-scale 8
```

## Reports and exit codes

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/result.png --json > artifacts/report.json
if ($LASTEXITCODE -ne 0) { throw "Processing failed" }
$report = Get-Content artifacts/report.json -Raw | ConvertFrom-Json
$report.dominantColor.hex
```

For downscaling, the report includes absolute paths, source/crop/result dimensions, effective options, the dominant color and stage timings in seconds. `--json` keeps standard output JSON-only; errors go to standard error. The report is printed only after all requested outputs have been saved successfully.

| Exit code | Meaning |
| --- | --- |
| 0 | Success, help or version |
| 1 | Reading, processing or saving failed; output exists without `--overwrite` |
| 2 | Invalid or unknown arguments, incompatible options, duplicate paths, unsupported output extension or oversized target |
