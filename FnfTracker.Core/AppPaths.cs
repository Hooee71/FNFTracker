using System.Runtime.InteropServices;

namespace FnfTracker.Core;

/// <summary>
/// Where the app keeps its files.
///  - GitHub download (unpackaged): %LOCALAPPDATA%\FNFTracker
///  - Microsoft Store (MSIX):       %LOCALAPPDATA%\Packages\&lt;package family name&gt;\LocalState
/// The Store package uses its own per-package folder rather than the shared one, because writing to the
/// shared folder would need the restricted unvirtualizedResources capability (which the Store denied).
/// Writes there go straight to disk (no virtualization), so "Saved at" and "Open data folder" show the real file.
/// </summary>
public static class AppPaths
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The folder the GitHub version uses (and the Store version used before it had its own).</summary>
    public static string LegacyDataFolder => Path.Combine(LocalAppData, "FNFTracker");

    /// <summary>Package family name when running from the Store package, otherwise null.</summary>
    public static string? PackageFamilyName { get; } = GetPackageFamilyName();

    public static bool IsPackaged => PackageFamilyName != null;

    public static string DataFolder { get; } = PackageFamilyName is { } pfn
        ? Path.Combine(LocalAppData, "Packages", pfn, "LocalState")
        : LegacyDataFolder;

    /// <summary>
    /// First run of the Store version on a PC that already had the GitHub version: copy its files over so
    /// no games are lost. The originals are left alone, so the GitHub copy still works if it's kept.
    /// </summary>
    public static void CopyLegacyFilesIfNeeded()
    {
        if (!IsPackaged || File.Exists(Path.Combine(DataFolder, "fnf-data.json"))) return;
        try
        {
            if (!Directory.Exists(LegacyDataFolder)) return;
            Directory.CreateDirectory(DataFolder);
            foreach (var name in new[] { "fnf-data.json", "fnf-data.bak.json", "publish-token.dat" })
            {
                var from = Path.Combine(LegacyDataFolder, name);
                var to = Path.Combine(DataFolder, name);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }
        }
        catch (Exception)
        {
            // Not fatal: the app just starts with an empty history, and Import can bring the old file in.
        }
    }

    private static string? GetPackageFamilyName()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            uint length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer) return null; // not packaged
            var buffer = new char[length];
            if (GetCurrentPackageFamilyName(ref length, buffer) != 0) return null;
            return new string(buffer, 0, (int)length - 1); // length includes the terminating null
        }
        catch (Exception)
        {
            return null; // very old Windows without the API
        }
    }

    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, [Out] char[]? packageFamilyName);
}
