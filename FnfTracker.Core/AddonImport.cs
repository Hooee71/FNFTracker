using System.Security.Cryptography;
using System.Text;

namespace FnfTracker.Core;

public sealed class AddonImportResult
{
    public int Added { get; set; }
    public int AlreadyHad { get; set; }
    public int NotTwoVTwo { get; set; }
    public int Unusable { get; set; }
    /// <summary>Games already here that got newly recorded details (e.g. player ratings) filled in.</summary>
    public int Backfilled { get; set; }
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
        if (Backfilled > 0) parts.Add($"player ratings filled in on {Backfilled} game(s)");
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
                if (imported.Contains(externalId))   // seen on an earlier import
                {
                    if (Backfill(store, rec)) { result.Backfilled++; result.Changed = true; }
                    continue;
                }

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

        // Scoreboard ratings are all 0 when the client hid them; then none of them mean anything.
        bool ratingsKnown = players.Any(p => p.Int("rating") > 0);

        var me = players.FirstOrDefault(p => p.Bool("isMe") == true);
        var myTeam = me?.Int("team") ?? rec.Int("myTeam");
        if (myTeam is null) { skip = SkipReason.NoTeam; return null; }

        var winner = rec.Int("winner");
        if (winner is not (0 or 1)) { skip = SkipReason.NoResult; return null; }

        var ours = players.Where(p => p.Int("team") == myTeam).ToList();
        var theirs = players.Where(p => p.Int("team") is int t && t != myTeam).ToList();
        if (theirs.Count == 0) { skip = SkipReason.NoTeam; return null; }

        // Backups for when the scoreboard hides a spec: enemies as seen at the gates,
        // and our side as read by the addon (your own spec + your partner's inspect data).
        var prep = rec.Tables("prep").ToList();
        var teamTable = rec.Table("team");
        var team = new[] { teamTable?.Table("me"), teamTable?.Table("partner") }.OfType<LuaTable>().ToList();
        var used = new HashSet<LuaTable>();
        bool SameClass(LuaTable x, string cls) =>
            string.Equals(GameData.ClassFromToken(x.Str("class")), cls, StringComparison.OrdinalIgnoreCase);
        PlayerSlot Slot(LuaTable p, bool enemy)
        {
            var cls = GameData.ClassFromToken(p.Str("class")) ?? p.Str("class") ?? "";
            var spec = p.Str("spec");
            if (string.IsNullOrWhiteSpace(spec))
            {
                var pool = enemy ? prep : team;
                var hit = (enemy ? null : pool.FirstOrDefault(x => !used.Contains(x) && SameName(x.Str("name"), p.Str("name")) && SameClass(x, cls)))
                          ?? pool.FirstOrDefault(x => !used.Contains(x) && SameClass(x, cls));
                if (hit != null) { used.Add(hit); spec = hit.Str("spec"); }
            }
            return new PlayerSlot
            {
                Class = cls,
                Spec = GameData.NormalizeSpec(cls, spec),
                Name = p.Str("name"),
                Damage = p.Long("damage"),
                Healing = p.Long("healing"),
                Rating = ratingsKnown ? p.Int("rating") : null,
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
        var enemyTeam = theirs[0].Int("team");
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
            DampeningPct = Positive(rec.Int("dampening")),
            RatingChange = rated ? me?.Int("ratingChange") : null,
            RatingAfter = rated ? rec.Int("ratingAfter2v2") : null,
            // Team MMR (addon 0.1.4+) is what the end-of-match screen shows. The scoreboard's
            // per-player prematchMMR is 0 in 2v2, so it's only a fallback, and 0 means "not recorded".
            OurMmr = rated ? TeamMmr(rec, myTeam) ?? Positive((me ?? ours.FirstOrDefault())?.Int("mmrBefore")) : null,
            EnemyMmr = rated ? TeamMmr(rec, enemyTeam) ?? theirs.Select(p => Positive(p.Int("mmrBefore"))).FirstOrDefault(v => v != null) : null,
        };
        if (!rated) match.Tags.Add("skirmish");
        return match;
    }

    private static int? Positive(int? v) => v > 0 ? v : null;

    /// <summary>
    /// Fills in player ratings on a game imported before the app kept them, matching players by
    /// name. Only fills blanks, so nothing edited by hand is overwritten. True if anything changed.
    /// </summary>
    private static bool Backfill(DataStore store, LuaTable rec)
    {
        var existing = store.Data.Matches.FirstOrDefault(m => m.Id == StableGuid(ExternalIdOf(rec)));
        if (existing == null) return false;   // deleted, or merged into another copy
        var fresh = ToMatch(rec, out _);
        if (fresh == null) return false;

        bool changed = false;
        void Fill(List<PlayerSlot> have, List<PlayerSlot> from)
        {
            foreach (var p in have.Where(p => p.Rating == null && !string.IsNullOrEmpty(p.Name)))
            {
                var src = from.FirstOrDefault(f => SameName(f.Name, p.Name));
                if (src?.Rating != null) { p.Rating = src.Rating; changed = true; }
            }
        }
        Fill(existing.Enemies, fresh.Enemies);
        Fill(existing.OurTeam, fresh.OurTeam);
        return changed;
    }

    /// <summary>A side's team MMR from the record's "teams" list (one entry per faction 0/1).</summary>
    private static int? TeamMmr(LuaTable rec, int? faction) =>
        faction is null ? null
            : Positive(rec.Tables("teams").FirstOrDefault(t => t.Int("faction") == faction)?.Int("mmr"));

    /// <summary>"Buddy" and "Buddy-Area52" are the same player; the scoreboard drops your own realm.</summary>
    private static bool SameName(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Contains('-') && b.Contains('-')) return false;
        return string.Equals(a.Split('-')[0], b.Split('-')[0], StringComparison.OrdinalIgnoreCase);
    }

    public static string ExternalIdOf(LuaTable rec) =>
        $"{rec.Str("recordedBy") ?? "unknown"}/{rec.Str("id") ?? rec.Long("startedAt")?.ToString() ?? "?"}";

    private static Guid StableGuid(string text) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes("fnf-addon:" + text)));
}
