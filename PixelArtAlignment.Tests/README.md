# Pixel art alignment evaluation

Alignment is implemented in the separate `PixelArtAlignment` library. It does not invoke the downscaler, quantizer or palettes. The evaluator in `AlignmentQuality.cs` was written before the algorithm and does not reuse its grid, detector or color selection.

## What the percentage means

The reference is a known pixel image enlarged into integer square blocks. Evaluation covers the whole canvas, including background and edges. The test cell size is known in advance and is the same for the reference and algorithm.

Four components range from 0 to 1:

- **GridPurity**: fraction of pixels matching the most frequent ARGB within each reference cell. Partial edge cells use their actual area.
- **PixelAccuracy**: fraction of exact reference matches at the same coordinates.
- **ColorRecall**: average recovered-pixel fraction calculated separately for each reference color. A large background cannot hide complete loss of a rare color.
- **BoundaryF1**: F1 for horizontal and vertical transitions between neighboring source pixels, requiring exact coordinate matches.

The final score is `100 × min(GridPurity, PixelAccuracy, ColorRecall, BoundaryF1)`. Hidden RGB in fully transparent pixels does not affect it. Partially transparent colors are compared exactly, including alpha.

Evaluator checks cover a perfect result, solid fill, whole-cell translation, wrong colors and every distortion type. A solid fill or recoloring fails even with a perfect grid.

The series thresholds were set before tuning: overall mean **at least 90**, mean for each distortion type **at least 90**, and worst case **at least 75**. This measures reconstruction on a specific dataset; it is not the probability of correctness for an arbitrary AI-generated image. A single `--evaluate` result passes at 90 or above.

## Datasets and recorded results

Four artwork types are used: a transparent sword with a stepped outline and isolated details; a building scene with narrow windows; a checkerboard with rare colored dots; and random rectangular shapes with holes. Each has six conditions: perfect, global shift, unequal cell sizes, smooth local displacement, antialiasing, and color contamination in 1.8% of source pixels. Distorted inputs are generated from references independently of the algorithm.

| Dataset | Images | Cell sizes | Seeds | Mean | Worst |
| --- | ---: | --- | --- | ---: | ---: |
| Development | 96 | 4, 6, 8, 11 | 101 | 95.93 | 76.44 |
| Additional regressions | 96 | 5, 9 | 9137, 52021 | 97.11 | 84.62 |
| Final validation, without tuning against its results | 96 | 7, 10 | 34781, 88169 | 96.53 | 76.66 |

These are recorded benchmark results, not a new measurement for every release. All three series met every threshold. The second dataset initially tested unseen data and exposed a rare failure; after the fix it became a regression set. The third dataset therefore provides the independent final validation. The evaluator, reference dimensions, distortions and thresholds were not weakened during tuning.

On the final set: perfect images and global shift scored 100; unequal sizes 93.41; local displacement 96.15; antialiasing 96.34; contamination 93.30. Benchmark runs write reports under `artifacts/alignment/`, including every case, score components and timing. The `*-reference`, `*-input` and `*-aligned` PNG files support visual comparison.

## Additional checks

Invariant checks cover source immutability, canvas preservation with partial edge cells, no new visible colors or alpha values, repeated alignment, empty images, invalid options and detection on 16 perfect grids.

WPF checks run alignment and downscaling independently and compare the latter with the library. They cover profiles, color count and weighting, preview alpha, 800% zoom, input validation and PNG export with source protection. They also cover window controls, library history and deletion, grid reduction, ICO navigation, preview cancellation and narrow layouts. Synthetic images and screenshots are written under `artifacts/wpf/verification`.

The CLI has its own integration suite, including argument validation, image formats, icon export and file protection.

## Running checks

Use Windows with .NET SDK 8 or later and the .NET 8 Runtime. The `dotnet` command must be on PATH. Run from the repository root:

```powershell
dotnet run --project PixelArtAlignment.Tests -c Release
dotnet run --project PixelArtAlignment.Tests -c Release -- --benchmark --holdout
dotnet run --project PixelArtAlignment.Tests -c Release -- --benchmark --fresh
dotnet run --project PixelArtAlignment.Tests -c Release -- --invariants --ui
dotnet run --project Pixelizator.Cli.Tests -c Release
```

Evaluate an output against a known correct image:

```powershell
dotnet run --project PixelArtAlignment.Tests -c Release -- --evaluate actual.png reference.png --cell-size 8
```

Without a reference, grid regularity can be measured, but not an honest percentage of correctly preserved detail. Automatic cell detection has its own diagnostic confidence, separate from the quality score above. For highly irregular or fractional input grids, choose an explicit integer output cell size. Arbitrary scales and temporal consistency in animation are outside this test set.
