using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using AudioPenWin.Models;
using AudioPenWin.ViewModels;

namespace AudioPenWin.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } =
        App.Services.GetRequiredService<SettingsViewModel>();

    private bool _initializing;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _initializing = true;

        // STT provider radio buttons
        RbGcs.IsChecked        = ViewModel.SttProvider == SttProviderKind.GCS;
        RbAssemblyAi.IsChecked = ViewModel.SttProvider == SttProviderKind.AssemblyAI;
        RbWhisper.IsChecked    = ViewModel.SttProvider == SttProviderKind.Whisper;

        // Language combo
        foreach (ComboBoxItem item in CbLanguage.Items.Cast<ComboBoxItem>())
        {
            if ((string)item.Tag == ViewModel.Language)
            {
                CbLanguage.SelectedItem = item;
                break;
            }
        }
        if (CbLanguage.SelectedItem is null) CbLanguage.SelectedIndex = 0;

        // API keys
        PbGcs.Password        = ViewModel.GcsApiKey;
        PbAssemblyAi.Password = ViewModel.AssemblyAiApiKey;

        // Output folder
        TbOutputFolder.Text = ViewModel.OutputFolder;

        // Diarization
        TsDiarization.IsOn       = ViewModel.EnableDiarization;
        NbSpeakers.Value         = ViewModel.SpeakerCount;
        SpeakerCountPanel.Visibility = ViewModel.EnableDiarization ? Visibility.Visible : Visibility.Collapsed;

        _initializing = false;

        UpdateWhisperPanel();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.IsDownloadingModel)
                or nameof(ViewModel.IsModelDownloaded)
                or nameof(ViewModel.CanDownloadModel))
                UpdateWhisperPanel();
        };

        await ViewModel.LoadCostSummaryAsync();
    }

    private void UpdateWhisperPanel()
    {
        BtnDownloadModel.IsEnabled = ViewModel.CanDownloadModel;
        WhisperProgress.Visibility = ViewModel.IsDownloadingModel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        App.MainWindow.Navigate(typeof(HomePage));

    private void SttProvider_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var tag = (string)((RadioButton)sender).Tag;
        ViewModel.SttProvider = tag switch
        {
            "AssemblyAI" => SttProviderKind.AssemblyAI,
            "Whisper"    => SttProviderKind.Whisper,
            _            => SttProviderKind.GCS
        };
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || CbLanguage.SelectedItem is not ComboBoxItem item) return;
        ViewModel.Language = (string)item.Tag;
    }

    private void GcsKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.GcsApiKey = PbGcs.Password;
    }

    private void AssemblyAiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.AssemblyAiApiKey = PbAssemblyAi.Password;
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.OutputFolder = folder.Path;
            TbOutputFolder.Text = folder.Path;
        }
    }

    private void Diarization_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ViewModel.EnableDiarization = TsDiarization.IsOn;
        SpeakerCountPanel.Visibility = TsDiarization.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SpeakerCount_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_initializing || double.IsNaN(args.NewValue)) return;
        ViewModel.SpeakerCount = (int)args.NewValue;
    }

    private async void DownloadModel_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.DownloadWhisperModelAsync();
}
