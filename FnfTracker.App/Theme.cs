using System.Windows.Media;
using FnfTracker.Core;

namespace FnfTracker.App;

public static class Theme
{
    public static readonly Brush Win = Make("#4CC38A");
    public static readonly Brush Loss = Make("#E5534B");
    public static readonly Brush Neutral = Make("#D8DAE0");
    public static readonly Brush Dim = Make("#8A8F98");
    public static readonly Brush Accent = Make("#E6B450");
    public static readonly Brush Grid = Make("#33FFFFFF");

    private static readonly Dictionary<string, Brush> ClassBrushes =
        GameData.Classes.ToDictionary(c => c.Name, c => Make(c.ColorHex), StringComparer.OrdinalIgnoreCase);

    public static Brush ForClass(string? className) =>
        className != null && ClassBrushes.TryGetValue(className, out var b) ? b : Dim;

    /// <summary>Green for good matchups, red for bad; grey until there are 3+ games to judge.</summary>
    public static Brush ForWinRate(int games, double winRate) =>
        games < 3 ? Dim : winRate >= 0.6 ? Win : winRate <= 0.4 ? Loss : Neutral;

    public static Brush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
