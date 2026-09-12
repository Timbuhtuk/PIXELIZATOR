using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class LibraryChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "history-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string libraryPath = Path.Combine(folder, "library");
        string firstPath = Path.Combine(folder, "first.png"), secondPath = Path.Combine(folder, "second.png");
        using var logical = Fixtures.Logical(3, 172);
        using var first = Fixtures.Expand(logical, 16);
        first.Save(firstPath);
        using var second = new Bitmap(600, 480);
        using (var drawing = Graphics.FromImage(second)) drawing.Clear(Color.CornflowerBlue);
        second.Save(secondPath);
        var window = Create();
        try
        {
            // Exercise the actual drop event, including a mixed batch with one unreadable file.
            string invalid = Path.Combine(folder, "bad.png"); File.WriteAllText(invalid, "not an image");
            var data = new DataObject(DataFormats.FileDrop, new[] { firstPath, secondPath, invalid });
            // WPF constructs these internally; create the same routed event without moving the user's mouse.
            var drop = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [data, DragDropKeyStates.None, DragDropEffects.Copy, window, new System.Windows.Point(0, 0)], null)!;
            drop.RoutedEvent = DragDrop.PreviewDropEvent;
            window.RaiseEvent(drop); Wait();
            Require(Get<ListBox>("sourceStrip").Items.Count == 2, "Drop did not add both valid images");
            Require(Get<TextBlock>("statusLabel").Text.Contains("Не удалось открыть: 1"), "Mixed drop failure was not reported");
            var sources = Get<ListBox>("sourceStrip").Items.Cast<SourceEntry>().ToArray();
            Get<ListBox>("sourceStrip").SelectedItem = sources[0]; Pump();
            Require(Get<Bitmap>("_sourceImage").Size == first.Size, "Source strip did not switch image");
            Get<IntegerInput>("gridCellInput").Value = 16;
            Click("alignButton"); Wait();
            Get<IntegerInput>("widthInput").Value = 160;
            Get<IntegerInput>("heightInput").Value = 120;
            Get<ComboBox>("paletteInput").SelectedIndex = 1;
            Get<CheckBox>("ditheringInput").IsChecked = true;
            Get<IntegerInput>("quantizationColorsInput").Value = 8;
            Click("processButton"); Wait();
            Require(sources[0].Generations.Count == 2, "A generation replaced earlier history");
            Require(Get<CheckBox>("ditheringInput").IsChecked == true, "Restoring while busy lost dithering");
            using var downscaled = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            Get<ListBox>("generationStrip").SelectedItem = sources[0].Generations[0]; Pump();
            Require(Get<bool>("_resultIsAlignment") && Get<Bitmap>("_downscaledResult").Size == first.Size, "Generation strip did not restore alignment");
            Require(Get<IntegerInput>("gridCellInput").Value == 16, "Alignment options not restored");
            Get<ListBox>("generationStrip").SelectedItem = sources[0].Generations[1]; Pump();
            Require(Get<IntegerInput>("widthInput").Value == 160 && Get<IntegerInput>("quantizationColorsInput").Value == 8, "Downscale options not restored");
            EqualPixels(downscaled, Get<Bitmap>("_downscaledResult"));

            // Both axes and both directions: different image dimensions must use fractional offsets.
            Get<ComboBox>("zoomInput").SelectedIndex = 9; Pump();
            var sourceScroll = (ScrollViewer)Get<PixelPreview>("sourcePreview").Children[0];
            var resultScroll = (ScrollViewer)Get<PixelPreview>("resultPreview").Children[0];
            Require(sourceScroll.ScrollableWidth > 0 && resultScroll.ScrollableWidth > 0 && resultScroll.ScrollableHeight > 0, "Zoom did not create scrollable previews");
            sourceScroll.ScrollToHorizontalOffset(sourceScroll.ScrollableWidth * .7);
            sourceScroll.ScrollToVerticalOffset(sourceScroll.ScrollableHeight * .4); Pump();
            Require(Near(resultScroll.HorizontalOffset / resultScroll.ScrollableWidth, .7) && Near(resultScroll.VerticalOffset / resultScroll.ScrollableHeight, .4), "Source-to-result scroll synchronization failed");
            resultScroll.ScrollToHorizontalOffset(resultScroll.ScrollableWidth * .2);
            resultScroll.ScrollToVerticalOffset(resultScroll.ScrollableHeight * .8); Pump();
            Require(Near(sourceScroll.HorizontalOffset / sourceScroll.ScrollableWidth, .2) && Near(sourceScroll.VerticalOffset / sourceScroll.ScrollableHeight, .8), "Result-to-source scroll synchronization failed");
            Get<ComboBox>("zoomInput").SelectedIndex = 8; Pump();
            Require(Near(sourceScroll.HorizontalOffset / sourceScroll.ScrollableWidth, .2) && Near(resultScroll.HorizontalOffset / resultScroll.ScrollableWidth, .2), "Zoom lost synchronized position");
            Get<ComboBox>("zoomInput").SelectedIndex = 0; Pump();
            Get<ListBox>("sourceStrip").SelectedItem = sources[1]; Pump();
            Require(Get<PixelPreview>("resultPreview").Image is null, "Other source shows previous source result");
            Get<IntegerInput>("gridCellInput").Value = 8; Click("alignButton"); Wait();
            Require(sources[1].Generations.Count == 1 && sources[0].Generations.Count == 2, "Histories leaked across sources");
            Get<ListBox>("sourceStrip").SelectedItem = sources[0]; Pump();
            EqualPixels(downscaled, Get<Bitmap>("_downscaledResult"));
            Invoke("LoadImage", firstPath);
            Require(Get<ListBox>("sourceStrip").Items.Count == 2 && sources[0].Generations.Count == 2, "Duplicate import lost or duplicated history");
            Capture("library.png");
            window.Width = window.MinWidth; window.Height = window.MinHeight; Capture("library-minimum.png");
            Require(Get<ListBox>("sourceStrip").ActualWidth > 350 && Get<ListBox>("generationStrip").ActualWidth > 350 && Get<PixelPreview>("resultPreview").ActualHeight >= 50, "Filmstrips hide previews at minimum size");
            second.Save(firstPath);
            Invoke("LoadImage", firstPath);
            Require(Get<ListBox>("sourceStrip").Items.Count == 3 && sources[0].Generations.Count == 2 && Get<PixelPreview>("resultPreview").Image is null, "Changed file did not create a separate source");
            window.Close();
            // Removing the originals proves that the application saved independent source snapshots.
            File.Delete(firstPath); File.Delete(secondPath);
            window = Create();
            var restored = Get<ListBox>("sourceStrip").Items.Cast<SourceEntry>().ToArray();
            Require(restored.Length == 3 && restored[0].Generations.Count == 2 && restored[1].Generations.Count == 1, "History did not survive restart");
            Get<ListBox>("sourceStrip").SelectedItem = restored[0]; Pump();
            EqualPixels(downscaled, Get<Bitmap>("_downscaledResult"));
            Require(Get<IntegerInput>("quantizationColorsInput").Value == 8, "Restart lost generation parameters");
            Invoke("SaveResult", Path.Combine(folder, "restored-export.png"));
            using var exported = new Bitmap(Path.Combine(folder, "restored-export.png")); EqualPixels(downscaled, exported);
            // A damaged entry must not hide other saved sources or overwrite anything.
            string damaged = Path.Combine(libraryPath, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(damaged);
            File.WriteAllText(Path.Combine(damaged, "source.json"), "broken");
            var store = new ImageLibrary(libraryPath);
            Require(store.Load().Count == 3 && store.LoadWarnings.Count == 1, "Damaged metadata blocked intact history");
            Console.WriteLine("  Library: real drop, independent histories, saved parameters, bidirectional scroll, restart without originals and export passed.");
        }
        finally { window.Close(); }

        MainWindow Create()
        {
            var result = new MainWindow(libraryPath) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.Show(); Pump(); return result;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(25);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Library operation timed out"); Pump();
        }
        void Capture(string name)
        {
            Pump(); window.UpdateLayout();
            var root = Get<Grid>("rootLayout"); root.Background = window.Background;
            Pump(); window.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", name))); encoder.Save(stream);
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < .003;
    private static void EqualPixels(Bitmap a, Bitmap b)
    {
        Require(a.Size == b.Size, "History changed dimensions");
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++) Require(a.GetPixel(x, y).ToArgb() == b.GetPixel(x, y).ToArgb(), "History changed pixels");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
