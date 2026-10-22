using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FnfTracker.Core;

/// <summary>The games a published site carries for other apps to sync from (matches.json).</summary>
public sealed class SiteFeed
{
    public const int CurrentSchema = 1;
    public int Schema { get; set; } = CurrentSchema;
    public List<Match> Matches { get; set; } = new();
}

/// <summary>What a sync changed.</summary>
/// <param name="AlreadyHad">Site games skipped because you recorded the same game yourself.</param>
/// <param name="Dismissed">Site games skipped because you deleted them here earlier.</param>
public sealed record SyncResult(int Added, int Updated, int Removed, int AlreadyHad, int Dismissed, int SiteGames)
{
    public bool Changed => Added + Updated + Removed > 0;

    public string Summary()
    {
        if (!Changed) return $"Up to date ({SiteGames} game(s) on the site).";
        var parts = new List<string>();
        if (Added > 0) parts.Add($"{Added} new");
        if (Updated > 0) parts.Add($"{Updated} updated");
        if (Removed > 0) parts.Add($"{Removed} removed");
        return string.Join(", ", parts) + $" ({SiteGames} game(s) on the site).";
    }
}

/// <summary>A sync failure with a message fit to show the user.</summary>
public sealed class SyncException(string message) : Exception(message);

/// <summary>
/// Lets a friend's copy of the app follow a published FNF site. Publishing adds matches.json next to the
/// pages (every game except your private notes); following reads it over plain HTTPS, so friends need no
/// GitHub account or token. The site is the source of truth for the games it carries: sync adds them,
/// takes in your edits, and removes games you deleted. Games the friend recorded themselves are never
/// touched, and their own notes on a synced game are kept.
/// </summary>
public static class SiteSync
{
    public const string DataFile = "matches.json";

    /// <summary>
    /// The JSON published as <see cref="DataFile"/>. Notes stay private, like on the pages. No timestamp
    /// (version.json has it), so publishing unchanged games leaves the site unchanged.
    /// </summary>
    public static string BuildFeed(FnfData data)
    {
        var feed = new SiteFeed
        {
            Matches = data.Matches.Select(m =>
            {
                var copy = CloneMatch(m);
                copy.Notes = "";
                copy.SyncedFrom = null;
                return copy;
            }).ToList(),
        };
        return JsonSerializer.Serialize(feed, DataStore.JsonOptions) + "\n";
    }

    /// <summary>
    /// A site address as typed: "https://name.github.io/repo/", that with a page name on the end, or a
    /// GitHub repo ("name/repo") → the site's base address with a trailing slash. Null if it's neither.
    /// </summary>
    public static string? NormalizeSiteUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();

        if (!t.Contains("://") && !t.Contains('.') && SitePublisher.ParseRepo(t) is { } repo)
            return SitePublisher.SiteUrl(repo.Owner, repo.Repo);
        if (Regex.IsMatch(t, @"^(https?://)?(www\.)?github\.com/", RegexOptions.IgnoreCase) && SitePublisher.ParseRepo(t) is { } r2)
            return SitePublisher.SiteUrl(r2.Owner, r2.Repo);

        if (!t.Contains("://")) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0)
            return null;
        var path = uri.AbsolutePath;
        var last = path[(path.LastIndexOf('/') + 1)..];
        if (last.Contains('.')) path = path[..(path.Length - last.Length)];   // index.html, stats.html, matches.json
        if (!path.EndsWith('/')) path += "/";
        return $"https://{uri.Host.ToLowerInvariant()}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{path}";
    }

    public static HttpClient CreateClient(Version appVersion)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 50 * 1024 * 1024 };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FNFTracker", appVersion.ToString(3)));
        http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return http;
    }

    public static async Task<SiteFeed> FetchAsync(HttpClient http, string siteUrl, CancellationToken ct = default)
    {
        var url = siteUrl + DataFile + "?t=" + DateTime.UtcNow.Ticks;
        HttpResponseMessage res;
        try
        {
            res = await http.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncException("Couldn't reach the site: " + ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SyncException("The site took too long to answer. Try again.");
        }

        using (res)
        {
            if (res.StatusCode == HttpStatusCode.NotFound)
                throw new SyncException("That site has no game data to sync. Check the address, and ask its owner to " +
                                        "update FNF Tracker and publish once more (older versions didn't include it).");
            if (!res.IsSuccessStatusCode)
                throw new SyncException($"The site answered with an error ({(int)res.StatusCode}). Try again later.");

            SiteFeed? feed;
            try
            {
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                feed = JsonSerializer.Deserialize<SiteFeed>(text, DataStore.JsonOptions);
            }
            catch (JsonException)
            {
                throw new SyncException("That address isn't an FNF Tracker site (its game data couldn't be read).");
            }
            if (feed is null) throw new SyncException("The site's game data was empty.");
            if (feed.Schema > SiteFeed.CurrentSchema)
                throw new SyncException("That site was published by a newer FNF Tracker. Update the app, then sync again.");
            return feed;
        }
    }

    /// <summary>
    /// Brings <paramref name="data"/> in step with the site's games. Games already here with the same Id
    /// (from the site, or from an old partner-file merge) are replaced by the site's copy, keeping your notes.
    /// Games this site sent before but no longer has are removed. Site games you recorded yourself, or deleted
    /// here, are skipped. Your own games are never changed.
    /// </summary>
    public static SyncResult Apply(FnfData data, SiteFeed feed, string siteUrl)
    {
        var settings = data.Settings;
        var dismissed = settings.SyncDismissedIds.ToHashSet();
        var remote = new Dictionary<Guid, Match>();
        foreach (var m in feed.Matches)
            if (m.Id != Guid.Empty) remote.TryAdd(m.Id, m);

        int added = 0, updated = 0, removed = 0, alreadyHad = 0, skippedDismissed = 0;

        // Games that came from this site but are gone from it now.
        removed = data.Matches.RemoveAll(m => m.SyncedFrom == siteUrl && !remote.ContainsKey(m.Id));

        var byId = data.Matches.ToDictionary(m => m.Id);
        foreach (var incoming in remote.Values)
        {
            if (dismissed.Contains(incoming.Id))
            {
                skippedDismissed++;
                continue;
            }

            var copy = CloneMatch(incoming);
            copy.SyncedFrom = siteUrl;

            if (byId.TryGetValue(incoming.Id, out var local))
            {
                if (local.SyncedFrom != null && local.SyncedFrom != siteUrl)
                {
                    alreadyHad++;           // another followed site owns it; leave it alone
                    continue;
                }
                copy.Notes = local.Notes;   // notes are yours, and the site never carries them
                if (!SameContent(local, copy))
                {
                    data.Matches[data.Matches.IndexOf(local)] = copy;
                    byId[copy.Id] = copy;
                    updated++;
                }
                continue;
            }

            if (data.Matches.Any(m => m.SyncedFrom == null && DataStore.IsSameGame(m, incoming)))
            {
                alreadyHad++;
                continue;
            }

            copy.Notes = "";
            data.Matches.Add(copy);
            byId[copy.Id] = copy;
            added++;
        }

        // Only remember deletions the site still has; the rest can't come back anyway.
        settings.SyncDismissedIds = settings.SyncDismissedIds.Where(remote.ContainsKey).Distinct().ToList();

        if (added + updated + removed > 0) data.Matches.Sort((a, b) => a.PlayedAt.CompareTo(b.PlayedAt));
        return new SyncResult(added, updated, removed, alreadyHad, skippedDismissed, remote.Count);
    }

    /// <summary>
    /// The site is your own published site (following yourself would delete nothing but would mark all
    /// your games as synced). Compares normalized addresses.
    /// </summary>
    public static bool IsOwnSite(string siteUrl, string? publishRepo) =>
        SitePublisher.ParseRepo(publishRepo) is { } r &&
        string.Equals(SitePublisher.SiteUrl(r.Owner, r.Repo), siteUrl, StringComparison.OrdinalIgnoreCase);

    private static Match CloneMatch(Match m) =>
        JsonSerializer.Deserialize<Match>(JsonSerializer.Serialize(m, DataStore.JsonOptions), DataStore.JsonOptions)!;

    private static bool SameContent(Match a, Match b) =>
        JsonSerializer.Serialize(a, DataStore.JsonOptions) == JsonSerializer.Serialize(b, DataStore.JsonOptions);
}
