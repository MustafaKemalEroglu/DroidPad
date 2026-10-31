using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WaydroidController.Models;

namespace WaydroidController.Managers;

public partial class WorkspaceManager : ObservableObject
{
    public ObservableCollection<Coordinate> Coordinates { get; } = new();
    public ObservableCollection<MacroCommand> Commands { get; } = new();

    public void AddOrUpdateCoordinate(string name, int x, int y)
    {
        var existing = Coordinates.FirstOrDefault(c => c.Name == name);
        if (existing != null)
        {
            existing.X = x;
            existing.Y = y;
            var idx = Coordinates.IndexOf(existing);
            Coordinates[idx] = new Coordinate { Name = name, X = x, Y = y }; // UI güncellemesi için
        }
        else
        {
            Coordinates.Add(new Coordinate { Name = name, X = x, Y = y });
        }
    }

    public void ClearAll()
    {
        Coordinates.Clear();
        Commands.Clear();
    }

    public void LoadData(System.Collections.Generic.Dictionary<string, Coordinate> coords, System.Collections.Generic.List<MacroCommand> cmds)
    {
        Coordinates.Clear();
        foreach (var kv in coords)
        {
            kv.Value.Name = kv.Key;
            Coordinates.Add(kv.Value);
        }

        Commands.Clear();
        foreach (var c in cmds) Commands.Add(c);
    }
}