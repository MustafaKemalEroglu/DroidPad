using System;
using System.Collections.ObjectModel;
using WaydroidController.Models;

namespace WaydroidController.Managers;

public class MacroManager
{
    public ObservableCollection<MacroCommand> Commands { get; } = new();

    // Yeni makro komutu ekler
    public void AddCommand(string cmdType, string cmdValue)
    {
        MacroCommand? cmd = cmdType switch
        {
            "click" => new ClickCommand { Target = cmdValue.Trim() },
            "swipe" => ParseSwipe(cmdValue),
            "wait" => double.TryParse(cmdValue, out var s) ? new WaitCommand { Seconds = s } : null,
            "random_wait" => ParseRandomWait(cmdValue),
            "key" => new KeyCommand { Keycode = cmdValue.Trim() },
            "loop_start" => int.TryParse(cmdValue, out var c) ? new LoopStartCommand { Count = c } : new LoopStartCommand { Count = -1 },
            "loop_end" => new LoopEndCommand(),
            _ => null
        };

        if (cmd is null) return;
        Commands.Add(cmd);
    }

    public void Remove(MacroCommand? selectedCmd)
    {
        if (selectedCmd is null) return;
        Commands.Remove(selectedCmd);
    }

    public void MoveUp(MacroCommand? selectedCmd)
    {
        if (selectedCmd is null) return;
        var idx = Commands.IndexOf(selectedCmd);
        if (idx > 0) Commands.Move(idx, idx - 1);
    }

    public void MoveDown(MacroCommand? selectedCmd)
    {
        if (selectedCmd is null) return;
        var idx = Commands.IndexOf(selectedCmd);
        if (idx < Commands.Count - 1) Commands.Move(idx, idx + 1);
    }

    private static SwipeCommand? ParseSwipe(string value)
    {
        var parts = value.Replace("->", ",").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? new SwipeCommand { Start = parts[0], End = parts[1] } : null;
    }

    private static RandomWaitCommand? ParseRandomWait(string value)
    {
        var parts = value.Split('-');
        return parts.Length == 2 && double.TryParse(parts[0], out var min) && double.TryParse(parts[1], out var max)
            ? new RandomWaitCommand { Min = min, Max = max } : null;
    }
}