using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using DomainColorTest;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class WindowChromeChecks
{
    public static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "chrome-" + Guid.NewGuid().ToString("N")));
        var window = new MainWindow(directory) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show(); Pump();
            var handle = new WindowInteropHelper(window).Handle;
            Require(WindowChrome.GetWindowChrome(window) is { UseAeroCaptionButtons: false }, "Native title bar was not replaced");
            var caption = (Border)window.FindName("titleBar");
            var point = caption.PointToScreen(new Point(220, 20));
            Require(HitTest(handle, point) == 2, "Title bar cannot be dragged or double-clicked by Windows");
            var close = (Button)window.FindName("closeButton");
            Require(HitTest(handle, close.PointToScreen(new Point(20, 20))) == 1, "Caption button is swallowed by native dragging");
            Require(GetWindowRect(handle, out var normal), "Cannot read window bounds");
            Require(caption.PointToScreen(new Point(0, 0)).Y - normal.Top < 8, "An extra native title bar remains above the custom caption");
            Require(HitTest(handle, new Point(normal.Left + 2, normal.Top + 120)) == 10, "Left resize border is not active");
            Require(HitTest(handle, new Point(normal.Right - 2, normal.Bottom - 2)) == 17, "Corner resize border is not active");

            Click("maximizeButton");
            Require(window.WindowState == WindowState.Maximized, "Maximize button failed");
            Require(AutomationProperties.GetName((Button)window.FindName("maximizeButton")) == "Восстановить", "Restore label did not update");
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Require(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor), "Cannot read monitor work area");
            var content = (Grid)window.FindName("rootLayout");
            var topLeft = content.PointToScreen(new Point(0, 0));
            var bottomRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
            Require(topLeft.X >= monitor.Work.Left - 1 && topLeft.Y >= monitor.Work.Top - 1 &&
                bottomRight.X <= monitor.Work.Right + 1 && bottomRight.Y <= monitor.Work.Bottom + 1,
                $"Maximized content extends outside the work area: {topLeft}–{bottomRight}, work {monitor.Work.Left},{monitor.Work.Top}–{monitor.Work.Right},{monitor.Work.Bottom}");
            Click("maximizeButton");
            Require(window.WindowState == WindowState.Normal && Math.Abs(window.Width - 1220) < 1 && Math.Abs(window.Height - 880) < 1, "Restore lost the window dimensions");

            point = caption.PointToScreen(new Point(220, 20));
            SendMessage(handle, 0xA3, 2, Pack(point));
            Pump();
            Require(window.WindowState == WindowState.Maximized, "Native title double-click failed");
            Click("maximizeButton");
            Click("minimizeButton");
            Require(window.WindowState == WindowState.Minimized, "Minimize button failed");
            SystemCommands.RestoreWindow(window); Pump();
            Require(window.WindowState == WindowState.Normal, "Restore from minimized failed");

            SetBusy(true);
            Click("closeButton");
            Require(!closed && window.IsVisible, "Custom close bypassed the processing guard");
            SetBusy(false);
            Click("closeButton");
            Require(closed, "Custom close did not close the window");
            Console.WriteLine("  Window chrome: caption/button hit tests, resize edges, maximize/work area, restore, double-click, minimize and guarded close passed.");
        }
        finally
        {
            if (!closed) { SetBusy(false); window.Close(); }
        }

        void Click(string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        void SetBusy(bool value) => typeof(MainWindow).GetMethod("SetProcessingState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [value]);
    }

    private static int HitTest(nint handle, Point point) => (int)SendMessage(handle, 0x84, 0, Pack(point));
    private static nint Pack(Point point) => (nint)(((int)point.X & 0xffff) | (((int)point.Y & 0xffff) << 16));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
