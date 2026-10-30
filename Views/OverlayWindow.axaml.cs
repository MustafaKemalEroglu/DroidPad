using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();

        this.KeyDown += OnWindowKeyDown;
    }

    private void OnTogglePanelClick(object? sender, RoutedEventArgs e)
    {
        SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var keyName = e.Key.ToString();

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        await vm.OnKeyPressedAsync(keyName);
    }

    // Şeffaf pencere üzerinde tıklama yapıldığında koordinat yakalama
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DataContext is MainWindowViewModel vm && vm.IsPickingCoordinate)
        {
            var point = e.GetPosition(this);
            vm.SetPickedCoordinates((int)point.X, (int)point.Y);

            // Panelin tekrar otomatik açılması kullanıcıya kolaylık sağlar
            SettingsPanel.IsVisible = true;
            e.Handled = true;
        }
    }

    private async void OnSaveProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Profil Yükle",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON Profil") { Patterns = new[] { "*.json" } } }
        });
        if (files.Count == 0) return;
        await vm.LoadProfileAsync(files[0].Path.LocalPath);
    }
}