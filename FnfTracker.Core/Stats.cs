namespace FnfTracker.Core;

public sealed record StatRow(string Label, int Games, int Wins)
{
    public int Losses => Games - Wins;
    public double WinRate => Games == 0 ? 0 : (double)Wins / Games;
    public string Record => $"{Wins}-{Losses}";
    public string WinPct => Games == 0 ? "-" : $"{WinRate:P0}";
}

public sealed record RatingPoint(DateTime PlayedAt, int Rating);

/// <summary>
/// Our damage and healing vs. the enemy team's over a group of games, as percent of theirs.
/// DpsGames / HpsGames say how many games each figure is built from.
/// </summary>
public sealed record OutputGap(string Label, int Games, int Wins, double? DpsPct, int DpsGames, double? HpsPct, int HpsGames)
{
    public string Record => $"{Wins}-{Games - Wins}";
}

/// <summary>Opponent team MMR across games that recorded it (addon-imported rated games).</summary>
public sealed record OpponentMmr(int Games, int? Highest, int? Average, int? Lowest, bool? WonVsHighest, bool? WonVsLowest);

public sealed record Summary(
    int Games, int Wins, double WinRate,
    int CurrentStreak,          // +3 = three wins in a row, -2 = two losses
    int BestWinStreak,
    int? LatestRating, int? PeakRating,
    int Nights)
{
    public int Losses => Games - Wins;
    public string StreakText => CurrentStreak switch
    {
        > 0 => $"W{CurrentStreak}",
        < 0 => $"L{-CurrentStreak}",
        _ => "-",
    };
}

/// <summary>All the numbers on the Stats tab. Pure functions over a match list.</summary>
public static class Stats
{
    /// <summary>Our team from the most recent game (class + spec only), or empty if there's none.</summary>
    public static List<PlayerSlot> LastTeam(IEnumerable<Match> matches) =>
        matches.Where(m => m.OurTeam.Any(p => !p.IsEmpty))
               .OrderByDescending(m => m.PlayedAt)
               .Select(m => SpecsOnly(m.OurTeam))
               .FirstOrDefault() ?? new();

    /// <summary>
    /// The comps we've played lately, most-played first, for the quick-pick buttons on the
    /// match form. Only complete teams (two players with specs) count.
    /// </summary>
    public static List<List<PlayerSlot>> RecentTeams(IEnumerable<Match> matches, int lookback = 40, int take = 3) =>
        matches.OrderByDescending(m => m.PlayedAt)
               .Take(lookback)
               .Where(m => m.OurTeam.Count == 2 && m.OurTeam.All(p => !p.IsEmpty && !string.IsNullOrWhiteSpace(p.Spec)))
               .GroupBy(m => m.OurCompKey, StringComparer.OrdinalIgnoreCase)
               .OrderByDescending(g => g.Count())
               .ThenByDescending(g => g.First().PlayedAt)
               .Take(take)
               .Select(g => SpecsOnly(g.First().OurTeam))
               .ToList();

    private static List<PlayerSlot> SpecsOnly(IEnumerable<PlayerSlot> team) =>
        team.Where(p => !p.IsEmpty).Select(p => new PlayerSlot { Class = p.Class, Spec = p.Spec }).ToList();

    public static IEnumerable<Match> Filter(IEnumerable<Match> matches, DateTime? since) =>
        since is null ? matches : matches.Where(m => m.Night >= DateOnly.FromDateTime(since.Value));

    public static Summary Summarize(IReadOnlyList<Match> matches)
    {
        var ordered = matches.OrderBy(m => m.PlayedAt).ToList();
        int wins = ordered.Count(m => m.Won);

        int best = 0, run = 0;
        foreach (var m in ordered)
        {
            run = m.Won ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        int current = 0;
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            bool won = ordered[i].Won;
            if (current == 0) current = won ? 1 : -1;
            else if (won && current > 0) current++;
            else if (!won && current < 0) current--;
            else break;
        }

        var rated = ordered.Where(m => m.RatingAfter.HasValue).ToList();
        return new Summary(
            ordered.Count, wins,
            ordered.Count == 0 ? 0 : (double)wins / ordered.Count,
            current, best,
            rated.LastOrDefault()?.RatingAfter,
            rated.Count == 0 ? null : rated.Max(m => m.RatingAfter!.Value),
            ordered.Select(m => m.Night).Distinct().Count());
    }

    private static List<StatRow> Group(IEnumerable<(string Key, bool Won)> rows) =>
        rows.GroupBy(r => r.Key)
            .Select(g => new StatRow(g.Key, g.Count(), g.Count(r => r.Won)))
            .OrderByDescending(r => r.Games)
            .ThenBy(r => r.Label)
            .ToList();

    public static List<StatRow> ByEnemyComp(IEnumerable<Match> ms) =>
        Group(ms.Select(m => (m.EnemyCompKey, m.Won)));

    /// <summary>A game counts once per distinct enemy class in it (a mirror counts once).</summary>
    public static List<StatRow> ByEnemyClass(IEnumerable<Match> ms) =>
        Group(ms.SelectMany(m => m.Enemies.Where(e => !e.IsEmpty)
                                          .Select(e => e.Class)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .Select(c => (c, m.Won))));

    public static List<StatRow> ByEnemySpec(IEnumerable<Match> ms) =>
        Group(ms.SelectMany(m => m.Enemies.Where(e => !e.IsEmpty)
                                          .Select(e => e.Label)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .Select(s => (s, m.Won))));

    public static List<StatRow> ByCompType(IEnumerable<Match> ms) =>
        Group(ms.Select(m =>
        {
            var known = m.Enemies.Where(e => !e.IsEmpty && !string.IsNullOrWhiteSpace(e.Spec)).ToList();
            if (known.Count < 2) return ("(specs unknown)", m.Won);
            int healers = known.Count(GameData.IsHealer);
            return (healers switch { 0 => "Double DPS", 1 => "DPS + Healer", _ => "Double Healer" }, m.Won);
        }));

    public static List<StatRow> ByMap(IEnumerable<Match> ms) =>
        Group(ms.Select(m => (string.IsNullOrWhiteSpace(m.Map) ? "(no map)" : m.Map, m.Won)));

    public static List<StatRow> ByOurComp(IEnumerable<Match> ms) =>
        Group(ms.Select(m => (m.OurCompKey, m.Won)));

    public static List<StatRow> ByWeekday(IEnumerable<Match> ms) =>
        Group(ms.Select(m => (m.Night.DayOfWeek.ToString(), m.Won)));

    /// <summary>Most recent nights first.</summary>
    public static List<StatRow> ByNight(IEnumerable<Match> ms) =>
        ms.GroupBy(m => m.Night)
          .OrderByDescending(g => g.Key)
          .Select(g => new StatRow($"{g.Key:ddd MMM d}", g.Count(), g.Count(m => m.Won)))
          .ToList();

    /// <summary>The Nth game of the night (1-based), for every match.</summary>
    public static Dictionary<Guid, int> GameNumbers(IEnumerable<Match> ms) =>
        ms.GroupBy(m => m.Night)
          .SelectMany(g => g.OrderBy(m => m.PlayedAt).Select((m, i) => (m.Id, N: i + 1)))
          .ToDictionary(x => x.Id, x => x.N);

    /// <summary>Win rate by how deep into the night the game was — spots fatigue/tilt.</summary>
    public static List<StatRow> ByGameNumber(IReadOnlyList<Match> ms)
    {
        var numbers = GameNumbers(ms);
        string Bucket(int n) => n switch
        {
            <= 5 => "Games 1-5",
            <= 10 => "Games 6-10",
            <= 15 => "Games 11-15",
            <= 20 => "Games 16-20",
            _ => "Games 21+",
        };
        return ms.Select(m => (Bucket(numbers[m.Id]), m.Won))
                 .GroupBy(r => r.Item1)
                 .Select(g => new StatRow(g.Key, g.Count(), g.Count(r => r.Won)))
                 .OrderBy(r => r.Label.Length).ThenBy(r => r.Label)
                 .ToList();
    }

    public static List<StatRow> ByDuration(IEnumerable<Match> ms)
    {
        string Bucket(int s) => s switch
        {
            < 180 => "Under 3 min",
            < 360 => "3-6 min",
            < 600 => "6-10 min",
            _ => "10+ min",
        };
        var order = new[] { "Under 3 min", "3-6 min", "6-10 min", "10+ min" };
        return ms.Where(m => m.DurationSeconds.HasValue)
                 .Select(m => (Bucket(m.DurationSeconds!.Value), m.Won))
                 .GroupBy(r => r.Item1)
                 .Select(g => new StatRow(g.Key, g.Count(), g.Count(r => r.Won)))
                 .OrderBy(r => Array.IndexOf(order, r.Label))
                 .ToList();
    }

    public static List<StatRow> ByTag(IEnumerable<Match> ms) =>
        Group(ms.SelectMany(m => m.Tags.Where(t => !string.IsNullOrWhiteSpace(t))
                                       .Select(t => t.Trim().ToLowerInvariant())
                                       .Distinct()
                                       .Select(t => (t, m.Won))));

    /// <summary>Enemy players (by Name-Realm) you've met more than once.</summary>
    public static List<StatRow> RepeatOpponents(IEnumerable<Match> ms) =>
        Group(ms.SelectMany(m => m.Enemies.Where(e => !string.IsNullOrWhiteSpace(e.Name))
                                          .Select(e => ($"{e.Name!.Trim()} ({e.Label})", m.Won))))
            .Where(r => r.Games > 1)
            .ToList();

    /// <summary>Win rate by how the enemy team's MMR compared to yours (addon-imported rated games).</summary>
    public static List<StatRow> ByMmrGap(IEnumerable<Match> ms)
    {
        var order = new[] { "They were 100+ higher", "They were 25-99 higher", "Even (within 25)", "They were 25-99 lower", "They were 100+ lower" };
        string Bucket(int gap) => gap switch
        {
            >= 100 => order[0],
            >= 25 => order[1],
            > -25 => order[2],
            > -100 => order[3],
            _ => order[4],
        };
        return ms.Where(m => m.OurMmr.HasValue && m.EnemyMmr.HasValue)
                 .Select(m => (Bucket(m.EnemyMmr!.Value - m.OurMmr!.Value), m.Won))
                 .GroupBy(r => r.Item1)
                 .Select(g => new StatRow(g.Key, g.Count(), g.Count(r => r.Won)))
                 .OrderBy(r => Array.IndexOf(order, r.Label))
                 .ToList();
    }

    public static OpponentMmr OpponentMmrSummary(IEnumerable<Match> ms)
    {
        var withMmr = ms.Where(m => m.EnemyMmr.HasValue).ToList();
        if (withMmr.Count == 0) return new OpponentMmr(0, null, null, null, null, null);
        // Ties go to the most recent game.
        var high = withMmr.OrderByDescending(m => m.EnemyMmr).ThenByDescending(m => m.PlayedAt).First();
        var low = withMmr.OrderBy(m => m.EnemyMmr).ThenByDescending(m => m.PlayedAt).First();
        return new OpponentMmr(
            withMmr.Count,
            high.EnemyMmr,
            (int)Math.Round(withMmr.Average(m => m.EnemyMmr!.Value)),
            low.EnemyMmr,
            high.Won,
            low.Won);
    }

    public static List<RatingPoint> RatingHistory(IEnumerable<Match> ms) =>
        ms.Where(m => m.RatingAfter.HasValue)
          .OrderBy(m => m.PlayedAt)
          .Select(m => new RatingPoint(m.PlayedAt, m.RatingAfter!.Value))
          .ToList();

    /// <summary>Parses "4:32", "4m32s", "272" (seconds) or "4.5" (minutes).</summary>
    public static int? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().ToLowerInvariant().Replace("s", "");
        if (text.Contains(':') || text.Contains('m'))
        {
            var parts = text.Split(':', 'm');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var min)
                && int.TryParse(string.IsNullOrEmpty(parts[1]) ? "0" : parts[1], out var sec))
                return min * 60 + sec;
            return null;
        }
        if (int.TryParse(text, out var whole)) return whole <= 30 ? whole * 60 : whole; // "6" = 6 min
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var mins))
            return (int)Math.Round(mins * 60);
        return null;
    }

    public static string FormatDuration(int? seconds) =>
        seconds is null ? "" : $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>
    /// A team's total of one scoreboard stat (damage or healing), or null when no player on
    /// the team has it — manual games, or a scoreboard the addon couldn't read.
    /// </summary>
    public static long? TeamTotal(IEnumerable<PlayerSlot> team, Func<PlayerSlot, long?> stat)
    {
        long total = 0;
        bool any = false;
        foreach (var v in team.Select(stat))
            if (v is long x) { total += x; any = true; }
        return any ? total : null;
    }

    /// <summary>
    /// How far ahead (+) or behind (−) our total was, as a percent of theirs: +25 means we did
    /// 25% more. Both teams play the same length of game, so this is also the DPS / HPS gap.
    /// Null when either side is missing, or when they did none at all and a percent of zero
    /// means nothing.
    /// </summary>
    public static double? PercentVs(long? ours, long? theirs)
    {
        if (ours is not long o || theirs is not long t) return null;
        if (t == 0) return o == 0 ? 0 : null;
        return (o - t) * 100.0 / t;
    }

    public static long? OurDamage(Match m) => TeamTotal(m.OurTeam, p => p.Damage);
    public static long? TheirDamage(Match m) => TeamTotal(m.Enemies, p => p.Damage);
    public static long? OurHealing(Match m) => TeamTotal(m.OurTeam, p => p.Healing);
    public static long? TheirHealing(Match m) => TeamTotal(m.Enemies, p => p.Healing);

    /// <summary>True when the game has scoreboard damage/healing (addon imports).</summary>
    public static bool HasOutput(Match m) => OurDamage(m) != null || OurHealing(m) != null;

    public static double? DpsVs(Match m) => PercentVs(OurDamage(m), TheirDamage(m));
    public static double? HpsVs(Match m) => PercentVs(OurHealing(m), TheirHealing(m));

    /// <summary>
    /// Whether healing is a like-for-like comparison: false only when every spec is known and the
    /// teams brought a different number of healers (a healer comp out-heals double DPS by default,
    /// which says nothing about who played better).
    /// </summary>
    public static bool HealingComparable(Match m)
    {
        var all = m.OurTeam.Concat(m.Enemies).Where(p => !p.IsEmpty).ToList();
        if (all.Count == 0 || all.Any(p => string.IsNullOrWhiteSpace(p.Spec))) return true;
        return m.OurTeam.Count(GameData.IsHealer) == m.Enemies.Count(GameData.IsHealer);
    }

    /// <summary>
    /// Damage and healing gaps over a set of games, from the summed totals (so one tiny game
    /// can't swing the average). Healing only counts games where it's like-for-like.
    /// </summary>
    public static OutputGap SummarizeOutput(string label, IEnumerable<Match> ms)
    {
        var games = ms.Where(HasOutput).ToList();
        long? Sum(IEnumerable<Match> g, Func<Match, long?> f)
        {
            long t = 0; bool any = false;
            foreach (var m in g) if (f(m) is long v) { t += v; any = true; }
            return any ? t : null;
        }
        var dmg = games.Where(m => OurDamage(m) != null && TheirDamage(m) != null).ToList();
        var heal = games.Where(m => OurHealing(m) != null && TheirHealing(m) != null && HealingComparable(m)).ToList();
        return new OutputGap(label, games.Count, games.Count(m => m.Won),
            PercentVs(Sum(dmg, OurDamage), Sum(dmg, TheirDamage)), dmg.Count,
            PercentVs(Sum(heal, OurHealing), Sum(heal, TheirHealing)), heal.Count);
    }

    /// <summary>All games, then wins vs. losses: "do we lose when we get out-damaged?"</summary>
    public static List<OutputGap> OutputByResult(IEnumerable<Match> ms)
    {
        var games = ms.Where(HasOutput).ToList();
        if (games.Count == 0) return new();
        return new()
        {
            SummarizeOutput("All games", games),
            SummarizeOutput("Wins", games.Where(m => m.Won)),
            SummarizeOutput("Losses", games.Where(m => !m.Won)),
        };
    }

    /// <summary>The gap against each enemy comp, worst damage gap first.</summary>
    public static List<OutputGap> OutputByEnemyComp(IEnumerable<Match> ms) =>
        ms.Where(HasOutput)
          .GroupBy(m => m.EnemyCompKey, StringComparer.OrdinalIgnoreCase)
          .Select(g => SummarizeOutput(g.Key, g))
          .OrderBy(r => r.DpsPct ?? double.MaxValue)
          .ThenByDescending(r => r.Games)
          .ToList();

    /// <summary>"+12%", "-34%" or "0%" (a gap under half a percent rounds to even).</summary>
    public static string FormatPercentVs(double? pct)
    {
        if (pct is not double p) return "";
        var r = Math.Round(p, MidpointRounding.AwayFromZero);
        return r > 0 ? $"+{r:0}%" : r < 0 ? $"-{-r:0}%" : "0%";
    }

    /// <summary>1234 → "1.2k", 1234567 → "1.23M", for tooltips.</summary>
    public static string FormatAmount(double v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (v / 1_000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k",
        _ => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
    };
}
