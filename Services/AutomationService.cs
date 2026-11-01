using CommunityToolkit.Mvvm.ComponentModel;
using WaydroidController.Managers;
using WaydroidController.Models;

namespace WaydroidController.Services;

public partial class AutomationService : ObservableObject
{
    private readonly MacroEngine _engine = new MacroEngine();
    private CancellationTokenSource? _runCts;
    private CountdownService? _countdown;

    [ObservableProperty] private bool _isRunning;

    public event Action<int>? OnCountdownTick;
    public event Action<string>? OnLog;

    public async Task StartAsync(WorkspaceManager workspace, ResolutionDto resolution, bool useCountdown, int totalSeconds, bool stopCountdownOnStop)
    {
        if (CommandsEmpty(workspace)) { OnLog?.Invoke("Hata: Çalıştırılacak komut yok!"); return; }

        if (useCountdown)
        {
            if (totalSeconds <= 0) { OnLog?.Invoke("Hata: Geri sayım süresi 0'dan büyük olmalıdır!"); return; }

            _countdown?.Stop();
            _countdown = new CountdownService(
                (remaining) => OnCountdownTick?.Invoke(remaining),
                () => Stop());
            _countdown.Start(totalSeconds);
        }

        IsRunning = true;
        _runCts = new CancellationTokenSource();

        try
        {
            var coordsDict = workspace.Coordinates.ToDictionary(c => c.Name, c => c);
            await _engine.RunAsync(workspace.Commands, coordsDict, resolution, _runCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Makro Hatası: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
            if (stopCountdownOnStop && _countdown?.IsRunning == true) _countdown.Stop();
        }
    }

    public void Stop()
    {
        if (IsRunning)
        {
            _runCts?.Cancel();
            OnLog?.Invoke("Otomasyon durduruldu.");
        }
    }

    private bool CommandsEmpty(WorkspaceManager workspace) => workspace.Commands.Count == 0;
}