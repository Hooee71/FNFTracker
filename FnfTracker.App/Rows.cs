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

    public string Notes
    {
        get
        {
            var tags = Match.Tags.Count > 0 ? "[" + string.Join(", ", Match.Tags) + "] " : "";
            return (tags + Match.Notes).Replace('\n', ' ');
        }
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
