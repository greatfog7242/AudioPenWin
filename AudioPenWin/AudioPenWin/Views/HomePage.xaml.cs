using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using AudioPenWin.Data.Entities;
using AudioPenWin.Data.Repositories;
using AudioPenWin.Services;
using AudioPenWin.ViewModels;

namespace AudioPenWin.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } =
        App.Services.GetRequiredService<HomeViewModel>();

    private readonly DispatcherQueue _dq;
    private DispatcherQueueTimer? _pollTimer;

    public HomePage()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        StartPolling();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopPolling();
    }

    private void StartPolling()
    {
        _pollTimer = _dq.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(3);
        _pollTimer.Tick += async (_, _) => await ViewModel.RefreshStatusesAsync();
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    private void Record_Click(object sender, RoutedEventArgs e) =>
        App.MainWindow.Navigate(typeof(RecordPage));

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        App.MainWindow.Navigate(typeof(SettingsPage));

    private void RecordingList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecordingCardViewModel card)
            App.MainWindow.Navigate(typeof(PlaybackPage), card.Id);
    }

    private void RecordingList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement el) return;

        var card = FindCardInParents(el);
        if (card is null) return;

        var flyout = new MenuFlyout();

        var renameItem = new MenuFlyoutItem { Text = "Rename" };
        renameItem.Click += async (_, _) => await ShowRenameDialogAsync(card);

        var exportItem = new MenuFlyoutItem { Text = "Export…" };
        exportItem.Click += async (_, _) => await ExportRecordingAsync(card);

        var deleteItem = new MenuFlyoutItem
        {
            Text = "Delete",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68))
        };
        deleteItem.Click += async (_, _) => await ShowDeleteDialogAsync(card);

        flyout.Items.Add(renameItem);
        flyout.Items.Add(exportItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(deleteItem);
        flyout.ShowAt((UIElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;
    }

    private static RecordingCardViewModel? FindCardInParents(FrameworkElement el)
    {
        DependencyObject? current = el;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is RecordingCardViewModel card)
                return card;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async Task ShowRenameDialogAsync(RecordingCardViewModel card)
    {
        var tb = new TextBox
        {
            Text = card.Title,
            SelectionStart = 0,
            SelectionLength = card.Title.Length,
            PlaceholderText = "Recording title"
        };
        var dlg = new ContentDialog
        {
            Title = "Rename recording",
            Content = tb,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(tb.Text))
        {
            await ViewModel.RenameAsync(card.Id, tb.Text.Trim());
        }
    }

    private async Task ShowDeleteDialogAsync(RecordingCardViewModel card)
    {
        var dlg = new ContentDialog
        {
            Title = "Delete recording",
            Content = $"Delete \"{card.Title}\"? This also deletes the audio file and cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteAsync(card.Id);
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchText = sender.Text;
    }

    private async Task ExportRecordingAsync(RecordingCardViewModel card)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var recordingRepo = App.Services.GetRequiredService<RecordingRepository>();
        var transcriptRepo = App.Services.GetRequiredService<TranscriptRepository>();

        var recording = await recordingRepo.GetByIdAsync(card.Id);
        if (recording is null) return;
        var segments = await transcriptRepo.GetByRecordingIdAsync(card.Id);

        try
        {
            var exportDir = await AudioFileUtil.ExportAsync(recording, segments, folder.Path);
            var dlg = new ContentDialog
            {
                Title = "Export complete",
                Content = $"Saved to:\n{exportDir}",
                PrimaryButtonText = "Open folder",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                System.Diagnostics.Process.Start("explorer.exe", $"\"{exportDir}\"");
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "Export failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".mp4", ".mov", ".avi", ".mkv", ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".flac" })
            picker.FileTypeFilter.Add(ext);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await ImportFileAsync(file.Path);
    }

    private async Task ImportFileAsync(string sourcePath)
    {
        var id = Guid.NewGuid().ToString();
        var dir = RecordingService.GetRecordingDir(id);
        Directory.CreateDirectory(dir);

        var ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        bool isVideo = ext is ".mp4" or ".mov" or ".avi" or ".mkv";

        string audioPath;
        string sourceType;

        // Show busy overlay and wait cursor while FFmpeg is running
        ImportStatusText.Text = isVideo ? "Processing video…" : "Importing…";
        ImportOverlay.Visibility = Visibility.Visible;
        BtnImport.IsEnabled = false;
        BtnRecord.IsEnabled = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Wait);

        try
        {
            try
            {
                if (isVideo)
                {
                    sourceType = "VIDEO_IMPORT";
                    audioPath = System.IO.Path.Combine(dir, "audio.m4a");
                    await AudioFileUtil.ExtractAudioFromVideoAsync(sourcePath, audioPath);
                }
                else
                {
                    sourceType = "AUDIO_IMPORT";
                    var destName = "audio" + ext;
                    audioPath = System.IO.Path.Combine(dir, destName);
                    File.Copy(sourcePath, audioPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
                await new ContentDialog
                {
                    Title = "Import failed",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

        var title = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        var entity = new RecordingEntity
        {
            Id = id,
            Title = title,
            SourceType = sourceType,
            FilePath = audioPath,
            DurationMs = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status = "PENDING",
            SttProvider = App.Preferences.SttProvider.ToString(),
            Language = App.Preferences.Language,
            SpeakerCount = App.Preferences.SpeakerCount
        };

        var repo = App.Services.GetRequiredService<RecordingRepository>();
        await repo.InsertAsync(entity);
        _ = App.Services.GetRequiredService<TranscriptionService>().EnqueueAsync(id);

        await ViewModel.LoadAsync();
        App.MainWindow.Navigate(typeof(PlaybackPage), id);
        }
        finally
        {
            ImportOverlay.Visibility = Visibility.Collapsed;
            BtnImport.IsEnabled = true;
            BtnRecord.IsEnabled = true;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        }
    }
}
