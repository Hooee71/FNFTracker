using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FnfTracker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        Resources["HeadingFont"] = ResolveHeadingFont();
        base.OnStartup(e);
    }

    /// <summary>
    /// WoW's UI font is Friz Quadrata, a commercial font we can't ship. Use it if it's
    /// installed on this PC; otherwise use the bundled Marcellus, a free font in the same style.
    /// </summary>
    private static FontFamily ResolveHeadingFont()
    {
        try
        {
            var friz = Fonts.SystemFontFamilies.FirstOrDefault(f =>
                f.Source.Contains("Friz Quadrata", StringComparison.OrdinalIgnoreCase)
                || f.FamilyNames.Values.Any(n => n.Contains("Friz Quadrata", StringComparison.OrdinalIgnoreCase)));
            if (friz != null) return friz;
        }
        catch
        {
            // A broken system font shouldn't stop the app; fall through to the bundled font.
        }
        return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Marcellus");
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Something went wrong:\n\n" + e.Exception.Message +
            "\n\nYour saved matches are safe. The app will keep running.",
            "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
