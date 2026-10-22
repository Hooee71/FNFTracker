using System.Globalization;
using System.Net;
using System.Text;

namespace FnfTracker.Core;

/// <summary>
/// Builds the public website: a Matches page (index.html) and a Stats page (stats.html), as plain
/// static files that any free host (GitHub Pages) can serve. All the numbers come from
/// <see cref="Stats"/>, the same code behind the Stats tab, so the site and the app always agree.
/// The Stats page is pre-built for a few ranges (all time, this season, last 30 days, latest night)
/// and a little JavaScript switches between them; there's no server and nothing is computed in the browser.
/// Images and the font are added by the app (see <see cref="AssetPaths"/>).
/// </summary>
public static class WebSite
{
    public const string MatchesPage = "index.html";
    public const string StatsPage = "stats.html";
    public const string StyleSheet = "site.css";
    public const string Script = "site.js";

    /// <summary>Binary files the pages reference, which the app supplies from its own Assets.</summary>
    public static class AssetPaths
    {
        public const string Logo = "assets/logo.png";
        public const string Background = "assets/background.jpg";
        public const string Font = "assets/Marcellus-Regular.ttf";
        public const string Icon = "favicon.ico";
        /// <summary>The mascot art from the app's Matches and Stats tabs, top right of each page.</summary>
        public const string MatchesArt = "assets/mascots.png";
        public const string StatsArt = "assets/mascots-stats.png";
    }

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Browsers (and GitHub Pages' 10-minute cache) can keep an old site.css next to a freshly published
    // page, which leaves the new page unstyled. A content hash in the link makes every change a new URL.
    private static readonly string CssUrl = StyleSheet + "?v=" + ShortHash(Css);
    private static readonly string JsUrl = Script + "?v=" + ShortHash(Js);

    private static string ShortHash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..10].ToLowerInvariant();

    /// <summary>
    /// Small file holding the site's current version. Open pages check it every minute and reload
    /// themselves when a newer publish shows up (see the auto-refresh part of <see cref="Js"/>).
    /// </summary>
    public const string VersionFile = "version.json";

    // Stand-in for the version inside the pages, swapped for the real one once everything is built.
    private const string VersionToken = "__FNF_SITE_VERSION__";

    /// <summary>Every text file of the site, keyed by its path in the site.</summary>
    public static Dictionary<string, string> Build(FnfData data, DateTime now)
    {
        var files = new Dictionary<string, string>
        {
            [MatchesPage] = MatchesHtml(data, now),
            [StatsPage] = StatsHtml(data, now),
            [StyleSheet] = Css,
            [Script] = Js,
            // Tells GitHub Pages to serve the files as they are (no Jekyll build step).
            [".nojekyll"] = "",
        };

        // The version is a hash of everything else, so it changes exactly when the site does.
        var version = ShortHash(string.Concat(files.OrderBy(f => f.Key, StringComparer.Ordinal)
                                                   .Select(f => f.Key + "\0" + f.Value)));
        foreach (var key in files.Keys.ToList()) files[key] = files[key].Replace(VersionToken, version);
        files[VersionFile] = $"{{\"version\":\"{version}\",\"published\":\"{now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)}\"}}\n";
        return files;
    }

    // ───────────────────────── Shared page parts ─────────────────────────

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Num(double v) => v.ToString("0.##", Inv);
    private static string Pct(double rate) => Math.Round(rate * 100).ToString("0", Inv) + "%";

    /// <summary>Same thresholds as the app: green 60%+, red 40% or worse, grey under 3 games.</summary>
    private static string RateClass(int games, double rate) =>
        games < 3 ? "dim" : rate >= 0.6 ? "win" : rate <= 0.4 ? "loss" : "";

    private static string Page(string title, string active, FnfData data, DateTime now, string body)
    {
        var art = active == StatsPage ? AssetPaths.StatsArt : AssetPaths.MatchesArt;
        var (session, overall) = HeaderLines(data, now);
        string Nav(string href, string label) =>
            $"<a href=\"{href}\"{(active == href ? " class=\"active\" aria-current=\"page\"" : "")}>{label}</a>";

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{H(title)}} · FNF Arena Tracker</title>
<meta name="description" content="2v2 arena games and stats from FNF Arena Tracker.">
<meta name="fnf-version" content="{{VersionToken}}">
<link rel="icon" href="{{AssetPaths.Icon}}">
<link rel="stylesheet" href="{{CssUrl}}">
</head>
<body class="{{(active == StatsPage ? "page-stats" : "page-matches")}}">
<header class="banner">
  <div class="wrap banner-row">
    <a class="logo" href="{{MatchesPage}}"><img src="{{AssetPaths.Logo}}" alt="FNF Arena Tracker"></a>
    <div class="record">
      <span class="session">{{H(session)}}</span>
      <span class="overall">{{H(overall)}}</span>
    </div>
  </div>
  <nav class="wrap tabs">{{Nav(MatchesPage, "Matches")}}{{Nav(StatsPage, "Stats")}}</nav>
  <div class="wrap art" aria-hidden="true"><img src="{{art}}" alt="" width="360"></div>
</header>
<main class="wrap">
{{body}}
</main>
<footer class="wrap">
  Updated {{H(now.ToString("ddd MMM d, yyyy 'at' h:mm tt", En))}} ·
  made with <a href="https://github.com/{{Updater.Owner}}/{{Updater.Repo}}">FNF Arena Tracker</a>
</footer>
<script src="{{JsUrl}}"></script>
</body>
</html>
""";
    }

    /// <summary>The two lines beside the logo, worded like the app's header.</summary>
    private static (string Session, string Overall) HeaderLines(FnfData data, DateTime now)
    {
        var all = data.Matches;
        if (all.Count == 0) return ("", "No games yet");
        var season = data.Settings.SeasonStart;
        var overall = Stats.Summarize(Stats.Filter(all, season).ToList());
        var overallText = overall.Games == 0
            ? "No games this season yet"
            : $"{(season is null ? "All time" : "Season")} {overall.Wins}-{overall.Losses} ({Pct(overall.WinRate)})";

        var lastNight = all.Max(m => m.Night);
        var s = Stats.Summarize(all.Where(m => m.Night == lastNight).ToList());
        var label = lastNight == StatsRange.CurrentNight(now) ? "Tonight" : "Last night out, " + lastNight.ToString("ddd MMM d", En);
        return ($"{label}: {s.Wins}-{s.Losses} · streak {s.StreakText}", overallText);
    }

    /// <summary>A player as a class-coloured line: bar, "Spec Class", then the name (linked when possible).</summary>
    private static string Chip(PlayerSlot p, string? homeRealm, string region, bool showName)
    {
        var color = GameData.ColorFor(p.Class);
        var sb = new StringBuilder();
        sb.Append($"<div class=\"chip\"><i style=\"background:{color}\"></i><b style=\"color:{color}\">{H(p.Label)}</b>");
        if (showName && !string.IsNullOrWhiteSpace(p.Name))
        {
            var url = CheckPvp.ProfileUrl(p.Name, homeRealm, region);
            sb.Append(url is null
                ? $"<span class=\"name\">{H(p.Name)}</span>"
                : $"<a class=\"name\" href=\"{H(url)}\" target=\"_blank\" rel=\"noopener\" title=\"Open on check-pvp.fr\">{H(p.Name)}</a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string GapClass(string text, bool muted = false) =>
        text.Length == 0 || muted ? "dim" : text.StartsWith('+') ? "win" : text.StartsWith('-') ? "loss" : "";

    // ───────────────────────── Matches page ─────────────────────────

    private static string MatchesHtml(FnfData data, DateTime now)
    {
        var settings = data.Settings;
        var ms = data.Matches;
        var numbers = Stats.GameNumbers(ms);
        var sb = new StringBuilder();

        sb.Append($"""
<div class="toolbar">
  <div class="filter" role="group" aria-label="Show">
    <button type="button" class="on" data-filter="all">All</button>
    <button type="button" data-filter="w">Wins</button>
    <button type="button" data-filter="l">Losses</button>
  </div>
  <input type="search" id="search" placeholder="Search comp, spec, player, map, tag…" aria-label="Search games">
  <span class="count" id="count">{ms.Count} game(s)</span>
</div>
""");

        if (ms.Count == 0)
        {
            sb.Append("<p class=\"empty\">No games yet.</p>");
            return Page("Matches", MatchesPage, data, now, sb.ToString());
        }

        sb.Append("""
<div class="table-scroll">
<table class="matches sortable" id="matches">
<thead><tr>
  <th data-type="num" class="c">Night</th><th data-type="num" class="c">#</th><th data-type="num" class="c">Time</th>
  <th class="c">W/L</th><th>Enemies</th><th>Our team</th><th>Map</th>
  <th data-type="num" class="c">Rating</th><th data-type="num" class="c">+/-</th>
  <th data-type="num" class="c">Their MMR</th><th data-type="num" class="c">DPS/HPS</th><th data-type="num" class="c">Length</th>
</tr></thead>
<tbody>
""");

        foreach (var m in ms.OrderByDescending(m => m.PlayedAt))
        {
            var homeRealm = CheckPvp.RealmOf(m.RecordedBy) ?? settings.HomeRealm;
            var enemies = m.Enemies.Where(e => !e.IsEmpty).ToList();
            var ours = m.OurTeam.Where(p => !p.IsEmpty).ToList();
            var search = string.Join(" ", enemies.Select(e => e.Label + " " + e.Name)
                                              .Concat(ours.Select(p => p.Label))
                                              .Append(m.Map)
                                              .Concat(m.Tags)).ToLowerInvariant();
            var ticks = m.PlayedAt.Ticks.ToString(Inv);
            var change = m.RatingChange is int c ? (c > 0 ? $"+{c}" : c.ToString(Inv)) : "";
            var dps = Stats.DpsVs(m);

            sb.Append($"<tr data-result=\"{(m.Won ? "w" : "l")}\" data-search=\"{H(search)}\">");
            sb.Append($"<td class=\"c\" data-sort=\"{ticks}\">{H(m.Night.ToString("ddd M/d", En))}</td>");
            sb.Append($"<td class=\"c\">{numbers[m.Id]}</td>");
            sb.Append($"<td class=\"c\" data-sort=\"{m.PlayedAt.TimeOfDay.Ticks.ToString(Inv)}\">{H(m.PlayedAt.ToString("h:mm tt", En))}</td>");
            sb.Append($"<td class=\"c result {(m.Won ? "win" : "loss")}\">{(m.Won ? "W" : "L")}</td>");
            sb.Append("<td>").Append(string.Concat(enemies.Select(e => Chip(e, homeRealm, settings.Region, showName: true)))).Append("</td>");
            sb.Append("<td class=\"ours\">").Append(string.Concat(ours.Select(p => Chip(p, null, settings.Region, showName: false)))).Append("</td>");
            sb.Append($"<td>{H(m.Map)}{TagsHtml(m.Tags)}</td>");
            sb.Append($"<td class=\"c\">{m.RatingAfter}</td>");
            sb.Append($"<td class=\"c {(m.RatingChange < 0 ? "loss" : "win")}\" data-sort=\"{m.RatingChange}\">{change}</td>");
            sb.Append($"<td class=\"c\">{m.EnemyMmr}</td>");
            sb.Append($"<td class=\"c\" data-sort=\"{(dps is double d ? Num(d) : "")}\">{OutputCell(m)}</td>");
            sb.Append($"<td class=\"c\" data-sort=\"{m.DurationSeconds}\">{Stats.FormatDuration(m.DurationSeconds)}</td>");
            sb.Append("</tr>\n");
        }

        sb.Append("</tbody></table></div>");
        return Page("Matches", MatchesPage, data, now, sb.ToString());
    }

    private static string TagsHtml(List<string> tags)
    {
        var clean = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        return clean.Count == 0 ? "" : "<div class=\"tags\">" + string.Concat(clean.Select(t => $"<span>{H(t)}</span>")) + "</div>";
    }

    /// <summary>The two stacked DPS / HPS lines, with the raw numbers in the hover text.</summary>
    private static string OutputCell(Match m)
    {
        if (!Stats.HasOutput(m)) return "";
        int? secs = m.DurationSeconds;
        string Side(long? v) => v is long x
            ? Stats.FormatAmount(x) + (secs > 0 ? $" ({Stats.FormatAmount((double)x / secs!.Value)}/s)" : "")
            : "?";
        var comparable = Stats.HealingComparable(m);
        var tip = $"Damage: us {Side(Stats.OurDamage(m))}, them {Side(Stats.TheirDamage(m))}\n" +
                  $"Healing: us {Side(Stats.OurHealing(m))}, them {Side(Stats.TheirHealing(m))}" +
                  (comparable ? "" : "\nHealing greyed out: the teams brought a different number of healers.");

        string Line(string label, double? pct, bool muted)
        {
            var text = Stats.FormatPercentVs(pct);
            return $"<div class=\"gap\"><span>{label}</span><b class=\"{GapClass(text, muted)}\">{(text.Length == 0 ? "—" : text)}</b></div>";
        }
        return $"<div class=\"output\" title=\"{H(tip)}\">{Line("DPS", Stats.DpsVs(m), false)}{Line("HPS", Stats.HpsVs(m), !comparable)}</div>";
    }

    // ───────────────────────── Stats page ─────────────────────────

    private sealed record RangeView(string Id, string Label, string Description, List<Match> Matches);

    private static List<RangeView> Ranges(FnfData data, DateTime now)
    {
        var all = data.Matches;
        var today = StatsRange.CurrentNight(now);
        RangeView Make(string id, string label, (DateOnly? From, DateOnly? To) r) =>
            new(id, label, StatsRange.Describe(r.From, r.To), StatsRange.Apply(all, r.From, r.To).ToList());

        var list = new List<RangeView>();
        if (data.Settings.SeasonStart is DateTime season)
            list.Add(Make("season", "This season", (DateOnly.FromDateTime(season), null)));
        list.Add(Make("all", "All time", (null, null)));
        list.Add(Make("last30", "Last 30 days", (today.AddDays(-29), today)));
        if (StatsRange.LatestNight(all) is DateOnly latest)
            list.Add(Make("night", "Latest night", (latest, latest)));
        return list;
    }

    private static string StatsHtml(FnfData data, DateTime now)
    {
        var ranges = Ranges(data, now);
        var sb = new StringBuilder();
        sb.Append("<div class=\"toolbar\"><div class=\"filter\" role=\"tablist\" aria-label=\"Which games\">");
        for (int i = 0; i < ranges.Count; i++)
            sb.Append($"<button type=\"button\" role=\"tab\" data-range=\"{ranges[i].Id}\"{(i == 0 ? " class=\"on\"" : "")}>{H(ranges[i].Label)}</button>");
        sb.Append("</div></div>\n");

        for (int i = 0; i < ranges.Count; i++)
        {
            var r = ranges[i];
            sb.Append($"<section class=\"range\" data-panel=\"{r.Id}\"{(i == 0 ? "" : " hidden")}>\n");
            sb.Append($"<p class=\"range-summary\">{H(r.Description)} · {r.Matches.Count} game(s)</p>\n");
            sb.Append(RangeBody(r.Matches));
            sb.Append("</section>\n");
        }
        return Page("Stats", StatsPage, data, now, sb.ToString());
    }

    private static string RangeBody(List<Match> ms)
    {
        var sb = new StringBuilder();
        var s = Stats.Summarize(ms);

        sb.Append("<div class=\"cards\">");
        sb.Append(Card("Record", s.Games == 0 ? "-" : $"{s.Wins}-{s.Losses}", ""));
        sb.Append(Card("Win rate", s.Games == 0 ? "-" : Pct(s.WinRate), RateClass(s.Games, s.WinRate)));
        sb.Append(Card("Streak", s.StreakText, s.CurrentStreak > 0 ? "win" : s.CurrentStreak < 0 ? "loss" : ""));
        sb.Append(Card("Best win streak", s.BestWinStreak.ToString(Inv), ""));
        sb.Append(Card("Nights", s.Nights.ToString(Inv), ""));
        sb.Append(ToughestCard("Toughest DPS", Stats.ToughestSpec(ms, healers: false)));
        sb.Append(ToughestCard("Toughest healer", Stats.ToughestSpec(ms, healers: true)));
        sb.Append(Card("Current rating", s.LatestRating?.ToString(Inv) ?? "-", "accent"));
        sb.Append(Card("Peak rating", s.PeakRating?.ToString(Inv) ?? "-", "accent"));
        sb.Append("</div>\n");

        sb.Append("<h2>Rating history <small>each point is a game; dashed lines separate nights</small></h2>\n");
        sb.Append("<div class=\"chart\">").Append(RatingSvg(Stats.RatingHistory(ms))).Append("</div>\n");

        sb.Append("<div class=\"tables\">\n");
        sb.Append(MmrTable(Stats.OpponentMmrSummary(ms)));
        sb.Append(OutputTable("Damage & healing vs. them", "Result", Stats.OutputByResult(ms)));
        sb.Append(OutputTable("Damage & healing by enemy comp", "Comp", Stats.OutputByEnemyComp(ms)));
        sb.Append(Table("Enemy comps", "Comp", Stats.ByEnemyComp(ms)));
        sb.Append(Table("Enemy classes (once per game)", "Class", Stats.ByEnemyClass(ms)));
        sb.Append(Table("Enemy specs", "Spec", Stats.ByEnemySpec(ms)));
        sb.Append(Table("Comp type", "Type", Stats.ByCompType(ms)));
        sb.Append(Table("Maps", "Map", Stats.ByMap(ms)));
        sb.Append(Table("Game number in the night", "Games", Stats.ByGameNumber(ms)));
        sb.Append(Table("Game length", "Length", Stats.ByDuration(ms)));
        sb.Append(Table("Day of week", "Day", Stats.ByWeekday(ms)));
        sb.Append(Table("Nights", "Night", Stats.ByNight(ms)));
        sb.Append(Table("Tags", "Tag", Stats.ByTag(ms)));
        sb.Append(Table("Repeat opponents", "Player", Stats.RepeatOpponents(ms)));
        var mmrGap = Stats.ByMmrGap(ms);
        if (mmrGap.Count > 0) sb.Append(Table("Enemy MMR vs yours", "Matchup", mmrGap));
        var ourComps = Stats.ByOurComp(ms);
        if (ourComps.Count > 1) sb.Append(Table("Our comps", "Comp", ourComps));
        sb.Append("</div>\n");
        return sb.ToString();
    }

    private static string Card(string title, string value, string cls, string? tip = null) =>
        $"<div class=\"card\"{(tip is null ? "" : $" title=\"{H(tip)}\"")}><span>{H(title)}</span><strong class=\"{cls}\">{H(value)}</strong></div>";

    private static string ToughestCard(string title, StatRow? row) =>
        row is null
            ? Card(title, "-", "", $"Shows once you've played {Stats.ToughestMinGames}+ games against a spec.")
            : Card(title, $"{row.Label} ({Pct(row.WinRate)})",
                   row.WinRate > 0.5 ? "win" : row.WinRate < 0.5 ? "loss" : "",
                   $"{row.Record} against {row.Label} over {row.Games} game(s).");

    private static string TableStart(string title, string tip = "") =>
        $"<section class=\"stat\"><h3>{H(title)}</h3><div class=\"stat-scroll\"{(tip.Length == 0 ? "" : $" title=\"{H(tip)}\"")}><table class=\"sortable\">";

    private const string TableEnd = "</tbody></table></div></section>\n";

    private static string Empty(string title, string text) =>
        $"<section class=\"stat\"><h3>{H(title)}</h3><p class=\"empty\">{H(text)}</p></section>\n";

    private static string Table(string title, string labelHeader, List<StatRow> rows)
    {
        if (rows.Count == 0) return Empty(title, "Nothing yet.");
        var sb = new StringBuilder(TableStart(title));
        sb.Append($"<thead><tr><th>{H(labelHeader)}</th><th data-type=\"num\" class=\"r\">Games</th><th data-type=\"num\" class=\"r\">W-L</th><th data-type=\"num\" class=\"r\">Win %</th></tr></thead><tbody>");
        foreach (var r in rows)
            sb.Append($"<tr><td>{H(r.Label)}</td><td class=\"r\">{r.Games}</td><td class=\"r\" data-sort=\"{r.Games}\">{r.Record}</td>" +
                      $"<td class=\"r pct {RateClass(r.Games, r.WinRate)}\" data-sort=\"{Num(r.WinRate)}\">{(r.Games == 0 ? "-" : Pct(r.WinRate))}</td></tr>");
        sb.Append(TableEnd);
        return sb.ToString();
    }

    private static string OutputTable(string title, string labelHeader, List<OutputGap> rows)
    {
        if (rows.Count == 0) return Empty(title, "Nothing yet. Damage and healing come from games recorded by the FNF addon.");
        var sb = new StringBuilder(TableStart(title,
            "Your team's total damage and healing vs. the enemy team's over these games. -20% DPS = 20% less damage than them. " +
            "Healing only counts games where both teams brought the same number of healers."));
        sb.Append($"<thead><tr><th>{H(labelHeader)}</th><th data-type=\"num\" class=\"r\">Games</th><th data-type=\"num\" class=\"r\">W-L</th><th data-type=\"num\" class=\"r\">DPS</th><th data-type=\"num\" class=\"r\">HPS</th></tr></thead><tbody>");
        foreach (var g in rows)
        {
            var dps = Stats.FormatPercentVs(g.DpsPct);
            var hps = Stats.FormatPercentVs(g.HpsPct);
            var hpsTip = g.HpsGames == g.Games ? ""
                : g.HpsGames == 0 ? " title=\"No like-for-like games: every game had a different number of healers on each side.\""
                : $" title=\"From {g.HpsGames} of {g.Games} games; the rest had a different number of healers on each side.\"";
            sb.Append($"<tr><td>{H(g.Label)}</td><td class=\"r\">{g.Games}</td><td class=\"r\" data-sort=\"{g.Games}\">{g.Record}</td>" +
                      $"<td class=\"r pct {GapClass(dps)}\" data-sort=\"{(g.DpsPct is double d ? Num(d) : "")}\">{(dps.Length == 0 ? "—" : dps)}</td>" +
                      $"<td class=\"r pct {GapClass(hps)}\" data-sort=\"{(g.HpsPct is double h ? Num(h) : "")}\"{hpsTip}>{(hps.Length == 0 ? "—" : hps)}</td></tr>");
        }
        sb.Append(TableEnd);
        return sb.ToString();
    }

    private static string MmrTable(OpponentMmr mmr)
    {
        if (mmr.Games == 0) return Empty("MMR", "Nothing yet. Opponent MMR comes from games recorded by the FNF addon.");
        string Row(string label, int? value, string note, string cls) =>
            $"<tr><td>{label}</td><td class=\"r accent\">{value?.ToString(Inv) ?? "-"}</td><td class=\"{cls}\">{H(note)}</td></tr>";
        string Won(bool? w) => w switch { true => "you won", false => "you lost", _ => "" };
        return TableStart("MMR", $"Opponent team MMR from {mmr.Games} game(s) recorded by the FNF addon.")
             + "<thead><tr><th>Opponent MMR</th><th class=\"r\">MMR</th><th>Result</th></tr></thead><tbody>"
             + Row("Highest", mmr.Highest, Won(mmr.WonVsHighest), mmr.WonVsHighest == true ? "win" : "loss")
             + Row("Average", mmr.Average, $"over {mmr.Games} game(s)", "dim")
             + Row("Lowest", mmr.Lowest, Won(mmr.WonVsLowest), mmr.WonVsLowest == true ? "win" : "loss")
             + TableEnd;
    }

    /// <summary>The rating line as inline SVG, laid out like the app's chart.</summary>
    private static string RatingSvg(List<RatingPoint> pts)
    {
        if (pts.Count < 2)
            return "<p class=\"empty\">Fill in 'Rating after' on your games to see your rating line here.</p>";

        int min = pts.Min(p => p.Rating), max = pts.Max(p => p.Rating);
        if (max - min < 20) { max += 10; min -= 10; }
        const double w = 1000, h = 220, padL = 52, padR = 16, padT = 14, padB = 14;
        double plotW = w - padL - padR, plotH = h - padT - padB;
        double X(int i) => padL + i * plotW / (pts.Count - 1);
        double Y(int r) => padT + (max - r) * plotH / (max - min);

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {Num(w)} {Num(h)}\" role=\"img\" aria-label=\"Rating over time, from {pts[0].Rating} to {pts[^1].Rating}\">");
        foreach (var r in new[] { max, (max + min) / 2, min })
        {
            sb.Append($"<line class=\"guide\" x1=\"{Num(padL)}\" x2=\"{Num(w - padR)}\" y1=\"{Num(Y(r))}\" y2=\"{Num(Y(r))}\"/>");
            sb.Append($"<text x=\"6\" y=\"{Num(Y(r) + 4)}\">{r}</text>");
        }
        for (int i = 1; i < pts.Count; i++)
        {
            if (DateOnly.FromDateTime(pts[i - 1].PlayedAt.AddHours(-6)) == DateOnly.FromDateTime(pts[i].PlayedAt.AddHours(-6))) continue;
            double x = (X(i - 1) + X(i)) / 2;
            sb.Append($"<line class=\"night\" x1=\"{Num(x)}\" x2=\"{Num(x)}\" y1=\"{Num(padT)}\" y2=\"{Num(h - padB)}\"/>");
        }
        sb.Append("<polyline class=\"line\" points=\"");
        for (int i = 0; i < pts.Count; i++) sb.Append($"{Num(X(i))},{Num(Y(pts[i].Rating))} ");
        sb.Append("\"/>");
        // Invisible hover targets, one per game, so a hover shows the date and rating.
        for (int i = 0; i < pts.Count; i++)
            sb.Append($"<circle class=\"pt\" cx=\"{Num(X(i))}\" cy=\"{Num(Y(pts[i].Rating))}\" r=\"6\"><title>{H(pts[i].PlayedAt.ToString("ddd MMM d, h:mm tt", En))}: {pts[i].Rating}</title></circle>");
        sb.Append($"<circle class=\"last\" cx=\"{Num(X(pts.Count - 1))}\" cy=\"{Num(Y(pts[^1].Rating))}\" r=\"4\"/>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    // ───────────────────────── CSS and JavaScript ─────────────────────────

    private const string Css = """
@font-face { font-family: "Marcellus"; src: url("assets/Marcellus-Regular.ttf") format("truetype"); font-display: swap; }
:root {
  --bg: #1B1B1F; --surface: rgba(255,255,255,.05); --line: rgba(255,255,255,.09);
  --text: #D8DAE0; --dim: #8A8F98; --gold: #D8B56A; --accent: #E6B450;
  --win: #4CC38A; --loss: #E5534B;
  --heading: "Friz Quadrata", "Friz Quadrata TT", "Marcellus", Georgia, serif;
  color-scheme: dark;
}
* { box-sizing: border-box; }
html, body { margin: 0; }
body { background: var(--bg); color: var(--text); font: 14px/1.45 "Segoe UI", system-ui, -apple-system, sans-serif; }
a { color: var(--accent); }
.wrap { max-width: 1500px; margin: 0 auto; padding: 0 16px; }
.banner {
  position: relative;
  background: linear-gradient(to bottom, rgba(27,27,31,.3), rgba(27,27,31,.55) 45%, var(--bg)), url("assets/background.jpg") center 30% / cover;
  border-bottom: 1px solid rgba(106,74,34,.5); margin-bottom: 18px;
}
.banner-row { display: flex; align-items: center; gap: 28px; flex-wrap: wrap; padding-top: 16px; padding-bottom: 8px; }
.logo img { height: 54px; display: block; }
/* Mascot art, top right like the app: fades out downward, ignores the mouse, hidden on narrow screens.
   The dark radial glow behind it keeps the background art from showing through the characters. */
.art { position: absolute; left: 0; right: 0; top: 0; height: 0; pointer-events: none; z-index: 2; }
.art::before {
  content: ""; position: absolute; right: -70px; top: -60px; width: 600px; height: 440px;
  background: radial-gradient(closest-side, rgba(14,11,13,.8) 45%, rgba(14,11,13,0));
}
.art img {
  position: absolute; right: 24px; top: 4px; width: 360px; opacity: .85;
  -webkit-mask-image: linear-gradient(#000 0, #000 45%, transparent 95%);
          mask-image: linear-gradient(#000 0, #000 45%, transparent 95%);
}
@media (min-width: 1100px) {
  /* Leave the art room so it doesn't sit over the tables. */
  .banner-row, .tabs { padding-right: 400px; }
  main { position: relative; z-index: 3; }
  .page-matches main { padding-top: 40px; }
  .page-matches .art img { width: 330px; }
  .page-stats main { padding-top: 20px; }
}
@media (max-width: 1099px) { .art { display: none; } }
.record { font-family: var(--heading); font-size: 17px; display: flex; gap: 6px 24px; flex-wrap: wrap; }
.record .overall { opacity: .75; }
.tabs { display: flex; gap: 4px; }
.tabs a {
  font-family: var(--heading); font-size: 16px; color: var(--text); text-decoration: none;
  padding: 8px 16px 9px; border-bottom: 2px solid transparent; opacity: .75;
}
.tabs a:hover { opacity: 1; }
.tabs a.active { opacity: 1; color: var(--gold); border-bottom-color: var(--accent); }
h2 { font-family: var(--heading); font-weight: normal; font-size: 17px; margin: 22px 0 8px; }
h2 small { font-family: "Segoe UI", system-ui, sans-serif; font-size: 12px; color: var(--dim); margin-left: 8px; }
h3 { font-family: var(--heading); font-weight: normal; font-size: 17px; margin: 0 0 6px; }
.win { color: var(--win); } .loss { color: var(--loss); } .dim { color: var(--dim); } .accent { color: var(--accent); }
.toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
.filter { display: inline-flex; background: var(--surface); border: 1px solid var(--line); border-radius: 8px; padding: 2px; flex-wrap: wrap; }
.filter button {
  font: inherit; color: var(--text); background: none; border: 0; border-radius: 6px;
  padding: 5px 14px; cursor: pointer; opacity: .75;
}
.filter button:hover { opacity: 1; }
.filter button.on { background: rgba(230,180,80,.18); color: #F2DDA8; opacity: 1; }
input[type=search] {
  font: inherit; color: var(--text); background: var(--surface); border: 1px solid var(--line);
  border-radius: 8px; padding: 6px 10px; width: 300px; max-width: 100%;
}
input[type=search]:focus, .filter button:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
.count, .range-summary { color: var(--dim); }
.range-summary { margin: 0 0 12px; }
.table-scroll { overflow-x: auto; border-radius: 8px; background: var(--surface); }
table { border-collapse: collapse; width: 100%; }
th {
  font-family: var(--heading); font-weight: normal; text-align: left; white-space: nowrap;
  padding: 8px 10px; border-bottom: 1px solid var(--line); position: sticky; top: 0; background: #232328;
}
th.c, td.c { text-align: center; } th.r, td.r { text-align: right; }
.sortable th { cursor: pointer; user-select: none; }
.sortable th:hover { color: var(--gold); }
th[aria-sort=ascending]::after { content: " \25B2"; font-size: 9px; }
th[aria-sort=descending]::after { content: " \25BC"; font-size: 9px; }
td { padding: 6px 10px; border-bottom: 1px solid var(--line); vertical-align: middle; }
tbody tr:hover { background: rgba(255,255,255,.03); }
.matches td { white-space: nowrap; }
.result { font-weight: 700; }
.chip { display: flex; align-items: center; gap: 8px; margin: 1px 0; }
.chip i { width: 3px; height: 16px; border-radius: 1px; flex: none; }
.chip b { font-weight: 600; }
.chip .name { font-size: 12px; color: rgba(216,218,224,.6); text-decoration: none; }
.chip a.name:hover { color: var(--accent); text-decoration: underline; }
.ours .chip b { font-weight: 400; }
.tags { margin-top: 2px; }
.tags span { font-size: 11px; color: var(--dim); background: rgba(255,255,255,.06); border-radius: 4px; padding: 1px 6px; margin-right: 4px; }
.output { display: inline-block; text-align: left; cursor: help; }
.gap { display: flex; gap: 10px; justify-content: space-between; min-width: 84px; }
.gap span { font-size: 11px; color: var(--dim); }
.gap b { font-weight: 600; }
.cards { display: flex; flex-wrap: wrap; gap: 10px; }
.card { background: var(--surface); border-radius: 8px; padding: 10px 16px; min-width: 120px; }
.card span { display: block; font-family: var(--heading); opacity: .7; }
.card strong { display: block; font-family: var(--heading); font-weight: normal; font-size: 28px; line-height: 1.2; }
.chart { background: var(--surface); border-radius: 8px; padding: 4px; }
.chart svg { display: block; width: 100%; height: auto; }
.chart .guide, .chart .night { stroke: rgba(255,255,255,.2); stroke-width: 1; vector-effect: non-scaling-stroke; }
.chart .night { stroke-dasharray: 3 3; }
.chart .line { fill: none; stroke: var(--accent); stroke-width: 2; vector-effect: non-scaling-stroke; }
.chart text { fill: var(--dim); font-size: 11px; }
.chart .pt { fill: transparent; }
.chart .pt:hover { fill: var(--accent); }
.chart .last { fill: var(--accent); }
.chart .empty { padding: 80px 16px; margin: 0; }
.tables { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px 20px; margin-top: 20px; }
@media (max-width: 900px) { .tables { grid-template-columns: minmax(0, 1fr); } }
.stat-scroll { max-height: 320px; overflow: auto; background: var(--surface); border-radius: 8px; }
.pct { font-weight: 600; }
.empty { color: var(--dim); }
footer { color: var(--dim); font-size: 12px; padding-top: 24px; padding-bottom: 24px; }
@media (max-width: 600px) {
  .logo img { height: 40px; }
  .record { font-size: 15px; }
  .card { flex: 1 1 140px; }
  .card strong { font-size: 22px; }
  input[type=search] { width: 100%; }
}
""";

    private const string Js = """
(function () {
  // After an auto-refresh, pick up where the viewer was (filter, search, sort, scroll). Saved just
  // before the reload, used once, and only on the same page.
  var RESTORE = "fnf-restore", restored = null;
  try {
    restored = JSON.parse(sessionStorage.getItem(RESTORE) || "null");
    sessionStorage.removeItem(RESTORE);
  } catch (e) { }
  if (restored && restored.path !== location.pathname) restored = null;
  var pageState = function () { return {}; };   // the Matches page fills this in
  var lastSort = null;                          // { table, col, dir } of the last header click

  // Click a column header to sort; click again to reverse. Cells sort by data-sort when present.
  function key(cell, num) {
    var v = cell.hasAttribute("data-sort") ? cell.getAttribute("data-sort") : cell.textContent.trim();
    if (!num) return v.toLowerCase();
    var n = parseFloat(String(v).replace(/[^0-9.+-]/g, ""));
    return isNaN(n) ? null : n;
  }
  var sortables = document.querySelectorAll("table.sortable");
  sortables.forEach(function (table, tableIndex) {
    var heads = table.querySelectorAll("thead th");
    heads.forEach(function (th, col) {
      th.tabIndex = 0;
      function sort() {
        var num = th.getAttribute("data-type") === "num";
        var dir = th.getAttribute("aria-sort") === "descending" ? 1 : -1;
        if (!th.hasAttribute("aria-sort") && !num) dir = 1;
        heads.forEach(function (h) { h.removeAttribute("aria-sort"); });
        th.setAttribute("aria-sort", dir === 1 ? "ascending" : "descending");
        var body = table.tBodies[0];
        var rows = Array.prototype.slice.call(body.rows);
        rows.sort(function (a, b) {
          var x = key(a.cells[col], num), y = key(b.cells[col], num);
          if (x === y) return 0;
          if (x === null || x === "") return 1;   // blanks always last
          if (y === null || y === "") return -1;
          return (x < y ? -1 : 1) * dir;
        });
        rows.forEach(function (r) { body.appendChild(r); });
      }
      th.addEventListener("click", function () {
        sort();
        lastSort = { table: tableIndex, col: col, dir: th.getAttribute("aria-sort") };
      });
      th.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); sort(); } });
    });
  });

  // Matches page: win/loss filter and search.
  var matches = document.getElementById("matches");
  if (matches) {
    var search = document.getElementById("search"), count = document.getElementById("count");
    var mode = "all";
    function apply() {
      var q = search.value.trim().toLowerCase(), shown = 0, wins = 0;
      Array.prototype.forEach.call(matches.tBodies[0].rows, function (r) {
        var ok = (mode === "all" || r.getAttribute("data-result") === mode) &&
                 (!q || r.getAttribute("data-search").indexOf(q) >= 0);
        r.hidden = !ok;
        if (ok) { shown++; if (r.getAttribute("data-result") === "w") wins++; }
      });
      count.textContent = shown + " game(s)" + (shown && mode === "all" ? " · " + wins + "-" + (shown - wins) : "");
    }
    document.querySelectorAll("[data-filter]").forEach(function (b) {
      b.addEventListener("click", function () {
        mode = b.getAttribute("data-filter");
        document.querySelectorAll("[data-filter]").forEach(function (x) { x.classList.toggle("on", x === b); });
        apply();
      });
    });
    search.addEventListener("input", apply);
    if (restored) {
      if (restored.mode) {
        mode = restored.mode;
        document.querySelectorAll("[data-filter]").forEach(function (x) { x.classList.toggle("on", x.getAttribute("data-filter") === mode); });
      }
      if (restored.q) search.value = restored.q;
    }
    pageState = function () { return { mode: mode, q: search.value }; };
    apply();
  }

  // Stats page: switch between the pre-built ranges; the choice is kept in the address (#all, #night…).
  var tabs = document.querySelectorAll("[data-range]");
  if (tabs.length) {
    function show(id) {
      var found = false;
      tabs.forEach(function (t) { var on = t.getAttribute("data-range") === id; t.classList.toggle("on", on); t.setAttribute("aria-selected", on); found = found || on; });
      if (!found) return false;
      document.querySelectorAll("[data-panel]").forEach(function (p) { p.hidden = p.getAttribute("data-panel") !== id; });
      return true;
    }
    tabs.forEach(function (t) {
      t.addEventListener("click", function () {
        var id = t.getAttribute("data-range");
        show(id);
        try { history.replaceState(null, "", "#" + id); } catch (e) { }
      });
    });
    if (!show(location.hash.slice(1))) show(tabs[0].getAttribute("data-range"));
  }

  if (restored) {
    // Re-apply the sort by clicking its header (once, or twice to reverse it).
    var s = restored.sort, table = s && sortables[s.table];
    var th = table && table.querySelectorAll("thead th")[s.col];
    if (th) for (var i = 0; i < 2 && th.getAttribute("aria-sort") !== s.dir; i++) th.click();
    if (restored.y) window.scrollTo(0, restored.y);
  }

  // Auto-refresh: every minute (and whenever the tab comes back into view), check version.json.
  // When a newer publish is there, save the viewer's place and reload. Pages opened straight from
  // disk (file://) can't check, so they don't try.
  var meta = document.querySelector('meta[name="fnf-version"]');
  if (meta && /^https?:$/.test(location.protocol) && window.fetch) {
    var current = meta.getAttribute("content"), checking = false, TRIED = "fnf-tried";
    function check() {
      if (checking || document.hidden) return;
      checking = true;
      fetch("version.json?t=" + Date.now(), { cache: "no-store" })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (v) {
          if (!v || !v.version || v.version === current) return;
          // GitHub can take a moment to serve the new pages everywhere. If a reload for this version
          // still brought back the old page, wait a few minutes before trying again.
          var tried = null;
          try { tried = JSON.parse(sessionStorage.getItem(TRIED) || "null"); } catch (e) { }
          if (tried && tried.v === v.version && Date.now() - tried.at < 3 * 60 * 1000) return;
          try {
            sessionStorage.setItem(TRIED, JSON.stringify({ v: v.version, at: Date.now() }));
            var state = pageState();
            state.path = location.pathname;
            state.y = window.scrollY;
            state.sort = lastSort;
            sessionStorage.setItem(RESTORE, JSON.stringify(state));
          } catch (e) { }
          location.reload();
        })
        .catch(function () { })
        .then(function () { checking = false; });
    }
    setInterval(check, 60 * 1000);
    document.addEventListener("visibilitychange", function () { if (!document.hidden) check(); });
  }
})();
""";
}
