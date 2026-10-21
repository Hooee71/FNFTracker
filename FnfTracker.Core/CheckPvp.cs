using System.Text;

namespace FnfTracker.Core;

/// <summary>
/// Links to a player's profile on check-pvp.fr:
/// https://check-pvp.fr/{region}/{Realm Name}/{Character}, e.g. /us/Area%2052/Bonk.
/// The site wants the realm's display name ("Area 52", "Blade's Edge"), not the
/// squashed form the arena scoreboard gives ("Area52", "Blade'sEdge").
/// </summary>
public static class CheckPvp
{
    public const string BaseUrl = "https://check-pvp.fr";
    public const string DefaultRegion = "us";

    /// <summary>Regions check-pvp.fr serves, as they appear in its URLs.</summary>
    public static readonly IReadOnlyList<string> Regions = new[] { "us", "eu", "kr", "tw" };

    /// <summary>
    /// Profile URL for a scoreboard name. <paramref name="player"/> is "Name" or "Name-Realm";
    /// the scoreboard leaves the realm off for players on your own realm, so
    /// <paramref name="defaultRealm"/> fills that in. Null when there's no name or no realm.
    /// </summary>
    public static string? ProfileUrl(string? player, string? defaultRealm, string? region)
    {
        var (name, realm) = Split(player);
        if (name is null) return null;
        realm ??= Clean(defaultRealm);
        if (realm is null) return null;

        var r = NormalizeRegion(region);
        return $"{BaseUrl}/{r}/{Uri.EscapeDataString(DisplayRealm(realm))}/{Uri.EscapeDataString(name)}";
    }

    /// <summary>The realm half of "Name-Realm", or null if there isn't one.</summary>
    public static string? RealmOf(string? player) => Split(player).Realm;

    /// <summary>"us" for anything that isn't a known region.</summary>
    public static string NormalizeRegion(string? region)
    {
        var r = region?.Trim().ToLowerInvariant();
        return r != null && Regions.Contains(r) ? r : DefaultRegion;
    }

    /// <summary>
    /// "Area52" → "Area 52", "Blade'sEdge" → "Blade's Edge". Also accepts the display name itself
    /// in any case or spacing ("area 52"). Unknown realms get spaces inserted at word breaks.
    /// </summary>
    public static string DisplayRealm(string realm)
    {
        realm = realm.Trim();
        if (RealmNames.Slugs.TryGetValue(realm, out var exact)) return exact;
        if (Loose.Value.TryGetValue(LooseKey(realm), out var loose)) return loose;
        return realm.Contains(' ') ? realm : SpaceOut(realm);
    }

    // Character names can't contain '-', so the first one splits name from realm.
    private static (string? Name, string? Realm) Split(string? player)
    {
        var s = Clean(player);
        if (s is null) return (null, null);
        var dash = s.IndexOf('-');
        if (dash < 0) return (s, null);
        return (Clean(s[..dash]), Clean(s[(dash + 1)..]));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Case, spaces and hyphens ignored: "Area 52", "area52" and "Area52" all match.
    private static string LooseKey(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c) && c != '-') sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static readonly Lazy<Dictionary<string, string>> Loose = new(() =>
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, display) in RealmNames.Slugs)
        {
            d.TryAdd(LooseKey(key), display);
            d.TryAdd(LooseKey(display), display);
        }
        return d;
    });

    // "MoonGuard" → "Moon Guard", "Area52" → "Area 52".
    private static string SpaceOut(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0)
            {
                var p = s[i - 1];
                if ((char.IsUpper(c) && char.IsLower(p)) || (char.IsDigit(c) && char.IsLetter(p)))
                    sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
