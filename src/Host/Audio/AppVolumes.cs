using System.Collections.Concurrent;
using System.Text.Json;

namespace LanCast.Audio;

/// <summary>
/// Volume individual por aplicativo (1.0 = 100%, até 2.0). Fica em memória para o mixer ler sem lock
/// e é gravado em volumes.json (com pequeno atraso) para valer também nas próximas execuções.
/// </summary>
public static class AppVolumes
{
    public const double Min = 0, Max = 2, Default = 1;

    private static readonly ConcurrentDictionary<string, double> Map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SaveLock = new();
    private static Timer? _saveTimer;
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (SaveLock)
        {
            if (_loaded) return;
            try
            {
                if (File.Exists(Paths.VolumesFile))
                    foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(Paths.VolumesFile)) ?? new())
                        Map[kv.Key] = Math.Clamp(kv.Value, Min, Max);
            }
            catch (Exception ex) { AppLog.Info($"[audio] ler volumes: {ex.Message}"); }
            _loaded = true;
        }
    }

    public static double Get(string name)
    {
        EnsureLoaded();
        return Map.TryGetValue(name, out var v) ? v : Default;
    }

    public static void Set(string name, double volume)
    {
        EnsureLoaded();
        volume = Math.Clamp(volume, Min, Max);
        if (Math.Abs(volume - Default) < 0.001) Map.TryRemove(name, out _); else Map[name] = volume;
        lock (SaveLock) (_saveTimer ??= new Timer(_ => Flush())).Change(400, Timeout.Infinite);
    }

    public static void Flush()
    {
        lock (SaveLock)
        {
            try { File.WriteAllText(Paths.VolumesFile, JsonSerializer.Serialize(Map.ToDictionary(kv => kv.Key, kv => kv.Value))); }
            catch (Exception ex) { AppLog.Info($"[audio] salvar volumes: {ex.Message}"); }
        }
    }
}
