using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;
using AudioPenWin.Views;

namespace AudioPenWin;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType,
                                           int cxDesired, int cyDesired, uint fuLoad);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint IMAGE_ICON    = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint WM_SETICON    = 0x0080;

    public MainWindow()
    {
        InitializeComponent();
        Title = "AudioPen";
        ConfigureWindow();
        Navigate(typeof(HomePage));
    }

    public void Navigate(Type pageType, object? parameter = null)
    {
        RootFrame.Navigate(pageType, parameter);
    }

    private void RootFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception($"Navigation failed to page {e.SourcePageType.FullName}: {e.Exception.Message}");
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        // Set window icon via Win32 (works for both title bar and taskbar)
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            var smallIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            var largeIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            if (smallIcon != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (UIntPtr)0, smallIcon);
            if (largeIcon != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (UIntPtr)1, largeIcon);
        }

        // Ensure window is resizable
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.IsResizable   = true;
            overlapped.IsMaximizable = true;
            overlapped.IsMinimizable = true;
        }
    }
}
