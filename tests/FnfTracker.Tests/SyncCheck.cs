using System.Net;
using System.Text;
using System.Text.Json;
using FnfTracker.Core;

public static class SyncCheck
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
        PlayerSlot P(string c, string s, string? n = null) => new() { Class = c, Spec = s, Name = n };

        // Site addresses
        const string Site = "https://hooee71.github.io/fnf-stats/";
        Check(SiteSync.NormalizeSiteUrl("Hooee71/fnf-stats") == Site, "repo name -> site");
        Check(SiteSync.NormalizeSiteUrl("https://github.com/Hooee71/fnf-stats") == Site, "repo URL -> site");
        Check(SiteSync.NormalizeSiteUrl("hooee71.github.io/fnf-stats") == Site, "site without scheme");
        Check(SiteSync.NormalizeSiteUrl("https://Hooee71.github.io/fnf-stats/stats.html#night") == Site, "page address -> site");
        Check(SiteSync.NormalizeSiteUrl("https://hooee71.github.io/fnf-stats/matches.json") == Site, "data address -> site");
        Check(SiteSync.NormalizeSiteUrl("https://hooee71.github.io/") == "https://hooee71.github.io/", "user site root");
        Check(SiteSync.NormalizeSiteUrl("http://hooee71.github.io/fnf-stats") == null, "plain http refused");
        Check(SiteSync.NormalizeSiteUrl("") == null && SiteSync.NormalizeSiteUrl("not a site") == null, "junk refused");
        Check(SiteSync.IsOwnSite(Site, "Hooee71/fnf-stats") && !SiteSync.IsOwnSite(Site, "Friend/fnf-stats") && !SiteSync.IsOwnSite(Site, null), "own site detected");

        // The publisher's games
        var fri = new DateTime(2026, 10, 16, 21, 0, 0);
        var us = new List<PlayerSlot> { P("Rogue", "Subtlety"), P("Priest", "Discipline") };
        Match G(int i, bool won, string enemy1 = "Warrior") => new()
        {
            PlayedAt = fri.AddMinutes(i * 12), Won = won, Map = "Nagrand Arena", OurTeam = us,
            Enemies = new() { P(enemy1, "Arms", "Bonk-Stormrage"), P("Druid", "Restoration") },
            RatingAfter = 1800 + i, Tags = new() { "dampening" }, Notes = "publisher's private note", Source = "addon",
        };
        var publisher = new FnfData();
        for (int i = 0; i < 5; i++) publisher.Matches.Add(G(i, i % 2 == 0));

        var site = WebSite.Build(publisher, fri.AddHours(3));
        Check(site.ContainsKey(SiteSync.DataFile), "site includes matches.json");
        var json = site[SiteSync.DataFile];
        Check(!json.Contains("private note"), "notes are left out of the data");
        Check(WebSite.Build(publisher, fri.AddHours(5))[SiteSync.DataFile] == json, "data file doesn't change when the games don't");
        Check(WebSite.Build(publisher, fri.AddHours(3))[WebSite.VersionFile].Contains(
                  JsonDocument.Parse(site[WebSite.VersionFile]).RootElement.GetProperty("version").GetString()!),
              "republishing the same games keeps the site version");
        var feed = JsonSerializer.Deserialize<SiteFeed>(json)!;
        Check(feed.Schema == 1 && feed.Matches.Count == 5 && feed.Matches[0].Id == publisher.Matches[0].Id, "feed round-trips with the same Ids");
        Check(feed.Matches[0].PlayedAt == publisher.Matches[0].PlayedAt && feed.Matches[0].Enemies[0].Name == "Bonk-Stormrage", "feed keeps game details");

        // A friend who has one game of their own (also logged on the publisher's side) and one unrelated game
        var friend = new FnfData();
        var theirCopy = G(2, true);              // same game as publisher #2 (same time, result, enemies), different Id
        theirCopy.Notes = "friend's own";
        var theirOther = G(40, false, "Mage");
        friend.Matches.Add(theirCopy);
        friend.Matches.Add(theirOther);

        var r1 = SiteSync.Apply(friend, Feed(publisher), Site);
        Check(r1.Added == 4 && r1.AlreadyHad == 1 && r1.Updated == 0 && r1.Removed == 0, "first sync: " + r1);
        Check(friend.Matches.Count == 6 && friend.Matches.Count(m => m.SyncedFrom == Site) == 4, "synced games are marked");
        Check(friend.Matches.Contains(theirCopy) && theirCopy.SyncedFrom == null && theirCopy.Notes == "friend's own", "friend's own copy untouched");
        Check(friend.Matches.SequenceEqual(friend.Matches.OrderBy(m => m.PlayedAt)), "kept in time order");

        var r2 = SiteSync.Apply(friend, Feed(publisher), Site);
        Check(!r2.Changed && r2.Summary().StartsWith("Up to date"), "second sync changes nothing: " + r2.Summary());

        // Friend writes a note on a synced game; publisher edits that game and deletes another.
        var g0 = friend.Matches.Single(m => m.Id == publisher.Matches[0].Id);
        g0.Notes = "they kited us";
        publisher.Matches[0].Map = "Mugambala";
        publisher.Matches[0].Notes = "changed private note";
        var gone = publisher.Matches[4];
        publisher.Matches.Remove(gone);
        var r3 = SiteSync.Apply(friend, Feed(publisher), Site);
        Check(r3.Updated == 1 && r3.Removed == 1 && r3.Added == 0, "edit and delete flow down: " + r3);
        var g0b = friend.Matches.Single(m => m.Id == publisher.Matches[0].Id);
        Check(g0b.Map == "Mugambala" && g0b.Notes == "they kited us", "edit taken, friend's note kept");
        Check(friend.Matches.All(m => m.Id != gone.Id), "deleted game removed");
        Check(friend.Matches.Contains(theirOther), "friend's unrelated game untouched");

        // Friend deletes a synced game: it stays gone.
        var dropped = friend.Matches.First(m => m.SyncedFrom == Site);
        friend.Matches.Remove(dropped);
        friend.Settings.SyncDismissedIds.Add(dropped.Id);
        var r4 = SiteSync.Apply(friend, Feed(publisher), Site);
        Check(!r4.Changed && r4.Dismissed == 1 && friend.Matches.All(m => m.Id != dropped.Id), "deleted-here game doesn't come back");
        publisher.Matches.RemoveAll(m => m.Id == dropped.Id);
        SiteSync.Apply(friend, Feed(publisher), Site);
        Check(friend.Settings.SyncDismissedIds.Count == 0, "dismissed list forgets games the site dropped");

        // A partner who used the old Export/Merge has the publisher's games with the same Ids: they're adopted.
        var merged = new FnfData();
        merged.Matches.Add(Clone(publisher.Matches[1]));
        merged.Matches[0].Notes = "merged note";
        var r5 = SiteSync.Apply(merged, Feed(publisher), Site);
        var adopted = merged.Matches.Single(m => m.Id == publisher.Matches[1].Id);
        Check(r5.Added == publisher.Matches.Count - 1 && r5.Updated == 1 && adopted.SyncedFrom == Site, "merged games adopted by Id: " + r5);
        Check(adopted.Notes == "merged note", "adopted game keeps its note");

        // Games from a different followed site are left alone, and switching sites removes nothing of theirs.
        var other = "https://someone.github.io/fnf/";
        var before = friend.Matches.Count;
        var r6 = SiteSync.Apply(friend, new SiteFeed(), other);
        Check(!r6.Changed && friend.Matches.Count == before, "empty other site touches nothing");

        // Fetching over HTTP
        fails += FetchChecks(json, Check);
        return fails;
    }

    private static SiteFeed Feed(FnfData data) =>
        JsonSerializer.Deserialize<SiteFeed>(SiteSync.BuildFeed(data))!;

    private static Match Clone(Match m) =>
        JsonSerializer.Deserialize<Match>(JsonSerializer.Serialize(m))!;

    private static int FetchChecks(string json, Action<bool, string> check)
    {
        int port = 47000 + Random.Shared.Next(2000);
        var prefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        try { listener.Start(); }
        catch (Exception ex) { Console.WriteLine("SKIP fetch checks (no local listener): " + ex.Message); return 0; }

        var serve = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var path = ctx.Request.Url!.AbsolutePath;
                var body = path switch
                {
                    "/good/matches.json" => json,
                    "/newer/matches.json" => "{\"Schema\":99,\"Matches\":[]}",
                    "/junk/matches.json" => "<html>not json</html>",
                    _ => null,
                };
                ctx.Response.StatusCode = body is null ? 404 : 200;
                if (body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.OutputStream.Write(bytes);
                }
                ctx.Response.Close();
            }
        });

        using var http = SiteSync.CreateClient(new Version(1, 0, 0));
        var feed = SiteSync.FetchAsync(http, prefix + "good/").GetAwaiter().GetResult();
        check(feed.Matches.Count > 0, "fetch reads matches.json");
        check(Fails(() => SiteSync.FetchAsync(http, prefix + "missing/")) is { } a && a.Contains("no game data"), "404 explained");
        check(Fails(() => SiteSync.FetchAsync(http, prefix + "newer/")) is { } b && b.Contains("newer FNF Tracker"), "newer schema explained");
        check(Fails(() => SiteSync.FetchAsync(http, prefix + "junk/")) is { } c && c.Contains("isn't an FNF Tracker site"), "non-data explained");
        listener.Stop();
        return 0;
    }

    private static string? Fails(Func<Task> act)
    {
        try { act().GetAwaiter().GetResult(); return null; }
        catch (SyncException ex) { return ex.Message; }
    }
}
