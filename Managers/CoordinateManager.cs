using System;
using System.Collections.ObjectModel;
using System.Linq;
using WaydroidController.Models;

namespace WaydroidController.Managers;

public class CoordinateManager
{
    public ObservableCollection<Coordinate> Coordinates { get; } = new();

    public void AddOrUpdate(string name, string coordX, string coordY, Action<string> logger)
    {
        if (!int.TryParse(coordX, out var x) || !int.TryParse(coordY, out var y)) return;
        if (string.IsNullOrWhiteSpace(name)) return;

        var existing = Coordinates.FirstOrDefault(c => c.Name == name);
        if (existing != null)
        {
            existing.X = x;
            existing.Y = y;
            var idx = Coordinates.IndexOf(existing);
            Coordinates[idx] = new Coordinate { Name = name, X = x, Y = y };
            logger($"Koordinat güncellendi: {name} ({x}, {y})");
        }
        else
        {
            Coordinates.Add(new Coordinate { Name = name, X = x, Y = y });
            logger($"Koordinat eklendi: {name} ({x}, {y})");
        }
    }

    public void Remove(Coordinate? selectedCoord, Action<string> logger)
    {
        if (selectedCoord is null) return;
        Coordinates.Remove(selectedCoord);
        logger("Koordinat silindi.");
    }
}