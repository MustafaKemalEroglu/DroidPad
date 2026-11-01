using Avalonia.Threading;
using WaydroidController.Models;
using WaydroidController.Services;

namespace WaydroidController.Managers;

public class AutomationManager
{
    private readonly MacroEngine _engine = new();
    private CancellationTokenSource? _runCts;
    private CountdownService? _countdown;

    public bool IsRunning { get; private set; }
    public bool EnableCountdown { get; set; }
    public bool StopCountdownOnStop { get; set; }
    public int CdHours { get; set; } = 1;
    public int CdMinutes { get; set; }
    public int CdSeconds { get; set; }
    public string MiniCountdownText { get; private set; } = "01:00:00";
    public bool CanEditCountdown => EnableCountdown && !IsRunning;

    public event Action? StateChanged;

    public async Task StartAsync(
        IEnumerable<MacroCommand> commands,
        Dictionary<string, Coordinate> coordsDict,
        ResolutionDto resolution,
        Action onStateChanged)
    {
        var cmdList = commands.ToList();
        if (!cmdList.Any())
        {
            LogService.Error("Hata: Çalıştırılacak komut yok!");
            return;
        }

        if (EnableCountdown)
        {
            var total = CdHours * 3600 + CdMinutes * 60 + CdSeconds;
            if (total <= 0)
            {
                LogService.Error("Hata: Geri sayım süresi 0'dan büyük olmalıdır!");
                return;
            }
            _countdown?.Stop();
            _countdown = new CountdownService(OnCountdownTick, () => Dispatcher.UIThread.Post(Stop));
            _countdown.Start(total);
        }

        IsRunning = true;
        onStateChanged();

        _runCts = new CancellationTokenSource();
        try
        {
            await _engine.RunAsync(cmdList, coordsDict, resolution, _runCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            onStateChanged();
            _runCts?.Dispose();
            _runCts = null;

            if (StopCountdownOnStop && _countdown?.IsRunning == true)
            {
                _countdown.Stop();
            }
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _runCts?.Cancel();
        LogService.Info("Otomasyon durduruldu.");
    }

    private void OnCountdownTick(int remaining)
    {
        var h = remaining / 3600;
        var m = remaining % 3600 / 60;
        var s = remaining % 60;
        Dispatcher.UIThread.Post(() =>
        {
            MiniCountdownText = $"{h:00}:{m:00}:{s:00}";
            CdHours = h; CdMinutes = m; CdSeconds = s;
            StateChanged?.Invoke();
        });
    }
}