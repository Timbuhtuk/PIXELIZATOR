# Using Tessera

Download `Tessera.exe` or `Tessera-win-x64.zip` from [GitHub Releases](https://github.com/Timbuhtuk/PIXELIZATOR/releases/latest). These Windows x64 builds run offline without installing .NET. Earlier releases used the Pixelizator name.

The application interface currently uses Russian labels. This guide describes the actions in English.

## Library and editor

Open images with the Open button, Ctrl+O or drag and drop. The home screen shows your library, image dimensions and result counts. Open a card to edit its source. The back arrow returns to the library; the layout adapts to narrower windows.

The editor has three starting profiles:

| Profile | Intended use | Initial settings |
| --- | --- | --- |
| Background | Broad color areas and composition | K-Means LAB, automatic block selection, RGB step 16 |
| Scene | Shapes, lighting and color transitions | K-Means LAB, automatic block selection, RGB step 16 |
| Details | Sprites, outlines and transparency | Median Cut, no additional palette, full image, 75% alpha coverage |

You can change the size, color count and other options. Processing adds a new result to the history. Grid alignment is a separate operation that preserves the canvas dimensions and selects original colors. Reducing grid cells to one pixel compresses an aligned result without quantizing it again.

Previews preserve aspect ratio and enlarge pixels without smoothing. Ctrl+mouse wheel changes zoom; source and result scrolling stay synchronized. Choose a checkerboard, dark or light preview background.

The upper filmstrip contains results for the current source; the lower filmstrip contains sources. Drag a result to the source filmstrip to process it independently. Removing a library item leaves external originals and exported files intact. Save exports the selected result; use PNG for transparency.

## Icons

The icon banner opens a separate ICO workspace. Open or drop an image, or use the editor's source or result. Select several standard sizes from 16 to 256 pixels, or add a custom size from 1 to 256. Choose the resizing and fitting modes, inspect the previews and save all selected sizes into one ICO file.

Optional background removal makes a solid color transparent. It can infer the color from the edges or use white or black; tolerance ranges from 0 to 100%. The preview and saved ICO show the same transparency, and the original stays unchanged. Matching colors inside the foreground may also be removed. This is color-based removal, not object segmentation for complex photographic backgrounds.

Ctrl+O opens an image for the currently active workspace.

## Storage and updates

The library is saved automatically in `%LOCALAPPDATA%\Pixelizator\Library`. This historical path is retained by Tessera to preserve existing images and histories. To move the library to another computer, copy that directory separately.

Download the new executable to update the application. GitHub releases are generated automatically, but the installed application does not replace itself.

For more detail, see [grid alignment](../PixelArtAlignment/README.md), [ICO export](../PixelArtDownscale/ICO.md) and the [CLI reference](CLI.md).
