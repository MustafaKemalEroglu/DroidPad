using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public sealed class MacroEngine
{
    private readonly Action<string> _log;

    public MacroEngine(Action<string> log) => _log = log;

    public async Task RunAsync(
        IReadOnlyList<MacroCommand> commands,
        IReadOnlyDictionary<string, Coordinate> coordinates,
        ResolutionDto profileRes,
        CancellationToken ct)
    {
        var adbTarget = await AdbService.FindTargetAsync(ct);
        if (adbTarget is null)
        {
            _log("Aktif ADB hedefi bulunamadı. Waydroid bağlantısı deneniyor...");
            await AdbService.TrySequentialAsyncPublicAsync(new[] { new[] { "connect", "127.0.0.1:5555" } }, ct);
            await Task.Delay(1000, ct);
            adbTarget = await AdbService.FindTargetAsync(ct) ?? "127.0.0.1:5555";
        }

        var (cw, ch) = await AdbService.GetResolutionAsync(adbTarget, ct);
        var scaleX = profileRes.Width > 0 ? (double)cw / profileRes.Width : 1.0;
        var scaleY = profileRes.Height > 0 ? (double)ch / profileRes.Height : 1.0;

        _log($"ADB: {adbTarget} | Ekran: {cw}x{ch} | Ölçek: {scaleX:F2}x{scaleY:F2}");

        var hasInfiniteLoop = commands.OfType<LoopStartCommand>().Any(l => l.Count == -1);

        do
        {
            var loopStack = new Stack<(int StartIndex, int Counter, bool Infinite)>();
            var i = 0;

            while (i < commands.Count && !ct.IsCancellationRequested)
            {
                var cmd = commands[i];
                switch (cmd)
                {
                    case ClickCommand click:
                        if (coordinates.TryGetValue(click.Target, out var c)) // click.Target = "TargetClick"
                        {
                            var (ok, err) = await AdbService.TapAsync(adbTarget, (int)(c.X * scaleX), (int)(c.Y * scaleY), ct);
                            if (!ok) _log($"Tıklama hatası ({click.Target}): {err}");
                        }
                        else _log($"Hata: '{click.Target}' koordinatı bulunamadı!");
                        i++; break;

                    case SwipeCommand swipe:
                        if (coordinates.TryGetValue(swipe.Start, out var s) && coordinates.TryGetValue(swipe.End, out var e))
                        {
                            var (ok, err) = await AdbService.SwipeAsync(adbTarget,
                                (int)(s.X * scaleX), (int)(s.Y * scaleY),
                                (int)(e.X * scaleX), (int)(e.Y * scaleY), 300, ct);
                            if (!ok) _log($"Kaydırma hatası: {err}");
                        }
                        i++; break;

                    case WaitCommand wait:
                        await Task.Delay(TimeSpan.FromSeconds(wait.Seconds), ct);
                        i++; break;

                    case RandomWaitCommand rw:
                        var sec = Random.Shared.NextDouble() * (rw.Max - rw.Min) + rw.Min;
                        await Task.Delay(TimeSpan.FromSeconds(sec), ct);
                        i++; break;

                    case KeyCommand key:
                        var (kOk, kErr) = await AdbService.KeyEventAsync(adbTarget, key.Keycode, ct);
                        if (!kOk) _log($"Tuş hatası: {kErr}");
                        i++; break;

                    case LoopStartCommand ls:
                        loopStack.Push((i, ls.Count, ls.Count == -1));
                        i++; break;

                    case LoopEndCommand:
                        if (loopStack.Count > 0)
                        {
                            var (start, counter, infinite) = loopStack.Peek();
                            if (infinite) i = start + 1;
                            else if (--counter > 0) { loopStack.Pop(); loopStack.Push((start, counter, false)); i = start + 1; }
                            else { loopStack.Pop(); i++; }
                        }
                        else i++;
                        break;

                    default: i++; break;
                }
            }
        } while (hasInfiniteLoop && !ct.IsCancellationRequested);
    }
}