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
    public MainWindow()
    {
        InitializeComponent();
        Title = "AudioPen";
        SetMinSize(400, 600);
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

    private void SetMinSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
            appWindow.SetIcon(iconPath);

        if (appWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.IsResizable = true;
            overlapped.IsMaximizable = true;
            overlapped.IsMinimizable = true;
        }
    }
}
