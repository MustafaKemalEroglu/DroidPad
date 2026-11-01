using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class ScrcpyService
{
    private static TcpClient? _videoClient;
    private static NetworkStream? _videoStream;
    private static TcpClient? _controlClient;
    private static NetworkStream? _controlStream;
    private static bool _isConnected = false;
    private static Process? _serverProcess;

    private static int _screenWidth = 1920;
    private static int _screenHeight = 1080;

    // Scrcpy sabitleri
    private const uint BUTTON_PRIMARY = 1; // MotionEvent.BUTTON_PRIMARY

    public static async Task<bool> EnsureServerRunningAsync(string version = "2.4")
    {
        string remotePath = $"/data/local/tmp/scrcpy-server-v{version}.jar";
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string localFilePath = Path.Combine(baseDir, "Resources", $"scrcpy-server-v{version}");

        if (!File.Exists(localFilePath))
        {
            LogService.Error($"[SCRCPY] HATA: Yerel dosya bulunamadı! Yol: {localFilePath}");
            return false;
        }

        try
        {
            // Dosya kontrolü ve yükleme
            var checkProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"shell \"[ -f {remotePath} ] && echo 'exists' || echo 'missing'\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            var checkResult = (await checkProcess!.StandardOutput.ReadToEndAsync()).Trim();
            await checkProcess.WaitForExitAsync();

            if (checkResult.Contains("missing"))
            {
                var pushProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = $"push \"{localFilePath}\" {remotePath}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (pushProcess != null) await pushProcess.WaitForExitAsync();
            }

            // Yönlendirmeleri temizle ve oluştur
            RunAdbCommand("forward --remove-all");
            RunAdbCommand("forward tcp:27183 localabstract:scrcpy");
            RunAdbCommand("forward tcp:27184 localabstract:scrcpy_control");

            LogService.Info("[SCRCPY] Port yönlendirmeleri yapıldı.");

            // Sunucuyu başlat (ses kapalı, video ve kontrol açık)
            if (_serverProcess == null || _serverProcess.HasExited)
            {
                LogService.Info("[SCRCPY] Sunucu başlatılıyor...");
                _serverProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "adb",
                        Arguments = $"shell CLASSPATH={remotePath} app_process / com.genymobile.scrcpy.Server {version} tunnel_forward=true control=true video=true audio=false",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                _serverProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) LogService.Debug($"[SERVER] {e.Data}"); };
                _serverProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) LogService.Debug($"[SERVER ERR] {e.Data}"); };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                await Task.Delay(2000);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"[SCRCPY] Sunucu başlatma hatası: {ex.Message}");
            return false;
        }
    }

    private static void RunAdbCommand(string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "adb",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        p?.WaitForExit();
    }

    public static async Task<bool> ConnectAsync(int width, int height)
    {
        if (_isConnected && _controlClient != null && _controlClient.Connected) return true;
        _screenWidth = width;
        _screenHeight = height;

        if (!await EnsureServerRunningAsync()) return false;

        try
        {
            // Video soketine bağlan
            _videoClient = new TcpClient();
            await _videoClient.ConnectAsync("127.0.0.1", 27183);
            _videoStream = _videoClient.GetStream();

            // Kontrol soketine bağlan
            _controlClient = new TcpClient();
            await _controlClient.ConnectAsync("127.0.0.1", 27184);
            _controlStream = _controlClient.GetStream();

            _isConnected = true;
            LogService.Success("[SCRCPY] Soketler bağlandı.");

            // Video akışını oku (sunucunun dummy byte'ını ve handshake verilerini tüketmek için)
            _ = Task.Run(ReadVideoStreamAsync);
            // Kontrol soketini de oku (sunucunun gönderdiği mesajları tüketmek için)
            _ = Task.Run(ReadControlStreamAsync);

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"[SCRCPY] Soket bağlantı hatası: {ex.Message}");
            _isConnected = false;
            return false;
        }
    }

    private static async Task ReadVideoStreamAsync()
    {
        try
        {
            byte[] buffer = new byte[4096];
            while (_videoClient != null && _videoClient.Connected)
            {
                int bytesRead = await _videoStream!.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
            }
        }
        catch { }
        finally { LogService.Warning("[SCRCPY] Video akışı koptu."); }
    }

    private static async Task ReadControlStreamAsync()
    {
        try
        {
            byte[] buffer = new byte[4096];
            while (_controlClient != null && _controlClient.Connected)
            {
                int bytesRead = await _controlStream!.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
            }
        }
        catch { }
        finally { LogService.Warning("[SCRCPY] Kontrol akışı koptu."); }
    }

    public static void SendTouchDown(int x, int y) => SendTouchEvent(x, y, 0);
    public static void SendTouchMove(int x, int y) => SendTouchEvent(x, y, 2);
    public static void SendTouchUp(int x, int y) => SendTouchEvent(x, y, 1);

    private static void SendTouchEvent(int x, int y, int action)
    {
        if (!_isConnected || _controlStream == null) return;

        try
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Scrcpy v2.4 INJECT_TOUCH_EVENT (32 byte, Big-Endian)
            writer.Write((byte)2);           // Type: INJECT_TOUCH_EVENT
            writer.Write((byte)action);      // Action: 0=DOWN, 1=UP, 2=MOVE
            WriteInt64BE(writer, -1);        // Pointer ID (-1: mouse)
            WriteInt32BE(writer, x);         // X
            WriteInt32BE(writer, y);         // Y
            WriteUInt16BE(writer, (ushort)_screenWidth);  // Width
            WriteUInt16BE(writer, (ushort)_screenHeight); // Height

            // Pressure: 2 byte ushort. UP için 0, diğerleri için max.
            ushort pressure = (ushort)(action == 1 ? 0 : 0xFFFF);
            WriteUInt16BE(writer, pressure);

            // Action Button (4 byte): her zaman 1 (BUTTON_PRIMARY)
            WriteUInt32BE(writer, BUTTON_PRIMARY);

            // Buttons (4 byte): DOWN ve MOVE'da basılı (1), UP'ta boş (0)
            WriteUInt32BE(writer, (uint)(action == 1 ? 0 : BUTTON_PRIMARY));

            var buffer = ms.ToArray();
            _controlStream.Write(buffer, 0, buffer.Length);
            _controlStream.Flush();

            LogService.Debug($"[SCRCPY] Touch: {action}, X:{x}, Y:{y}");
        }
        catch (Exception ex)
        {
            LogService.Error($"[SCRCPY] Gönderme hatası: {ex.Message}");
            _isConnected = false;
        }
    }

    // Big-Endian yazıcılar
    private static void WriteInt32BE(BinaryWriter writer, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }
    private static void WriteUInt32BE(BinaryWriter writer, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }
    private static void WriteUInt16BE(BinaryWriter writer, ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }
    private static void WriteInt64BE(BinaryWriter writer, long value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }
}