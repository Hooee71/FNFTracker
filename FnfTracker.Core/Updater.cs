using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace FnfTracker.Core;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string ExeUrl, string? Sha256Url, string PageUrl, string Notes);

/// <summary>
/// Checks this project's GitHub Releases for a newer FNFTracker.exe, downloads it, verifies its
/// checksum, and swaps it in. Releases are made by .github/workflows/release.yml when a v* tag is pushed.
/// </summary>
public static class Updater
{
    public const string Owner = "Hooee71";
    public const string Repo = "FNFTracker";
    public const string ExeAssetName = "FNFTracker.exe";
    public const string ShaAssetName = "FNFTracker.exe.sha256";

    public static string LatestReleaseApi => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases";

    public static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FNFTracker", current.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>Returns the latest release if it's newer than <paramref name="current"/>, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, Version current, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet (or repo private)
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var latest = ParseRelease(json);
        return latest != null && IsNewer(latest.Version, current) ? latest : null;
    }

    /// <summary>Reads a GitHub "release" JSON object. Null if it has no usable version or .exe asset.</summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var version = ParseVersion(tag);
        if (version == null) return null;

        string? exeUrl = null, shaUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (url == null) continue;
                if (string.Equals(name, ExeAssetName, StringComparison.OrdinalIgnoreCase)) exeUrl = url;
                else if (string.Equals(name, ShaAssetName, StringComparison.OrdinalIgnoreCase)) shaUrl = url;
            }
        }
        if (exeUrl == null) return null;

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(version, tag, exeUrl, shaUrl, page, notes);
    }

    /// <summary>"v1.2.3" or "1.2.3" → 1.2.3 (always three parts). Null if it isn't a version.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(new[] { '-', '+' });          // ignore "-beta", "+build"
        if (dash >= 0) text = text[..dash];
        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    /// <summary>Compares major.minor.patch only (assembly versions carry a 4th part we don't use).</summary>
    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>
    /// Downloads the new .exe next to the running one (same drive, so the swap is a rename) and
    /// checks it against the published SHA-256. Returns the downloaded file's path.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo update, string currentExePath,
                                                   IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var target = currentExePath + ".download";
        if (File.Exists(target)) File.Delete(target);

        using (var response = await http.GetAsync(update.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dest = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report((double)done / total.Value);
            }
        }

        if (update.Sha256Url != null)
        {
            var expected = (await http.GetStringAsync(update.Sha256Url, ct).ConfigureAwait(false)).Trim();
            if (!ChecksumMatches(target, expected))
            {
                File.Delete(target);
                throw new InvalidDataException("The downloaded update didn't match its checksum, so it wasn't installed. Try again later.");
            }
        }
        return target;
    }

    /// <summary>Expected may be just the hash, or "hash  filename" (sha256sum style).</summary>
    public static bool ChecksumMatches(string path, string expected)
    {
        var want = expected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the running .exe with the downloaded one. Windows won't let a running .exe be
    /// overwritten, but it can be renamed: current → .old, download → current. The caller then
    /// starts the new .exe and exits; <see cref="CleanUpOldVersion"/> removes the .old next launch.
    /// If the second step fails, the original is put back.
    /// </summary>
    public static void SwapIn(string downloadedPath, string currentExePath)
    {
        var old = currentExePath + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(currentExePath, old);
        try
        {
            File.Move(downloadedPath, currentExePath);
        }
        catch
        {
            File.Move(old, currentExePath);
            throw;
        }
    }

    /// <summary>Deletes leftovers from a previous update. Safe to call on every start.</summary>
    public static void CleanUpOldVersion(string currentExePath)
    {
        foreach (var leftover in new[] { currentExePath + ".old", currentExePath + ".download" })
        {
            try { if (File.Exists(leftover)) File.Delete(leftover); }
            catch { /* still locked by the old process for a moment; next launch gets it */ }
        }
    }

    /// <summary>
    /// True when running from a build folder (bin\Debug, bin\Release) rather than a published .exe.
    /// Self-update is skipped there so it never overwrites a development build.
    /// </summary>
    public static bool IsDevelopmentBuild(string exePath) =>
        exePath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(exePath).Equals(ExeAssetName, StringComparison.OrdinalIgnoreCase);
}
