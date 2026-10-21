using System.Windows;
using System.Windows.Media;
using FnfTracker.Core;

namespace FnfTracker.App;

/// <summary>One enemy in the Matches grid. The name links to check-pvp.fr when we know its realm.</summary>
public sealed record Chip(string Text, Brush Brush, string? Name, string? ProfileUrl)
{
    public Visibility LinkVisibility => ProfileUrl != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlainNameVisibility => ProfileUrl == null ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>One line in the Matches grid.</summary>
public sealed class MatchRow
{
    public MatchRow(Match match, int gameNo, AppSettings settings)
    {
        Match = match;
        GameNo = gameNo;
        // Addon games are stamped "Name-Realm" by whoever recorded them: that's the realm the
        // scoreboard left off. Manual games fall back to the realm in Settings.
        var homeRealm = CheckPvp.RealmOf(match.RecordedBy) ?? settings.HomeRealm;
        EnemyChips = match.Enemies.Where(e => !e.IsEmpty)
                                  .Select(e => new Chip(e.Label, Theme.ForClass(e.Class), e.Name,
                                                        CheckPvp.ProfileUrl(e.Name, homeRealm, settings.Region)))
                                  .ToList();
    }

    public Match Match { get; }
    public int GameNo { get; }
    public DateTime PlayedAt => Match.PlayedAt;
    public string Night => Match.Night.ToString("ddd M/d");
    public string Time => Match.PlayedAt.ToString("h:mm tt");
    public string ResultText => Match.Won ? "W" : "L";
    public Brush ResultBrush => Match.Won ? Theme.Win : Theme.Loss;
    public string Map => Match.Map;
    public string Enemies => Match.EnemyCompKey;
    public List<Chip> EnemyChips { get; }
    public int? Rating => Match.RatingAfter;
    public string Change => Match.RatingChange is int c ? (c > 0 ? $"+{c}" : c.ToString()) : "";
    public Brush ChangeBrush => Match.RatingChange is int c && c < 0 ? Theme.Loss : Theme.Win;
    public int? EnemyMmr => Match.EnemyMmr;
    public string Duration => Stats.FormatDuration(Match.DurationSeconds);

    // ---- Output vs. the enemy team (addon games; the scoreboard gives damage and healing) ----

    private long? OurDamage => Stats.TeamTotal(Match.OurTeam, p => p.Damage);
    private long? TheirDamage => Stats.TeamTotal(Match.Enemies, p => p.Damage);
    private long? OurHealing => Stats.TeamTotal(Match.OurTeam, p => p.Healing);
    private long? TheirHealing => Stats.TeamTotal(Match.Enemies, p => p.Healing);

    /// <summary>Damage gap as a percent of theirs; also what the column sorts by.</summary>
    public double? DpsVs => Stats.PercentVs(OurDamage, TheirDamage);
    public double? HpsVs => Stats.PercentVs(OurHealing, TheirHealing);

    /// <summary>The two stacked lines in the DPS/HPS column, empty for games with no scoreboard.</summary>
    public List<OutputLine> Output
    {
        get
        {
            if (OurDamage is null && OurHealing is null) return new();
            return new()
            {
                OutputLine.Make("DPS", DpsVs),
                OutputLine.Make("HPS", HpsVs),
            };
        }
    }

    /// <summary>Hover text with the actual numbers behind the percentages.</summary>
    public string? OutputTip
    {
        get
        {
            if (OurDamage is null && OurHealing is null) return null;
            int? secs = Match.DurationSeconds;
            string Side(long? v) => v is long x
                ? Stats.FormatAmount(x) + (secs > 0 ? $" ({Stats.FormatAmount((double)x / secs!.Value)}/s)" : "")
                : "?";
            var tip = $"Damage   us {Side(OurDamage)}  ·  them {Side(TheirDamage)}\n" +
                      $"Healing  us {Side(OurHealing)}  ·  them {Side(TheirHealing)}";
            return secs > 0 ? tip + $"\nLength   {Duration}" : tip;
        }
    }
}

/// <summary>One line of the DPS/HPS column: "DPS  +12%", green ahead, red behind.</summary>
public sealed record OutputLine(string Label, string Text, Brush Brush)
{
    public static OutputLine Make(string label, double? pct)
    {
        var text = Stats.FormatPercentVs(pct);
        var brush = text.Length == 0 ? Theme.Dim
                  : text.StartsWith('+') ? Theme.Win
                  : text.StartsWith('-') ? Theme.Loss
                  : Theme.Neutral;
        return new(label, text.Length == 0 ? "—" : text, brush);
    }
}

/// <summary>One line in a simple value table (e.g. MMR: Highest / Average / Lowest).</summary>
public sealed record InfoLine(string Label, string Value, string Note, Brush NoteBrush);

/// <summary>One line in a Stats table.</summary>
public sealed class StatView
{
    public StatView(StatRow row)
    {
        Label = row.Label;
        Games = row.Games;
        Record = row.Record;
        WinRate = row.WinRate;
        WinPct = row.WinPct;
        Brush = Theme.ForWinRate(row.Games, row.WinRate);
    }

    public string Label { get; }
    public int Games { get; }
    public string Record { get; }
    public double WinRate { get; }
    public string WinPct { get; }
    public Brush Brush { get; }
}
