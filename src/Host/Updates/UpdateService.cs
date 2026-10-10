using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LanCast.Updates;

internal sealed record UpdateRelease(Version Version, Uri DownloadUrl, string Sha256, long Size, bool Installed);

internal sealed class UpdateService
{
    internal const string Repository = "Guma325/LanCast";
    private readonly HttpClient _http;
    internal static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Location intentionally identifies the published single-file executable, which can be copied as the update worker.")]
    internal static bool CanApply => string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location);

    internal UpdateService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LanCast/" + CurrentVersion.ToString(3));
    }

    internal static bool IsInstalled(string executable)
    {
        const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B6C5F2F4-3A0E-4F64-9D61-5E2C7A1D9B11}_is1";
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(keyName);
            if (key?.GetValue("InstallLocation") is string directory &&
                Path.GetFullPath(Path.Combine(directory, "LanCast.exe")).Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal async Task<UpdateRelease?> CheckAsync(bool installed, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return ParseRelease(json.RootElement, CurrentVersion, installed);
    }

    internal static UpdateRelease? ParseRelease(JsonElement release, Version current, bool installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var version)) return null;
        var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        if (version <= normalizedCurrent) return null;
        var expected = installed ? $"LanCast-Setup-{version}.exe" : $"LanCast-Portable-{version}.exe";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != expected) continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
                !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/{tag}/", StringComparison.Ordinal))
                throw new InvalidDataException("O endereço do arquivo de atualização não pertence ao repositório do LanCast.");
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            if (digest == null || !Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("A Release não possui SHA-256 para validar a atualização. Publique novamente o arquivo no GitHub.");
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > 1024L * 1024 * 1024) throw new InvalidDataException("Tamanho de atualização inválido.");
            return new(version, uri, digest[7..], size, installed);
        }
        throw new InvalidDataException($"A versão {version} está publicada, mas falta o arquivo {expected} na Release.");
    }

    internal async Task<string> DownloadAsync(UpdateRelease release, string directory, IProgress<int> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "payload.exe");
        try
        {
            using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("O download tem um tamanho diferente do publicado.");
                    await dest.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress.Report((int)(total * 100 / release.Size));
                }
            }
            await VerifyAsync(path, release.Sha256, release.Size, cancellationToken);
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    internal static async Task VerifyAsync(string path, string expectedHash, long size, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length != size) throw new InvalidDataException("O download está incompleto.");
        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A verificação SHA-256 falhou. O arquivo não será executado.");
    }
}
