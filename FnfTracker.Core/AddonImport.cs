using System.Security.Cryptography;
using System.Text;

namespace FnfTracker.Core;

public sealed class AddonImportResult
{
    public int Added { get; set; }
    public int AlreadyHad { get; set; }
    public int NotTwoVTwo { get; set; }
    public int Unusable { get; set; }
    public List<string> FilesRead { get; } = new();
    public List<string> Problems { get; } = new();
    public bool Changed { get; set; }

    public string Summary()
    {
        if (FilesRead.Count == 0) return "No FNF addon data found yet.";
        var parts = new List<string> { $"{Added} new game(s) imported" };
        if (AlreadyHad > 0) parts.Add($"{AlreadyHad} already here");
        if (NotTwoVTwo > 0) parts.Add($"{NotTwoVTwo} non-2v2 skipped");
        if (Unusable > 0) parts.Add($"{Unusable} incomplete (left early?) skipped");
        return string.Join(", ", parts) + ".";
    }
}

/// <summary>
/// Imports games recorded by the FNF Tracker addon from
/// WTF\Account\&lt;account&gt;\SavedVariables\FNFTracker.lua.
/// </summary>
public static class AddonImport
{
    public const string SavedVariablesFile = "FNFTracker.lua";
    public const string DefaultWowFolder = @"C:\Program Files (x86)\World of Warcraft";

    /// <summary>Finds FNFTracker.lua for every account under the WoW folder (or its _retail_ subfolder).</summary>
    public static List<string> FindSavedVariables(string? wowFolder)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(wowFolder) || !Directory.Exists(wowFolder)) return found;

        foreach (var root in new[] { wowFolder, Path.Combine(wowFolder, "_retail_") })
        {
            var accounts = Path.Combine(root, "WTF", "Account");
            if (!Directory.Exists(accounts)) continue;
            foreach (var account in Directory.GetDirectories(accounts))
            {
                var file = Path.Combine(account, "SavedVariables", SavedVariablesFile);
                if (File.Exists(file)) found.Add(Path.GetFullPath(file));
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The folder WoW SavedVariables are watched in, for each account (for a FileSystemWatcher).</summary>
    public static List<string> FindSavedVariablesFolders(string? wowFolder)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(wowFolder) || !Directory.Exists(wowFolder)) return found;
        foreach (var root in new[] { wowFolder, Path.Combine(wowFolder, "_retail_") })
        {
            var accounts = Path.Combine(root, "WTF", "Account");
            if (!Directory.Exists(accounts)) continue;
            foreach (var account in Directory.GetDirectories(accounts))
            {
                var sv = Path.Combine(account, "SavedVariables");
                if (Directory.Exists(sv)) found.Add(sv);
            }
        }
        return found;
    }

    public static AddonImportResult ImportInto(DataStore store, string? wowFolder) =>
        ImportFiles(store, FindSavedVariables(wowFolder));

    public static AddonImportResult ImportFiles(DataStore store, IEnumerable<string> files)
    {
        var result = new AddonImportResult();
        var imported = new HashSet<string>(store.Data.Settings.ImportedAddonIds, StringComparer.Ordinal);

        foreach (var file in files)
        {
            List<LuaTable> records;
            try
            {
                records = ReadRecords(ReadShared(file));
                result.FilesRead.Add(file);
            }
            catch (Exception ex)
            {
                result.Problems.Add($"{file}: {ex.Message}");
                continue;
            }

            foreach (var rec in records)
            {
                var externalId = ExternalIdOf(rec);
                if (imported.Contains(externalId)) continue;   // seen on an earlier import

                var match = ToMatch(rec, out var skip);
                if (match == null)
                {
                    if (skip == SkipReason.NotTwoVTwo)
                    {
                        // 3v3 / Solo Shuffle: never going to be 2v2, so stop looking at it.
                        result.NotTwoVTwo++;
                        imported.Add(externalId);
                        result.Changed = true;
                    }
                    else
                    {
                        result.Unusable++;
                    }
                    continue;
                }

                if (store.Data.Matches.Any(m => m.Id == match.Id))
                {
                    result.AlreadyHad++;
                    imported.Add(externalId);
                    result.Changed = true;
                    continue;
                }

                imported.Add(match.ExternalId!);
                result.Changed = true;

                // Already logged by hand, or by your partner's addon and merged in.
                if (store.Data.Matches.Any(m => DataStore.IsSameGame(m, match)))
                {
                    result.AlreadyHad++;
                    continue;
                }

                store.Upsert(match);
                result.Added++;
            }
        }

        if (result.Changed) store.Data.Settings.ImportedAddonIds = imported.ToList();
        return result;
    }

    /// <summary>Reads a file WoW may be writing at the same moment.</summary>
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static List<LuaTable> ReadRecords(string savedVariablesText)
    {
        var globals = LuaSavedVariablesReader.Parse(savedVariablesText);
        if (globals.GetValueOrDefault("FNFTrackerDB") is not LuaTable db) return new();
        return db.Tables("matches").ToList();
    }

    public enum SkipReason { None, NotTwoVTwo, NoResult, NoTeam }

    public static Match? ToMatch(LuaTable rec, out SkipReason skip)
    {
        skip = SkipReason.None;
        var players = rec.Tables("players").ToList();
        if (players.Count != 4) { skip = SkipReason.NotTwoVTwo; return null; }

        var me = players.FirstOrDefault(p => p.Bool("isMe") == true);
        var myTeam = me?.Int("team") ?? rec.Int("myTeam");
        if (myTeam is null) { skip = SkipReason.NoTeam; return null; }

        var winner = rec.Int("winner");
        if (winner is not (0 or 1)) { skip = SkipReason.NoResult; return null; }

        var ours = players.Where(p => p.Int("team") == myTeam).ToList();
        var theirs = players.Where(p => p.Int("team") is int t && t != myTeam).ToList();
        if (theirs.Count == 0) { skip = SkipReason.NoTeam; return null; }

        var prep = rec.Tables("prep").ToList();
        var usedPrep = new HashSet<LuaTable>();
        PlayerSlot Slot(LuaTable p, bool enemy)
        {
            var cls = GameData.ClassFromToken(p.Str("class")) ?? p.Str("class") ?? "";
            var spec = p.Str("spec");
            if (string.IsNullOrWhiteSpace(spec) && enemy)
            {
                // Scoreboard hid the spec: fall back to what we saw at the gates.
                var hit = prep.FirstOrDefault(x => !usedPrep.Contains(x)
                    && string.Equals(GameData.ClassFromToken(x.Str("class")), cls, StringComparison.OrdinalIgnoreCase));
                if (hit != null) { usedPrep.Add(hit); spec = hit.Str("spec"); }
            }
            return new PlayerSlot
            {
                Class = cls,
                Spec = GameData.NormalizeSpec(cls, spec),
                Name = p.Str("name"),
                Damage = p.Long("damage"),
                Healing = p.Long("healing"),
            };
        }

        var recordedBy = rec.Str("recordedBy") ?? "unknown";
        var externalId = ExternalIdOf(rec);

        // Played-at uses the END of the match so it lines up with games typed in right after.
        var ended = rec.Long("endedAt") ?? rec.Long("startedAt");
        var playedAt = ended is long secs
            ? DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime
            : DateTime.Now;

        int? duration = rec.Int("duration");
        if (duration > 4 * 3600) duration /= 1000;   // in case the client reports milliseconds
        if (duration <= 0) duration = null;

        bool rated = rec.Bool("rated") == true;
        var match = new Match
        {
            Id = StableGuid(externalId),
            ExternalId = externalId,
            Source = "addon",
            RecordedBy = recordedBy,
            PlayedAt = playedAt,
            Won = winner == myTeam,
            Map = rec.Str("map") ?? "",
            OurTeam = ours.Select(p => Slot(p, enemy: false)).ToList(),
            Enemies = theirs.Select(p => Slot(p, enemy: true)).ToList(),
            DurationSeconds = duration,
            RatingChange = rated ? me?.Int("ratingChange") : null,
            RatingAfter = rated ? rec.Int("ratingAfter2v2") : null,
            OurMmr = rated ? (me ?? ours.FirstOrDefault())?.Int("mmrBefore") : null,
            EnemyMmr = rated ? theirs.Select(p => p.Int("mmrBefore")).FirstOrDefault(v => v != null) : null,
        };
        if (!rated) match.Tags.Add("skirmish");
        return match;
    }

    public static string ExternalIdOf(LuaTable rec) =>
        $"{rec.Str("recordedBy") ?? "unknown"}/{rec.Str("id") ?? rec.Long("startedAt")?.ToString() ?? "?"}";

    private static Guid StableGuid(string text) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes("fnf-addon:" + text)));
}
