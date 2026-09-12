using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class IconWorkspace : UserControl, IDisposable
{
    private Bitmap? _image;
    private string? _inputPath;
    private bool _disposed;
    private bool _busy;
    private int _revision;
    private CancellationTokenSource? _previewCancellation;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    public bool IsBusy => _busy;
    public bool IsRendering { get; private set; }
    public event EventHandler? BackRequested;
    public event EventHandler? SourceRequested;
    public event EventHandler? ResultRequested;
    public event EventHandler? BusyChanged;
    public event Action<string>? StatusChanged;
    public sealed record FramePreview(int Size, BitmapSource Image)
    {
        public string Label => $"{Size} × {Size}";
    }

    public IconWorkspace()
    {
        InitializeComponent();
        foreach (int size in new[] { 16, 24, 32, 48, 64, 96, 128, 256 })
        {
            var check = new CheckBox { Content = $"{size} × {size}", Tag = size, IsChecked = size != 96, Margin = new Thickness(0, 0, 22, 16) };
            check.Checked += (_, _) => SchedulePreview();
            check.Unchecked += (_, _) => SchedulePreview();
            iconSizes.Children.Add(check);
        }
        iconBackButton.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        iconOpenButton.Click += async (_, _) => await OpenAsync();
        iconSaveButton.Click += async (_, _) => await SaveDialogAsync();
        iconUseSourceButton.Click += (_, _) => SourceRequested?.Invoke(this, EventArgs.Empty);
        iconUseResultButton.Click += (_, _) => ResultRequested?.Invoke(this, EventArgs.Empty);
        iconCustomCheck.Checked += (_, _) => { iconCustomSize.IsEnabled = true; SchedulePreview(); };
        iconCustomCheck.Unchecked += (_, _) => { iconCustomSize.IsEnabled = false; SchedulePreview(); };
        iconCustomSize.ValueChanged += (_, _) => SchedulePreview();
        iconResizeMode.SelectionChanged += (_, _) => SchedulePreview();
        iconFitMode.SelectionChanged += (_, _) => SchedulePreview();
        iconRemoveBackground.Checked += (_, _) => { iconBackgroundSettings.Visibility = Visibility.Visible; SchedulePreview(); };
        iconRemoveBackground.Unchecked += (_, _) => { iconBackgroundSettings.Visibility = Visibility.Collapsed; SchedulePreview(); };
        iconBackgroundTolerance.ValueChanged += (_, _) => SchedulePreview();
        iconBackgroundColor.SelectionChanged += (_, _) => SchedulePreview();
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RefreshFramesAsync(); };
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    public void SetEditorAvailability(bool source, bool result)
    {
        iconEditorChoices.Visibility = source || result ? Visibility.Visible : Visibility.Collapsed;
        iconUseSourceButton.IsEnabled = source;
        iconUseResultButton.IsEnabled = result;
    }

    public static bool SupportsFile(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff";

    public async Task OpenAsync()
    {
        if (_busy || _disposed) return;
        var dialog = new OpenFileDialog { Title = "Изображение для иконки", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await LoadFileAsync(dialog.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (_busy || _disposed) return;
        SetBusy(true);
        try
        {
            using var image = await Task.Run(() => ReadImage(path));
            if (_disposed) return;
            SetImage(image, Path.GetFileName(path), path);
            StatusChanged?.Invoke("Изображение открыто. Выберите размеры и сохраните ICO.");
        }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось открыть изображение: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private static Bitmap ReadImage(string path)
    {
        using var metadata = new Bitmap(path);
        int orientation = metadata.PropertyIdList.Contains(0x112) && metadata.GetPropertyItem(0x112)?.Value is { Length: >= 2 } bytes
            ? BitConverter.ToUInt16(bytes, 0) : 1;
        var image = ImageLibrary.ReadBitmap(path);
        try
        {
            image.RotateFlip(orientation switch
            {
                2 => System.Drawing.RotateFlipType.RotateNoneFlipX,
                3 => System.Drawing.RotateFlipType.Rotate180FlipNone,
                4 => System.Drawing.RotateFlipType.Rotate180FlipX,
                5 => System.Drawing.RotateFlipType.Rotate90FlipX,
                6 => System.Drawing.RotateFlipType.Rotate90FlipNone,
                7 => System.Drawing.RotateFlipType.Rotate270FlipX,
                8 => System.Drawing.RotateFlipType.Rotate270FlipNone,
                _ => System.Drawing.RotateFlipType.RotateNoneFlipNone
            });
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    public void SetImage(Bitmap source, string label, string? inputPath = null)
    {
        if (_disposed) return;
        var preview = PixelPreview.ToBitmapSource(source);
        var copy = (Bitmap)source.Clone();
        _image?.Dispose();
        _image = copy;
        _inputPath = inputPath is null ? null : Path.GetFullPath(inputPath);
        iconSourceImage.Source = preview;
        iconFileLabel.Text = label;
        iconFileLabel.ToolTip = label;
        iconEmptyHint.Visibility = Visibility.Collapsed;
        SchedulePreview();
    }

    public IconExportOptions GetOptions()
    {
        var sizes = iconSizes.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (int)c.Tag).ToList();
        if (iconCustomCheck.IsChecked == true) sizes.Add(iconCustomSize.Value);
        return new IconExportOptions
        {
            Sizes = sizes.Distinct().Order().ToArray(),
            ResizeMode = iconResizeMode.SelectedIndex == 0 ? IconResizeMode.NearestNeighbor : IconResizeMode.Smooth,
            RemoveBackground = iconRemoveBackground.IsChecked == true,
            BackgroundTolerance = (int)iconBackgroundTolerance.Value,
            BackgroundColor = iconBackgroundColor.SelectedIndex switch { 1 => System.Drawing.Color.White, 2 => System.Drawing.Color.Black, _ => (System.Drawing.Color?)null },
            FitMode = (IconFitMode)Math.Clamp(iconFitMode.SelectedIndex, 0, 2)
        };
    }

    private void SchedulePreview()
    {
        if (_disposed) return;
        ++_revision;
        _previewCancellation?.Cancel();
        iconFrames.ItemsSource = null;
        IsRendering = _image is not null;
        UpdateSaveState();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    public async Task RefreshFramesAsync()
    {
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        int revision = ++_revision;
        var options = GetOptions();
        if (_disposed || _image is null)
        {
            iconFrames.ItemsSource = null;
            IsRendering = false;
            iconPreviewHint.Text = options.Sizes.Count == 0 ? "Выберите хотя бы один размер." : "Выберите размеры — они будут сохранены в одном ICO.";
            UpdateSaveState();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        var image = (Bitmap)_image.Clone();
        IsRendering = true;
        UpdateSaveState();
        try
        {
            var frames = await Task.Run(() =>
            {
                using (image)
                {
                    using var prepared = options.RemoveBackground ? BackgroundRemover.Remove(image, options.BackgroundTolerance, options.BackgroundColor, cancellation.Token) : null;
                    var preview = PixelPreview.ToBitmapSource(prepared ?? image);
                    var result = new List<FramePreview>();
                    foreach (int size in options.Sizes)
                    {
                        using var frame = IconExporter.CreateFrame(prepared ?? image, size, options.ResizeMode, options.FitMode, cancellation.Token);
                        result.Add(new FramePreview(size, PixelPreview.ToBitmapSource(frame)));
                    }
                    return (Frames: result, Preview: preview);
                }
            });
            if (revision != _revision || _disposed) return;
            iconFrames.ItemsSource = frames.Frames;
            iconSourceImage.Source = frames.Preview;
            iconPreviewHint.Text = options.Sizes.Count == 0 ? "Выберите хотя бы один размер." : "Выбранные размеры будут сохранены в одном ICO.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (revision == _revision) StatusChanged?.Invoke($"Не удалось построить предпросмотр: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            if (revision == _revision) { IsRendering = false; UpdateSaveState(); }
        }
    }

    private async Task SaveDialogAsync()
    {
        if (_busy || _image is null) return;
        iconCustomSize.Commit();
        if (GetOptions().Sizes.Count == 0) return;
        var dialog = new SaveFileDialog { Title = "Сохранить иконку", Filter = "Иконка Windows|*.ico", DefaultExt = ".ico", AddExtension = true,
            OverwritePrompt = true, FileName = Path.GetFileNameWithoutExtension(iconFileLabel.Text) + ".ico" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await SaveAsync(dialog.FileName, true); }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось сохранить ICO: {ex.Message}"); }
    }

    public async Task SaveAsync(string path, bool overwrite = false)
    {
        if (_busy || _disposed || _image is null) throw new InvalidOperationException("Сначала откройте изображение.");
        if (string.Equals(Path.GetFullPath(path), _inputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нельзя перезаписать исходное изображение.");
        var options = GetOptions();
        if (options.Sizes.Count == 0) throw new InvalidOperationException("Выберите хотя бы один размер.");
        using var image = (Bitmap)_image.Clone();
        SetBusy(true);
        StatusChanged?.Invoke("Сохранение ICO…");
        try
        {
            await Task.Run(() => IconExporter.Save(image, path, options, overwrite));
            StatusChanged?.Invoke($"Сохранено: {path}");
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        iconControls.IsEnabled = iconBackButton.IsEnabled = !busy;
        UpdateSaveState();
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSaveState() => iconSaveButton.IsEnabled = !_busy && !IsRendering && _image is not null && GetOptions().Sizes.Count > 0;

    private void UpdateLayoutForWidth()
    {
        bool compact = ActualWidth < 800;
        iconContent.Margin = new Thickness(compact ? 18 : 32, 16, compact ? 18 : 32, 32);
        iconSetupGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 180, compact ? GridUnitType.Star : GridUnitType.Pixel);
        iconSetupGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 1, GridUnitType.Star);
        Grid.SetColumn(iconControls, compact ? 0 : 1);
        Grid.SetRow(iconControls, compact ? 1 : 0);
        iconImagePanel.Width = compact ? 156 : double.NaN;
        iconImagePanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        iconImagePanel.Margin = compact ? new Thickness(0, 0, 0, 22) : new Thickness(0, 0, 28, 0);
    }

    public void Dispose()
    {
        _disposed = true;
        ++_revision;
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        _image?.Dispose();
        _image = null;
        iconSourceImage.Source = null;
        iconFrames.ItemsSource = null;
    }
}
