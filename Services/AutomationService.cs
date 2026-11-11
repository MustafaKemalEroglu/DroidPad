using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DroidPad.Models;

namespace DroidPad.Services;

public partial class AutomationService : ObservableObject
{
    private readonly MacroEngine _engine = new();
    private CancellationTokenSource? _cts;
    private bool _userStopped;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _remainingText = "";

    /// <param name="timeLimitSeconds">0 = süresiz; aksi halde süre dolunca makro durdurulur.</param>
    public async Task StartAsync(
        IReadOnlyList<MacroCommand> commands,
        IReadOnlyDictionary<string, Coordinate> coordinates,
        int timeLimitSeconds)
    {
        if (IsRunning) return;
        if (commands.Count == 0)
        {
            LogService.Warning("Çalıştırılacak komut yok.");
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        _userStopped = false;
        IsRunning = true;

        Task? ticker = null;
        if (timeLimitSeconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(timeLimitSeconds));
            ticker = TickAsync(timeLimitSeconds, cts.Token);
        }

        try
        {
            await _engine.RunAsync(commands, coordinates, cts.Token);
        }
        catch (OperationCanceledException)
        {
            LogService.Info(_userStopped ? "Makro durduruldu." : "Süre doldu, makro durduruldu.");
        }
        catch (Exception ex)
        {
            LogService.Error($"Makro hatası: {ex.Message}");
        }
        finally
        {
            cts.Cancel();
            if (ticker is not null) await ticker;
            _cts = null;
            RemainingText = "";
            IsRunning = false;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _userStopped = true;
        _cts?.Cancel();
    }

    private async Task TickAsync(int total, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var left = Math.Max(0, total - (int)Math.Ceiling(sw.Elapsed.TotalSeconds));
                RemainingText = $"{left / 3600:00}:{left % 3600 / 60:00}:{left % 60:00}";
                await Task.Delay(250, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
