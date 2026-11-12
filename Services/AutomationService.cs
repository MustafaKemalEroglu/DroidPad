using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DroidPad.Models;

namespace DroidPad.Services;

public partial class AutomationService : ObservableObject
{
    private readonly MacroEngine _engine = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _idleCts;
    private DateTime? _deadline;
    private bool _userStopped;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _remainingText = "";
    /// <summary>Makro elle durdurulmuş ama geri sayım arka planda sürüyor.</summary>
    [ObservableProperty] private bool _isCountdownPaused;

    /// <param name="timeLimitSeconds">0 = süresiz; aksi halde süre dolunca makro durdurulur.</param>
    /// <param name="keepCountingAfterMacro">Makro bitince ya da elle durdurulunca geri sayım süre dolana
    /// kadar sürer; makro yeniden başlatılırsa kalan süreden devam eder.</param>
    public async Task StartAsync(
        IReadOnlyList<MacroCommand> commands,
        IReadOnlyDictionary<string, Coordinate> coordinates,
        int timeLimitSeconds,
        bool keepCountingAfterMacro = false)
    {
        if (IsRunning) return;
        if (commands.Count == 0)
        {
            LogService.Warning("Çalıştırılacak komut yok.");
            return;
        }

        var keep = keepCountingAfterMacro && timeLimitSeconds > 0;
        StopIdleTicker();
        if (keep && _deadline is { } d && d > DateTime.UtcNow)
            LogService.Info("Geri sayım kalan süreden devam ediyor.");
        else
            _deadline = timeLimitSeconds > 0 ? DateTime.UtcNow.AddSeconds(timeLimitSeconds) : null;
        if (!keep && _deadline is null) RemainingText = "";

        using var cts = new CancellationTokenSource();
        _cts = cts;
        _userStopped = false;
        IsRunning = true;

        Task? ticker = null;
        if (_deadline is { } dl)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, (dl - DateTime.UtcNow).TotalSeconds)));
            ticker = TickAsync(dl, cts.Token);
        }

        try
        {
            var completed = await _engine.RunAsync(commands, coordinates, cts.Token);
            if (completed && keep)
            {
                LogService.Info("Makro bitti, geri sayım sürüyor (süre dolunca ya da durdurunca biter).");
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
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
            IsRunning = false;

            if (keep && _userStopped && _deadline is { } left && left > DateTime.UtcNow)
            {
                LogService.Info("Geri sayım arka planda sürüyor.");
                StartIdleTicker(left);
            }
            else
            {
                _deadline = null;
                RemainingText = "";
            }
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _userStopped = true;
        _cts?.Cancel();
    }

    /// <summary>Arka planda süren (duraklatılmış) geri sayımı iptal eder.</summary>
    public void CancelCountdown()
    {
        if (IsRunning) return;
        StopIdleTicker();
        _deadline = null;
        RemainingText = "";
    }

    private void StartIdleTicker(DateTime deadline)
    {
        var idle = new CancellationTokenSource();
        _idleCts = idle;
        IsCountdownPaused = true;
        _ = RunIdleAsync();

        async Task RunIdleAsync()
        {
            await TickAsync(deadline, idle.Token);
            if (idle.IsCancellationRequested) return; // iptal ya da yeniden başlatma: durumu çağıran yönetir
            LogService.Info("Geri sayım bitti.");
            _deadline = null;
            RemainingText = "";
            IsCountdownPaused = false;
        }
    }

    private void StopIdleTicker()
    {
        _idleCts?.Cancel();
        _idleCts = null;
        IsCountdownPaused = false;
    }

    private async Task TickAsync(DateTime deadline, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var left = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
                RemainingText = $"{left / 3600:00}:{left % 3600 / 60:00}:{left % 60:00}";
                if (left == 0) return;
                await Task.Delay(250, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
