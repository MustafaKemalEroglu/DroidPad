using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DroidPad.Models;

namespace DroidPad.Services;

public sealed class MacroEngine
{
    public async Task RunAsync(
        IReadOnlyList<MacroCommand> source,
        IReadOnlyDictionary<string, Coordinate> coordinates,
        CancellationToken ct)
    {
        if (!await AdbService.EnsureConnectedAsync())
        {
            LogService.Error("Makro başlatılamadı: ADB bağlantısı yok.");
            return;
        }

        var commands = Normalize(source);
        if (!Validate(commands, coordinates)) return;

        var loopStart = MatchLoops(commands);
        var remaining = new Dictionary<int, int>();

        LogService.Info($"Makro başladı ({source.Count} komut)");
        var i = 0;
        while (i < commands.Count)
        {
            ct.ThrowIfCancellationRequested();

            switch (commands[i])
            {
                case ClickCommand c:
                    var p = coordinates[c.Target];
                    AdbService.Tap(p.X, p.Y);
                    i++;
                    break;

                case LongPressCommand lp:
                    var q = coordinates[lp.Target];
                    var ms = (int)(Math.Max(0.1, lp.Seconds) * 1000);
                    AdbService.LongPress(q.X, q.Y, ms);
                    await Task.Delay(ms, ct);
                    i++;
                    break;

                case TextCommand t:
                    AdbService.Text(t.Text);
                    i++;
                    break;

                case SwipeCommand s:
                    AdbService.Swipe(coordinates[s.Start].X, coordinates[s.Start].Y,
                                     coordinates[s.End].X, coordinates[s.End].Y);
                    i++;
                    break;

                case WaitCommand w:
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, w.Seconds)), ct);
                    i++;
                    break;

                case RandomWaitCommand rw:
                    var lo = Math.Min(rw.Min, rw.Max);
                    var hi = Math.Max(rw.Min, rw.Max);
                    await Task.Delay(TimeSpan.FromSeconds(lo + Random.Shared.NextDouble() * (hi - lo)), ct);
                    i++;
                    break;

                case KeyCommand k:
                    AdbService.KeyEvent(k.Keycode.Trim());
                    i++;
                    break;

                case LoopStartCommand ls:
                    remaining[i] = ls.Count;
                    i = ls.Count == 0 ? SkipLoop(commands, i) : i + 1;
                    break;

                case LoopEndCommand:
                    if (loopStart.TryGetValue(i, out var start) &&
                        (remaining[start] < 0 || --remaining[start] > 0))
                    {
                        // Bekleme içermeyen döngünün cihazı boğmasını önle
                        await Task.Delay(20, ct);
                        i = start + 1;
                    }
                    else i++;
                    break;

                default:
                    i++;
                    break;
            }
        }

        LogService.Success("Makro tamamlandı.");
    }

    /// <summary>Eşleşmeyen LOOP START'lar için sona örtük LOOP END ekler.</summary>
    private static List<MacroCommand> Normalize(IReadOnlyList<MacroCommand> source)
    {
        var list = source.ToList();
        var depth = 0;
        foreach (var c in list)
        {
            if (c is LoopStartCommand) depth++;
            else if (c is LoopEndCommand && depth > 0) depth--;
        }
        for (; depth > 0; depth--) list.Add(new LoopEndCommand());
        return list;
    }

    /// <summary>LOOP END indeksi → eşleşen LOOP START indeksi.</summary>
    private static Dictionary<int, int> MatchLoops(List<MacroCommand> cmds)
    {
        var map = new Dictionary<int, int>();
        var stack = new Stack<int>();
        for (var i = 0; i < cmds.Count; i++)
        {
            if (cmds[i] is LoopStartCommand) stack.Push(i);
            else if (cmds[i] is LoopEndCommand && stack.Count > 0) map[i] = stack.Pop();
        }
        return map;
    }

    private static int SkipLoop(List<MacroCommand> cmds, int start)
    {
        var depth = 0;
        for (var i = start; i < cmds.Count; i++)
        {
            if (cmds[i] is LoopStartCommand) depth++;
            else if (cmds[i] is LoopEndCommand && --depth == 0) return i + 1;
        }
        return cmds.Count;
    }

    private static bool Validate(List<MacroCommand> cmds, IReadOnlyDictionary<string, Coordinate> coords)
    {
        var missing = new HashSet<string>();
        foreach (var c in cmds)
        {
            switch (c)
            {
                case ClickCommand cc when !coords.ContainsKey(cc.Target): missing.Add(cc.Target); break;
                case LongPressCommand lc when !coords.ContainsKey(lc.Target): missing.Add(lc.Target); break;
                case SwipeCommand sc:
                    if (!coords.ContainsKey(sc.Start)) missing.Add(sc.Start);
                    if (!coords.ContainsKey(sc.End)) missing.Add(sc.End);
                    break;
            }
        }
        if (missing.Count == 0) return true;
        LogService.Error($"Makro durduruldu, tanımsız koordinat(lar): {string.Join(", ", missing)}");
        return false;
    }
}
