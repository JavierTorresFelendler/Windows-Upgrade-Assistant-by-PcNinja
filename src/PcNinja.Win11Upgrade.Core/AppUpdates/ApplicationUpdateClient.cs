using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcNinja.Win11Upgrade.Core;

/// <summary>Stable, product-pinned discovery and verified bytes only; no file creation or replacement.</summary>
public sealed class AppUpdateClient : IDisposable
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/" + AppUpdateIdentity.Repository + "/releases/latest";
    public const string BetaReleasesUrl = "https://api.github.com/repos/" + AppUpdateIdentity.Repository + "/releases?per_page=5&page=";
    internal const int MaximumJsonBytes = 128 * 1024;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public AppUpdateClient() : this(new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan }, true) { }

    // Injected handlers must not follow redirects themselves; production always disables them.
    internal AppUpdateClient(HttpClient http) : this(http, false) { }
    private AppUpdateClient(HttpClient http, bool ownsHttp)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _ownsHttp = ownsHttp;
    }

    public async Task<AppUpdateCheckResult> CheckAsync(string installedVersion, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(CheckTimeout);
        try
        {
            var installed = ReleaseVersion.Parse(installedVersion);
            var selected = await SelectReleaseAsync(installed.IsBeta, deadline.Token).ConfigureAwait(false);
            if (selected is null)
                return installed.IsBeta ? new(AppUpdateCheckState.NoBetaRelease, "No Beta application release has been published yet.")
                    : new(AppUpdateCheckState.NoStableRelease, "No stable application release has been published yet.");
            var root = selected.Value;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v') || !ReleaseVersion.TryParse(tag[1..], out var available) ||
                root.GetProperty("draft").GetBoolean() ||
                root.GetProperty("prerelease").GetBoolean() != installed.IsBeta ||
                (installed.IsBeta ? !available!.IsBeta : !available!.IsStable))
                throw new InvalidDataException("The release is outside this application's update channel.");
            var asset = FindAsset(root, tag, AppUpdateIdentity.AssetNameFor(tag[1..]), AppUpdateIdentity.MaximumBytes);
            var manifestAsset = FindAsset(root, tag, AppUpdateIdentity.ManifestName, MaximumJsonBytes);
            using var manifestResponse = await GetAsync(new Uri(manifestAsset.Url), true, deadline.Token).ConfigureAwait(false);
            manifestResponse.EnsureSuccessStatusCode();
            var manifestBytes = await ReadJsonBytesAsync(manifestResponse, manifestAsset.Size, deadline.Token).ConfigureAwait(false);
            VerifyOptionalDigest(manifestAsset.Digest, Convert.ToHexString(SHA256.HashData(manifestBytes)));
            using var manifestJson = ReadJson(manifestBytes);
            var manifest = manifestJson.RootElement.Deserialize<AppUpdateManifest>(ManifestJson)
                ?? throw new InvalidDataException("The update manifest is empty.");
            ValidateManifest(manifest);
            if (manifest.Tag != tag || manifest.Channel != (installed.IsBeta ? "beta" : "stable") ||
                manifest.AssetUrl != asset.Url || manifest.SizeBytes != asset.Size)
                throw new InvalidDataException("The update manifest does not match its published release assets.");
            VerifyOptionalDigest(asset.Digest, manifest.Sha256);
            return available.CompareTo(installed) > 0
                ? new(AppUpdateCheckState.Available, $"Application update {manifest.Version} is available.", manifest)
                : new(AppUpdateCheckState.UpToDate, "No newer application version is available in this channel.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(AppUpdateCheckState.Unavailable, "The application update check timed out. Normal tool use can continue."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or FormatException or
            InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return new(AppUpdateCheckState.Unavailable, "Application updates are unavailable or release verification failed. Normal tool use can continue."); }
    }

    private async Task<JsonElement?> SelectReleaseAsync(bool beta, CancellationToken token)
    {
        if (!beta)
        {
            using var response = await GetAsync(new Uri(LatestReleaseUrl), false, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var json = ReadJson(await ReadJsonBytesAsync(response, null, token).ConfigureAwait(false));
            return json.RootElement.Clone();
        }
        JsonElement? selected = null;
        ReleaseVersion? best = null;
        // Bounded preview discovery; stable builds never request this list.
        for (var page = 1; page <= 3; page++)
        {
            using var response = await GetAsync(new Uri(BetaReleasesUrl + page), false, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var json = ReadJson(await ReadJsonBytesAsync(response, null, token).ConfigureAwait(false));
            var entries = json.RootElement.EnumerateArray().ToArray();
            if (entries.Length > 5) throw new InvalidDataException("The Beta release page exceeds its entry limit.");
            foreach (var entry in entries)
            {
                if (entry.GetProperty("draft").GetBoolean() || !entry.GetProperty("prerelease").GetBoolean()) continue;
                var tag = entry.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith('v') || !ReleaseVersion.TryParse(tag[1..], out var version) || !version!.IsBeta) continue;
                if (best is null || version.CompareTo(best) > 0) { best = version; selected = entry.Clone(); }
            }
            if (entries.Length < 5) break;
        }
        return selected;
    }

    /// <summary>The caller owns staging and must discard all output if this method fails.</summary>
    public async Task DownloadAsync(AppUpdateManifest release, Stream destination,
        IProgress<AppUpdateProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateManifest(release);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || destination.CanSeek && (destination.Position != 0 || destination.Length != 0))
            throw new ArgumentException("Application update staging must be an empty writable stream.", nameof(destination));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(DownloadTimeout);
        try
        {
            using var response = await GetAsync(new Uri(release.AssetUrl), true, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declared && declared != release.SizeBytes)
                throw new InvalidDataException("The application update Content-Length does not match its manifest.");
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long received = 0;
            while (true)
            {
                var count = await ReadWithDeadlineAsync(source, buffer, deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (received + count > release.SizeBytes || received + count > AppUpdateIdentity.MaximumBytes)
                    throw new InvalidDataException("The application update exceeds its verified byte limit.");
                hash.AppendData(buffer, 0, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                received += count;
                progress?.Report(new(received, release.SizeBytes));
            }
            if (received != release.SizeBytes)
                throw new InvalidDataException("The application update was truncated.");
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The application update SHA-256 does not match its manifest.");
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("The application update transfer timed out; the current application was not replaced."); }
        catch (HttpRequestException ex)
        { throw new IOException("The application update could not be downloaded; the current application was not replaced.", ex); }
    }

    public static void ValidateManifest(AppUpdateManifest release)
    {
        if (release is null || release.SchemaVersion != 1 || release.Product != AppUpdateIdentity.Product ||
            release.Repository != AppUpdateIdentity.Repository ||
            !ReleaseVersion.TryParse(release.Version, out var version) ||
            !(release.Channel == "stable" && version!.IsStable || release.Channel == "beta" && version!.IsBeta) || release.Version.Contains('+') ||
            release.Tag != "v" + release.Version || release.AssetName != AppUpdateIdentity.AssetNameFor(release.Version) ||
            release.SizeBytes <= 0 || release.SizeBytes > AppUpdateIdentity.MaximumBytes ||
            !IsPinnedUrl(release.AssetUrl, release.Tag, release.AssetName) ||
            release.ReleaseNotesUrl != ReleasePage(release.Tag) ||
            release.Importance is not ("normal" or "critical") ||
            string.IsNullOrWhiteSpace(release.Message) || release.Message.Length > 1200 ||
            release.Message.Any(c => char.IsControl(c) && c is not ('\r' or '\n')) ||
            release.Message.Any(c => c is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069') ||
            release.Sha256 is not { Length: 64 } || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The application update manifest has invalid identity, version, release URL, size or SHA-256.");
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri initial, bool releaseAsset, CancellationToken token)
    {
        var current = initial;
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("PcNinja-WindowsUpgradeAssistant-AppUpdate/1.0");
            request.Headers.Accept.ParseAdd(releaseAsset ? "application/octet-stream" : "application/vnd.github+json");
            if (!releaseAsset) request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            headersDeadline.CancelAfter(CheckTimeout);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false);
            // An injected/custom handler must not silently redirect before policy gets to inspect it.
            if (response.RequestMessage?.RequestUri is { } actual && actual != current)
            {
                response.Dispose();
                throw new InvalidDataException("The application update transport redirected unexpectedly.");
            }
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            try
            {
                if (!releaseAsset || redirects >= 5 || response.Headers.Location is not { } location ||
                    !Uri.TryCreate(current, location, out var next) || !IsAllowedCdn(next))
                    throw new InvalidDataException("The application update redirected outside its allowed release asset hosts.");
                current = next;
            }
            finally { response.Dispose(); }
        }
    }

    private static async Task<byte[]> ReadJsonBytesAsync(HttpResponseMessage response, long? expected, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } declared &&
            (declared > MaximumJsonBytes || declared < 0 || expected.HasValue && declared != expected.Value))
            throw new InvalidDataException("Application update metadata has an invalid byte count.");
        var exact = expected ?? response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await ReadWithDeadlineAsync(source, buffer, token).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > MaximumJsonBytes || exact.HasValue && output.Length + count > exact.Value)
                throw new InvalidDataException("Application update metadata exceeds its byte limit.");
            output.Write(buffer, 0, count);
        }
        if (exact.HasValue && output.Length != exact.Value)
            throw new InvalidDataException("Application update metadata was truncated.");
        return output.ToArray();
    }

    private static async Task<int> ReadWithDeadlineAsync(Stream source, byte[] buffer, CancellationToken token)
    {
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(token);
        stalled.CancelAfter(ReadTimeout);
        return await source.ReadAsync(buffer, stalled.Token).ConfigureAwait(false);
    }

    private static JsonDocument ReadJson(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate application update metadata fields are not allowed.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private static PublishedAsset FindAsset(JsonElement release, string tag, string name, long maximum)
    {
        var assets = release.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("A required application update asset is missing or ambiguous.");
        var item = assets[0];
        var url = item.GetProperty("browser_download_url").GetString() ?? "";
        var size = item.GetProperty("size").GetInt64();
        if (size <= 0 || size > maximum || !IsPinnedUrl(url, tag, name) ||
            item.TryGetProperty("state", out var state) && state.GetString() != "uploaded")
            throw new InvalidDataException("A published application update asset has invalid identity or size.");
        var digest = item.TryGetProperty("digest", out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
        return new(url, size, digest);
    }

    private static void VerifyOptionalDigest(string? digest, string hash)
    {
        if (digest is not null && !digest.Equals("sha256:" + hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The published asset digest does not match application update metadata.");
    }

    private static bool IsPinnedUrl(string? url, string tag, string name) =>
        url == $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
    private static string ReleasePage(string tag) => $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/{Uri.EscapeDataString(tag)}";
    private static bool IsAllowedCdn(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.IdnHost == "release-assets.githubusercontent.com" || uri.IdnHost == "objects.githubusercontent.com");
    private sealed record PublishedAsset(string Url, long Size, string? Digest);
}
