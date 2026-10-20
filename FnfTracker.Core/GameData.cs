namespace FnfTracker.Core;

/// <summary>
/// Classes, specs and arena maps. When Blizzard adds a spec or rotates the map
/// pool, this is the only file to edit. The map box in the app also accepts
/// any typed name, so a missing map never blocks entry.
/// </summary>
public static class GameData
{
    public sealed record WowClass(string Name, string ColorHex, string[] Specs);

    public static readonly IReadOnlyList<WowClass> Classes = new List<WowClass>
    {
        new("Death Knight", "#C41E3A", new[] { "Blood", "Frost", "Unholy" }),
        new("Demon Hunter", "#A330C9", new[] { "Havoc", "Vengeance", "Devourer" }),
        new("Druid",        "#FF7C0A", new[] { "Balance", "Feral", "Guardian", "Restoration" }),
        new("Evoker",       "#33937F", new[] { "Augmentation", "Devastation", "Preservation" }),
        new("Hunter",       "#AAD372", new[] { "Beast Mastery", "Marksmanship", "Survival" }),
        new("Mage",         "#3FC7EB", new[] { "Arcane", "Fire", "Frost" }),
        new("Monk",         "#00FF98", new[] { "Brewmaster", "Mistweaver", "Windwalker" }),
        new("Paladin",      "#F48CBA", new[] { "Holy", "Protection", "Retribution" }),
        new("Priest",       "#FFFFFF", new[] { "Discipline", "Holy", "Shadow" }),
        new("Rogue",        "#FFF468", new[] { "Assassination", "Outlaw", "Subtlety" }),
        new("Shaman",       "#0070DD", new[] { "Elemental", "Enhancement", "Restoration" }),
        new("Warlock",      "#8788EE", new[] { "Affliction", "Demonology", "Destruction" }),
        new("Warrior",      "#C69B6D", new[] { "Arms", "Fury", "Protection" }),
    };

    public static readonly IReadOnlyList<string> Maps = new List<string>
    {
        "Ashamane's Fall",
        "Black Rook Hold",
        "Blade's Edge Arena",
        "Cage of Carnage",
        "Dalaran Sewers",
        "Empyrean Domain",
        "Enigma Crucible",
        "Hook Point",
        "Maldraxxus Coliseum",
        "Mugambala",
        "Nagrand Arena",
        "Nokhudon Proving Grounds",
        "Ruins of Lordaeron",
        "The Robodrome",
        "Tiger's Peak",
        "Tol'viron Arena",
    };

    /// <summary>"DEATHKNIGHT" → "Death Knight" (WoW's classToken / classFile).</summary>
    public static string? ClassFromToken(string? token) =>
        token == null ? null :
        Classes.FirstOrDefault(c => string.Equals(c.Name.Replace(" ", ""), token, StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>Matches a spec name to our list (fixing case); unknown names pass through unchanged.</summary>
    public static string NormalizeSpec(string className, string? spec) =>
        spec == null ? "" :
        SpecsFor(className).FirstOrDefault(s => string.Equals(s, spec.Trim(), StringComparison.OrdinalIgnoreCase)) ?? spec.Trim();

    public static string[] SpecsFor(string? className) =>
        Classes.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase))?.Specs
        ?? Array.Empty<string>();

    public static string ColorFor(string? className) =>
        Classes.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase))?.ColorHex
        ?? "#BBBBBB";

    /// <summary>Healer specs, used to label comps as "double DPS" vs "DPS/healer".</summary>
    public static readonly IReadOnlySet<string> HealerSpecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Restoration Druid", "Preservation Evoker", "Mistweaver Monk", "Holy Paladin",
        "Discipline Priest", "Holy Priest", "Restoration Shaman",
    };

    public static bool IsHealer(PlayerSlot p) => HealerSpecs.Contains(p.Label);
}
