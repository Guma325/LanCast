using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using LanCast.Updates;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}
var bytes = "LanCast update fixture"u8.ToArray();
var hash = Convert.ToHexString(SHA256.HashData(bytes));
string Release(string tag = "v1.10.0", bool installed = false, bool draft = false, bool prerelease = false,
    string? digest = "default", string? url = null, string? name = null, long? size = null) => JsonSerializer.Serialize(new
{
    tag_name = tag, draft, prerelease,
    assets = new[] { new { name = name ?? $"LanCast-{(installed ? "Setup" : "Portable")}-{tag.TrimStart('v')}.exe",
        browser_download_url = url ?? $"https://github.com/Guma325/LanCast/releases/download/{tag}/payload.exe",
        digest = digest == "default" ? "sha256:" + hash : digest, size = size ?? bytes.Length } }
});
UpdateRelease? Parse(string json, Version? current = null, bool installed = false)
{
    using var doc = JsonDocument.Parse(json);
    return UpdateService.ParseRelease(doc.RootElement, current ?? new Version(1, 9, 0, 0), installed);
}
Check(Parse(Release())?.Version == new Version(1, 10, 0), "numeric version comparison (1.10 > 1.9)");
Check(Parse(Release("v1.9.0")) == null, "same version with assembly revision is ignored");
Check(Parse(Release("v1.8.0")) == null, "downgrades are ignored");
Check(Parse(Release(draft: true)) == null, "draft is ignored");
Check(Parse(Release(prerelease: true)) == null, "prerelease is ignored");
Check(Parse(Release("v2.0.0-beta.1")) == null, "prerelease tag is ignored");
Check(Parse(Release("banana")) == null, "invalid version is ignored");
Check(Parse(Release(installed: true), installed: true)?.Installed == true, "installed mode selects Setup");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(installed: true))), "missing portable asset fails clearly");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(digest: null))), "missing digest rejected");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(digest: "sha256:abc"))), "malformed digest rejected");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(url: "https://example.com/payload.exe"))), "foreign download host rejected");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(url: "http://github.com/Guma325/LanCast/releases/download/v1.10.0/payload.exe"))), "non HTTPS rejected");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(url: "https://github.com/attacker/LanCast/releases/download/v1.10.0/payload.exe"))), "foreign repository rejected");
await Reject<InvalidDataException>(() => Task.FromResult(Parse(Release(size: 0))), "empty asset rejected");
var directory = Path.Combine(Path.GetTempPath(), "LanCast-UpdateChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var release = Parse(Release())!;
    using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    var service = new UpdateService(http);
    var destination = Path.Combine(directory, "valid");
    var path = await service.DownloadAsync(release, destination, new Progress<int>(), CancellationToken.None);
    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "download validated and saved");
    await Reject<InvalidDataException>(() => UpdateService.VerifyAsync(path, new string('0', 64), bytes.Length, CancellationToken.None), "tampered hash rejected");
    await Reject<InvalidDataException>(() => UpdateService.VerifyAsync(path, hash, bytes.Length + 1, CancellationToken.None), "truncated download rejected");
    var badFolder = Path.Combine(directory, "bad-hash");
    await Reject<InvalidDataException>(() => service.DownloadAsync(release with { Sha256 = new string('0', 64) }, badFolder, new Progress<int>(), CancellationToken.None), "download hash mismatch fails");
    Check(!File.Exists(Path.Combine(badFolder, "payload.exe")), "invalid payload is deleted");
    await Reject<InvalidDataException>(() => service.DownloadAsync(release with { Size = bytes.Length - 1 }, Path.Combine(directory, "oversized"), new Progress<int>(), CancellationToken.None), "oversized download rejected");
    await Reject<InvalidDataException>(() => service.DownloadAsync(release with { Size = bytes.Length + 1 }, Path.Combine(directory, "short"), new Progress<int>(), CancellationToken.None), "short download rejected");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Reject<OperationCanceledException>(() => service.DownloadAsync(release, Path.Combine(directory, "cancelled"), new Progress<int>(), cancelled.Token), "download cancellation");
    var target = Path.Combine(directory, "LanCast.exe");
    File.WriteAllText(target, "old version");
    UpdateWorker.ReplacePortable(path, target);
    Check(File.ReadAllBytes(target).SequenceEqual(bytes), "portable replacement succeeds");
    Check(!Directory.GetFiles(directory).Any(p => p.StartsWith(target + ".", StringComparison.OrdinalIgnoreCase)), "replacement leaves no staging/backup files");
    File.WriteAllText(target, "old version");
    using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        await Reject<IOException>(() => { UpdateWorker.ReplacePortable(path, target); return Task.CompletedTask; }, "locked executable replacement fails safely");
    Check(File.ReadAllText(target) == "old version", "old executable preserved on failure");
    Check(!Directory.GetFiles(directory).Any(p => p.StartsWith(target + ".", StringComparison.OrdinalIgnoreCase)), "failed replacement cleans staging");
    using var missingHttp = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
    Check(await new UpdateService(missingHttp).CheckAsync(false, CancellationToken.None) == null, "repository without releases handled");
    using var rateHttp = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
    await Reject<HttpRequestException>(() => new UpdateService(rateHttp).CheckAsync(false, CancellationToken.None), "rate limit fails without executing anything");
    using var checkHttp = new HttpClient(new FakeHandler(request =>
    {
        Check(request.Headers.UserAgent.Count > 0, "GitHub request has User-Agent");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release("v99.0.0")) };
    }));
    Check((await new UpdateService(checkHttp).CheckAsync(false, CancellationToken.None))?.Version.Major == 99, "GitHub response parsed");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(response(request));
    }
}
