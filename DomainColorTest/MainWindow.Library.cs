using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly ImageLibrary _library;
    private readonly ObservableCollection<SourceEntry> _sources = [];
    private SourceEntry? _activeSource;
    private bool _restoringHistory;

    private void InitializeLibrary()
    {
        sourceStrip.ItemsSource = _sources;
        sourceStrip.SelectionChanged += (_, _) =>
        {
            if (_restoringHistory || _processing || sourceStrip.SelectedItem is not SourceEntry source) return;
            try { SelectSource(source); }
            catch (Exception ex)
            {
                statusLabel.Text = $"Не удалось открыть сохранённый исходник: {ex.Message}";
                _restoringHistory = true; sourceStrip.SelectedItem = _activeSource; _restoringHistory = false;
            }
        };
        generationStrip.SelectionChanged += (_, _) =>
        {
            if (_restoringHistory || _processing || generationStrip.SelectedItem is not GenerationEntry generation) return;
            try { SelectGeneration(generation); }
            catch (Exception ex)
            {
                statusLabel.Text = $"Не удалось открыть результат: {ex.Message}";
                _restoringHistory = true; generationStrip.SelectedItem = _activeSource?.SelectedGeneration; _restoringHistory = false;
            }
        };
        foreach (var preview in new[] { sourcePreview, resultPreview })
            preview.ScrollPositionChanged += (_, _) =>
            {
                var other = ReferenceEquals(preview, sourcePreview) ? resultPreview : sourcePreview;
                var position = preview.ScrollPosition;
                other.ScrollToRelativePosition(position.X, position.Y);
            };
        PreviewDragOver += (sender, e) =>
        {
            if (IsIconDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropIcon(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
                return;
            }
            bool supported = e.Data.GetDataPresent(GenerationDragFormat)
                ? TryGetDraggedGeneration(e.Data, out _, out _) && IsSourceDropTarget(e.OriginalSource as DependencyObject)
                : DropPaths(e).Any(IsSupportedImage);
            e.Effects = !_processing && supported ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewDrop += async (_, e) =>
        {
            e.Handled = true;
            if (IsIconDropTarget(e.OriginalSource as DependencyObject)) { await DropIconAsync(e); return; }
            if (_processing) { e.Effects = DragDropEffects.None; return; }
            if (e.Data.GetDataPresent(GenerationDragFormat))
            {
                if (IsSourceDropTarget(e.OriginalSource as DependencyObject) && TryGetDraggedGeneration(e.Data, out var source, out var generation))
                {
                    e.Effects = DragDropEffects.Copy;
                    await AddGenerationAsSourceAsync(source, generation);
                }
                else e.Effects = DragDropEffects.None;
                return;
            }
            string[] paths = DropPaths(e);
            if (paths.Length > 0) await AddImagesAsync(paths);
        };
        try
        {
            foreach (var source in _library.Load()) _sources.Add(source);
            if (_sources.Count > 0) SelectSource(_sources[^1]);
            if (_library.LoadWarnings.Count > 0)
                statusLabel.Text = $"История открыта. Не удалось прочитать записей: {_library.LoadWarnings.Count}.";
        }
        catch (Exception ex) { statusLabel.Text = $"Не удалось загрузить историю: {ex.Message}"; }
        UpdateFilmstripLabels();
    }

    private static string[] DropPaths(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];
    private static bool IsSupportedImage(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    private async Task AddImagesAsync(IEnumerable<string> paths)
    {
        if (_processing) return;
        SetProcessingState(true);
        var failures = new List<string>();
        SourceEntry? selected = null;
        int added = 0;
        try
        {
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    statusLabel.Text = $"Добавление: {Path.GetFileName(path)}…";
                    var existing = _sources.ToArray();
                    var source = await Task.Run(() => _library.Import(path, existing));
                    if (!_sources.Contains(source)) { _sources.Add(source); added++; }
                    selected = source;
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            }
            if (selected is not null) SelectSource(selected);
            statusLabel.Text = failures.Count == 0
                ? $"Добавлено: {added}. Исходников в ленте: {_sources.Count}. История сохраняется автоматически."
                : $"Добавлено: {added}. Не удалось открыть: {failures.Count}. {failures[0]}";
            statusLabel.ToolTip = failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
        }
        finally { UpdateFilmstripLabels(); SetProcessingState(false); }
    }

    private void LoadImage(string path)
    {
        var source = _library.Import(path, _sources);
        if (!_sources.Contains(source)) _sources.Add(source);
        SelectSource(source);
    }

    private void SelectSource(SourceEntry source)
    {
        if (ReferenceEquals(_activeSource, source)) { ShowEditor(); return; }
        var bitmap = ImageLibrary.ReadBitmap(_library.SourcePath(source));
        ShowEditor();
        if (_activeSource is not null) _activeSource.DraftSettings = CaptureSettings();
        _restoringHistory = true;
        try
        {
            sourcePreview.Image = null;
            _sourceImage?.Dispose();
            _sourceImage = bitmap;
            _activeSource = source;
            _selectedFilePath = source.OriginalPath;
            ClearDisplayedResult();
            sourceStrip.SelectedItem = source;
            sourceStrip.ScrollIntoView(source);
            generationStrip.ItemsSource = source.Generations;
            fileLabel.Text = source.Label;
            fileLabel.ToolTip = source.OriginalPath;
        }
        finally { _restoringHistory = false; }
        if (source.SelectedGeneration is { } selected) SelectGeneration(selected);
        else
        {
            if (source.DraftSettings is { } draft) RestoreSettings(draft);
            else if (aspectLock.IsChecked == true) SizeChangedByUser(widthChanged: true);
            UpdateSourcePreview();
            UpdateSizeHint();
            statusLabel.Text = "Исходник выбран. Перетащите ещё изображения или запустите обработку.";
        }
        sourcePreview.ScrollToRelativePosition(0, 0);
        resultPreview.ScrollToRelativePosition(0, 0);
        UpdateFilmstripLabels();
        SetProcessingState(_processing);
    }

    private void ClearDisplayedResult()
    {
        showCropInput.IsChecked = false;
        showCropInput.IsEnabled = false;
        _croppedImage?.Dispose(); _croppedImage = null;
        resultPreview.Image = null;
        _downscaledResult?.Dispose(); _downscaledResult = null;
        _resultStale = _resultPreservesTransparency = _resultIsAlignment = false;
        resultCaption.Text = "РЕЗУЛЬТАТ";
        processingLog.Clear();
    }

    private void SelectGeneration(GenerationEntry generation)
    {
        if (_activeSource is null || !_activeSource.Generations.Contains(generation)) return;
        var output = ImageLibrary.ReadBitmap(_library.ResultPath(_activeSource, generation));
        System.Drawing.Bitmap? crop;
        try { crop = generation.HasCrop ? ImageLibrary.ReadBitmap(_library.CropPath(_activeSource, generation)) : null; }
        catch { output.Dispose(); throw; }
        RestoreSettings(generation.Settings);
        _restoringHistory = true;
        try
        {
            showCropInput.IsChecked = false;
            sourcePreview.Image = null;
            _croppedImage?.Dispose(); _croppedImage = crop;
            showCropInput.IsEnabled = crop is not null;
            resultPreview.Image = null;
            _downscaledResult?.Dispose(); _downscaledResult = output;
            resultPreview.Image = output;
            _resultIsAlignment = generation.IsAlignment;
            _resultPreservesTransparency = generation.PreservesTransparency;
            _resultStale = false;
            resultCaption.Text = generation.Caption;
            processingLog.Text = generation.Log;
            _activeSource.SelectedGeneration = generation;
            generationStrip.SelectedItem = generation;
            generationStrip.ScrollIntoView(generation);
        }
        finally { _restoringHistory = false; }
        UpdateSourcePreview();
        UpdateSizeHint();
        UpdateFilmstripLabels();
        SetProcessingState(_processing);
        statusLabel.Text = $"Результат от {generation.CreatedAt:dd.MM.yyyy HH:mm:ss}. Сохранён в истории.";
    }

    private void UpdateFilmstripLabels()
    {
        sourcesCaption.Text = $"Исходники ({_sources.Count})";
    }

    private void RemoveLibraryItem(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_processing || sender is not FrameworkElement element) return;
        _dragCandidate = null;
        try
        {
            if (element.DataContext is SourceEntry source) RemoveSource(source);
            else if (element.DataContext is GenerationEntry generation) RemoveGeneration(generation);
        }
        catch (Exception ex) { statusLabel.Text = $"Не удалось удалить запись или открыть соседнюю: {ex.Message}"; }
        finally { UpdateFilmstripLabels(); SetProcessingState(_processing); }
    }

    private void RemoveSource(SourceEntry source)
    {
        int index = _sources.IndexOf(source);
        if (index < 0) return;
        bool selected = ReferenceEquals(_activeSource, source);
        var next = _sources.Count <= 1 ? null : _sources[index + 1 < _sources.Count ? index + 1 : index - 1];
        _library.RemoveSource(source);
        _restoringHistory = true;
        try
        {
            _sources.Remove(source);
            if (selected)
            {
                ClearDisplayedResult();
                sourcePreview.Image = null;
                _sourceImage?.Dispose(); _sourceImage = null;
                _activeSource = null;
                _selectedFilePath = null;
                sourceStrip.SelectedItem = null;
                generationStrip.ItemsSource = null;
                fileLabel.Text = "Откройте изображение";
                fileLabel.ToolTip = null;
                UpdateSourcePreview();
                UpdateSizeHint();
            }
        }
        finally { _restoringHistory = false; }
        if (selected && next is not null) SelectSource(next);
        statusLabel.Text = _sources.Count == 0
            ? "Все исходники удалены из программы. Откройте или перетащите новое изображение."
            : "Исходник и его результаты удалены из программы.";
    }

    private void RemoveGeneration(GenerationEntry generation)
    {
        var source = _sources.FirstOrDefault(s => s.Generations.Contains(generation));
        if (source is null) return;
        int index = source.Generations.IndexOf(generation);
        bool selected = ReferenceEquals(source.SelectedGeneration, generation);
        bool visible = ReferenceEquals(source, _activeSource);
        var next = source.Generations.Count <= 1 ? null
            : source.Generations[index + 1 < source.Generations.Count ? index + 1 : index - 1];
        _library.RemoveGeneration(source, generation);
        _restoringHistory = true;
        try
        {
            source.Generations.Remove(generation);
            if (selected)
            {
                source.SelectedGeneration = visible ? null : next;
                if (visible)
                {
                    generationStrip.SelectedItem = null;
                    ClearDisplayedResult();
                }
            }
        }
        finally { _restoringHistory = false; }
        if (selected && visible)
        {
            if (next is not null) SelectGeneration(next);
            else { UpdateSourcePreview(); UpdateSizeHint(); }
        }
        statusLabel.Text = "Результат удалён из истории.";
    }

    private EditorSettings CaptureSettings() => new()
    {
        Mode = detailsMode.IsChecked == true ? 2 : backgroundMode.IsChecked == true ? 0 : 1,
        Downscale = BuildOptionsFromUi(), CellSize = gridCellInput.Value,
        DetectGrid = detectGridInput.IsChecked == true, AspectLock = aspectLock.IsChecked == true
    };

    private void RestoreSettings(EditorSettings settings)
    {
        _restoringHistory = true;
        try
        {
            (settings.Mode == 2 ? detailsMode : settings.Mode == 0 ? backgroundMode : sceneMode).IsChecked = true;
            ApplyProfile(restoring: true);
            var options = settings.Downscale;
            widthInput.Value = options.TargetWidth; heightInput.Value = options.TargetHeight;
            spriteInput.IsChecked = options.SpriteMode; alphaInput.Value = options.AlphaThreshold;
            cropHorizontalInput.SelectedIndex = Math.Clamp((int)options.CropHorizontal, 0, 2);
            cropVerticalInput.SelectedIndex = Math.Clamp((int)options.CropVertical, 0, 2);
            paletteInput.SelectedIndex = Math.Clamp((int)options.Palette, 0, 5);
            paletteStepInput.Value = options.PaletteStep;
            quantizationInput.SelectedIndex = Math.Clamp((int)options.Quantization, 0, 2);
            quantizationColorsInput.Value = options.QuantizationColors;
            colorWeightsInput.IsChecked = options.UseColorWeights;
            threadsInput.Value = options.ThreadCount;
            automaticInput.IsChecked = options.BlockMode != BlockSelectionMode.Manual;
            manualInput.IsChecked = options.BlockMode == BlockSelectionMode.Manual;
            brightnessInput.Value = (options.ManualCriteria?.TargetBrightness ?? .5) * 100;
            contrastInput.Value = (options.ManualCriteria?.TargetContrast ?? .5) * 100;
            saturationInput.Value = (options.ManualCriteria?.TargetSaturation ?? .5) * 100;
            edgeInput.Value = (options.ManualCriteria?.TargetEdge ?? .5) * 100;
            aspectLock.IsChecked = settings.AspectLock;
            gridCellInput.Value = settings.CellSize; detectGridInput.IsChecked = settings.DetectGrid;
            UpdateDependentControls();
            ditheringInput.IsChecked = !options.SpriteMode && options.Palette is >= PaletteKind.DB16 and <= PaletteKind.GameBoy && options.EnableDithering;
            profileState.Text = "Параметры выбранного результата";
        }
        finally { _restoringHistory = false; }
    }
}
