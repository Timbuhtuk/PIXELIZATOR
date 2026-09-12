using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private void InitializeIcons()
    {
        iconWorkspace.BackRequested += (_, _) => ShowHome();
        iconWorkspace.SourceRequested += (_, _) => UseEditorImageForIcon(false);
        iconWorkspace.ResultRequested += (_, _) => UseEditorImageForIcon(true);
        iconWorkspace.StatusChanged += message => statusLabel.Text = message;
        iconWorkspace.BusyChanged += (_, _) => SetVisible(busyIndicator, _processing || iconWorkspace.IsBusy);
        Closed += (_, _) => iconWorkspace.Dispose();
    }

    private void OpenIconWorkspace(object sender, RoutedEventArgs e) => ShowIconWorkspace();

    private void ShowIconWorkspace()
    {
        if (_processing || iconWorkspace.IsBusy) return;
        SetVisible(homeScroll, false);
        SetVisible(workspaceHost, false);
        SetVisible(editorToolbar, false);
        SetVisible(iconWorkspace, true);
        iconWorkspace.SetEditorAvailability(_sourceImage is not null, _downscaledResult is not null);
        statusLabel.Text = "Готово";
    }

    private void UseEditorImageForIcon(bool result)
    {
        if (_processing || iconWorkspace.IsBusy) return;
        var image = result ? _downscaledResult : _sourceImage;
        if (image is null) return;
        string label = _activeSource?.Label ?? "Изображение";
        if (result) label = System.IO.Path.GetFileNameWithoutExtension(label) + "_result.png";
        iconWorkspace.SetImage(image, label, result ? null : _selectedFilePath);
    }

    private bool IsIconDropTarget(DependencyObject? target)
    {
        if (iconWorkspace.Visibility == Visibility.Visible) return true;
        for (var current = target; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, iconBanner)) return true;
        return false;
    }

    private bool CanDropIcon(IDataObject data) => !_processing && !iconWorkspace.IsBusy &&
        (TryGetDraggedGeneration(data, out _, out _) ||
         data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile));

    private async Task DropIconAsync(DragEventArgs e)
    {
        if (!CanDropIcon(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowIconWorkspace();
        try
        {
            if (TryGetDraggedGeneration(e.Data, out var source, out var generation))
            {
                using var image = ImageLibrary.ReadBitmap(_library.ResultPath(source, generation));
                iconWorkspace.SetImage(image, System.IO.Path.GetFileNameWithoutExtension(source.Label) + "_result.png");
            }
            else
            {
                string? path = DropPaths(e).FirstOrDefault(IconWorkspace.SupportsFile);
                if (path is not null) await iconWorkspace.LoadFileAsync(path);
            }
        }
        catch (Exception ex) { statusLabel.Text = $"Не удалось открыть изображение для ICO: {ex.Message}"; }
    }
}
