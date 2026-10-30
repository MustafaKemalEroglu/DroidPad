using CommunityToolkit.Mvvm.ComponentModel;

namespace WaydroidController.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _greeting = "Hello, Avalonia!";
}
