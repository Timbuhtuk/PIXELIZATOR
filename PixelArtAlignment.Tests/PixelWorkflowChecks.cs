using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtAlignment;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class PixelWorkflowChecks
{
    public static void Run()
    {
        foreach (int pitch in new[] { 2, 3, 7, 11 })
        {
            using var logical = Fixtures.Logical(3, 1729);
            using var expanded = Fixtures.Expand(logical, pitch);
            using var partial = expanded.Clone(new System.Drawing.Rectangle(0, 0, expanded.Width - pitch / 2, expanded.Height - pitch / 2), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var compact = PixelGridReducer.Reduce(partial, pitch);
            EqualPixels(compact, logical, "Partial-cell roundtrip");
        }

        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "pixel-workflow-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string library = Path.Combine(folder, "library");
        string input = Path.Combine(folder, "rich-grid.png");
        using var expected = new Bitmap(33, 25);
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
            expected.SetPixel(x, y, Color.FromArgb((x + y) % 3 == 0 ? 97 : 255, x * 7, y * 9, (x * 11 + y * 13) % 256));
        using var full = Fixtures.Expand(expected, 4);
        using var original = full.Clone(new System.Drawing.Rectangle(0, 0, 129, 97), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        original.Save(input);
        var window = Create();
        try
        {
            Require(!Get<Button>("compactGridButton").IsEnabled, "Empty state permits grid reduction");
            Invoke("LoadImage", input);
            Get<IntegerInput>("gridCellInput").Value = 4;
            Click("alignButton"); Wait();
            var parent = (SourceEntry)Get<ListBox>("sourceStrip").SelectedItem;
            var aligned = parent.SelectedGeneration!;
            Require(aligned.CellSize == 4 && Get<Button>("compactGridButton").IsEnabled, "Alignment did not enable grid reduction");
            // Reduction must use the stored pitch and selected output, ignoring current downscale settings.
            Get<IntegerInput>("gridCellInput").Value = 8;
            Get<IntegerInput>("widthInput").Value = 2;
            Get<IntegerInput>("heightInput").Value = 3;
            Get<IntegerInput>("quantizationColorsInput").Value = 1;
            Get<ComboBox>("paletteInput").SelectedIndex = 4;
            Click("compactGridButton"); Wait();
            var reduced = parent.SelectedGeneration!;
            Require(reduced.IsGridReduction && reduced.CellSize == 4 && reduced.ParentGenerationId == aligned.Id && parent.Generations.Count == 2, "Reduction metadata/history missing");
            EqualPixels(expected, Get<Bitmap>("_downscaledResult"), "Reduction changed ARGB, partial edges or palette");
            EqualPixels(original, Get<Bitmap>("_sourceImage"), "Reduction changed source");
            Require(!Get<Button>("compactGridButton").IsEnabled, "Compact output can be reduced again");
            Invoke("SaveResult", Path.Combine(folder, "one-pixel.png"));
            using (var saved = new Bitmap(Path.Combine(folder, "one-pixel.png"))) EqualPixels(expected, saved, "PNG export");

            Get<ComboBox>("zoomInput").SelectedIndex = 9; Pump(); // 1600%
            var preview = Get<PixelPreview>("resultPreview");
            var other = Get<PixelPreview>("sourcePreview");
            var pointer = new Point(preview.ActualWidth / 2, preview.ActualHeight / 2);
            var before = preview.ImagePointAt(pointer);
            bool handled = (bool)Invoke("ChangePreviewZoom", preview, 120, pointer, ModifierKeys.Control)!;
            Pump();
            Require(handled && preview.Zoom == 32 && other.Zoom == 32, "Ctrl+wheel did not zoom both previews");
            var after = preview.ImagePointAt(pointer);
            Require(Math.Abs(before.X - after.X) < .004 && Math.Abs(before.Y - after.Y) < .004, "Zoom moved the point under the cursor");
            Require(!(bool)Invoke("ChangePreviewZoom", preview, -120, pointer, ModifierKeys.None)! && preview.Zoom == 32, "Ordinary wheel was intercepted");
            Invoke("ChangePreviewZoom", preview, -120, pointer, ModifierKeys.Control); Pump();
            Require(preview.Zoom == 16 && other.Zoom == 16, "Ctrl+wheel down failed");
            Get<ComboBox>("zoomInput").SelectedIndex = 0; Pump();
            double fit = other.EffectiveZoom;
            Invoke("ChangePreviewZoom", other, 120, new Point(100, 80), ModifierKeys.Control); Pump();
            Require(other.Zoom > fit, "Wheel from Fit reduced the zoom");
            Get<ComboBox>("zoomInput").SelectedIndex = 0; Pump();

            var data = (DataObject)Invoke("CreateGenerationDragData", parent, reduced)!;
            // A result is accepted over the source tray or source preview, not over the result itself.
            var rejected = Drop(data, Get<PixelPreview>("resultPreview"));
            Require(rejected.Effects == DragDropEffects.None && Get<ListBox>("sourceStrip").Items.Count == 1, "Internal drop accepted outside sources");
            Drop(data, Get<ListBox>("sourceStrip")); Wait();
            var imported = (SourceEntry)Get<ListBox>("sourceStrip").SelectedItem;
            Require(Get<ListBox>("sourceStrip").Items.Count == 2 && imported.OriginGenerationId == reduced.Id && imported.Generations.Count == 0, "Drag did not create an independent source");
            Require(imported.Label.Contains("pixels_33x25") && imported.AlignedCellSize == 1, "Imported result lost its name/grid metadata");
            EqualPixels(expected, Get<Bitmap>("_sourceImage"), "Drag changed the result's pixels");
            Drop(data, Get<PixelPreview>("sourcePreview")); Wait();
            Require(Get<ListBox>("sourceStrip").Items.Count == 2, "Repeated result drop created a duplicate");
            Get<IntegerInput>("widthInput").Value = 16; Get<IntegerInput>("heightInput").Value = 12;
            Click("processButton"); Wait();
            Require(imported.Generations.Count == 1 && parent.Generations.Count == 2, "Imported result cannot be processed independently");
            var alignedData = (DataObject)Invoke("CreateGenerationDragData", parent, aligned)!;
            Drop(alignedData, Get<PixelPreview>("sourcePreview")); Wait();
            var importedAligned = (SourceEntry)Get<ListBox>("sourceStrip").SelectedItem;
            Require(importedAligned.AlignedCellSize == 4 && Get<Button>("compactGridButton").IsEnabled, "Dragging alignment lost its cell size");
            Click("compactGridButton"); Wait();
            EqualPixels(expected, Get<Bitmap>("_downscaledResult"), "Promoted alignment reduction");
            Capture("pixel-workflow.png");
            window.Close(); window = Create();
            var restored = Get<ListBox>("sourceStrip").Items.Cast<SourceEntry>().ToArray();
            Require(restored.Length == 3 && restored.Single(s => s.Id == importedAligned.Id).AlignedCellSize == 4, "Restart lost promoted-source metadata");
            Get<ListBox>("sourceStrip").SelectedItem = restored.Single(s => s.Id == parent.Id); Pump();
            Require(restored.Single(s => s.Id == parent.Id).SelectedGeneration!.IsGridReduction, "Restart lost reduction kind");
            EqualPixels(expected, Get<Bitmap>("_downscaledResult"), "Restart changed compact result");
            Console.WriteLine("  Pixel workflow: exact grid reduction/alpha/partial cells, history, Ctrl-wheel anchors and result-to-source drops passed.");
        }
        finally { window.Close(); }

        MainWindow Create()
        {
            var instance = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            instance.Show(); Pump(); return instance;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
        void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        DragEventArgs Drop(DataObject data, UIElement target)
        {
            var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(0, 0)], null)!;
            e.RoutedEvent = DragDrop.PreviewDropEvent;
            target.RaiseEvent(e);
            return e;
        }
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(25);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Operation timed out"); Pump();
        }
        void Capture(string name)
        {
            var root = Get<Grid>("rootLayout"); root.Background = window.Background;
            Pump(); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine("artifacts", "wpf", "verification", name)); encoder.Save(stream);
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void EqualPixels(Bitmap expected, Bitmap actual, string message)
    {
        Require(expected.Size == actual.Size, message + ": dimensions");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), message + $": {x},{y}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
