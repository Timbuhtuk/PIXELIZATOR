using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace DomainColorTest;

public partial class MainWindow
{
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(340.0));
    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    private bool _homeReady;
    private bool _compactLayout;

    private void InitializeHome()
    {
        libraryCards.ItemsSource = _sources;
        _sources.CollectionChanged += (_, _) => RefreshHome();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        homeScroll.SizeChanged += (_, _) => UpdateCardWidth();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (iconWorkspace.Visibility == Visibility.Visible) _ = iconWorkspace.OpenAsync();
                else OpenImage(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        _homeReady = true;
        ShowHome();
        Loaded += (_, _) => UpdateResponsiveLayout();
    }

    private void RefreshHome()
    {
        if (!_homeReady) return;
        SetVisible(emptyLibrary, _sources.Count == 0);
    }

    private void ShowHome()
    {
        if (!_homeReady || _processing || iconWorkspace.IsBusy) return;
        RefreshHome();
        SetVisible(iconWorkspace, false);
        SetVisible(homeScroll, true);
        SetVisible(workspaceHost, false);
        SetVisible(editorToolbar, false);
        homeScroll.ScrollToTop();
        statusLabel.Text = "Готово";
        if (SystemParameters.ClientAreaAnimation)
            homeContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(180)));
        UpdateCardWidth();
    }

    private void ShowEditor()
    {
        if (!_homeReady) return;
        SetVisible(homeScroll, false);
        SetVisible(iconWorkspace, false);
        SetVisible(workspaceHost, true);
        SetVisible(editorToolbar, true);
        UpdateResponsiveLayout();
    }
    private void HomeOpenImage(object sender, RoutedEventArgs e) => OpenImage(sender, e);

    private void GoToLibrary(object sender, RoutedEventArgs e)
    {
        ShowHome();
        homeScroll.UpdateLayout();
        libraryHeading.BringIntoView();
    }

    private void OpenLibraryCard(object sender, RoutedEventArgs e)
    {
        if (_processing || sender is not Button { Tag: SourceEntry source }) return;
        try
        {
            SelectSource(source);
            ShowEditor();
        }
        catch (Exception ex) { statusLabel.Text = $"Не удалось открыть изображение: {ex.Message}"; }
    }

    private void UpdateCardWidth()
    {
        double width = homeContent.ActualWidth;
        if (width <= 0) return;
        int columns = width < 650 ? 1 : width < 1050 ? 2 : 3;
        CardWidth = Math.Max(200, (width - 12 * columns) / columns);
    }

    private void UpdateResponsiveLayout()
    {
        if (!_homeReady) return;
        bool compact = ActualWidth < 940;
        homeContent.Margin = new Thickness(compact ? 18 : 32, 8, compact ? 18 : 32, 24);
        heroCopy.Margin = new Thickness(compact ? 22 : 32, 28, compact ? 22 : 32, 28);
        heroTitle.FontSize = compact ? 29 : 34;
        iconBannerTitle.FontSize = compact ? 29 : 34;
        iconBannerCopy.Margin = heroCopy.Margin;
        SetVisible(iconBannerArt, ActualWidth >= 1000);
        heroTitle.LineHeight = compact ? 36 : 42;
        if (_compactLayout != compact || workspaceGrid.RowDefinitions.Count == 0)
        {
            _compactLayout = compact;
            workspaceGrid.ColumnDefinitions.Clear();
            workspaceGrid.RowDefinitions.Clear();
            if (compact)
            {
                workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition());
                workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(270) });
                workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(630) });
                Grid.SetColumn(settingsScroller, 0);
                Grid.SetColumn(previewArea, 0);
                Grid.SetRow(previewArea, 1);
                workspaceGrid.Height = 900;
                workspaceHost.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            else
            {
                workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350), MinWidth = 330 });
                workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });
                Grid.SetColumn(settingsScroller, 0);
                Grid.SetColumn(previewArea, 2);
                Grid.SetRow(previewArea, 0);
                workspaceGrid.Height = double.NaN;
                workspaceHost.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            SetVisible(settingsSplitter, !compact);
        }
        UpdateCardWidth();
    }

    private void HomeScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (landscapeOffset is null || !SystemParameters.ClientAreaAnimation) return;
        landscapeOffset.Y = Math.Min(12, homeScroll.VerticalOffset * 0.035);
    }

    private void HeroMouseMove(object sender, MouseEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var position = e.GetPosition(heroPanel);
        landscapeOffset.X = (position.X / Math.Max(1, heroPanel.ActualWidth) - 0.5) * 10;
    }

    private void HeroMouseLeave(object sender, MouseEventArgs e) => landscapeOffset.X = 0;
}
