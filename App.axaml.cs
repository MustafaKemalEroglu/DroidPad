using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WaydroidController.ViewModels;
using WaydroidController.Views;

namespace WaydroidController;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();

            // Doğrudan şeffaf Kontrol Paneli (OverlayWindow) ana pencere olarak atanır
            desktop.MainWindow = new OverlayWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}