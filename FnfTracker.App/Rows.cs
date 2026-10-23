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
    public string? Dampening => Match.DampeningPct is int d ? $"Dampening {d}%" : null;
    public Visibility DampeningVisibility => Match.DampeningPct.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public string Enemies => Match.EnemyCompKey;
    public List<Chip> EnemyChips { get; }
    public int? Rating => Match.RatingAfter;
    public string Change => Match.RatingChange is int c ? (c > 0 ? $"+{c}" : c.ToString()) : "";
    public Brush ChangeBrush => Match.RatingChange is int c && c < 0 ? Theme.Loss : Theme.Win;
    public int? EnemyMmr => Match.EnemyMmr;
    public string Duration => Stats.FormatDuration(Match.DurationSeconds);

    // ---- Output vs. the enemy team (addon games; the scoreboard gives damage and healing) ----

    /// <summary>Damage gap as a percent of theirs; also what the column sorts by.</summary>
    public double? DpsVs => Stats.DpsVs(Match);
    public double? HpsVs => Stats.HpsVs(Match);

    /// <summary>The two stacked lines in the DPS/HPS column, empty for games with no scoreboard.</summary>
    public List<OutputLine> Output => !Stats.HasOutput(Match) ? new() : new()
    {
        OutputLine.Make("DPS", DpsVs),
        OutputLine.Make("HPS", HpsVs, muted: !Stats.HealingComparable(Match)),
    };

    /// <summary>Hover text with the actual numbers behind the percentages.</summary>
    public string? OutputTip
    {
        get
        {
            if (!Stats.HasOutput(Match)) return null;
            int? secs = Match.DurationSeconds;
            string Side(long? v) => v is long x
                ? Stats.FormatAmount(x) + (secs > 0 ? $" ({Stats.FormatAmount((double)x / secs!.Value)}/s)" : "")
                : "?";
            var tip = $"Damage   us {Side(Stats.OurDamage(Match))}  ·  them {Side(Stats.TheirDamage(Match))}\n" +
                      $"Healing  us {Side(Stats.OurHealing(Match))}  ·  them {Side(Stats.TheirHealing(Match))}";
            if (secs > 0) tip += $"\nLength   {Duration}";
            if (!Stats.HealingComparable(Match))
                tip += "\n\nHealing is greyed out: the teams brought a different number of healers, so it isn't a like-for-like comparison.";
            return tip;
        }
    }
}

/// <summary>
/// One line of the DPS/HPS column: "DPS  +12%" with a small bar growing right (green, ahead) or
/// left (red, behind) from a centre line. The bar is full at <see cref="FullScale"/>%.
/// Muted lines (healing that isn't like-for-like) keep the number but drop the colour.
/// </summary>
public sealed record OutputLine(string Label, string Text, Brush Brush, double NegWidth, double PosWidth)
{
    public const double FullScale = 50;   // percent gap that fills one side of the bar
    public const double HalfWidth = 18;   // pixels per side; keep in step with MainWindow.xaml

    public static OutputLine Make(string label, double? pct, bool muted = false)
    {
        var text = Stats.FormatPercentVs(pct);
        var brush = text.Length == 0 || muted ? Theme.Dim
                  : text.StartsWith('+') ? Theme.Win
                  : text.StartsWith('-') ? Theme.Loss
                  : Theme.Neutral;
        double w = pct is double p ? Math.Min(Math.Abs(p), FullScale) / FullScale * HalfWidth : 0;
        if (w > 0 && w < 1) w = 1;   // any visible gap gets at least a sliver
        return new(label, text.Length == 0 ? "—" : text, brush,
                   pct < 0 ? w : 0, pct > 0 ? w : 0);
    }
}

/// <summary>One line in a Stats "damage & healing vs. them" table.</summary>
public sealed class OutputGapView
{
    public OutputGapView(OutputGap g)
    {
        Label = g.Label;
        Games = g.Games;
        Record = g.Record;
        DpsPct = g.DpsPct;
        HpsPct = g.HpsPct;
        var dps = OutputLine.Make("DPS", g.DpsPct);
        var hps = OutputLine.Make("HPS", g.HpsPct);
        Dps = dps.Text; DpsBrush = dps.Brush;
        Hps = hps.Text; HpsBrush = hps.Brush;
        HpsTip = g.HpsGames == g.Games ? null
               : g.HpsGames == 0 ? "No like-for-like games: every game had a different number of healers on each side."
               : $"From {g.HpsGames} of {g.Games} games — the rest had a different number of healers on each side.";
    }

    public string Label { get; }
    public int Games { get; }
    public string Record { get; }
    public double? DpsPct { get; }
    public double? HpsPct { get; }
    public string Dps { get; }
    public Brush DpsBrush { get; }
    public string Hps { get; }
    public Brush HpsBrush { get; }
    public string? HpsTip { get; }
}

/// <summary>One line in a simple value table (e.g. MMR: Highest / Average / Lowest).</summary>
public sealed record InfoLine(string Label, string Value, string Note, Brush NoteBrush);

/// <summary>One line in a Stats table, optionally with our damage/healing vs. them.</summary>
public sealed class StatView
{
    public StatView(StatRow row, OutputGap? output = null)
    {
        Label = row.Label;
        Games = row.Games;
        Record = row.Record;
        WinRate = row.WinRate;
        WinPct = row.WinPct;
        Brush = Theme.ForWinRate(row.Games, row.WinRate);

        DpsPct = output?.DpsPct;
        HpsPct = output?.HpsPct;
        var dps = OutputLine.Make("DPS", DpsPct);
        var hps = OutputLine.Make("HPS", HpsPct);
        Dps = dps.Text; DpsBrush = dps.Brush;
        Hps = hps.Text; HpsBrush = hps.Brush;
        int dpsGames = output?.DpsGames ?? 0, hpsGames = output?.HpsGames ?? 0;
        DpsTip = dpsGames == 0 ? "No addon-recorded games against this comp yet."
               : dpsGames < Games ? $"From the {dpsGames} of {Games} games recorded by the addon." : null;
        HpsTip = dpsGames == 0 ? "No addon-recorded games against this comp yet."
               : hpsGames == 0 ? "No like-for-like games: every game had a different number of healers on each side."
               : hpsGames < Games ? $"From {hpsGames} of {Games} games: addon-recorded, with the same number of healers on each side."
               : null;
    }

    public string Label { get; }
    public int Games { get; }
    public string Record { get; }
    public double WinRate { get; }
    public string WinPct { get; }
    public Brush Brush { get; }

    public double? DpsPct { get; }
    public double? HpsPct { get; }
    public string Dps { get; }
    public Brush DpsBrush { get; }
    public string Hps { get; }
    public Brush HpsBrush { get; }
    public string? DpsTip { get; }
    public string? HpsTip { get; }
}
