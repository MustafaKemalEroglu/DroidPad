using System.Diagnostics;

namespace WaydroidController.Services;

public sealed class CountdownService
{
    private readonly Action<int> _onTick;
    private readonly Action _onExpire;
    private CancellationTokenSource? _cts;

    public CountdownService(Action<int> onTick, Action onExpire)
    {
        _onTick = onTick;
        _onExpire = onExpire;
    }

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public void Start(int totalSeconds)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var lastRemaining = -1;

                while (!ct.IsCancellationRequested)
                {
                    var remaining = totalSeconds - (int)sw.Elapsed.TotalSeconds;

                    if (remaining <= 0)
                    {
                        _onTick(0);
                        _onExpire();
                        break;
                    }

                    if (remaining != lastRemaining)
                    {
                        _onTick(remaining);
                        lastRemaining = remaining;
                    }
                    await Task.Delay(100, ct);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}