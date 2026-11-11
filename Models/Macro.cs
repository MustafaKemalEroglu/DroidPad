using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DroidPad.Models;

/// <summary>Adlandırılmış komut dizisi; bir profilde birden fazla makro bulunabilir.</summary>
public partial class Macro : ObservableObject
{
    [ObservableProperty] private string _name = "Makro";
    public ObservableCollection<MacroCommand> Commands { get; set; } = new();

    public override string ToString() => Name;
}
