using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace DomainColorTest;

public partial class MainWindow
{
    private void InitializeWindowFrame()
    {
        SourceInitialized += (_, _) => UpdateWindowFrame();
        StateChanged += (_, _) => UpdateWindowFrame();
        LocationChanged += (_, _) => UpdateWindowFrame();
        SizeChanged += (_, _) => UpdateWindowFrame();
    }

    private void UpdateWindowFrame()
    {
        var padding = new Thickness();
        if (WindowState == WindowState.Maximized)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (handle != 0 && GetWindowRect(handle, out var bounds) && GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
            {
                // Windows keeps an invisible resize border outside the maximized work area.
                var dpi = VisualTreeHelper.GetDpi(this);
                padding = new Thickness(
                    Math.Max(0, monitor.Work.Left - bounds.Left) / dpi.DpiScaleX,
                    Math.Max(0, monitor.Work.Top - bounds.Top) / dpi.DpiScaleY,
                    Math.Max(0, bounds.Right - monitor.Work.Right) / dpi.DpiScaleX,
                    Math.Max(0, bounds.Bottom - monitor.Work.Bottom) / dpi.DpiScaleY);
            }
        }
        rootLayout.Margin = padding;
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null && chrome.CaptionHeight != 40 + padding.Top)
            chrome.CaptionHeight = 40 + padding.Top;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(UpdateWindowFrame));
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void ToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}