using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using AudioPenWin.Data;
using AudioPenWin.Data.Dao;
using AudioPenWin.Data.Repositories;
using AudioPenWin.Models;
using AudioPenWin.Services;
using AudioPenWin.Services.Stt;
using AudioPenWin.ViewModels;

namespace AudioPenWin;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;
    public static AppPreferences Preferences { get; private set; } = null!;
    public static AppSecrets Secrets { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Preferences = AppPreferences.Load();
        Secrets = AppSecrets.Load();
        Services = ConfigureServices();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Reset any recordings stuck in PROCESSING from a previous crash
        await Services.GetRequiredService<TranscriptionService>().RequeueStaleAsync();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Data
        services.AddSingleton<AppDatabase>();
        services.AddSingleton<RecordingDao>();
        services.AddSingleton<TranscriptSegmentDao>();
        services.AddSingleton<RecordingRepository>();
        services.AddSingleton<TranscriptRepository>();

        // Services
        services.AddSingleton<RecordingService>();
        services.AddSingleton<GcsSttService>();
        services.AddSingleton<AssemblySttService>();
        services.AddSingleton<WhisperSttService>();
        services.AddSingleton<TranscriptionService>();

        // ViewModels
        services.AddTransient<HomeViewModel>();
        services.AddTransient<RecordViewModel>();
        services.AddTransient<PlaybackViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
