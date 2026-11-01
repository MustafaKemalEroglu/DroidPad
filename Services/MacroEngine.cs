using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public class MacroEngine
{
    public async Task RunAsync(
        IReadOnlyList<MacroCommand> commands,
        IReadOnlyDictionary<string, Coordinate> coordinates,
        ResolutionDto profileRes,
        CancellationToken ct)
    {
        if (!await AdbService.EnsureConnectedAsync())
        {
            LogService.Error("Hata: ADB bağlantısı kurulamadı!");
            return;
        }

        var resolution = await AdbService.GetResolutionAsync();
        int cw = resolution.Width;
        int ch = resolution.Height;
        var scaleX = profileRes.Width > 0 ? (double)cw / profileRes.Width : 1.0;
        var scaleY = profileRes.Height > 0 ? (double)ch / profileRes.Height : 1.0;

        LogService.Info($"Makro Başladı | Ekran: {cw}x{ch} | Ölçek: {scaleX:F2}x{scaleY:F2}");

        var hasInfiniteLoop = commands.OfType<LoopStartCommand>().Any(l => l.Count == -1);

        do
        {
            var loopStack = new Stack<(int StartIndex, int Counter, bool Infinite)>();
            var i = 0;

            while (i < commands.Count)
            {
                ct.ThrowIfCancellationRequested(); // İptal kontrolü
                var cmd = commands[i];

                switch (cmd)
                {
                    case ClickCommand click:
                        if (coordinates.TryGetValue(click.Target, out var c))
                            await AdbService.TapAsync((int)(c.X * scaleX), (int)(c.Y * scaleY));
                        else
                            LogService.Error($"Hata: '{click.Target}' bulunamadı!");
                        i++;
                        break;

                    case SwipeCommand swipe:
                        if (coordinates.TryGetValue(swipe.Start, out var s) && coordinates.TryGetValue(swipe.End, out var e))
                            await AdbService.SwipeAsync((int)(s.X * scaleX), (int)(s.Y * scaleY), (int)(e.X * scaleX), (int)(e.Y * scaleY), 300);
                        i++;
                        break;

                    case WaitCommand wait:
                        // Thread.Sleep yerine asenkron bekleme (CPU'yu yormaz, anında iptal edilebilir)
                        await Task.Delay(TimeSpan.FromSeconds(wait.Seconds), ct);
                        i++;
                        break;

                    case RandomWaitCommand rw:
                        var sec = Random.Shared.NextDouble() * (rw.Max - rw.Min) + rw.Min;
                        await Task.Delay(TimeSpan.FromSeconds(sec), ct);
                        i++;
                        break;

                    case KeyCommand key:
                        await AdbService.KeyEventAsync(key.Keycode);
                        i++;
                        break;

                    case LoopStartCommand ls:
                        loopStack.Push((i, ls.Count, ls.Count == -1));
                        i++;
                        break;

                    case LoopEndCommand:
                        if (loopStack.Count > 0)
                        {
                            var (start, counter, infinite) = loopStack.Peek();
                            if (infinite)
                                i = start + 1;
                            else if (--counter > 0)
                            {
                                loopStack.Pop();
                                loopStack.Push((start, counter, false));
                                i = start + 1;
                            }
                            else
                            {
                                loopStack.Pop();
                                i++;
                            }
                        }
                        else
                            i++;
                        break;

                    default:
                        i++;
                        break;
                }
            }
        } while (hasInfiniteLoop); // Sonsuz döngü varsa baştan başla
    }
}