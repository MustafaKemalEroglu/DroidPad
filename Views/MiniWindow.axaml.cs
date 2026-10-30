using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WaydroidController.Views;

public partial class MiniWindow : Window
{
    public MiniWindow()
    {
        InitializeComponent();

        // Olay dinleyicisini kod tarafında ekliyoruz
        this.PointerPressed += OnWindowPointerPressed;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Sadece sol tık basılıysa ve tıklanan yer bir buton değilse sürüklemeye izin ver
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.Source is not Button)
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void OnBackToMainClick(object? sender, RoutedEventArgs e) => Close();
}