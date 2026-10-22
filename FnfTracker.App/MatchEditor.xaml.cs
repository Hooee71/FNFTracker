using System.Globalization;
using System.Windows;
using System.Windows.Input;
using FnfTracker.Core;

namespace FnfTracker.App;

/// <summary>
/// Pop-up for fixing one game. Writes into the <see cref="Match"/> only when Save succeeds
/// (DialogResult true); the caller then saves the data file and refreshes.
/// Fields the editor doesn't show (scoreboard damage/healing, our MMR, source, recorded-by) are kept.
/// </summary>
public partial class MatchEditor : Window
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    private readonly Match _match;
    private readonly IReadOnlyList<Match> _all;

    public MatchEditor(Match match, string heading, IReadOnlyList<Match> allMatches)
    {
        InitializeComponent();
        _match = match;
        _all = allMatches;

        Title = "Edit game · " + heading;
        HeadingText.Text = heading;
        SourceText.Text = DescribeSource(match);
        MapBox.ItemsSource = GameData.Maps;

        WinRadio.IsChecked = match.Won;
        LossRadio.IsChecked = !match.Won;
        Enemy1.Slot = match.Enemies.ElementAtOrDefault(0) ?? new PlayerSlot();
        Enemy2.Slot = match.Enemies.ElementAtOrDefault(1) ?? new PlayerSlot();
        Our1.Slot = match.OurTeam.ElementAtOrDefault(0) ?? new PlayerSlot();
        Our2.Slot = match.OurTeam.ElementAtOrDefault(1) ?? new PlayerSlot();
        MapBox.Text = match.Map;
        RatingAfterBox.Text = match.RatingAfter?.ToString() ?? "";
        RatingChangeBox.Text = match.RatingChange?.ToString() ?? "";
        EnemyMmrBox.Text = match.EnemyMmr?.ToString() ?? "";
        DurationBox.Text = Stats.FormatDuration(match.DurationSeconds);
        TagsBox.Text = string.Join(", ", match.Tags);
        NotesBox.Text = match.Notes;
        PlayedAtBox.Text = match.PlayedAt.ToString(TimeFormat);

        Loaded += (_, _) =>
        {
            // Never taller than the screen; the fields scroll instead.
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            (match.Won ? WinRadio : LossRadio).Focus();
        };
    }

    private static string DescribeSource(Match m)
    {
        var source = m.Source switch
        {
            "addon" => "Recorded by the addon",
            "manual" => "Entered by hand",
            var other => "Imported from " + other,
        };
        if (!string.IsNullOrWhiteSpace(m.RecordedBy)) source += " · " + m.RecordedBy;
        return source;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.S: Save(); e.Handled = true; break;
            case Key.W: WinRadio.IsChecked = true; e.Handled = true; break;
            case Key.L: LossRadio.IsChecked = true; e.Handled = true; break;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (WinRadio.IsChecked != true && LossRadio.IsChecked != true)
        {
            Fail("Pick Win or Loss.");
            return;
        }

        if (!DateTime.TryParseExact(PlayedAtBox.Text.Trim(), TimeFormat, CultureInfo.CurrentCulture,
                                    DateTimeStyles.None, out var playedAt)
            && !DateTime.TryParse(PlayedAtBox.Text.Trim(), CultureInfo.CurrentCulture,
                                  DateTimeStyles.None, out playedAt))
        {
            Fail($"Couldn't read 'Played at'. Use {TimeFormat}, e.g. {DateTime.Now.ToString(TimeFormat)}.", PlayedAtBox);
            return;
        }

        if (!TryParseInt(RatingAfterBox.Text, out var ratingAfter))
        {
            Fail("'Rating after' should be a number, like 1840.", RatingAfterBox);
            return;
        }
        if (!TryParseInt(RatingChangeBox.Text, out var ratingChange))
        {
            Fail("'+/-' should be a number, like 14 or -11.", RatingChangeBox);
            return;
        }
        if (!TryParseInt(EnemyMmrBox.Text, out var enemyMmr))
        {
            Fail("'Their MMR' should be a number, like 1790.", EnemyMmrBox);
            return;
        }

        var duration = Stats.ParseDuration(DurationBox.Text);
        if (!string.IsNullOrWhiteSpace(DurationBox.Text) && duration is null)
        {
            Fail("Length: use 4:32, or 4.5 for minutes.", DurationBox);
            return;
        }

        // Work out +/- from the previous rated game if it was left blank.
        if (ratingChange is null && ratingAfter is int after)
        {
            var previous = _all
                .Where(m => m.Id != _match.Id && m.PlayedAt < playedAt && m.RatingAfter.HasValue)
                .OrderByDescending(m => m.PlayedAt)
                .FirstOrDefault();
            if (previous != null) ratingChange = after - previous.RatingAfter!.Value;
        }

        _match.PlayedAt = playedAt;
        _match.Won = WinRadio.IsChecked == true;
        _match.Map = MapBox.Text.Trim();
        _match.Enemies = new[] { Enemy1.Slot, Enemy2.Slot }.Where(p => !p.IsEmpty).ToList();
        _match.OurTeam = new[] { Our1.Slot, Our2.Slot }.Where(p => !p.IsEmpty).ToList();
        _match.RatingAfter = ratingAfter;
        _match.RatingChange = ratingChange;
        _match.EnemyMmr = enemyMmr;
        _match.DurationSeconds = duration;
        _match.Tags = TagsBox.Text.Split(',', ';').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        _match.Notes = NotesBox.Text.Trim();

        DialogResult = true;
    }

    private void Fail(string message, UIElement? field = null)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        field?.Focus();
    }

    private static bool TryParseInt(string text, out int? value)
    {
        value = null;
        text = text.Trim().Replace("+", "");
        if (text.Length == 0) return true;
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return false;
        value = v;
        return true;
    }
}
