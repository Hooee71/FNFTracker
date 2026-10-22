using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FnfTracker.App;

/// <summary>
/// The GitHub token used to publish the website. Kept out of fnf-data.json (which gets sent to your
/// partner) in its own file, encrypted with Windows DPAPI so only your Windows account can read it.
/// </summary>
public static class PublishToken
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FNFTracker.PublishToken.v1");

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FNFTracker", "publish-token.dat");

    public static bool Exists => File.Exists(FilePath);

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain).Trim();
            return token.Length == 0 ? null : token;
        }
        catch (Exception)
        {
            return null; // unreadable (e.g. copied from another PC): treat as not set
        }
    }

    public static void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, data);
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
