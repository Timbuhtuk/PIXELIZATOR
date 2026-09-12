using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class LibraryRemovalChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "removal-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var library = new ImageLibrary(Path.Combine(folder, "library"));
        using var logical = Fixtures.Logical(3, 172);
        using var bitmap = Fixtures.Expand(logical, 8);
        var seeded = new List<SourceEntry>();
        for (int i = 0; i < 10; i++)
        {
            string path = Path.Combine(folder, $"image-{i + 1}.png"); bitmap.Save(path);
            seeded.Add(library.Import(path, seeded));
        }
        for (int i = 0; i < 10; i++)
            seeded[0].Generations.Add(library.SaveGeneration(seeded[0], bitmap, null,
                new GenerationEntry { IsAlignment = true, CellSize = 8, Caption = $"ВЫРОВНЕННАЯ СЕТКА · вариант {i + 1}", PreservesTransparency = true }));
        var promoted = library.ImportGeneration(seeded[0], seeded[0].Generations[8], seeded);
        string exported = Path.Combine(folder, "exported.png"); bitmap.Save(exported);
        var window = Create();
        try
        {
            var sources = Get<ListBox>("sourceStrip");
            var generations = Get<ListBox>("generationStrip");
            var original = sources.Items.Cast<SourceEntry>().Single(s => s.Id == seeded[0].Id);
            sources.SelectedItem = original; Pump();
            Get<ComboBox>("zoomInput").SelectedIndex = 9; Pump();
            Capture("thumbnail-scrollbars.png");
            foreach (var bar in Descendants<ScrollBar>(window)) VerifyStyle(bar);
            var previewScroll = (ScrollViewer)Get<PixelPreview>("sourcePreview").Children[0];
            VerifyScroll(previewScroll, Orientation.Vertical);
            VerifyScroll(previewScroll, Orientation.Horizontal);
            VerifyScroll(Descendants<ScrollViewer>(sources).First(), Orientation.Horizontal);
            VerifyScroll(Descendants<ScrollViewer>(generations).First(), Orientation.Horizontal);

            Get<CheckBox>("advancedToggle").IsChecked = true;
            var log = Get<TextBox>("processingLog");
            log.Text = string.Join(Environment.NewLine, Enumerable.Range(1, 40).Select(i => $"Строка журнала {i}"));
            Pump(); log.BringIntoView(); Pump();
            foreach (var bar in Descendants<ScrollBar>(log)) VerifyStyle(bar);
            VerifyScroll(Descendants<ScrollViewer>(log).First(), Orientation.Vertical);
            Capture("scrollbars-log.png");
            var zoom = Get<ComboBox>("zoomInput"); zoom.MaxDropDownHeight = 80; zoom.IsDropDownOpen = true; Pump();
            var popup = (Popup)zoom.Template.FindName("PART_Popup", zoom);
            foreach (var bar in Descendants<ScrollBar>(popup.Child)) VerifyStyle(bar);
            zoom.IsDropDownOpen = false;
            Get<CheckBox>("advancedToggle").IsChecked = false;

            var selected = original.SelectedGeneration;
            var unselected = original.Generations[0];
            Remove(generations, unselected);
            Require(ReferenceEquals(selected, original.SelectedGeneration) && ReferenceEquals(selected, generations.SelectedItem), "Removing another result changed selection");
            Require(!File.Exists(library.ResultPath(original, unselected)), "Deleted result remains on disk");
            Remove(generations, selected!);
            Require(!original.Generations.Contains(selected!) && original.SelectedGeneration == original.Generations[^1] && generations.SelectedItem == original.SelectedGeneration, "Selected result did not switch to neighbour: " + Get<TextBlock>("statusLabel").Text);
            Invoke("SetProcessingState", true);
            var blocked = original.Generations[0];
            var button = RemoveButton(generations, blocked);
            Require(!button.IsEnabled, "Delete button is enabled during processing");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(original.Generations.Contains(blocked) && File.Exists(library.ResultPath(original, blocked)), "Busy deletion was allowed");
            Capture("thumbnail-scrollbars-busy.png");
            Invoke("SetProcessingState", false);
            while (original.Generations.Count > 0)
            {
                int before = original.Generations.Count;
                Remove(generations, original.SelectedGeneration ?? original.Generations[0]);
                Require(original.Generations.Count < before, "Result deletion stopped: " + Get<TextBlock>("statusLabel").Text);
            }
            Require(generations.Visibility == Visibility.Collapsed && Get<PixelPreview>("resultPreview").Image is null &&
                !Get<Button>("saveButton").IsEnabled && Get<PixelPreview>("sourcePreview").Image is not null, "Deleting last result left stale UI");

            // Keep a generation on a different source to check cascading deletion of its history.
            var other = sources.Items.Cast<SourceEntry>().Single(s => s.Id == seeded[1].Id);
            other.Generations.Add(library.SaveGeneration(other, bitmap, null, new GenerationEntry()));
            Remove(sources, other);
            Require(sources.SelectedItem == original && !Directory.Exists(Path.GetDirectoryName(library.SourcePath(other))), "Deleting an unselected source switched selection or left its history");

            // A locked directory must report a failure without removing the source from the UI.
            using (var locked = new FileStream(library.SourcePath(original), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Remove(sources, original);
                Require(sources.Items.Contains(original) && Get<TextBlock>("statusLabel").Text.Contains("Не удалось"), "Failed deletion removed a visible source");
            }
            Remove(sources, original);
            Require(!sources.Items.Contains(original) && sources.SelectedItem is SourceEntry && Get<PixelPreview>("sourcePreview").Image is not null, "Selected source did not switch to neighbour");
            var independent = sources.Items.Cast<SourceEntry>().Single(s => s.Id == promoted.Id);
            sources.SelectedItem = independent; Pump();
            Require(Get<PixelPreview>("sourcePreview").Image!.Size == bitmap.Size && File.Exists(library.SourcePath(independent)), "Deleting parent broke a promoted source");
            window.Close(); window = Create();
            sources = Get<ListBox>("sourceStrip"); generations = Get<ListBox>("generationStrip");
            Require(sources.Items.Count == 9 && sources.Items.Cast<SourceEntry>().All(s => s.Id != original.Id && s.Id != other.Id), "Deleted entries returned after restart");
            while (sources.Items.Count > 0)
            {
                int before = sources.Items.Count;
                Remove(sources, sources.SelectedItem ?? sources.Items[0]);
                Require(sources.Items.Count < before, "Source deletion stopped: " + Get<TextBlock>("statusLabel").Text);
            }
            Require(Get<PixelPreview>("sourcePreview").Image is null && Get<PixelPreview>("resultPreview").Image is null &&
                !Get<Button>("processButton").IsEnabled && !Get<Button>("saveButton").IsEnabled && !Get<Button>("alignButton").IsEnabled &&
                generations.Visibility == Visibility.Collapsed && library.Load().Count == 0, "Deleting last source left stale state/history");
            Require(seeded.All(s => File.Exists(s.OriginalPath)) && File.Exists(exported), "Deletion touched original/exported files");
            Invoke("LoadImage", seeded[0].OriginalPath); Pump();
            Require(sources.Items.Count == 1 && Get<Button>("processButton").IsEnabled, "Cannot import after deleting all sources");
            Console.WriteLine("  Deletion/theme: thumbnail buttons, neighbouring selections, busy/locked files, restart, independent copies, originals/export protection and both scrollbar axes passed.");
        }
        finally { Invoke("SetProcessingState", false); window.Close(); }

        MainWindow Create()
        {
            var result = new MainWindow(library.DirectoryPath) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.Show(); typeof(MainWindow).GetMethod("ShowEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(result, null); Pump(); return result;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        Button RemoveButton(ListBox strip, object entry)
        {
            strip.ScrollIntoView(entry); Pump();
            var container = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(entry);
            return Descendants<Button>(container).Single();
        }
        void Remove(ListBox strip, object entry) { RemoveButton(strip, entry).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void VerifyStyle(ScrollBar bar)
        {
            Require(ReferenceEquals(bar.Template, window.FindResource(bar.Orientation == Orientation.Horizontal ? "HorizontalScrollBar" : "VerticalScrollBar")), "Native scrollbar escaped the theme");
        }
        void VerifyScroll(ScrollViewer viewer, Orientation orientation)
        {
            Pump();
            var bar = Descendants<ScrollBar>(viewer).First(b => b.Orientation == orientation);
            Require(bar.IsVisible && bar.Maximum > 0, "Expected scrollbar is missing");
            if (orientation == Orientation.Vertical) viewer.ScrollToTop(); else viewer.ScrollToLeftEnd();
            Pump();
            var arrow = Descendants<RepeatButton>(bar).Last();
            ((RoutedCommand)arrow.Command).Execute(arrow.CommandParameter, arrow); Pump();
            Require((orientation == Orientation.Vertical ? viewer.VerticalOffset : viewer.HorizontalOffset) > 0, "Styled scrollbar arrow no longer scrolls");
            var thumb = Descendants<Thumb>(bar).Single();
            double before = bar.Value;
            thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
            // Filmstrips scroll by whole items, so move far enough to cross an item boundary.
            thumb.RaiseEvent(new DragDeltaEventArgs(orientation == Orientation.Horizontal ? 160 : 0, orientation == Orientation.Vertical ? 80 : 0));
            thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false)); Pump();
            Require(bar.Value > before, "Styled scrollbar thumb no longer scrolls");
        }
        void Capture(string name)
        {
            var root = Get<Grid>("rootLayout"); root.Background = window.Background; Pump(); window.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", name))); encoder.Save(stream);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
