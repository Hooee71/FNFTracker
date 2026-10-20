using System.Security.Cryptography;
using System.Text;
using FnfTracker.Core;

public static class UpdaterCheck
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

        // Version parsing and comparison
        Check(Updater.ParseVersion("v0.5.0") == new Version(0, 5, 0), "parse v0.5.0");
        Check(Updater.ParseVersion("1.2.3-beta") == new Version(1, 2, 3), "parse with suffix");
        Check(Updater.ParseVersion("1.2") == new Version(1, 2, 0), "parse two-part");
        Check(Updater.ParseVersion("latest") == null, "reject non-version");
        Check(Updater.IsNewer(new Version(0, 5, 0), new Version(0, 4, 0, 0)), "0.5.0 newer than assembly 0.4.0.0");
        Check(!Updater.IsNewer(new Version(0, 4, 0), new Version(0, 4, 0, 0)), "same version is not newer");
        Check(Updater.IsNewer(new Version(0, 10, 0), new Version(0, 9, 9)), "0.10.0 > 0.9.9 (numeric, not text)");

        // GitHub release JSON
        const string json = """
        {
          "tag_name": "v0.5.0",
          "html_url": "https://github.com/Hooee71/FNFTracker/releases/tag/v0.5.0",
          "draft": false, "prerelease": false,
          "body": "- Stats range picker",
          "assets": [
            { "name": "FNFTracker.exe", "browser_download_url": "https://example.test/FNFTracker.exe" },
            { "name": "FNFTracker.exe.sha256", "browser_download_url": "https://example.test/FNFTracker.exe.sha256" }
          ]
        }
        """;
        var info = Updater.ParseRelease(json);
        Check(info != null && info.Version == new Version(0, 5, 0) && info.ExeUrl.EndsWith("FNFTracker.exe")
              && info.Sha256Url!.EndsWith(".sha256") && info.Notes.Contains("range"), "parse release");
        Check(Updater.ParseRelease("""{ "tag_name": "v0.6.0", "assets": [] }""") == null, "release without exe ignored");
        Check(Updater.ParseRelease("""{ "tag_name": "v0.6.0", "prerelease": true, "assets": [{ "name": "FNFTracker.exe", "browser_download_url": "x" }] }""") == null,
              "prerelease ignored");

        // Checksum + swap on real files
        var dir = Path.Combine(Path.GetTempPath(), "fnf-update-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var current = Path.Combine(dir, "FNFTracker.exe");
            var download = current + ".download";
            File.WriteAllText(current, "old build");
            File.WriteAllText(download, "new build");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("new build"))).ToLowerInvariant();

            Check(Updater.ChecksumMatches(download, hash), "checksum matches");
            Check(Updater.ChecksumMatches(download, hash.ToUpperInvariant() + "  FNFTracker.exe\n"), "checksum, sha256sum format");
            Check(!Updater.ChecksumMatches(download, new string('0', 64)), "checksum mismatch detected");

            Updater.SwapIn(download, current);
            Check(File.ReadAllText(current) == "new build" && File.ReadAllText(current + ".old") == "old build"
                  && !File.Exists(download), "swap: new in place, old kept as .old");

            Updater.CleanUpOldVersion(current);
            Check(!File.Exists(current + ".old"), "old version cleaned up");

            // Failed swap restores the original
            File.WriteAllText(current, "good build");
            bool threw = false;
            try { Updater.SwapIn(Path.Combine(dir, "missing.download"), current); } catch { threw = true; }
            Check(threw && File.ReadAllText(current) == "good build", "failed swap puts the original back");
        }
        finally
        {
            Directory.Delete(dir, true);
        }

        // Development builds never self-update
        var sep = Path.DirectorySeparatorChar;
        Check(Updater.IsDevelopmentBuild($"{sep}dev{sep}FNFTracker{sep}FnfTracker.App{sep}bin{sep}Release{sep}FNFTracker.exe"), "bin folder = dev build");
        Check(!Updater.IsDevelopmentBuild($"{sep}Games{sep}FNFTracker{sep}FNFTracker.exe"), "published exe can update");
        Check(Updater.IsDevelopmentBuild($"{sep}Games{sep}dotnet.exe"), "other host = dev build");

        return fails;
    }
}
