using System.Text.Json.Serialization;

namespace FnfTracker.Core;

/// <summary>One player in a match: class + spec, and optionally their Name-Realm.</summary>
public sealed class PlayerSlot
{
    public string Class { get; set; } = "";
    public string Spec { get; set; } = "";
    public string? Name { get; set; }

    /// <summary>From the addon's scoreboard read, when available.</summary>
    public long? Damage { get; set; }
    public long? Healing { get; set; }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Class);

    /// <summary>"Subtlety Rogue", or just "Rogue" when the spec is unknown.</summary>
    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Spec) ? Class : $"{Spec} {Class}";

    public PlayerSlot Clone() => new() { Class = Class, Spec = Spec, Name = Name, Damage = Damage, Healing = Healing };
}

public sealed class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Local time the match was played.</summary>
    public DateTime PlayedAt { get; set; } = DateTime.Now;

    public bool Won { get; set; }
    public string Map { get; set; } = "";

    public List<PlayerSlot> OurTeam { get; set; } = new();
    public List<PlayerSlot> Enemies { get; set; } = new();

    /// <summary>Your team rating after the match, if you noted it.</summary>
    public int? RatingAfter { get; set; }

    /// <summary>Rating gained or lost (+14 / -11).</summary>
    public int? RatingChange { get; set; }

    public int? DurationSeconds { get; set; }

    /// <summary>Team matchmaking ratings from the scoreboard (addon imports only).</summary>
    public int? OurMmr { get; set; }
    public int? EnemyMmr { get; set; }

    public List<string> Tags { get; set; } = new();
    public string Notes { get; set; } = "";

    /// <summary>"manual", or the name of an import source.</summary>
    public string Source { get; set; } = "manual";

    /// <summary>For addon imports: "Character-Realm/addon-id", used to avoid importing twice.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Which player's copy of the app recorded it (helps when merging files).</summary>
    public string? RecordedBy { get; set; }

    /// <summary>
    /// The arena "night" this match belongs to. Games before 6 AM count toward
    /// the previous evening, so a Friday session running past midnight stays Friday.
    /// </summary>
    [JsonIgnore]
    public DateOnly Night => DateOnly.FromDateTime(PlayedAt.AddHours(-6));

    [JsonIgnore]
    public string EnemyCompKey => Comp.Key(Enemies);
    [JsonIgnore]
    public string OurCompKey => Comp.Key(OurTeam);
}

public sealed class AppSettings
{
    /// <summary>Who is running this copy of the app (stamped on matches as RecordedBy).</summary>
    public string MyName { get; set; } = "";

    /// <summary>check-pvp.fr region for enemy profile links: "us", "eu", "kr" or "tw".</summary>
    public string Region { get; set; } = CheckPvp.DefaultRegion;

    /// <summary>
    /// Your realm. The scoreboard leaves the realm off players on your own realm, so their
    /// profile links use this when the game doesn't say who recorded it.
    /// </summary>
    public string? HomeRealm { get; set; }

    /// <summary>Start of the current PvP season, for the "This season" range and the header record.</summary>
    public DateTime? SeasonStart { get; set; }

    /// <summary>The Stats tab's last range choice, and its custom dates.</summary>
    public RangeKind StatsRange { get; set; } = RangeKind.AllTime;
    public DateTime? StatsFrom { get; set; }
    public DateTime? StatsTo { get; set; }

    /// <summary>WoW install folder (the one containing _retail_, or _retail_ itself).</summary>
    public string? WowFolder { get; set; }

    /// <summary>Addon games already imported — so a game you delete doesn't come back next import.</summary>
    public List<string> ImportedAddonIds { get; set; } = new();

    /// <summary>Look for a newer release on GitHub each time the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Where the window was last time the app closed.</summary>
    public WindowPlacement? Window { get; set; }

    /// <summary>Matches table column widths, order and sort, as the user left them.</summary>
    public List<ColumnLayout>? MatchColumns { get; set; }

    /// <summary>
    /// GitHub repo ("owner/name") the public website is published to. The access token is NOT kept
    /// here (this file is shared with your partner); it's stored encrypted in its own file.
    /// </summary>
    public string? PublishRepo { get; set; }

    /// <summary>Publish the website by itself after the addon brings in new games.</summary>
    public bool AutoPublish { get; set; }

    /// <summary>When the website was last published.</summary>
    public DateTime? LastPublishedAt { get; set; }
}

public sealed class ColumnLayout
{
    /// <summary>Column header text — the stable key. Columns that no longer exist are ignored.</summary>
    public string Header { get; set; } = "";
    public int DisplayIndex { get; set; }
    /// <summary>Width value; its meaning depends on <see cref="WidthUnit"/>.</summary>
    public double Width { get; set; }
    /// <summary>"Pixel", "Star" or "Auto" (Auto keeps the column's default sizing).</summary>
    public string WidthUnit { get; set; } = "Auto";
    /// <summary>"Ascending" or "Descending" on the sorted column; null on the others.</summary>
    public string? Sort { get; set; }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

public sealed class FnfData
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
}

public static class Comp
{
    /// <summary>Order-independent key for a team, e.g. "Holy Paladin + Subtlety Rogue".</summary>
    public static string Key(IEnumerable<PlayerSlot> team)
    {
        var labels = team.Where(p => !p.IsEmpty)
                         .Select(p => p.Label)
                         .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        return labels.Count == 0 ? "(unknown)" : string.Join(" + ", labels);
    }
}
