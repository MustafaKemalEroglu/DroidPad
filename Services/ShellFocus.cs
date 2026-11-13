using System.Diagnostics;

namespace DroidPad.Services;

/// <summary>
/// GNOME eklentisi (droidpad-follow) üzerinden klavye odağını Waydroid ile kaplama arasında taşır.
/// Metin kutusu açıkken odak Waydroid'e verilir; klavye yerel girdi olarak gider (Türkçe karakterler dahil).
/// Eklenti yoksa false döner ve uygulama kendi yazma yoluna düşer.
/// </summary>
public static class ShellFocus
{
    public static Task<bool> FocusWaydroidAsync() => CallAsync("FocusWaydroid");
    public static Task<bool> FocusPadAsync() => CallAsync("FocusPad");

    private static Task<bool> CallAsync(string method) => Task.Run(() =>
    {
        try
        {
            var psi = new ProcessStartInfo("gdbus")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var a in new[]
            {
                "call", "--session", "--dest", "org.droidpad.Follow", "--object-path", "/org/droidpad/Follow",
                "--method", $"org.droidpad.Follow.{method}"
            }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(1500)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    });
}
