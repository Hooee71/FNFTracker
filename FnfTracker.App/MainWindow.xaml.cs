using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FnfTracker.Core;
using Microsoft.Win32;

namespace FnfTracker.App;

public partial class MainWindow : Window
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    private readonly DataStore _store = new();
    private bool _loadFailed;
    private bool _busy;               // suppresses event handlers while we set controls from code
    private Guid? _editingId;
    private List<RatingPoint> _ratingPoints = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private ImageSource? _matchesArt, _statsArt;
    private DateOnly? _singleNight;   // the night shown in "Single night" mode
    private readonly Version _currentVersion =
        Updater.Normalize(typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0));
    private UpdateInfo? _pendingUpdate;
    private bool _updating;
    private bool _skipSaveOnClose;    // set while handing over to a freshly installed update
    private readonly DispatcherTimer _importDebounce = new() { Interval = TimeSpan.FromSeconds(2) };

    private AppSettings Settings => _store.Data.Settings;
    private List<Match> AllMatches => _store.Data.Matches;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _store.Load();
        }
        catch (Exception ex)
        {
            // Never overwrite a file we couldn't read — the user may be able to fix it.
            _loadFailed = true;
            MessageBox.Show(
                $"Couldn't read your data file:\n{_store.FilePath}\n\n{ex.Message}\n\n" +
                "Nothing will be saved until this is fixed. A backup may exist next to it (fnf-data.bak.json).",
                "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _matchesArt = LoadArt("mascots.png");
        _statsArt = LoadArt("mascots-stats.png");
        UpdateMascotArt();

        RestoreWindowPlacement();
        Closing += (_, _) => SaveWindowPlacement();
        Loaded += (_, _) => Window_StateChanged(this, EventArgs.Empty); // sync title bar if we start maximized

        MapBox.ItemsSource = GameData.Maps;
        DataPathText.Text = "Saved at: " + _store.FilePath;
        LoadSettingsIntoUi();
        ClearForm();
        Refresh();
        Loaded += (_, _) => Enemy1.FocusClass();

        // Addon import: once now, then whenever WoW rewrites the SavedVariables file.
        if (string.IsNullOrWhiteSpace(Settings.WowFolder) && Directory.Exists(AddonImport.DefaultWowFolder))
            Settings.WowFolder = AddonImport.DefaultWowFolder;
        WowFolderBox.Text = Settings.WowFolder ?? "";
        _importDebounce.Tick += (_, _) =>
        {
            _importDebounce.Stop();
            RunAddonImport(auto: true);
        };
        RunAddonImport(auto: true);
        StartWatching();
        Closed += (_, _) => StopWatching();

        // Updates: tidy up after a previous update, then check GitHub in the background.
        if (Environment.ProcessPath is string exe) Updater.CleanUpOldVersion(exe);
        VersionText.Text = $"You're running FNF Tracker {_currentVersion.ToString(3)}.";
        Loaded += async (_, _) =>
        {
            if (Settings.CheckForUpdates) await CheckForUpdatesAsync(userAsked: false);
        };
    }

    // ───────────────────────── Per-tab art ─────────────────────────

    private static ImageSource LoadArt(string file)
    {
        var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Assets/{file}"));
        bitmap.Freeze();
        return bitmap;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection changes inside a tab (the match list, combo boxes) bubble up here too; ignore them.
        if (!ReferenceEquals(e.OriginalSource, Tabs)) return;
        UpdateMascotArt();
    }

    /// <summary>Matches: bull and fox. Stats: zombie and orc. Settings: no art.</summary>
    private void UpdateMascotArt()
    {
        if (MascotArt == null) return; // fires while the window is still being built
        ImageSource? art = Tabs.SelectedIndex switch
        {
            0 => _matchesArt,
            1 => _statsArt,
            _ => null,
        };
        MascotArt.Source = art;
        MascotArt.Visibility = art == null ? Visibility.Collapsed : Visibility.Visible;
        if (MascotScrim != null) MascotScrim.Visibility = MascotArt.Visibility;
    }

    // ───────────────────────── Custom title bar ─────────────────────────

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaxRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;
        MaxButton.Content = max ? "\uE923" : "\uE922";   // restore / maximize glyphs
        MaxButton.ToolTip = max ? "Restore" : "Maximize";
        // A maximized custom-chrome window hangs past the screen edge by the resize border; pad it back in.
        RootGrid.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    // ───────────────────────── Updates ─────────────────────────

    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        UpdateStatusText.Text = "Checking for updates…";
        try
        {
            using var http = Updater.CreateClient(_currentVersion);
            var update = await Updater.CheckAsync(http, _currentVersion);
            if (update == null)
            {
                _pendingUpdate = null;
                UpdateBanner.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = $"You're up to date ({_currentVersion.ToString(3)}). Checked {DateTime.Now:h:mm tt}.";
                if (userAsked) Status("You're on the latest version.");
                return;
            }

            _pendingUpdate = update;
            UpdateText.Text = $"Update available: {update.Tag}";
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateStatusText.Text = $"{update.Tag} is available. Use \"Update now\" at the bottom of the window.";
        }
        catch (Exception ex)
        {
            // Offline, GitHub down, rate-limited: never bother the user unless they asked.
            UpdateStatusText.Text = "Couldn't check for updates: " + ex.Message;
            if (userAsked) Status("Couldn't check for updates. See Settings.");
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not UpdateInfo update || _updating) return;

        var exe = Environment.ProcessPath;
        if (exe == null || Updater.IsDevelopmentBuild(exe))
        {
            MessageBox.Show("This copy was built from source (Visual Studio or dotnet run), so it won't replace itself.\n\n" +
                            "Run 'git pull' and rebuild, or download the new FNFTracker.exe from the releases page.",
                            "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            OpenUrl(update.PageUrl);
            return;
        }

        _updating = true;
        UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = false;
        try
        {
            using var http = Updater.CreateClient(_currentVersion);
            var progress = new Progress<double>(p => UpdateText.Text = $"Downloading {update.Tag}… {p:P0}");
            var downloaded = await Updater.DownloadAsync(http, update, exe, progress);

            // Save now, then hand over: the new copy must not start while this one is still writing.
            SaveWindowPlacement();
            _skipSaveOnClose = true;
            StopWatching();

            Updater.SwapIn(downloaded, exe);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (UnauthorizedAccessException)
        {
            _skipSaveOnClose = false;
            StartWatching();
            MessageBox.Show($"Windows wouldn't let the app replace itself in:\n{Path.GetDirectoryName(exe)}\n\n" +
                            "Move FNFTracker.exe to a folder you own (for example C:\\Games\\FNFTracker) and try again, " +
                            "or download the update from the releases page.",
                            "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenUrl(update.PageUrl);
        }
        catch (Exception ex)
        {
            _skipSaveOnClose = false;
            StartWatching();
            MessageBox.Show("The update didn't install, and nothing was changed:\n\n" + ex.Message,
                            "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updating = false;
            UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = true;
            if (_pendingUpdate != null) UpdateText.Text = $"Update available: {_pendingUpdate.Tag}";
        }
    }

    private void WhatsNew_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(_pendingUpdate?.PageUrl ?? Updater.ReleasesPage);

    private void UpdateLater_Click(object sender, RoutedEventArgs e) =>
        UpdateBanner.Visibility = Visibility.Collapsed;   // asks again next launch

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(userAsked: true);

    private void ReleasesPage_Click(object sender, RoutedEventArgs e) => OpenUrl(Updater.ReleasesPage);

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Settings.CheckForUpdates = AutoUpdateCheck.IsChecked == true;
        Persist();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser registered; nothing useful to do */ }
    }

    // ───────────────────────── Window size & position ─────────────────────────

    private void RestoreWindowPlacement()
    {
        var p = Settings.Window;
        if (p is { Width: > 0, Height: > 0 } && IsOnScreen(p))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = p.Left;
            Top = p.Top;
            Width = Math.Max(MinWidth, p.Width);
            Height = Math.Max(MinHeight, p.Height);
            if (p.Maximized) WindowState = WindowState.Maximized;
            return;
        }

        // First run (or the saved spot is off-screen): wide enough for every column, capped to the screen.
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(1600, work.Width * 0.95));
        Height = Math.Max(MinHeight, Math.Min(920, work.Height * 0.92));
    }

    /// <summary>True if enough of the title bar would be on a monitor to grab it (monitors change).</summary>
    private static bool IsOnScreen(WindowPlacement p)
    {
        double left = SystemParameters.VirtualScreenLeft, top = SystemParameters.VirtualScreenTop;
        double right = left + SystemParameters.VirtualScreenWidth, bottom = top + SystemParameters.VirtualScreenHeight;
        return p.Left + p.Width > left + 100 && p.Left < right - 100
            && p.Top >= top - 10 && p.Top < bottom - 50;
    }

    private void SaveWindowPlacement()
    {
        // RestoreBounds is the normal-size rectangle even while maximized or minimized.
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (_skipSaveOnClose || b.IsEmpty || b.Width <= 0) return;
        Settings.Window = new WindowPlacement
        {
            Left = b.Left,
            Top = b.Top,
            Width = b.Width,
            Height = b.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        if (_loadFailed) return;
        try { _store.Save(); } catch { /* closing anyway; a lost window size isn't worth a popup */ }
    }

    // ───────────────────────── Refresh ─────────────────────────

    private void Refresh()
    {
        var numbers = Stats.GameNumbers(AllMatches);
        _busy = true;
        MatchesGrid.ItemsSource = AllMatches
            .OrderByDescending(m => m.PlayedAt)
            .Select(m => new MatchRow(m, numbers[m.Id]))
            .ToList();
        _busy = false;

        RefreshHeader();
        RefreshStats();
    }

    private void RefreshHeader()
    {
        var overall = Stats.Summarize(Stats.Filter(AllMatches, Settings.SeasonStart).ToList());
        OverallText.Text = overall.Games == 0
            ? "No games yet"
            : $"{(Settings.SeasonStart is null ? "All time" : "Season")} {overall.Wins}-{overall.Losses} ({overall.WinRate:P0})";

        var tonight = DateOnly.FromDateTime(DateTime.Now.AddHours(-6));
        var lastNight = AllMatches.Count == 0 ? (DateOnly?)null : AllMatches.Max(m => m.Night);
        if (lastNight is null)
        {
            SessionText.Text = "";
            return;
        }

        var session = Stats.Summarize(AllMatches.Where(m => m.Night == lastNight).ToList());
        var label = lastNight == tonight ? "Tonight" : lastNight.Value.ToString("ddd MMM d");
        SessionText.Text = $"{label}: {session.Wins}-{session.Losses}  ·  streak {session.StreakText}";
    }

    // ───────────────────────── Match form ─────────────────────────

    private void ClearForm()
    {
        _editingId = null;
        FormTitle.Text = "New match";
        SaveButton.Content = "Save game";
        DeleteButton.IsEnabled = false;

        WinRadio.IsChecked = false;
        LossRadio.IsChecked = false;
        Enemy1.Clear();
        Enemy2.Clear();
        MapBox.Text = "";
        RatingAfterBox.Text = "";
        RatingChangeBox.Text = "";
        DurationBox.Text = "";
        TagsBox.Text = "";
        NotesBox.Text = "";
        PlayedAtBox.Text = DateTime.Now.ToString(TimeFormat);
        Our1.Slot = Settings.DefaultTeam.ElementAtOrDefault(0) ?? new PlayerSlot();
        Our2.Slot = Settings.DefaultTeam.ElementAtOrDefault(1) ?? new PlayerSlot();

        _busy = true;
        MatchesGrid.SelectedItem = null;
        _busy = false;
    }

    private void LoadIntoForm(MatchRow row)
    {
        var m = row.Match;
        _editingId = m.Id;
        FormTitle.Text = $"Editing {row.Night} game #{row.GameNo}";
        SaveButton.Content = "Update game";
        DeleteButton.IsEnabled = true;

        WinRadio.IsChecked = m.Won;
        LossRadio.IsChecked = !m.Won;
        Enemy1.Slot = m.Enemies.ElementAtOrDefault(0) ?? new PlayerSlot();
        Enemy2.Slot = m.Enemies.ElementAtOrDefault(1) ?? new PlayerSlot();
        MapBox.Text = m.Map;
        RatingAfterBox.Text = m.RatingAfter?.ToString() ?? "";
        RatingChangeBox.Text = m.RatingChange?.ToString() ?? "";
        DurationBox.Text = Stats.FormatDuration(m.DurationSeconds);
        TagsBox.Text = string.Join(", ", m.Tags);
        NotesBox.Text = m.Notes;
        PlayedAtBox.Text = m.PlayedAt.ToString(TimeFormat);
        Our1.Slot = m.OurTeam.ElementAtOrDefault(0) ?? new PlayerSlot();
        Our2.Slot = m.OurTeam.ElementAtOrDefault(1) ?? new PlayerSlot();
    }

    private void SaveMatch()
    {
        if (WinRadio.IsChecked != true && LossRadio.IsChecked != true)
        {
            Status("Pick Win or Loss first (Ctrl+W / Ctrl+L).");
            return;
        }

        if (!DateTime.TryParseExact(PlayedAtBox.Text.Trim(), TimeFormat, CultureInfo.CurrentCulture,
                                    DateTimeStyles.None, out var playedAt)
            && !DateTime.TryParse(PlayedAtBox.Text.Trim(), CultureInfo.CurrentCulture,
                                  DateTimeStyles.None, out playedAt))
        {
            MoreExpander.IsExpanded = true;
            Status($"Couldn't read 'Played at'. Use {TimeFormat}, e.g. {DateTime.Now.ToString(TimeFormat)}.");
            return;
        }

        if (!TryParseInt(RatingAfterBox.Text, out var ratingAfter))
        {
            Status("'Rating after' should be a number, like 1840.");
            return;
        }
        if (!TryParseInt(RatingChangeBox.Text, out var ratingChange))
        {
            Status("'+/-' should be a number, like 14 or -11.");
            return;
        }

        var duration = Stats.ParseDuration(DurationBox.Text);
        if (!string.IsNullOrWhiteSpace(DurationBox.Text) && duration is null)
        {
            Status("Length: use 4:32, or 4.5 for minutes.");
            return;
        }

        var existing = _editingId is Guid id ? AllMatches.FirstOrDefault(m => m.Id == id) : null;
        var match = existing ?? new Match
        {
            RecordedBy = string.IsNullOrWhiteSpace(Settings.MyName) ? null : Settings.MyName.Trim(),
        };

        // Work out +/- from the previous rated game if it was left blank.
        if (ratingChange is null && ratingAfter is int after)
        {
            var previous = AllMatches
                .Where(m => m.Id != match.Id && m.PlayedAt < playedAt && m.RatingAfter.HasValue)
                .OrderByDescending(m => m.PlayedAt)
                .FirstOrDefault();
            if (previous != null) ratingChange = after - previous.RatingAfter!.Value;
        }

        match.PlayedAt = playedAt;
        match.Won = WinRadio.IsChecked == true;
        match.Map = MapBox.Text.Trim();
        match.Enemies = new[] { Enemy1.Slot, Enemy2.Slot }.Where(p => !p.IsEmpty).ToList();
        match.OurTeam = new[] { Our1.Slot, Our2.Slot }.Where(p => !p.IsEmpty).ToList();
        match.RatingAfter = ratingAfter;
        match.RatingChange = ratingChange;
        match.DurationSeconds = duration;
        match.Tags = TagsBox.Text.Split(',', ';').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        match.Notes = NotesBox.Text.Trim();

        _store.Upsert(match);
        if (!Persist()) return;

        Refresh();
        ClearForm();
        Status(existing == null
            ? $"Saved: {(match.Won ? "Win" : "Loss")} vs {match.EnemyCompKey}"
            : "Game updated.");
        Enemy1.FocusClass();
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

    private bool Persist()
    {
        if (_loadFailed)
        {
            MessageBox.Show("Your data file couldn't be read when the app started, so saving is turned off to protect it.\n\n" +
                            _store.FilePath, "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try
        {
            _store.Save();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't save:\n\n" + ex.Message, "FNF Tracker",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Status(string text) => StatusText.Text = text;

    // ───────────────────────── Form events ─────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e) => SaveMatch();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
        Enemy1.FocusClass();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is not Guid id) return;
        if (MessageBox.Show("Delete this game?", "FNF Tracker",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _store.Delete(id);
        if (!Persist()) return;
        Refresh();
        ClearForm();
        Status("Game deleted.");
    }

    private void SameAsLast_Click(object sender, RoutedEventArgs e)
    {
        var last = AllMatches.Where(m => m.Id != _editingId).OrderByDescending(m => m.PlayedAt).FirstOrDefault();
        if (last == null)
        {
            Status("No previous game to copy.");
            return;
        }
        Enemy1.Slot = last.Enemies.ElementAtOrDefault(0)?.Clone() ?? new PlayerSlot();
        Enemy2.Slot = last.Enemies.ElementAtOrDefault(1)?.Clone() ?? new PlayerSlot();
    }

    private void Now_Click(object sender, RoutedEventArgs e) =>
        PlayedAtBox.Text = DateTime.Now.ToString(TimeFormat);

    private void MatchesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy) return;
        if (MatchesGrid.SelectedItem is MatchRow row) LoadIntoForm(row);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || Tabs.SelectedIndex != 0) return;
        switch (e.Key)
        {
            case Key.S: SaveMatch(); e.Handled = true; break;
            case Key.N: ClearForm(); Enemy1.FocusClass(); e.Handled = true; break;
            case Key.W: WinRadio.IsChecked = true; e.Handled = true; break;
            case Key.L: LossRadio.IsChecked = true; e.Handled = true; break;
        }
    }

    // ───────────────────────── Stats ─────────────────────────

    private void RefreshStats()
    {
        var (rangeFrom, rangeTo) = CurrentRange();
        var ms = StatsRange.Apply(AllMatches, rangeFrom, rangeTo).ToList();
        RangeSummary.Text = $"{StatsRange.Describe(rangeFrom, rangeTo)}  ·  {ms.Count} game(s)"
            + (SelectedRangeKind == RangeKind.Season && Settings.SeasonStart is null ? "  ·  set the season start in Settings" : "");
        var s = Stats.Summarize(ms);

        SummaryCards.Children.Clear();
        AddCard("Record", s.Games == 0 ? "-" : $"{s.Wins}-{s.Losses}", Theme.Neutral);
        AddCard("Win rate", s.Games == 0 ? "-" : $"{s.WinRate:P0}", Theme.ForWinRate(s.Games, s.WinRate));
        AddCard("Streak", s.StreakText, s.CurrentStreak > 0 ? Theme.Win : s.CurrentStreak < 0 ? Theme.Loss : Theme.Neutral);
        AddCard("Best win streak", s.BestWinStreak.ToString(), Theme.Neutral);
        AddCard("Nights", s.Nights.ToString(), Theme.Neutral);

        RatingCards.Children.Clear();
        AddCard("Current rating", s.LatestRating?.ToString() ?? "-", Theme.Accent, target: RatingCards);
        AddCard("Peak rating", s.PeakRating?.ToString() ?? "-", Theme.Accent, target: RatingCards);

        MmrHost.Children.Clear();
        var mmrPanel = BuildMmrTable(Stats.OpponentMmrSummary(ms));
        Grid.SetColumn(mmrPanel, 0);
        MmrHost.Children.Add(mmrPanel);

        StatTables.Children.Clear();
        AddTable("Enemy comps", "Comp", Stats.ByEnemyComp(ms));
        AddTable("Enemy classes (once per game)", "Class", Stats.ByEnemyClass(ms));
        AddTable("Enemy specs", "Spec", Stats.ByEnemySpec(ms));
        AddTable("Comp type", "Type", Stats.ByCompType(ms));
        AddTable("Maps", "Map", Stats.ByMap(ms));
        AddTable("Game number in the night", "Games", Stats.ByGameNumber(ms));
        AddTable("Game length", "Length", Stats.ByDuration(ms));
        AddTable("Day of week", "Day", Stats.ByWeekday(ms));
        AddTable("Nights", "Night", Stats.ByNight(ms));
        AddTable("Tags", "Tag", Stats.ByTag(ms));
        AddTable("Repeat opponents", "Player", Stats.RepeatOpponents(ms));
        var mmrGap = Stats.ByMmrGap(ms);
        if (mmrGap.Count > 0) AddTable("Enemy MMR vs yours", "Matchup", mmrGap);
        var ourComps = Stats.ByOurComp(ms);
        if (ourComps.Count > 1) AddTable("Our comps", "Comp", ourComps);

        _ratingPoints = Stats.RatingHistory(ms);
        DrawRating();
    }

    private void AddCard(string title, string value, Brush valueBrush,
                         string? caption = null, string? tooltip = null, WrapPanel? target = null)
    {
        var panel = new StackPanel();
        var titleBlock = new TextBlock { Text = title, Opacity = 0.7 };
        var valueBlock = new TextBlock { Text = value, FontSize = 28, Foreground = valueBrush };
        titleBlock.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        valueBlock.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        panel.Children.Add(titleBlock);
        panel.Children.Add(valueBlock);
        if (!string.IsNullOrEmpty(caption))
            panel.Children.Add(new TextBlock { Text = caption, Opacity = 0.55, FontSize = 12 });
        (target ?? SummaryCards).Children.Add(new Border
        {
            ToolTip = tooltip,
            Child = panel,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 10, 10),
            MinWidth = 120,
            CornerRadius = new CornerRadius(8),
            Background = Theme.Make("#14FFFFFF"),
        });
    }

    /// <summary>The "MMR" table: highest, average and lowest opponent team MMR.</summary>
    private FrameworkElement BuildMmrTable(OpponentMmr mmr)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };
        var heading = new TextBlock { Text = "MMR", FontSize = 17, Margin = new Thickness(0, 6, 0, 6) };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        panel.Children.Add(heading);

        if (mmr.Games == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Nothing yet. Opponent MMR comes from games recorded by the FNF addon.",
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            return panel;
        }

        InfoLine Result(string label, int? value, bool? won) => new(
            label, value?.ToString() ?? "-",
            won switch { true => "you won", false => "you lost", _ => "" },
            won == true ? Theme.Win : Theme.Loss);

        var lines = new List<InfoLine>
        {
            Result("Highest", mmr.Highest, mmr.WonVsHighest),
            new("Average", mmr.Average?.ToString() ?? "-", $"over {mmr.Games} game(s)", Theme.Dim),
            Result("Lowest", mmr.Lowest, mmr.WonVsLowest),
        };

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeRows = false,
            CanUserSortColumns = false,
            ItemsSource = lines,
            ToolTip = $"Opponent team MMR from {mmr.Games} game(s) recorded by the FNF addon.",
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Opponent MMR",
            Binding = new Binding(nameof(InfoLine.Label)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });

        var valueStyle = new Style(typeof(TextBlock));
        valueStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.Accent));
        valueStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "MMR",
            Binding = new Binding(nameof(InfoLine.Value)),
            ElementStyle = valueStyle,
            Width = DataGridLength.Auto,
            MinWidth = 72,
        });

        var noteStyle = new Style(typeof(TextBlock));
        noteStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(nameof(InfoLine.NoteBrush))));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Result",
            Binding = new Binding(nameof(InfoLine.Note)),
            ElementStyle = noteStyle,
            Width = DataGridLength.Auto,
            MinWidth = 110,
        });

        // Let the mouse wheel scroll the page rather than stopping at this small table.
        grid.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            panel.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = grid,
            });
        };

        panel.Children.Add(grid);
        return panel;
    }

    private void AddTable(string title, string labelHeader, List<StatRow> rows)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };
        var heading = new TextBlock { Text = title, FontSize = 17, Margin = new Thickness(0, 6, 0, 6) };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        panel.Children.Add(heading);

        if (rows.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nothing yet.", Opacity = 0.5, Margin = new Thickness(0, 0, 0, 8) });
            StatTables.Children.Add(panel);
            return;
        }

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeRows = false,
            MaxHeight = 300,
            ItemsSource = rows.Select(r => new StatView(r)).ToList(),
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = labelHeader,
            Binding = new Binding(nameof(StatView.Label)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn { Header = "Games", Binding = new Binding(nameof(StatView.Games)), Width = DataGridLength.Auto, MinWidth = 64 });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "W-L",
            Binding = new Binding(nameof(StatView.Record)),
            SortMemberPath = nameof(StatView.Games),
            Width = DataGridLength.Auto,
            MinWidth = 64,
        });

        var pctStyle = new Style(typeof(TextBlock));
        pctStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(nameof(StatView.Brush))));
        pctStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Win %",
            Binding = new Binding(nameof(StatView.WinPct)),
            SortMemberPath = nameof(StatView.WinRate),
            ElementStyle = pctStyle,
            Width = DataGridLength.Auto,
            MinWidth = 64,
        });

        // Short tables pass the mouse wheel up to the page; tall ones (at MaxHeight) scroll themselves.
        grid.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled || grid.ActualHeight >= grid.MaxHeight - 1) return;
            e.Handled = true;
            var forward = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = grid,
            };
            panel.RaiseEvent(forward);
        };

        panel.Children.Add(grid);
        StatTables.Children.Add(panel);
    }

    private void DrawRating()
    {
        RatingCanvas.Children.Clear();
        double w = RatingCanvas.ActualWidth, h = RatingCanvas.ActualHeight;
        if (w < 80 || h < 60) return;

        if (_ratingPoints.Count < 2)
        {
            var hint = new TextBlock
            {
                Text = "Fill in 'Rating after' on your games to see your rating line here.",
                Opacity = 0.55,
            };
            Canvas.SetLeft(hint, 16);
            Canvas.SetTop(hint, h / 2 - 10);
            RatingCanvas.Children.Add(hint);
            return;
        }

        int min = _ratingPoints.Min(p => p.Rating), max = _ratingPoints.Max(p => p.Rating);
        if (max - min < 20) { max += 10; min -= 10; }
        const double padL = 52, padR = 16, padT = 14, padB = 14;
        double plotW = w - padL - padR, plotH = h - padT - padB;
        double X(int i) => padL + i * plotW / (_ratingPoints.Count - 1);
        double Y(int r) => padT + (max - r) * plotH / (max - min);

        // Horizontal guides at max / mid / min.
        foreach (var r in new[] { max, (max + min) / 2, min })
        {
            RatingCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = padL, X2 = w - padR, Y1 = Y(r), Y2 = Y(r),
                Stroke = Theme.Grid, StrokeThickness = 1,
            });
            var label = new TextBlock { Text = r.ToString(), Opacity = 0.6, FontSize = 11 };
            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, Y(r) - 8);
            RatingCanvas.Children.Add(label);
        }

        // Dashed separators between nights.
        for (int i = 1; i < _ratingPoints.Count; i++)
        {
            var prevNight = DateOnly.FromDateTime(_ratingPoints[i - 1].PlayedAt.AddHours(-6));
            var night = DateOnly.FromDateTime(_ratingPoints[i].PlayedAt.AddHours(-6));
            if (night == prevNight) continue;
            double x = (X(i - 1) + X(i)) / 2;
            RatingCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = x, X2 = x, Y1 = padT, Y2 = h - padB,
                Stroke = Theme.Grid, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            });
        }

        var line = new System.Windows.Shapes.Polyline { Stroke = Theme.Accent, StrokeThickness = 2 };
        for (int i = 0; i < _ratingPoints.Count; i++) line.Points.Add(new Point(X(i), Y(_ratingPoints[i].Rating)));
        RatingCanvas.Children.Add(line);

        // Highlight the latest rating.
        var last = _ratingPoints[^1];
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Theme.Accent };
        Canvas.SetLeft(dot, X(_ratingPoints.Count - 1) - 4);
        Canvas.SetTop(dot, Y(last.Rating) - 4);
        RatingCanvas.Children.Add(dot);
    }

    private void RatingCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawRating();

    // ───────────────────────── Stats range ─────────────────────────

    private RangeKind SelectedRangeKind =>
        RangeBox.SelectedIndex >= 0 ? StatsRange.Options[RangeBox.SelectedIndex].Kind : RangeKind.AllTime;

    private (DateOnly? From, DateOnly? To) CurrentRange()
    {
        var today = StatsRange.CurrentNight(DateTime.Now);
        if (SelectedRangeKind == RangeKind.SingleNight && _singleNight is null)
            _singleNight = StatsRange.LatestNight(AllMatches) ?? today;
        return StatsRange.Resolve(SelectedRangeKind, today, Settings.SeasonStart, _singleNight,
                                  ToDateOnly(FromPicker.SelectedDate), ToDateOnly(ToPicker.SelectedDate));
    }

    private static DateOnly? ToDateOnly(DateTime? d) => d is DateTime v ? DateOnly.FromDateTime(v) : null;

    /// <summary>Shows the night stepper or the custom date pickers to match the range choice.</summary>
    private void UpdateRangeControls()
    {
        var kind = SelectedRangeKind;
        NightPanel.Visibility = kind == RangeKind.SingleNight ? Visibility.Visible : Visibility.Collapsed;
        CustomPanel.Visibility = kind == RangeKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        if (kind == RangeKind.SingleNight)
        {
            CurrentRange(); // picks the latest night played if none chosen yet
            _busy = true;
            NightPicker.SelectedDate = _singleNight?.ToDateTime(TimeOnly.MinValue);
            _busy = false;
        }
    }

    private void Range_Changed(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true; // don't let this bubble to the tab control
        if (_busy) return;
        Settings.StatsRange = SelectedRangeKind;
        Persist();
        UpdateRangeControls();
        RefreshStats();
    }

    private void NightPicker_Changed(object? sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (_busy || NightPicker.SelectedDate is not DateTime d) return;
        _singleNight = DateOnly.FromDateTime(d);
        RefreshStats();
    }

    private void StepNight(int direction)
    {
        var current = _singleNight ?? StatsRange.CurrentNight(DateTime.Now);
        var next = StatsRange.StepNight(AllMatches, current, direction);
        if (next is null)
        {
            Status(direction < 0 ? "No earlier nights with games." : "No later nights with games.");
            return;
        }
        _singleNight = next;
        UpdateRangeControls();
        RefreshStats();
    }

    private void PrevNight_Click(object sender, RoutedEventArgs e) => StepNight(-1);
    private void NextNight_Click(object sender, RoutedEventArgs e) => StepNight(+1);

    private void CustomRange_Changed(object? sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (_busy) return;
        Settings.StatsFrom = FromPicker.SelectedDate;
        Settings.StatsTo = ToPicker.SelectedDate;
        Persist();
        RefreshStats();
    }

    private void SeasonStart_Changed(object? sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (_busy) return;
        Settings.SeasonStart = SeasonStartPicker.SelectedDate;
        Persist();
        RefreshHeader();
        RefreshStats();
    }

    private void ClearSeason_Click(object sender, RoutedEventArgs e) => SeasonStartPicker.SelectedDate = null;

    // ───────────────────────── Settings & data ─────────────────────────

    private void LoadSettingsIntoUi()
    {
        _busy = true;
        MyNameBox.Text = Settings.MyName;
        DefaultSlot1.Slot = Settings.DefaultTeam.ElementAtOrDefault(0) ?? new PlayerSlot();
        DefaultSlot2.Slot = Settings.DefaultTeam.ElementAtOrDefault(1) ?? new PlayerSlot();
        SeasonStartPicker.SelectedDate = Settings.SeasonStart;
        AutoUpdateCheck.IsChecked = Settings.CheckForUpdates;
        RangeBox.ItemsSource = StatsRange.Options.Select(o => o.Label).ToList();
        RangeBox.SelectedIndex = Math.Max(0, StatsRange.Options.ToList().FindIndex(o => o.Kind == Settings.StatsRange));
        FromPicker.SelectedDate = Settings.StatsFrom;
        ToPicker.SelectedDate = Settings.StatsTo;
        _busy = false;
        UpdateRangeControls();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        Settings.MyName = MyNameBox.Text.Trim();
        Settings.DefaultTeam = new List<PlayerSlot> { DefaultSlot1.Slot, DefaultSlot2.Slot };
        if (!Persist()) return;
        if (_editingId is null)
        {
            Our1.Slot = Settings.DefaultTeam[0];
            Our2.Slot = Settings.DefaultTeam[1];
        }
        Status("Settings saved.");
    }

    // ───────────────────────── Addon ─────────────────────────

    private void RunAddonImport(bool auto)
    {
        if (_loadFailed) return;
        AddonImportResult result;
        try
        {
            result = AddonImport.ImportInto(_store, Settings.WowFolder);
        }
        catch (Exception ex)
        {
            AddonStatusText.Text = "Import failed: " + ex.Message;
            if (!auto) Status("Addon import failed: " + ex.Message);
            return;
        }

        if (result.Changed)
        {
            if (!Persist()) return;
            Refresh();
        }

        if (string.IsNullOrWhiteSpace(Settings.WowFolder) || !Directory.Exists(Settings.WowFolder))
            AddonStatusText.Text = "Set your WoW folder to import games from the addon.";
        else if (result.FilesRead.Count == 0 && result.Problems.Count == 0)
            AddonStatusText.Text = "No addon data yet. Install the addon, play a game, then log out or /reload.";
        else
            AddonStatusText.Text = $"Last import {DateTime.Now:h:mm tt}: {result.Summary()}" +
                                   (result.Problems.Count > 0 ? "\n" + string.Join("\n", result.Problems) : "");

        if (result.Problems.Count > 0) Status("Addon import problem — see Settings.");
        else if (!auto || result.Added > 0) Status("Addon: " + result.Summary());
    }

    private void StartWatching()
    {
        StopWatching();
        foreach (var dir in AddonImport.FindSavedVariablesFolders(Settings.WowFolder))
        {
            try
            {
                var w = new FileSystemWatcher(dir, AddonImport.SavedVariablesFile)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                // WoW may write the file in a few steps; wait for it to go quiet before reading.
                void Poke() => Dispatcher.InvokeAsync(() =>
                {
                    _importDebounce.Stop();
                    _importDebounce.Start();
                });
                w.Changed += (_, _) => Poke();
                w.Created += (_, _) => Poke();
                w.Renamed += (_, _) => Poke();
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch
            {
                // Folder vanished or isn't watchable; startup import still covers it.
            }
        }
    }

    private void StopWatching()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    private void ApplyWowFolder()
    {
        var folder = WowFolderBox.Text.Trim();
        if (folder == (Settings.WowFolder ?? "")) return;
        Settings.WowFolder = folder.Length == 0 ? null : folder;
        Persist();
        StartWatching();
    }

    private void BrowseWow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your World of Warcraft folder" };
        if (Directory.Exists(WowFolderBox.Text)) dialog.InitialDirectory = WowFolderBox.Text;
        if (dialog.ShowDialog(this) != true) return;
        WowFolderBox.Text = dialog.FolderName;
        ApplyWowFolder();
        RunAddonImport(auto: false);
    }

    private void ImportNow_Click(object sender, RoutedEventArgs e)
    {
        ApplyWowFolder();
        RunAddonImport(auto: false);
    }

    private void InstallAddon_Click(object sender, RoutedEventArgs e)
    {
        ApplyWowFolder();
        var folder = Settings.WowFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("Set your WoW folder first (the one that contains _retail_).", "FNF Tracker",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var retail = Directory.Exists(Path.Combine(folder, "_retail_")) ? Path.Combine(folder, "_retail_") : folder;
        var target = Path.Combine(retail, "Interface", "AddOns", "FNFTracker");
        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in new[] { "FNFTracker.toc", "FNFTracker.lua" })
            {
                var resource = "FnfTracker.Addon." + file;
                using var source = typeof(MainWindow).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("Addon file missing from the app: " + resource);
                using var dest = File.Create(Path.Combine(target, file));
                source.CopyTo(dest);
            }
            MessageBox.Show($"Addon installed to:\n{target}\n\nIf WoW is running, type /reload. In game, /fnf shows what it has recorded.",
                            "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show($"Windows wouldn't let the app write to:\n{target}\n\nCopy the Addon\\FNFTracker folder from the download there by hand instead.",
                            "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        StartWatching();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_store.FilePath)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var who = string.IsNullOrWhiteSpace(Settings.MyName) ? "me" : Settings.MyName.Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Export FNF data",
            Filter = "FNF Tracker data (*.json)|*.json",
            FileName = $"fnf-{who}-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dialog.ShowDialog(this) != true) return;
        _store.ExportTo(dialog.FileName);
        Status($"Exported {AllMatches.Count} games to {dialog.FileName}");
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Merge your partner's FNF data",
            Filter = "FNF Tracker data (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true) return;

        FnfData other;
        try
        {
            other = DataStore.ReadFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("That file couldn't be read:\n\n" + ex.Message, "FNF Tracker",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var result = _store.MergeFrom(other);
        if (!Persist()) return;
        Refresh();
        MessageBox.Show($"Added {result.Added} new game(s).\nSkipped {result.Duplicates} you already had.",
                        "FNF Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"fnf-games-{DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog(this) != true) return;

        static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var numbers = Stats.GameNumbers(AllMatches);
        var sb = new StringBuilder();
        sb.AppendLine("PlayedAt,Night,GameNo,Result,Map,OurComp,Enemy1,Enemy1Name,Enemy2,Enemy2Name,RatingAfter,RatingChange,LengthSeconds,Tags,Notes,RecordedBy");
        foreach (var m in AllMatches.OrderBy(m => m.PlayedAt))
        {
            var e1 = m.Enemies.ElementAtOrDefault(0);
            var e2 = m.Enemies.ElementAtOrDefault(1);
            sb.AppendLine(string.Join(",",
                m.PlayedAt.ToString("yyyy-MM-dd HH:mm"), m.Night.ToString("yyyy-MM-dd"), numbers[m.Id],
                m.Won ? "W" : "L", Q(m.Map), Q(m.OurCompKey),
                Q(e1?.Label), Q(e1?.Name), Q(e2?.Label), Q(e2?.Name),
                m.RatingAfter?.ToString() ?? "", m.RatingChange?.ToString() ?? "",
                m.DurationSeconds?.ToString() ?? "",
                Q(string.Join("; ", m.Tags)), Q(m.Notes), Q(m.RecordedBy)));
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        Status($"Exported CSV to {dialog.FileName}");
    }
}
