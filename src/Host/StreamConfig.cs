using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoStreaming;

public sealed class StreamConfig
{
    public int Port { get; set; } = 8080;
    /// <summary>Senha opcional exigida dos espectadores (vazio = sem senha).</summary>
    public string Password { get; set; } = "";
    // ---- fonte compartilhada ----
    /// <summary>monitor | window</summary>
    public string SourceType { get; set; } = "monitor";
    public int Monitor { get; set; } = 0;
    /// <summary>Nome do dispositivo do monitor (ex.: \\.\DISPLAY1); mais estável que o índice.</summary>
    public string MonitorDevice { get; set; } = "";
    public string WindowExe { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    /// <summary>Ao compartilhar uma janela, enviar só o áudio do aplicativo dela.</summary>
    public bool OnlyAppAudio { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    public int Fps { get; set; } = 60;
    public int VideoBitrateKbps { get; set; } = 10000;
    /// <summary>0 = resolução nativa. Ex.: 720 ou 1080 para reduzir (usa CPU).</summary>
    public int Height { get; set; } = 0;
    public double KeyframeSeconds { get; set; } = 1.0;
    public bool ShowCursor { get; set; } = true;
    public int AudioBitrateKbps { get; set; } = 128;
    /// <summary>Apps silenciados na primeira execução (nome do processo, sem .exe).</summary>
    public string[] DefaultMutedApps { get; set; } = Array.Empty<string>();

    /// <summary>auto | nvenc | amf | qsv | x264</summary>
    public string Encoder { get; set; } = "auto";
    public bool StartOnLaunch { get; set; } = false;
    public bool MicEnabled { get; set; } = false;
    /// <summary>Id do dispositivo de captura (vazio = padrão do Windows).</summary>
    public string MicDeviceId { get; set; } = "";
    public double MicGain { get; set; } = 1.0;

    /// <summary>IPs banidos (persistidos). Use Ban/Unban/IsBanned; a lista é acessada por várias threads.</summary>
    public List<BanEntry> Bans { get; set; } = new();
    [JsonIgnore] private readonly object _banLock = new();

    public bool IsBanned(string ip) { lock (_banLock) return Bans.Any(b => b.Ip == ip); }
    public IReadOnlyList<BanEntry> BanSnapshot() { lock (_banLock) return Bans.ToList(); }

    public bool Ban(string ip, string name)
    {
        lock (_banLock)
        {
            if (Bans.Any(b => b.Ip == ip)) return false;
            Bans.Add(new BanEntry { Ip = ip, Name = name, At = DateTime.Now });
        }
        Save();
        return true;
    }

    public void Unban(string ip)
    {
        lock (_banLock) Bans.RemoveAll(b => b.Ip == ip);
        Save();
    }

    public static StreamConfig Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<StreamConfig>(File.ReadAllText(Paths.SettingsFile)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try { File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    public StreamConfig Clone() => JsonSerializer.Deserialize<StreamConfig>(JsonSerializer.Serialize(this))!;
}

public static class AppLog
{
    private static readonly object L = new();
    public static string FilePath => Path.Combine(Paths.DataDir, "log.txt");

    public static void Info(string msg)
    {
        try
        {
            lock (L)
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}", new System.Text.UTF8Encoding(true));
            }
        }
        catch { }
    }
}

public static class SystemInfo
{
    public static List<string> Gpus()
    {
        var list = new List<string>();
        try
        {
            using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var name in cls?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var k = cls!.OpenSubKey(name);
                var desc = k?.GetValue("DriverDesc") as string;
                var ver = k?.GetValue("DriverVersion") as string;
                if (!string.IsNullOrEmpty(desc)) list.Add($"{desc} (driver {ver})");
            }
        }
        catch { }
        if (list.Count == 0) list.Add("(não foi possível listar)");
        return list;
    }
}

public sealed class BanEntry
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime At { get; set; }
}

public static class Paths
{
    // VIDEOSTREAMING_DATA: usado só para testes (pasta de dados alternativa)
    public static readonly string DataDir = Directory.CreateDirectory(
        Environment.GetEnvironmentVariable("VIDEOSTREAMING_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoStreaming")).FullName;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string MutesFile => Path.Combine(DataDir, "mutes.json");

    /// <summary>Extrai o ffmpeg embutido (comprimido) na primeira execução / quando muda.</summary>
    private static readonly object FfmpegLock = new();

    public static string EnsureFfmpeg()
    {
        lock (FfmpegLock) return ExtractFfmpeg();     // encoder e pré-visualização podem pedir ao mesmo tempo na 1ª execução
    }

    private static string ExtractFfmpeg()
    {
        var asm = typeof(Paths).Assembly;
        var dest = Path.Combine(DataDir, "ffmpeg.exe");
        var marker = Path.Combine(DataDir, "ffmpeg.size");
        using var rs = asm.GetManifestResourceStream("ffmpeg.exe.gz");
        if (rs == null)
        {
            var side = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            return File.Exists(side) ? side : throw new FileNotFoundException("ffmpeg não encontrado");
        }
        string sig = rs.Length.ToString();
        if (File.Exists(dest) && File.Exists(marker) && File.ReadAllText(marker) == sig) return dest;

        var tmp = dest + ".tmp";
        using (var gz = new GZipStream(rs, CompressionMode.Decompress))
        using (var fs = File.Create(tmp))
            gz.CopyTo(fs);
        File.Move(tmp, dest, true);
        File.WriteAllText(marker, sig);
        return dest;
    }
}
