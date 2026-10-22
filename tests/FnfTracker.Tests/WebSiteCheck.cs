using System.Text;
using FnfTracker.Core;

public static class WebSiteCheck
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
        PlayerSlot P(string c, string s, string? n = null, long? d = null, long? h = null) =>
            new() { Class = c, Spec = s, Name = n, Damage = d, Healing = h };

        // Repo names and the Pages address
        Check(SitePublisher.ParseRepo("Hooee71/fnf-stats") == ("Hooee71", "fnf-stats"), "repo owner/name");
        Check(SitePublisher.ParseRepo("https://github.com/Hooee71/fnf-stats.git/") == ("Hooee71", "fnf-stats"), "repo from URL");
        Check(SitePublisher.ParseRepo("github.com/Hooee71/fnf.stats") == ("Hooee71", "fnf.stats"), "repo without scheme");
        Check(SitePublisher.ParseRepo("fnf-stats") == null && SitePublisher.ParseRepo("a/b/c") == null && SitePublisher.ParseRepo(" ") == null, "bad repo names rejected");
        Check(SitePublisher.SiteUrl("Hooee71", "fnf-stats") == "https://hooee71.github.io/fnf-stats/", "pages url");
        Check(SitePublisher.SiteUrl("Hooee71", "hooee71.github.io") == "https://hooee71.github.io/", "user site url");
        // Known value: `printf 'hello\n' | git hash-object --stdin`
        Check(SitePublisher.GitBlobSha(Encoding.UTF8.GetBytes("hello\n")) == "ce013625030ba8dba906f756967f9e9ca394464a", "git blob sha");
        Check(SitePublisher.GitBlobSha(Array.Empty<byte>()) == "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", "empty blob sha");

        // The pages
        var fri = new DateTime(2026, 10, 16, 21, 0, 0);
        var us = new List<PlayerSlot> { P("Rogue", "Subtlety"), P("Priest", "Discipline") };
        var data = new FnfData();
        data.Settings.HomeRealm = "Area52";
        data.Settings.SeasonStart = new DateTime(2026, 9, 1);
        for (int i = 0; i < 8; i++)
            data.Matches.Add(new Match
            {
                PlayedAt = fri.AddMinutes(i * 12), Won = i % 2 == 0, Map = "Nagrand Arena", OurTeam = us,
                Enemies = new() { P("Warrior", "Arms", "Bonk-Stormrage"), P("Druid", "Restoration", "Tree<script>") },
                RatingAfter = 1800 + i * 5, RatingChange = i % 2 == 0 ? 12 : -9, Tags = new() { "dampening" },
                Notes = "private note",
            });
        data.Matches.Add(new Match
        {
            PlayedAt = fri.AddDays(-7), Won = true, OurTeam = us, Map = "Mugambala", RecordedBy = "Stevo-Illidan",
            Enemies = new() { P("Mage", "Frost", "Icy", 900_000, 10_000), P("Rogue", "Assassination", null, 700_000, 0) },
        });
        data.Matches[^1].OurTeam = new() { P("Rogue", "Subtlety", null, 1_000_000, 0), P("Priest", "Discipline", null, 300_000, 500_000) };

        var site = WebSite.Build(data, fri.AddHours(3));
        Check(new[] { "index.html", "stats.html", "site.css", "site.js", ".nojekyll" }.All(site.ContainsKey), "all site files");
        var index = site["index.html"];
        var stats = site["stats.html"];
        Check(index.Contains("<tr data-result=\"w\"") && index.Split("<tr data-result=").Length - 1 == 9, "one row per game");
        Check(index.Contains("https://check-pvp.fr/us/Stormrage/Bonk"), "cross-realm enemy linked");
        Check(index.Contains("https://check-pvp.fr/us/Illidan/Icy"), "same-realm enemy uses recorder's realm");
        Check(!index.Contains("<script>\"") && index.Contains("Tree&lt;script&gt;"), "names are HTML-escaped");
        Check(!index.Contains("private note"), "notes stay private");
        Check(index.Contains("-19%") && index.Contains("title=\"Damage: us 1.30M"), "DPS gap cell with raw numbers");
        Check(index.Contains("Season 5-4 (56%)"), "header season record: " + Between(index, "class=\"overall\">", "<"));
        Check(index.Contains("Tonight: 4-4"), "header session line: " + Between(index, "class=\"session\">", "<"));
        Check(stats.Contains("data-range=\"season\"") && stats.Contains("data-range=\"night\"") && stats.Contains("data-panel=\"all\" hidden"), "range tabs, season first");
        Check(stats.Contains("Arms Warrior + Restoration Druid") && stats.Contains("<polyline"), "stat tables and rating chart");
        Check(stats.Contains("Repeat opponents") && stats.Contains("Bonk-Stormrage (Arms Warrior)"), "repeat opponents table");
        Check(site["site.css"].Contains("assets/Marcellus-Regular.ttf") && index.Contains(WebSite.AssetPaths.Logo), "pages reference the app's assets");

        Check(index.Contains(WebSite.AssetPaths.MatchesArt) && stats.Contains(WebSite.AssetPaths.StatsArt), "each page has its mascot art");
        Check(System.Text.RegularExpressions.Regex.IsMatch(index, @"href=""site\.css\?v=[0-9a-f]{10}""") && index.Contains("site.js?v="), "stylesheet and script links are versioned");
        var empty = WebSite.Build(new FnfData(), fri);
        Check(empty["index.html"].Contains("No games yet") && empty["stats.html"].Contains("data-range=\"all\""), "empty data still builds");
        return fails;
    }

    private static string Between(string s, string start, string end)
    {
        var i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return "";
        i += start.Length;
        var j = s.IndexOf(end, i, StringComparison.Ordinal);
        return j < 0 ? "" : s[i..j];
    }
}
