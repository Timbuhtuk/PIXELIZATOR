using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;
using PixelArtDownscale;
using PixelArtAlignment;

namespace DomainColorTest;

public partial class MainWindow : Window
{
    private readonly PixelArtDownscaler _downscaler = new();
    private string? _selectedFilePath;
    private Bitmap? _sourceImage;
    private Bitmap? _croppedImage;
    private Bitmap? _downscaledResult;
    private bool _resultPreservesTransparency;
    private bool _resultIsAlignment;
    private bool _applyingProfile;
    private bool _updatingSize;
    private bool _processing;
    private bool _resultStale;

    private readonly OpenFileDialog openDialog = new() { Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif", Title = "Добавить изображения", Multiselect = true };
    private readonly SaveFileDialog saveDialog = new() { DefaultExt = ".png", AddExtension = true, OverwritePrompt = true, Title = "Сохранить результат" };

    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixelizator", "Library")) { }

    public MainWindow(string libraryDirectory)
    {
        _library = new ImageLibrary(libraryDirectory);
        InitializeComponent();
        InitializeWindowFrame();
        threadsInput.Maximum = threadsInput.Value = Math.Max(1, Environment.ProcessorCount);
        foreach (var mode in new[] { backgroundMode, sceneMode, detailsMode })
            mode.Checked += (_, _) => ApplyProfile();
        foreach (var box in new[] { paletteInput, quantizationInput, cropHorizontalInput, cropVerticalInput })
            box.SelectionChanged += SettingsChanged;
        foreach (var input in new[] { paletteStepInput, alphaInput, quantizationColorsInput })
            input.ValueChanged += SettingsChanged;
        foreach (var check in new ToggleButton[] { spriteInput, ditheringInput, colorWeightsInput, automaticInput, manualInput })
        {
            check.Checked += SettingsChanged;
            check.Unchecked += SettingsChanged;
        }
        foreach (var slider in new[] { brightnessInput, contrastInput, saturationInput, edgeInput })
            slider.ValueChanged += (_, _) => SettingsChanged(null, EventArgs.Empty);
        widthInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: true);
        heightInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: false);
        aspectLock.Checked += (_, _) => SizeChangedByUser(widthChanged: true);
        zoomInput.SelectionChanged += (_, _) => UpdatePreviewSettings();
        backgroundInput.SelectionChanged += (_, _) => UpdatePreviewSettings();
        showCropInput.Checked += (_, _) => UpdateSourcePreview();
        showCropInput.Unchecked += (_, _) => UpdateSourcePreview();
        advancedToggle.Checked += (_, _) => SetVisible(advancedPanel, true);
        advancedToggle.Unchecked += (_, _) => SetVisible(advancedPanel, false);
        detectGridInput.Checked += (_, _) => { gridCellInput.IsEnabled = false; AlignmentSettingsChanged(); };
        detectGridInput.Unchecked += (_, _) => { gridCellInput.IsEnabled = true; AlignmentSettingsChanged(); };
        gridCellInput.ValueChanged += (_, _) => AlignmentSettingsChanged();
        openButton.Click += OpenImage;
        processButton.Click += ProcessImage;
        saveButton.Click += SaveImage;
        alignButton.Click += AlignImage;
        resetProfileButton.Click += ResetProfile;
        Closing += (_, e) => { if (_processing || iconWorkspace.IsBusy) { e.Cancel = true; statusLabel.Text = "Дождитесь завершения обработки."; } };
        Closed += (_, _) =>
        {
            _restoringHistory = true;
            sourcePreview.Image = resultPreview.Image = null;
            _sourceImage?.Dispose(); _sourceImage = null;
            _croppedImage?.Dispose(); _croppedImage = null;
            _downscaledResult?.Dispose(); _downscaledResult = null;
        };
        sceneMode.IsChecked = true;
        InitializeLibrary();
        InitializeImageActions();
        InitializeHome();
        InitializeIcons();
        UpdatePreviewSettings();
    }

    private string CurrentModeName => detailsMode.IsChecked == true ? "Детали" : backgroundMode.IsChecked == true ? "Фон" : "Сцена";
    private static void SetVisible(UIElement control, bool visible) => control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    private void ApplyProfile(bool restoring = false)
    {
        if (_restoringHistory && !restoring) return;
        _applyingProfile = true;
        try
        {
            bool details = detailsMode.IsChecked == true;
            quantizationInput.SelectedIndex = details ? 0 : 1;
            quantizationColorsInput.Value = 64;
            colorWeightsInput.IsChecked = false;
            paletteInput.SelectedIndex = details ? 0 : 5;
            paletteStepInput.Value = 16;
            spriteInput.IsChecked = details;
            alphaInput.Value = details ? 75 : 50;
            automaticInput.IsChecked = true;
            ditheringInput.IsChecked = false;
            cropHorizontalInput.SelectedIndex = cropVerticalInput.SelectedIndex = 0;
            brightnessInput.Value = contrastInput.Value = saturationInput.Value = edgeInput.Value = 50;
            modeDescription.Text = details
                ? "Пиксель-арт и спрайты: приоритет контуров и прозрачности. При уменьшении часть деталей может исчезнуть."
                : backgroundMode.IsChecked == true
                    ? "Крупные фоновые изображения: цельные цветовые области и общая композиция, без акцента на мелочах."
                    : "Сцены средней детализации: сохраняем значимые формы, свет и цветовые переходы.";
            profileState.Text = "Настройки режима";
            UpdateDependentControls();
            MarkResultStale();
            UpdateSizeHint();
            statusLabel.Text = $"Режим «{CurrentModeName}» выбран. Размер сохранён; нажмите «Обработать».";
        }
        finally { _applyingProfile = false; }
    }

    private void ResetProfile(object? sender, EventArgs e) => ApplyProfile();

    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_applyingProfile || _restoringHistory) return;
        UpdateDependentControls();
        profileState.Text = "Настройки изменены вручную";
        MarkResultStale();
        UpdateSizeHint();
    }

    private void UpdateDependentControls()
    {
        SetVisible(stepControls, paletteInput.SelectedIndex == 5);
        SetVisible(cropControls, spriteInput.IsChecked != true);
        SetVisible(alphaControls, spriteInput.IsChecked == true);
        bool ditheringAllowed = spriteInput.IsChecked != true && paletteInput.SelectedIndex is >= 1 and <= 4;
        ditheringInput.IsEnabled = ditheringAllowed;
        if (!ditheringAllowed) ditheringInput.IsChecked = false;
        SetVisible(manualControls, manualInput.IsChecked == true);
        brightnessLabel.Text = $"Яркость · {brightnessInput.Value}";
        contrastLabel.Text = $"Контраст · {contrastInput.Value}";
        saturationLabel.Text = $"Насыщенность · {saturationInput.Value}";
        edgeLabel.Text = $"Контур · {edgeInput.Value}";
    }

    private void SizeChangedByUser(bool widthChanged)
    {
        if (_updatingSize || _restoringHistory) return;
        _updatingSize = true;
        try
        {
            if (aspectLock.IsChecked == true && _sourceImage is not null)
            {
                var input = widthChanged ? heightInput : widthInput;
                double value = widthChanged
                    ? Math.Round((double)widthInput.Value * _sourceImage.Height / _sourceImage.Width)
                    : Math.Round((double)heightInput.Value * _sourceImage.Width / _sourceImage.Height);
                input.Value = (int)Math.Clamp(value, input.Minimum, input.Maximum);
            }
        }
        finally { _updatingSize = false; }
        MarkResultStale();
        UpdateSizeHint();
    }

    private void UpdateSizeHint()
    {
        if (_sourceImage is null)
        {
            sizeHint.Text = "Размер задаёт итоговую сетку. Чем она крупнее, тем больше деталей может сохраниться.";
            return;
        }
        int width = (int)widthInput.Value, height = (int)heightInput.Value;
        if (width > _sourceImage.Width || height > _sourceImage.Height)
            sizeHint.Text = $"Исходник {_sourceImage.Width} × {_sourceImage.Height}. Размер результата должен быть не больше исходника.";
        else if (spriteInput.IsChecked == true && Math.Abs((double)width / height / ((double)_sourceImage.Width / _sourceImage.Height) - 1) > .02)
            sizeHint.Text = "Пропорции отличаются от исходника: весь кадр будет растянут. Включите сохранение пропорций.";
        else
            sizeHint.Text = spriteInput.IsChecked == true
                ? "Весь исходник попадёт в сетку. Порог заполнения регулирует края прозрачных объектов."
                : "Область исходника подгоняется обрезкой под целые блоки. Привязка доступна в расширенных настройках.";
    }

    private void MarkResultStale()
    {
        if (_resultIsAlignment || _restoringHistory || _activeSource?.SelectedGeneration?.IsGridReduction == true) return;
        if (_downscaledResult is null) return;
        _resultStale = true;
        resultCaption.Text = $"РЕЗУЛЬТАТ · {_downscaledResult.Width} × {_downscaledResult.Height} · предыдущие настройки";
        statusLabel.Text = "Настройки изменены. Нажмите «Обработать», чтобы обновить результат.";
    }

    private void UpdatePreviewSettings()
    {
        double zoom = ZoomFactors[Math.Clamp(zoomInput.SelectedIndex, 0, ZoomFactors.Length - 1)];
        foreach (var preview in new[] { sourcePreview, resultPreview })
        {
            preview.Zoom = zoom;
            preview.PreviewBackground = backgroundInput.SelectedIndex;
        }
    }

    private void UpdateSourcePreview()
    {
        var image = showCropInput.IsChecked == true ? _croppedImage : _sourceImage;
        sourcePreview.Image = image;
        sourceCaption.Text = image is null ? "ИСХОДНИК" :
            $"{(showCropInput.IsChecked == true ? "ОБЛАСТЬ ПОСЛЕДНЕЙ ОБРАБОТКИ" : "ИСХОДНИК")} · {image.Width} × {image.Height}";
    }

    private async void OpenImage(object? sender, EventArgs e)
    {
        if (_processing || openDialog.ShowDialog(this) != true) return;
        await AddImagesAsync(openDialog.FileNames);
        if (_sourceImage is not null) ShowEditor();
    }

    private async void ProcessImage(object? sender, EventArgs e)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        DownscaleOptions options = settings.Downscale;
        if (options.TargetWidth > _sourceImage.Width || options.TargetHeight > _sourceImage.Height)
        {
            statusLabel.Text = "Уменьшите размер результата: он не должен превышать размер исходника.";
            return;
        }
        string mode = CurrentModeName;
        var document = _activeSource;
        SetProcessingState(true);
        statusLabel.Text = $"Обработка · {mode} · {options.TargetWidth} × {options.TargetHeight}…";
        try
        {
            using var source = (Bitmap)_sourceImage.Clone();
            var generation = await Task.Run(() =>
            {
                var result = _downscaler.Process(source, options);
                try
                {
                    return _library.SaveGeneration(document, result.Downscaled, result.CroppedSource, new GenerationEntry
                    {
                        Settings = settings,
                        PreservesTransparency = options.SpriteMode,
                        Caption = $"РЕЗУЛЬТАТ · {result.Downscaled.Width} × {result.Downscaled.Height} · {mode}",
                        Log = string.Join(Environment.NewLine, result.StageTimingsSeconds.Select(stage => $"{stage.Key}: {stage.Value:F3} с")) +
                            $"{Environment.NewLine}Основной цвет: {result.DominantColor}"
                    });
                }
                finally { result.CroppedSource.Dispose(); result.Downscaled.Dispose(); }
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = $"Готово · {generation.Width} × {generation.Height}. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Обработка или сохранение истории не завершены. Предыдущий результат доступен.";
            MessageBox.Show(this, ex.Message, "Ошибка обработки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
    private void SetProcessingState(bool processing)
    {
        _processing = processing;
        UpdateCompactButton();
        settingsPanel.IsEnabled = !processing;
        sourceStrip.IsEnabled = generationStrip.IsEnabled = !processing;
        openButton.IsEnabled = !processing;
        homeContent.IsEnabled = !processing;
        backToLibraryButton.IsEnabled = !processing;
        processButton.IsEnabled = !processing && _sourceImage is not null;
        alignButton.IsEnabled = !processing && _sourceImage is not null;
        saveButton.IsEnabled = !processing && _downscaledResult is not null;
        Cursor = processing ? Cursors.Wait : null;
        SetVisible(busyIndicator, processing);
    }

    private void CommitInputs()
    {
        foreach (var input in new[] { widthInput, heightInput, gridCellInput, paletteStepInput, alphaInput, quantizationColorsInput, threadsInput })
            input.Commit();
    }

    private DownscaleOptions BuildOptionsFromUi() => new()
    {
        TargetWidth = (int)widthInput.Value,
        TargetHeight = (int)heightInput.Value,
        SpriteMode = spriteInput.IsChecked == true,
        AlphaThreshold = (int)alphaInput.Value,
        CropHorizontal = (CropHorizontalAlignment)cropHorizontalInput.SelectedIndex,
        CropVertical = (CropVerticalAlignment)cropVerticalInput.SelectedIndex,
        Palette = (PaletteKind)paletteInput.SelectedIndex,
        PaletteStep = (int)paletteStepInput.Value,
        Quantization = (QuantizationMethod)quantizationInput.SelectedIndex,
        QuantizationColors = (int)quantizationColorsInput.Value,
        UseColorWeights = colorWeightsInput.IsChecked == true,
        ThreadCount = (int)threadsInput.Value,
        EnableDithering = spriteInput.IsChecked != true && paletteInput.SelectedIndex is >= 1 and <= 4 && ditheringInput.IsChecked == true,
        BlockMode = manualInput.IsChecked == true ? BlockSelectionMode.Manual : BlockSelectionMode.Automatic,
        ManualCriteria = manualInput.IsChecked == true ? new ManualBlockCriteria
        {
            TargetBrightness = brightnessInput.Value / 100.0,
            TargetContrast = contrastInput.Value / 100.0,
            TargetSaturation = saturationInput.Value / 100.0,
            TargetEdge = edgeInput.Value / 100.0
        } : null
    };

    private void SaveImage(object? sender, EventArgs e)
    {
        if (_downscaledResult is null) return;
        string suffix = _resultIsAlignment ? "aligned" : _activeSource?.SelectedGeneration?.IsGridReduction == true ? "pixels" : "pixelized";
        saveDialog.FileName = $"{Path.GetFileNameWithoutExtension(_activeSource?.Label ?? _selectedFilePath) ?? "result"}_{suffix}.png";
        saveDialog.Filter = _resultPreservesTransparency ? "PNG (с прозрачностью)|*.png"
            : "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp";
        saveDialog.FilterIndex = 1;
        if (saveDialog.ShowDialog(this) != true) return;
        try { SaveResult(saveDialog.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveResult(string path)
    {
        if (_downscaledResult is null) throw new InvalidOperationException("Сначала обработайте изображение.");
        string outputPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp") || (_resultPreservesTransparency && extension != ".png"))
        {
            throw new InvalidOperationException(_resultPreservesTransparency
                ? "Сохраните этот результат в PNG, чтобы сохранить прозрачность."
                : "Выберите формат PNG, JPEG или BMP.");
        }
        if (string.Equals(outputPath, _selectedFilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Выберите другое имя: исходное изображение нужно сохранить.");
        }
        string temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var format = extension switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };
            _downscaledResult.Save(temporaryPath, format);
            File.Move(temporaryPath, outputPath, overwrite: true);
            statusLabel.Text = $"Сохранено: {outputPath}" + (_resultStale ? " · результат предыдущих настроек" : "");
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private void AlignmentSettingsChanged()
    {
        if (_restoringHistory || !_resultIsAlignment || _downscaledResult is null) return;
        _resultStale = true;
        resultCaption.Text = $"ВЫРОВНЕННАЯ СЕТКА · {_downscaledResult.Width} × {_downscaledResult.Height} · предыдущие настройки";
        statusLabel.Text = "Размер ячейки изменён. Нажмите «Выровнять сетку».";
    }

    private async void AlignImage(object? sender, EventArgs e)
    {
        if (_sourceImage is null || _activeSource is null || _processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        var options = new GridAlignmentOptions { CellSize = settings.DetectGrid ? null : settings.CellSize };
        var document = _activeSource;
        SetProcessingState(true);
        statusLabel.Text = "Выравнивание пиксельной сетки…";
        try
        {
            using var source = (Bitmap)_sourceImage.Clone();
            var generation = await Task.Run(() =>
            {
                using var result = new PixelGridAligner().Align(source, options);
                return _library.SaveGeneration(document, result.Aligned, null, new GenerationEntry
                {
                    Settings = settings, IsAlignment = true, PreservesTransparency = true, CellSize = result.CellSize,
                    Caption = $"ВЫРОВНЕННАЯ СЕТКА · {result.Aligned.Width} × {result.Aligned.Height} · ячейка {result.CellSize} px",
                    Log = $"Выравнивание: {result.ElapsedSeconds:F3} с. Ячейка: {result.CellSize} px."
                });
            });
            document.Generations.Add(generation);
            SelectGeneration(generation);
            statusLabel.Text = $"Готово. Ячейка {generation.CellSize} px. Результат сохранён в истории.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Выравнивание или сохранение истории не завершены.";
            MessageBox.Show(this, ex.Message, "Выравнивание сетки", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { SetProcessingState(false); }
    }
}
