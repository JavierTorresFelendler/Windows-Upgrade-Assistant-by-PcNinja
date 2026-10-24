using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PcNinja.Win11Upgrade.Core;

internal static class ApplicationUpdateChecks
{
    private static readonly byte[] FixtureBytes = Encoding.UTF8.GetBytes("ordinary synthetic updater bytes - never an executable");
    private static readonly JsonSerializerOptions CamelJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task RunAsync()
    {
        CheckVersionsAndManifests();
        await CheckDiscoveryAsync();
        await CheckDownloadsAsync();
        Console.WriteLine("PASS: Stable application update identity/SemVer, pinned release/manifest assets, bounded JSON/redirects, cancellation, exact byte/hash checks with fake HTTP/memory only. No candidate executable or replacement used.");
    }

    private static AppUpdateManifest Manifest(string version = "1.1.0") => new(1, AppUpdateIdentity.Product,
        AppUpdateIdentity.Repository, AppUpdateIdentity.ChannelFor(version), version, "v" + version, AppUpdateIdentity.AssetNameFor(version),
        AssetUrl("v" + version, AppUpdateIdentity.AssetNameFor(version)), FixtureBytes.Length,
        Convert.ToHexString(SHA256.HashData(FixtureBytes)), NotesUrl("v" + version));

    private static string AssetUrl(string tag, string name) =>
        $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
    private static string NotesUrl(string tag) => $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/{Uri.EscapeDataString(tag)}";

    private static void CheckVersionsAndManifests()
    {
        var ordered = new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
        for (var index = 1; index < ordered.Length; index++)
            Require(ReleaseVersion.Parse(ordered[index]).CompareTo(ReleaseVersion.Parse(ordered[index - 1])) > 0, "SemVer precedence changed.");
        Require(ReleaseVersion.Parse("1.1.0+first.001").CompareTo(ReleaseVersion.Parse("1.1.0+different.002")) == 0,
            "Build metadata affected version precedence.");
        Require(ReleaseVersion.Parse("1.1.0-rc.99999999999999999999").CompareTo(ReleaseVersion.Parse("1.1.0-rc.10")) > 0,
            "Long numeric prerelease identifier comparison overflowed.");
        foreach (var invalid in new[] { "", "v1.1.0", "1.1", "1.1.0.0", "01.1.0", "1.01.0", "1.1.00", "1.1.0-",
            "1.1.0-rc.01", "1.1.0+", "1.1.0+bad..id", "1.1.0-rc..1", "1.1.0\n", " 1.1.0", "1.1.0+space id", "1.1.0-שלום" })
            Require(!ReleaseVersion.TryParse(invalid, out _), "Malformed release version was accepted: " + invalid);
        Require(ReleaseVersion.TryParse(AppUpdateIdentity.BuildVersion, out _) &&
            AppUpdateIdentity.Version == AppUpdateIdentity.BuildVersion.Split('+')[0], "Shared Core update identity is not a valid build version.");

        var valid = Manifest();
        AppUpdateClient.ValidateManifest(valid);
        MustRejectManifest(Manifest("1.1.0+unpublished"));
        foreach (var invalid in new[]
        {
            valid with { SchemaVersion = 2 }, valid with { Importance = "urgent" }, valid with { Message = "" },
            valid with { Message = new string('x', 1201) }, valid with { Message = "unsafe\u202Etext" }, valid with { Product = "Other product" }, valid with { Repository = "evil/repository" },
            valid with { Channel = "preview" }, valid with { Version = "1.1.0-rc.1", Tag = "v1.1.0-rc.1" },
            valid with { Version = "01.1.0" }, valid with { Tag = "v1.1.1" }, valid with { AssetName = "../other.exe" },
            valid with { AssetUrl = valid.AssetUrl.Replace(AppUpdateIdentity.Repository, "evil/repository") },
            valid with { AssetUrl = $"https://github.com/{AppUpdateIdentity.Repository}/releases/latest/download/{AppUpdateIdentity.AssetName}" },
            valid with { AssetUrl = valid.AssetUrl.Replace("https://", "http://") },
            valid with { AssetUrl = valid.AssetUrl.Replace("github.com", "user@github.com") },
            valid with { AssetUrl = valid.AssetUrl.Replace("github.com", "github.com:444") },
            valid with { AssetUrl = valid.AssetUrl + "?secret=value" }, valid with { AssetUrl = valid.AssetUrl + "#fragment" },
            valid with { AssetUrl = valid.AssetUrl.Replace("/releases/", "/%2e%2e/releases/") },
            valid with { SizeBytes = 0 }, valid with { SizeBytes = AppUpdateIdentity.MaximumBytes + 1 },
            valid with { Sha256 = "" }, valid with { Sha256 = new string('G', 64) }, valid with { ReleaseNotesUrl = NotesUrl("v9.9.9") }
        }) MustRejectManifest(invalid);
    }

    private static async Task CheckDiscoveryAsync()
    {
        var beta = Manifest("1.1.0-beta.2");
        Require((await CheckFixtureAsync(beta, "1.1.0-beta.1")).State == AppUpdateCheckState.Available, "Beta 1 did not offer Beta 2.");
        Require((await CheckFixtureAsync(beta, "1.1.0-beta.2")).State == AppUpdateCheckState.UpToDate, "Beta offered itself.");
        Require((await CheckFixtureAsync(beta, "1.1.0")).State == AppUpdateCheckState.Unavailable, "Stable accepted a Beta.");
        Require((await CheckFixtureAsync(Manifest(), "1.1.0-beta.1")).State == AppUpdateCheckState.NoBetaRelease, "Beta selected a stable release.");
        var release = Manifest();
        foreach (var pair in new[] { ("1.1.0-rc.8+commit", AppUpdateCheckState.Available), ("1.0.9", AppUpdateCheckState.Available),
            ("1.1.0", AppUpdateCheckState.UpToDate), ("1.1.0+different", AppUpdateCheckState.UpToDate), ("1.1.1", AppUpdateCheckState.UpToDate),
            ("1.2.0-rc.1", AppUpdateCheckState.UpToDate) })
        {
            var result = await CheckFixtureAsync(release, pair.Item1);
            Require(result.State == pair.Item2 && (result.Release is not null) == (pair.Item2 == AppUpdateCheckState.Available),
                "Stable/installed version discovery returned an incorrect state.");
        }
        foreach (var change in new Action<JsonObject>[]
        {
            root => root["draft"] = true, root => root["prerelease"] = true, root => root["tag_name"] = "v1.1.0-rc.9",
            root => root["assets"]!.AsArray().RemoveAt(0), root => root["assets"]!.AsArray().RemoveAt(1),
            root => root["assets"]!.AsArray().Add(root["assets"]![0]!.DeepClone()),
            root => root["assets"]![0]!["browser_download_url"] = "https://evil.test/app.exe",
            root => root["assets"]![1]!["browser_download_url"] = AssetUrl("v1.0.9", AppUpdateIdentity.ManifestName),
            root => root["assets"]![0]!["size"] = AppUpdateIdentity.MaximumBytes + 1,
            root => root["assets"]![1]!["size"] = AppUpdateClient.MaximumJsonBytes + 1,
            root => root["assets"]![0]!["digest"] = "sha256:" + new string('0', 64),
            root => root["assets"]![1]!["digest"] = "sha256:" + new string('0', 64),
            root => root["assets"]![0]!["state"] = "new"
        }) Require((await CheckFixtureAsync(release, "1.0.0", apiChange: change)).State == AppUpdateCheckState.Unavailable,
            "Malformed or unbound GitHub release metadata supplied an update.");
        foreach (var changed in new[] { release with { Product = "Wrong" }, release with { Channel = "preview" },
            release with { AssetUrl = AssetUrl("v1.0.9", AppUpdateIdentity.AssetNameFor("1.0.9")) }, release with { SizeBytes = release.SizeBytes + 1 },
            release with { Version = "1.1.1", Tag = "v1.1.1" } })
            Require((await CheckFixtureAsync(release, "1.0.0", manifestBytes: JsonSerializer.SerializeToUtf8Bytes(changed, CamelJson))).State == AppUpdateCheckState.Unavailable,
                "A malformed/mismatched manifest supplied an update.");

        var normal = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(release, CamelJson));
        foreach (var malformed in new[] { "not JSON", normal.Replace("{", "{\"schemaVersion\":2,", StringComparison.Ordinal),
            normal.TrimEnd('}') + ",\"unexpected\":true}", new string('[', 20) + "0" + new string(']', 20) })
            Require((await CheckFixtureAsync(release, "1.0.0", manifestBytes: Encoding.UTF8.GetBytes(malformed))).State == AppUpdateCheckState.Unavailable,
                "Malformed/duplicate/deep/unknown manifest JSON was accepted.");

        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            using var handler = new FixtureHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            var result = await client.CheckAsync("1.0.0", CancellationToken.None);
            Require(result.State == (status == HttpStatusCode.NotFound ? AppUpdateCheckState.NoStableRelease : AppUpdateCheckState.Unavailable) && handler.Requests.Count == 1,
                "Missing stable release/rate limit handling changed.");
        }
        using (var handler = new FixtureHandler((_, _) => throw new HttpRequestException("offline")))
        using (var http = new HttpClient(handler))
        using (var client = new AppUpdateClient(http))
            Require((await client.CheckAsync("1.0.0", CancellationToken.None)).State == AppUpdateCheckState.Unavailable, "Offline check blocked normal tool use.");
        foreach (var oversized in new HttpContent[] { new ByteArrayContent(new byte[AppUpdateClient.MaximumJsonBytes + 1]),
            new ChunkedContent(new byte[AppUpdateClient.MaximumJsonBytes + 1]) })
        {
            using var handler = new FixtureHandler((request, _) => Task.FromResult(Response(request, oversized)));
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            Require((await client.CheckAsync("1.0.0", CancellationToken.None)).State == AppUpdateCheckState.Unavailable,
                "Known/chunked JSON size bound was ignored.");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var handler = new FixtureHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Cancelled check resumed.");
        }))
        using (var http = new HttpClient(handler))
        using (var client = new AppUpdateClient(http))
            await MustCancelAsync(() => client.CheckAsync("1.0.0", cancellation.Token));

        Require((await CheckFixtureAsync(release, "1.0.0", redirectManifest: true)).State == AppUpdateCheckState.Available,
            "Allowed GitHub asset CDN metadata redirect failed.");
        Require((await CheckFixtureAsync(release, "1.0.0", redirectApi: true)).State == AppUpdateCheckState.Unavailable,
            "Latest API was allowed to redirect to an arbitrary endpoint.");
    }

    private static async Task<AppUpdateCheckResult> CheckFixtureAsync(AppUpdateManifest release, string installed,
        Action<JsonObject>? apiChange = null, byte[]? manifestBytes = null, bool redirectManifest = false, bool redirectApi = false)
    {
        manifestBytes ??= JsonSerializer.SerializeToUtf8Bytes(release, CamelJson);
        var api = JsonSerializer.SerializeToNode(new
        {
            tag_name = release.Tag, draft = false, prerelease = release.Channel == "beta",
            assets = new[]
            {
                new { name = release.AssetName, browser_download_url = release.AssetUrl, size = release.SizeBytes,
                    state = "uploaded", digest = "sha256:" + release.Sha256 },
                new { name = AppUpdateIdentity.ManifestName, browser_download_url = AssetUrl(release.Tag, AppUpdateIdentity.ManifestName),
                    size = (long)manifestBytes.Length, state = "uploaded", digest = "sha256:" + Convert.ToHexString(SHA256.HashData(manifestBytes)) }
            }
        })!.AsObject();
        apiChange?.Invoke(api);
        using var handler = new FixtureHandler((request, _) =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri.StartsWith(AppUpdateClient.BetaReleasesUrl, StringComparison.Ordinal))
                return Task.FromResult(Response(request, new StringContent("[" + api.ToJsonString() + "]")));
            if (uri == AppUpdateClient.LatestReleaseUrl)
                return Task.FromResult(redirectApi ? Redirect("https://api.github.com/other") : Response(request, new StringContent(api.ToJsonString())));
            if (uri == AssetUrl(release.Tag, AppUpdateIdentity.ManifestName))
                return Task.FromResult(redirectManifest ? Redirect("https://release-assets.githubusercontent.com/fixture/manifest?sig=test") :
                    Response(request, new ByteArrayContent(manifestBytes)));
            if (redirectManifest && request.RequestUri.Host == "release-assets.githubusercontent.com")
                return Task.FromResult(Response(request, new ByteArrayContent(manifestBytes)));
            throw new Exception("Unexpected synthetic updater request: " + uri);
        });
        using var http = new HttpClient(handler);
        using var client = new AppUpdateClient(http);
        return await client.CheckAsync(installed, CancellationToken.None);
    }

    private static async Task CheckDownloadsAsync()
    {
        var release = Manifest();
        foreach (var redirect in new[] { false, true })
        {
            using var handler = new FixtureHandler((request, _) => Task.FromResult(
                redirect && request.RequestUri!.Host == "github.com" ? Redirect("https://objects.githubusercontent.com/fixture/app?sig=test") :
                    Response(request, new ByteArrayContent(FixtureBytes))));
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            using var destination = new MemoryStream();
            var progress = new List<AppUpdateProgress>();
            await client.DownloadAsync(release, destination, new ImmediateProgress(progress.Add), CancellationToken.None);
            Require(destination.ToArray().SequenceEqual(FixtureBytes) && progress.Count > 0 && progress[^1] == new AppUpdateProgress(FixtureBytes.Length, FixtureBytes.Length),
                "Exact verified bytes or progress changed.");
        }
        foreach (var content in new Func<HttpContent>[]
        {
            () => new ChunkedContent(FixtureBytes[..^1]), () => new ChunkedContent([.. FixtureBytes, 0]),
            () => new ByteArrayContent(new byte[FixtureBytes.Length]),
            () => WrongLengthContent(FixtureBytes, FixtureBytes.Length + 1L)
        })
        {
            using var handler = new FixtureHandler((request, _) => Task.FromResult(Response(request, content())));
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            using var destination = new MemoryStream();
            await MustFailAsync(() => client.DownloadAsync(release, destination, null, CancellationToken.None));
        }
        foreach (var bad in new[] { "https://evil.test/app", "http://objects.githubusercontent.com/app", "https://objects.githubusercontent.com:444/app",
            "https://user@objects.githubusercontent.com/app", "https://release-assets.githubusercontent.com.evil/app",
            "https://github.com/other/repo/releases/download/v1.0.0/app.exe", "file:///C:/app.exe" })
        {
            using var handler = new FixtureHandler((_, _) => Task.FromResult(Redirect(bad)));
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            using var destination = new MemoryStream();
            await MustFailAsync(() => client.DownloadAsync(release, destination, null, CancellationToken.None));
            Require(handler.Requests.Count == 1 && destination.Length == 0, "An unsafe redirect was followed or wrote candidate bytes.");
        }
        using (var handler = new FixtureHandler((_, _) => Task.FromResult(Redirect("https://objects.githubusercontent.com/loop"))))
        using (var http = new HttpClient(handler))
        using (var client = new AppUpdateClient(http))
        using (var destination = new MemoryStream())
        {
            await MustFailAsync(() => client.DownloadAsync(release, destination, null, CancellationToken.None));
            Require(handler.Requests.Count == 6, "Manual redirect bound did not stop after five redirects.");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var handler = new FixtureHandler((request, _) => Task.FromResult(Response(request,
            new ChunkedContent(FixtureBytes, cancellation)))))
        using (var http = new HttpClient(handler))
        using (var client = new AppUpdateClient(http))
        using (var destination = new MemoryStream())
            await MustCancelAsync(() => client.DownloadAsync(release, destination, null, cancellation.Token));
        using (var cancellation = new CancellationTokenSource())
        using (var handler = new FixtureHandler((_, _) => throw new Exception("Pre-cancelled operation performed HTTP.")))
        using (var http = new HttpClient(handler))
        using (var client = new AppUpdateClient(http))
        using (var destination = new MemoryStream())
        {
            cancellation.Cancel();
            await MustCancelAsync(() => client.DownloadAsync(release, destination, null, cancellation.Token));
            await MustCancelAsync(() => client.CheckAsync("1.0.0", cancellation.Token));
            Require(handler.Requests.Count == 0 && destination.Length == 0, "Pre-cancelled update had effects.");
        }
        using (var handler = new FixtureHandler((_, _) => throw new Exception("Invalid manifest performed HTTP.")))
        using (var http = new HttpClient(handler))
        using (var client = new AppUpdateClient(http))
        using (var destination = new MemoryStream())
            await MustFailAsync(() => client.DownloadAsync(release with { Product = "wrong" }, destination, null, CancellationToken.None));
    }

    private static HttpContent WrongLengthContent(byte[] bytes, long length)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentLength = length;
        return content;
    }
    private static HttpResponseMessage Response(HttpRequestMessage request, HttpContent content) =>
        new(HttpStatusCode.OK) { RequestMessage = request, Content = content };
    private static HttpResponseMessage Redirect(string uri)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(uri);
        return response;
    }
    private static void MustRejectManifest(AppUpdateManifest manifest)
    {
        try { AppUpdateClient.ValidateManifest(manifest); }
        catch (InvalidDataException) { return; }
        throw new Exception("Invalid application update manifest was accepted.");
    }
    private static async Task MustFailAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or HttpRequestException) { return; }
        throw new Exception("Unsafe or incomplete application update was accepted.");
    }
    private static async Task MustCancelAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Application update user cancellation was swallowed.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private sealed class ImmediateProgress(Action<AppUpdateProgress> report) : IProgress<AppUpdateProgress>
    {
        public void Report(AppUpdateProgress value) => report(value);
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return send(request, cancellationToken);
        }
    }
    private sealed class ChunkedContent(byte[] bytes, CancellationTokenSource? cancel = null) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new CancelStream(bytes, cancel));
    }
    private sealed class CancelStream(byte[] bytes, CancellationTokenSource? cancel) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
