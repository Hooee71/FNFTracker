namespace FnfTracker.Core;

/// <summary>Which games the Stats tab shows.</summary>
public enum RangeKind { AllTime, Season, SingleNight, Last7Days, Last30Days, Custom }

/// <summary>
/// Turns a range choice into first/last arena nights (inclusive). Everything works on
/// "nights" (6 AM to 6 AM), so a Friday session that runs past midnight stays on Friday.
/// A null bound means unbounded on that side.
/// </summary>
public static class StatsRange
{
    public static readonly IReadOnlyList<(RangeKind Kind, string Label)> Options = new[]
    {
        (RangeKind.AllTime, "All time"),
        (RangeKind.Season, "This season"),
        (RangeKind.SingleNight, "Single night"),
        (RangeKind.Last7Days, "Last 7 days"),
        (RangeKind.Last30Days, "Last 30 days"),
        (RangeKind.Custom, "Custom range"),
    };

    /// <summary>The arena night "now" belongs to.</summary>
    public static DateOnly CurrentNight(DateTime now) => DateOnly.FromDateTime(now.AddHours(-6));

    public static (DateOnly? From, DateOnly? To) Resolve(
        RangeKind kind, DateOnly today, DateTime? seasonStart,
        DateOnly? singleNight, DateOnly? customFrom, DateOnly? customTo)
    {
        switch (kind)
        {
            case RangeKind.Season:
                return (seasonStart is DateTime s ? DateOnly.FromDateTime(s) : null, null);
            case RangeKind.SingleNight:
                var night = singleNight ?? today;
                return (night, night);
            case RangeKind.Last7Days:
                return (today.AddDays(-6), today);
            case RangeKind.Last30Days:
                return (today.AddDays(-29), today);
            case RangeKind.Custom:
                // Forgive a backwards range rather than showing nothing.
                if (customFrom is DateOnly f && customTo is DateOnly t && f > t) return (t, f);
                return (customFrom, customTo);
            default:
                return (null, null);
        }
    }

    public static IEnumerable<Match> Apply(IEnumerable<Match> matches, DateOnly? from, DateOnly? to) =>
        matches.Where(m => (from is null || m.Night >= from) && (to is null || m.Night <= to));

    /// <summary>Most recent night with games, or null if there are none.</summary>
    public static DateOnly? LatestNight(IEnumerable<Match> matches) =>
        matches.Select(m => (DateOnly?)m.Night).Max();

    /// <summary>The nearest night with games before (direction -1) or after (+1) the given one.</summary>
    public static DateOnly? StepNight(IEnumerable<Match> matches, DateOnly from, int direction)
    {
        var nights = matches.Select(m => m.Night).Distinct();
        return direction < 0
            ? nights.Where(n => n < from).Select(n => (DateOnly?)n).Max()
            : nights.Where(n => n > from).Select(n => (DateOnly?)n).Min();
    }

    /// <summary>"Fri Oct 16", "Oct 10 – Oct 16", "Since Sep 1", "Up to Oct 16", or "All games".</summary>
    public static string Describe(DateOnly? from, DateOnly? to)
    {
        static string D(DateOnly d) => d.ToString("MMM d");
        if (from is DateOnly f && to is DateOnly t)
            return f == t ? f.ToString("ddd MMM d") : $"{D(f)} – {D(t)}";
        if (from is DateOnly f2) return $"Since {D(f2)}";
        if (to is DateOnly t2) return $"Up to {D(t2)}";
        return "All games";
    }
}
