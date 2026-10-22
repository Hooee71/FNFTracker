using System.Text.Json;
using System.Text.Json.Serialization;

namespace FnfTracker.Core;

/// <summary>
/// Saves everything to one JSON file. No database to install, the file is easy
/// to back up, and two players can swap files and merge them.
/// Default location: %LOCALAPPDATA%\FNFTracker\fnf-data.json
/// </summary>
public sealed class DataStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FilePath { get; }
    public FnfData Data { get; private set; } = new();

    public DataStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FNFTracker", "fnf-data.json");
    }

    public void Load()
    {
        Data = File.Exists(FilePath) ? ReadFile(FilePath) : new FnfData();
    }

    /// <summary>
    /// Writes to a temp file then swaps it in, keeping the previous version as
    /// fnf-data.bak.json, so a crash mid-save can't wipe your history.
    /// </summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Data, JsonOptions));

        if (File.Exists(FilePath))
        {
            var backup = Path.Combine(dir, Path.GetFileNameWithoutExtension(FilePath) + ".bak.json");
            File.Replace(temp, FilePath, backup);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    public void Upsert(Match match)
    {
        var i = Data.Matches.FindIndex(m => m.Id == match.Id);
        if (i >= 0) Data.Matches[i] = match; else Data.Matches.Add(match);
        Data.Matches.Sort((a, b) => a.PlayedAt.CompareTo(b.PlayedAt));
    }

    public bool Delete(Guid id) => Data.Matches.RemoveAll(m => m.Id == id) > 0;

    public void ExportTo(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Data, JsonOptions));

    public static FnfData ReadFile(string path) =>
        JsonSerializer.Deserialize<FnfData>(File.ReadAllText(path), JsonOptions) ?? new FnfData();

    /// <summary>
    /// Merges another player's export into this file. A match is skipped if its Id
    /// is already here, or if it looks like the same game logged separately: same
    /// result and enemy classes, played within 10 minutes. Settings are not touched.
    /// </summary>
    public MergeResult MergeFrom(FnfData other)
    {
        int added = 0, duplicates = 0;
        foreach (var incoming in other.Matches)
        {
            if (Data.Matches.Any(m => m.Id == incoming.Id || IsSameGame(m, incoming)))
            {
                duplicates++;
                continue;
            }
            Data.Matches.Add(incoming);
            added++;
        }
        Data.Matches.Sort((a, b) => a.PlayedAt.CompareTo(b.PlayedAt));
        return new MergeResult(added, duplicates);
    }

    /// <summary>
    /// Same result, same enemy classes, within 10 minutes. Compares classes rather than
    /// specs so a hand-entered game without specs still matches the addon's copy.
    /// </summary>
    public static bool IsSameGame(Match a, Match b) =>
        a.Won == b.Won
        && Math.Abs((a.PlayedAt - b.PlayedAt).TotalMinutes) <= 10
        && EnemyClassKey(a) == EnemyClassKey(b);

    private static string EnemyClassKey(Match m) =>
        string.Join("+", m.Enemies.Where(e => !e.IsEmpty)
                                  .Select(e => e.Class.ToLowerInvariant())
                                  .OrderBy(c => c, StringComparer.Ordinal));
}

public readonly record struct MergeResult(int Added, int Duplicates);
