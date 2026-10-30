using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class ControlPanel : UserControl
{
    public ControlPanel()
    {
        InitializeComponent();
    }

    private async void OnSaveProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Profili Kaydet",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON Profil") { Patterns = new[] { "*.json" } } }
        });
        if (file is null) return;
        await vm.SaveProfileAsync(file.Path.LocalPath);
    }

    private async void OnLoadProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Profil Yükle",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON Profil") { Patterns = new[] { "*.json" } } }
        });
        if (files.Count == 0) return;
        await vm.LoadProfileAsync(files[0].Path.LocalPath);
    }
}