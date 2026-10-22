using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FnfTracker.Core;

/// <summary>What a publish did.</summary>
/// <param name="Changed">False when the site was already up to date (nothing was committed).</param>
/// <param name="Uploaded">Files whose content changed and had to be sent.</param>
/// <param name="PagesNote">Set when GitHub Pages couldn't be switched on automatically.</param>
public sealed record PublishResult(string SiteUrl, bool Changed, int Files, int Uploaded, string? PagesNote);

/// <summary>A publish failure with a message fit to show the user.</summary>
public sealed class PublishException(string message) : Exception(message);

/// <summary>
/// Publishes the website to a GitHub repository served by GitHub Pages (free for public repos).
/// Everything goes up as a single commit through GitHub's Git Data API, so the site is never half
/// updated, files the site no longer has are removed, and unchanged files (the images) aren't re-sent.
/// Needs a fine-grained token limited to that one repo with Contents: read and write (plus Pages and
/// Administration: read and write, which GitHub requires to switch Pages on with the first publish).
/// </summary>
public static class SitePublisher
{
    private const string Api = "https://api.github.com";

    public static HttpClient CreateClient(Version appVersion, string token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FNFTracker", appVersion.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return http;
    }

    /// <summary>
    /// "owner/repo", "github.com/owner/repo" or a full https://github.com/owner/repo(.git) URL →
    /// (owner, repo). Null if it isn't one of those.
    /// </summary>
    public static (string Owner, string Repo)? ParseRepo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        t = Regex.Replace(t, @"^(https?://)?(www\.)?github\.com/", "", RegexOptions.IgnoreCase);
        t = t.TrimEnd('/');
        if (t.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
        var m = Regex.Match(t, @"^([A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/([A-Za-z0-9._-]{1,100})$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    /// <summary>Where GitHub Pages serves the repo: https://owner.github.io/repo/ (or the root for owner.github.io).</summary>
    public static string SiteUrl(string owner, string repo)
    {
        var host = $"https://{owner.ToLowerInvariant()}.github.io/";
        return string.Equals(repo, $"{owner}.github.io", StringComparison.OrdinalIgnoreCase) ? host : host + repo + "/";
    }

    /// <summary>Git's object id for a file: SHA-1 of "blob {length}\0" + content.</summary>
    public static string GitBlobSha(byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        var all = new byte[header.Length + content.Length];
        header.CopyTo(all, 0);
        content.CopyTo(all, header.Length);
        return Convert.ToHexString(SHA1.HashData(all)).ToLowerInvariant();
    }

    public static async Task<PublishResult> PublishAsync(
        HttpClient http, string owner, string repo, IReadOnlyDictionary<string, byte[]> files,
        string message, CancellationToken ct = default)
    {
        var baseUrl = $"{Api}/repos/{owner}/{repo}";

        // 1. The repo, and which branch Pages will serve.
        var repoInfo = await SendAsync(http, HttpMethod.Get, baseUrl, null, ct, "find the repo") as JsonObject
                       ?? throw new PublishException("GitHub sent an unexpected reply about the repo.");
        var branch = repoInfo["default_branch"]?.GetValue<string>() ?? "main";
        if (repoInfo["private"]?.GetValue<bool>() == true)
            throw new PublishException($"{owner}/{repo} is private. Free GitHub Pages sites need a public repo: make it public in the repo's Settings.");

        // 2. The current commit, or bootstrap an empty repo (the Git Data API needs at least one commit).
        var head = await GetHeadAsync(http, baseUrl, branch, ct);
        if (head is null)
        {
            var body = new JsonObject { ["message"] = "Start the FNF site", ["content"] = "Cg==", ["branch"] = branch };
            await SendAsync(http, HttpMethod.Put, $"{baseUrl}/contents/.nojekyll", body, ct, "write to the repo");
            head = await GetHeadAsync(http, baseUrl, branch, ct)
                   ?? throw new PublishException("The repo is still empty after the first write. Try again in a minute.");
        }
        var (parentSha, oldTreeSha) = head.Value;

        // 3. What's there now, so unchanged files are reused instead of uploaded again.
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        var oldTree = await SendAsync(http, HttpMethod.Get, $"{baseUrl}/git/trees/{oldTreeSha}?recursive=1", null, ct, "read the repo") as JsonObject;
        if (oldTree?["tree"] is JsonArray entries)
            foreach (var e in entries.OfType<JsonObject>())
                if (e["type"]?.GetValue<string>() == "blob" && e["path"]?.GetValue<string>() is string p && e["sha"]?.GetValue<string>() is string s)
                    existing[p] = s;

        // 4. Upload what changed, then build the new tree from scratch (so removed files disappear).
        var tree = new JsonArray();
        int uploaded = 0;
        foreach (var (path, content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var sha = GitBlobSha(content);
            if (!existing.TryGetValue(path, out var have) || have != sha)
            {
                var blob = new JsonObject { ["content"] = Convert.ToBase64String(content), ["encoding"] = "base64" };
                var made = await SendAsync(http, HttpMethod.Post, $"{baseUrl}/git/blobs", blob, ct, "upload files");
                sha = made?["sha"]?.GetValue<string>() ?? throw new PublishException("GitHub didn't accept a file upload.");
                uploaded++;
            }
            tree.Add(new JsonObject { ["path"] = path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = sha });
        }

        var newTree = await SendAsync(http, HttpMethod.Post, $"{baseUrl}/git/trees", new JsonObject { ["tree"] = tree }, ct, "upload files");
        var newTreeSha = newTree?["sha"]?.GetValue<string>() ?? throw new PublishException("GitHub didn't accept the file list.");

        bool changed = newTreeSha != oldTreeSha;
        if (changed)
        {
            var commit = new JsonObject
            {
                ["message"] = message,
                ["tree"] = newTreeSha,
                ["parents"] = new JsonArray { parentSha },
            };
            var made = await SendAsync(http, HttpMethod.Post, $"{baseUrl}/git/commits", commit, ct, "commit");
            var commitSha = made?["sha"]?.GetValue<string>() ?? throw new PublishException("GitHub didn't accept the commit.");
            await SendAsync(http, HttpMethod.Patch, $"{baseUrl}/git/refs/heads/{branch}",
                            new JsonObject { ["sha"] = commitSha }, ct, "update the branch");
        }

        // 5. Make sure Pages is on. Not fatal: the files are up either way.
        var pagesNote = await EnsurePagesAsync(http, baseUrl, branch, ct);
        return new PublishResult(SiteUrl(owner, repo), changed, files.Count, changed ? uploaded : 0, pagesNote);
    }

    private static async Task<(string CommitSha, string TreeSha)?> GetHeadAsync(HttpClient http, string baseUrl, string branch, CancellationToken ct)
    {
        using var res = await http.GetAsync($"{baseUrl}/git/ref/heads/{branch}", ct).ConfigureAwait(false);
        // 404 = branch doesn't exist yet, 409 = "Git Repository is empty".
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict) return null;
        await ThrowIfFailed(res, "read the repo", ct);
        var refObj = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var commitSha = refObj?["object"]?["sha"]?.GetValue<string>() ?? throw new PublishException("GitHub sent an unexpected branch reply.");
        var commit = await SendAsync(http, HttpMethod.Get, $"{baseUrl}/git/commits/{commitSha}", null, ct, "read the repo");
        var treeSha = commit?["tree"]?["sha"]?.GetValue<string>() ?? throw new PublishException("GitHub sent an unexpected commit reply.");
        return (commitSha, treeSha);
    }

    private static async Task<string?> EnsurePagesAsync(HttpClient http, string baseUrl, string branch, CancellationToken ct)
    {
        try
        {
            using (var res = await http.GetAsync($"{baseUrl}/pages", ct).ConfigureAwait(false))
                if (res.IsSuccessStatusCode) return null;

            var body = new JsonObject
            {
                ["build_type"] = "legacy",
                ["source"] = new JsonObject { ["branch"] = branch, ["path"] = "/" },
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pages")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var made = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (made.IsSuccessStatusCode || made.StatusCode == HttpStatusCode.Conflict) return null; // 409: already on
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        return $"Files are up, but GitHub Pages couldn't be switched on (give the token Pages and Administration: read and write, " +
               $"or turn it on yourself: repo Settings → Pages → Deploy from a branch → {branch} / root).";
    }

    private static async Task<JsonNode?> SendAsync(HttpClient http, HttpMethod method, string url, JsonNode? body,
                                                   CancellationToken ct, string doing)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PublishException("Couldn't reach GitHub: " + ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PublishException("GitHub took too long to answer. Try again.");
        }
        using (res)
        {
            await ThrowIfFailed(res, doing, ct);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
    }

    private static async Task ThrowIfFailed(HttpResponseMessage res, string doing, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        string detail = "";
        try { detail = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false))?["message"]?.GetValue<string>() ?? ""; }
        catch { /* not JSON */ }

        throw new PublishException(res.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "GitHub rejected the token. It may have expired or been mistyped: make a new one and paste it in Settings.",
            HttpStatusCode.NotFound when doing == "find the repo" =>
                "Repo not found. Check the name in Settings, and that the token was given access to that repo.",
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound =>
                $"The token isn't allowed to {doing}. It needs access to this repo with Contents: read and write.",
            _ => $"GitHub couldn't {doing} ({(int)res.StatusCode}{(detail.Length > 0 ? ": " + detail : "")}).",
        });
    }
}
