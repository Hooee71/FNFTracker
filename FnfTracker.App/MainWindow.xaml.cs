using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FnfTracker.Core;
using Microsoft.Win32;

namespace FnfTracker.App;

public partial class MainWindow : Window
{
    private readonly DataStore _store = new();
    private bool _loadFailed;
    private bool _busy;               // suppresses event handlers while we set controls from code
    private List<RatingPoint> _ratingPoints = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private ImageSource? _matchesArt, _statsArt, _settingsArt;
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
        _settingsArt = LoadArt("mascots-settings.png");
        UpdateMascotArt();

        RestoreWindowPlacement();
        RestoreMatchColumns();
        Closing += (_, _) => SaveWindowPlacement();
        Loaded += (_, _) => Window_StateChanged(this, EventArgs.Empty); // sync title bar if we start maximized

        DataPathText.Text = "Saved at: " + _store.FilePath;
        LoadSettingsIntoUi();
        Refresh();

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

    /// <summary>Matches: bull and fox. Stats: zombie and orc. Settings: engineer portrait.</summary>
    private void UpdateMascotArt()
    {
        if (MascotArt == null) return; // fires while the window is still being built
        ImageSource? art = Tabs.SelectedIndex switch
        {
            0 => _matchesArt,
            1 => _statsArt,
            2 => _settingsArt,
            _ => null,
        };
        MascotArt.Source = art;
        // All art keeps 24px clear of the right edge so it doesn't collide with the scroll bar.
        // Matches: the table runs the full width now, so the art is smaller and tucked into the
        // corner above it (the strip over the table leaves it room). Its scrim shrinks to match.
        // Stats sits a little lower so it doesn't crowd the title bar.
        bool matches = Tabs.SelectedIndex == 0;
        MascotArt.Margin = Tabs.SelectedIndex switch
        {
            0 => new Thickness(0, 4, 24, 0),
            1 => new Thickness(0, 14, 24, 0),
            _ => new Thickness(0, -10, 24, 0),
        };
        MascotArt.Width = matches ? 250 : 390;
        MascotArt.Visibility = art == null ? Visibility.Collapsed : Visibility.Visible;
        if (MascotScrim != null)
        {
            MascotScrim.Visibility = MascotArt.Visibility;
            MascotScrim.Width = matches ? 420 : 660;
            MascotScrim.Height = matches ? 300 : 520;
            MascotScrim.Margin = matches ? new Thickness(0, -10, -45, 0) : new Thickness(0, -74, -95, 0);
        }
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
            if (userAsked) Status("Couldn't check for updates. See Settings.", error: true);
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

    private void ProfileLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.OriginalString);
        e.Handled = true;
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
        CaptureMatchColumns();
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

    // ───────────────────────── Matches table columns ─────────────────────────

    private static string ColumnKey(DataGridColumn c) => c.Header?.ToString() ?? "";

    private void CaptureMatchColumns()
    {
        Settings.MatchColumns = MatchesGrid.Columns.Select(c => new ColumnLayout
        {
            Header = ColumnKey(c),
            DisplayIndex = c.DisplayIndex,
            // A column the user dragged wider is Pixel; untouched Auto columns stay Auto so they keep fitting their content.
            Width = c.Width.IsAuto ? 0 : c.Width.Value,
            WidthUnit = c.Width.IsStar ? "Star" : c.Width.IsAbsolute ? "Pixel" : "Auto",
            Sort = c.SortDirection?.ToString(),
        }).ToList();
    }

    private void RestoreMatchColumns()
    {
        var saved = Settings.MatchColumns;
        if (saved is null || saved.Count == 0) return;
        try
        {
            var byKey = MatchesGrid.Columns.ToDictionary(ColumnKey);
            foreach (var l in saved)
            {
                if (!byKey.TryGetValue(l.Header, out var col)) continue; // a column that's since been removed
                if (l.WidthUnit == "Pixel" && l.Width > 0)
                    col.Width = new DataGridLength(Math.Max(col.MinWidth, l.Width));
                else if (l.WidthUnit == "Star" && l.Width > 0)
                    col.Width = new DataGridLength(l.Width, DataGridLengthUnitType.Star);
            }

            // Reapply the order front to back so each move lands where it was.
            int last = MatchesGrid.Columns.Count - 1;
            foreach (var l in saved.OrderBy(l => l.DisplayIndex))
                if (byKey.TryGetValue(l.Header, out var col))
                    col.DisplayIndex = Math.Clamp(l.DisplayIndex, 0, last);

            var sorted = saved.FirstOrDefault(l => l.Sort != null && byKey.ContainsKey(l.Header));
            if (sorted != null && Enum.TryParse<System.ComponentModel.ListSortDirection>(sorted.Sort, out var dir))
                _matchSort = (byKey[sorted.Header], dir);
        }
        catch { /* a bad saved layout just means default columns */ }
    }

    /// <summary>The Matches sort the user picked; reapplied whenever the rows are rebuilt.</summary>
    private (DataGridColumn Column, System.ComponentModel.ListSortDirection Direction)? _matchSort;

    private void ApplyMatchSort()
    {
        foreach (var c in MatchesGrid.Columns) c.SortDirection = null;
        var view = CollectionViewSource.GetDefaultView(MatchesGrid.ItemsSource);
        if (view is null) return;
        view.SortDescriptions.Clear();
        if (_matchSort is not { } s) return;
        var path = s.Column.SortMemberPath;
        if (string.IsNullOrEmpty(path)) return;
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(path, s.Direction));
        s.Column.SortDirection = s.Direction;
    }

    private void MatchesGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // Let the grid sort as usual, then remember it so a refresh (new game, edit, import) keeps it.
        Dispatcher.InvokeAsync(() =>
        {
            var col = MatchesGrid.Columns.FirstOrDefault(c => c.SortDirection != null);
            _matchSort = col is null ? null : (col, col.SortDirection!.Value);
        }, DispatcherPriority.Background);
    }

    // ───────────────────────── Refresh ─────────────────────────

    private void Refresh()
    {
        var numbers = Stats.GameNumbers(AllMatches);
        _busy = true;
        MatchesGrid.ItemsSource = AllMatches
            .OrderByDescending(m => m.PlayedAt)
            .Select(m => new MatchRow(m, numbers[m.Id], Settings))
            .ToList();
        ApplyMatchSort();
        _busy = false;

        MatchCountText.Text = AllMatches.Count == 1 ? "1 game" : $"{AllMatches.Count} games";

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

    // ───────────────────────── Edit / delete a game ─────────────────────────

    /// <summary>Opens the game in a pop-up editor; saves and refreshes if it comes back with changes.</summary>
    private void EditMatch(MatchRow row)
    {
        var editor = new MatchEditor(row.Match, $"{row.Night} · game #{row.GameNo}", AllMatches) { Owner = this };
        if (editor.ShowDialog() != true) return;

        _store.Upsert(row.Match);
        if (!Persist()) return;
        Refresh();
        SelectMatch(row.Match.Id);
        Status("Game updated.");
    }

    private void DeleteMatch(MatchRow row)
    {
        var m = row.Match;
        var what = $"{row.Night} game #{row.GameNo}: {(m.Won ? "win" : "loss")} vs {(m.Enemies.Count == 0 ? "unknown" : m.EnemyCompKey)}";
        if (MessageBox.Show(this, $"Delete this game?\n\n{what}", "FNF Tracker",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _store.Delete(m.Id);
        if (!Persist()) return;
        Refresh();
        Status("Game deleted.");
    }

    /// <summary>Re-selects a game after the rows are rebuilt, so you don't lose your place.</summary>
    private void SelectMatch(Guid id)
    {
        if (MatchesGrid.ItemsSource is not IEnumerable<MatchRow> rows) return;
        var row = rows.FirstOrDefault(r => r.Match.Id == id);
        if (row == null) return;
        MatchesGrid.SelectedItem = row;
        MatchesGrid.ScrollIntoView(row);
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MatchRow row) EditMatch(row);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MatchRow row) DeleteMatch(row);
    }

    private void MatchesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on a game row: not the column headers, the scroll bar, or the icons.
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null && hit is not DataGridRow)
        {
            if (hit is System.Windows.Controls.Primitives.DataGridColumnHeader or System.Windows.Controls.Primitives.ScrollBar or Button
                or System.Windows.Documents.Hyperlink) return;
            hit = hit is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(hit)
                : LogicalTreeHelper.GetParent(hit);
        }
        if (hit is DataGridRow { Item: MatchRow row })
        {
            e.Handled = true;
            EditMatch(row);
        }
    }

    private void MatchesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MatchesGrid.SelectedItem is not MatchRow row || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.Enter: EditMatch(row); e.Handled = true; break;
            case Key.Delete: DeleteMatch(row); e.Handled = true; break;
        }
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

    // ───────────────────────── Status bar ─────────────────────────

    private static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(15);
    private DispatcherTimer? _statusTimer;

    /// <summary>
    /// Shows a message in the bottom bar: the pill slides in with a quick pulse so it gets noticed,
    /// then fades out and clears after <see cref="StatusLifetime"/>. Errors show in red.
    /// </summary>
    private void Status(string text, bool error = false)
    {
        var color = (Color)ColorConverter.ConvertFromString(error ? "#E5534B" : "#E6B450");
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(0xFF, 0xB4, 0xAE) : Color.FromRgb(0xF2, 0xDD, 0xA8));
        StatusDot.Fill = new SolidColorBrush(color);
        StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, color.R, color.G, color.B));

        // Background flashes bright, then settles to a soft tint.
        var bg = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        StatusPill.Background = bg;
        bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            From = Color.FromArgb(0xB0, color.R, color.G, color.B),
            To = Color.FromArgb(0x33, color.R, color.G, color.B),
            Duration = TimeSpan.FromMilliseconds(900),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

        // Slide in from the left and fade up.
        StatusPill.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        StatusPillShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(250))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });

        // Restart the countdown so a new message always gets its full time on screen.
        _statusTimer ??= new DispatcherTimer();
        _statusTimer.Stop();
        _statusTimer.Interval = StatusLifetime;
        _statusTimer.Tick -= StatusTimer_Tick;
        _statusTimer.Tick += StatusTimer_Tick;
        _statusTimer.Start();
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        _statusTimer?.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(600));
        fade.Completed += (_, _) =>
        {
            // A new message may have arrived during the fade; only clear if nothing replaced it.
            if (_statusTimer is { IsEnabled: false }) StatusText.Text = "";
        };
        StatusPill.BeginAnimation(OpacityProperty, fade);
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
        AddToughestCard("Toughest DPS", Stats.ToughestSpec(ms, healers: false));
        AddToughestCard("Toughest healer", Stats.ToughestSpec(ms, healers: true));

        RatingCards.Children.Clear();
        AddCard("Current rating", s.LatestRating?.ToString() ?? "-", Theme.Accent, target: RatingCards);
        AddCard("Peak rating", s.PeakRating?.ToString() ?? "-", Theme.Accent, target: RatingCards);

        MmrHost.Children.Clear();
        var mmrPanel = BuildMmrTable(Stats.OpponentMmrSummary(ms));
        Grid.SetColumn(mmrPanel, 0);
        MmrHost.Children.Add(mmrPanel);

        StatTables.Children.Clear();
        AddOutputTable("Damage & healing vs. them", "Result", Stats.OutputByResult(ms));
        AddOutputTable("Damage & healing by enemy comp", "Comp", Stats.OutputByEnemyComp(ms));
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

    /// <summary>"Frost Mage (38%)": green above 50%, red below, plain at exactly 50%.</summary>
    private void AddToughestCard(string title, StatRow? row)
    {
        if (row is null)
        {
            AddCard(title, "-", Theme.Neutral,
                    tooltip: $"Shows once you've played {Stats.ToughestMinGames}+ games against a spec.");
            return;
        }
        var brush = row.WinRate > 0.5 ? Theme.Win : row.WinRate < 0.5 ? Theme.Loss : Theme.Neutral;
        AddCard(title, $"{row.Label} ({row.WinPct})", brush,
                tooltip: $"{row.Record} against {row.Label} over {row.Games} game(s). " +
                         $"Specs need {Stats.ToughestMinGames}+ games to count.");
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

    /// <summary>
    /// A "how outplayed were we" table: our total damage and healing as a percent above (+) or
    /// below (-) the enemy team's. Addon games only, since the numbers come from the scoreboard.
    /// </summary>
    private void AddOutputTable(string title, string labelHeader, List<OutputGap> rows)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 16) };
        var heading = new TextBlock { Text = title, FontSize = 17, Margin = new Thickness(0, 6, 0, 6) };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        panel.Children.Add(heading);

        if (rows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Nothing yet. Damage and healing come from games recorded by the FNF addon.",
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
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
            ItemsSource = rows.Select(r => new OutputGapView(r)).ToList(),
            ToolTip = "Your team's total damage and healing vs. the enemy team's, over these games.\n" +
                      "-20% DPS = you did 20% less damage than them. Healing only counts games where\n" +
                      "both teams brought the same number of healers.",
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = labelHeader,
            Binding = new Binding(nameof(OutputGapView.Label)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn { Header = "Games", Binding = new Binding(nameof(OutputGapView.Games)), Width = DataGridLength.Auto, MinWidth = 64 });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "W-L",
            Binding = new Binding(nameof(OutputGapView.Record)),
            SortMemberPath = nameof(OutputGapView.Games),
            Width = DataGridLength.Auto,
            MinWidth = 64,
        });

        Style Colored(string brushPath, string? tipPath = null)
        {
            var st = new Style(typeof(TextBlock));
            st.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding(brushPath)));
            st.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            if (tipPath != null) st.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(tipPath)));
            return st;
        }
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "DPS",
            Binding = new Binding(nameof(OutputGapView.Dps)),
            SortMemberPath = nameof(OutputGapView.DpsPct),
            ElementStyle = Colored(nameof(OutputGapView.DpsBrush)),
            Width = DataGridLength.Auto,
            MinWidth = 64,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "HPS",
            Binding = new Binding(nameof(OutputGapView.Hps)),
            SortMemberPath = nameof(OutputGapView.HpsPct),
            ElementStyle = Colored(nameof(OutputGapView.HpsBrush), nameof(OutputGapView.HpsTip)),
            Width = DataGridLength.Auto,
            MinWidth = 64,
        });

        // Short tables pass the mouse wheel up to the page; tall ones (at MaxHeight) scroll themselves.
        grid.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled || grid.ActualHeight >= grid.MaxHeight - 1) return;
            e.Handled = true;
            panel.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = grid,
            });
        };

        panel.Children.Add(grid);
        StatTables.Children.Add(panel);
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
        HomeRealmBox.Text = Settings.HomeRealm ?? "";
        RegionBox.ItemsSource = CheckPvp.Regions.Select(r => r.ToUpperInvariant()).ToList();
        RegionBox.SelectedIndex = CheckPvp.Regions.ToList().IndexOf(CheckPvp.NormalizeRegion(Settings.Region));
        SeasonStartPicker.SelectedDate = Settings.SeasonStart;
        AutoUpdateCheck.IsChecked = Settings.CheckForUpdates;
        PublishRepoBox.Text = Settings.PublishRepo ?? "";
        AutoPublishCheck.IsChecked = Settings.AutoPublish;
        RefreshPublishUi();
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
        var realm = HomeRealmBox.Text.Trim();
        Settings.HomeRealm = realm.Length == 0 ? null : realm;
        Settings.Region = RegionBox.SelectedIndex >= 0 ? CheckPvp.Regions[RegionBox.SelectedIndex] : CheckPvp.DefaultRegion;
        if (!Persist()) return;
        Refresh(); // profile links depend on realm and region
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
            if (!auto) Status("Addon import failed: " + ex.Message, error: true);
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

        if (result.Problems.Count > 0) Status("Addon import problem — see Settings.", error: true);
        else if (!auto || result.Added > 0) Status("Addon: " + result.Summary());

        if (result.Added > 0 && Settings.AutoPublish) _ = PublishSiteAsync(auto: true);
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
            foreach (var file in new[] { "FNFTracker.toc", "FNFTracker.lua", "Icon.tga" })
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
        static string Pct(double? p) => p?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var numbers = Stats.GameNumbers(AllMatches);
        var sb = new StringBuilder();
        sb.AppendLine("PlayedAt,Night,GameNo,Result,Map,OurComp,Enemy1,Enemy1Name,Enemy2,Enemy2Name,RatingAfter,RatingChange,LengthSeconds,OurDamage,TheirDamage,OurHealing,TheirHealing,DpsVsPct,HpsVsPct,Tags,Notes,RecordedBy");
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
                Stats.OurDamage(m)?.ToString() ?? "", Stats.TheirDamage(m)?.ToString() ?? "",
                Stats.OurHealing(m)?.ToString() ?? "", Stats.TheirHealing(m)?.ToString() ?? "",
                Pct(Stats.DpsVs(m)), Pct(Stats.HpsVs(m)),
                Q(string.Join("; ", m.Tags)), Q(m.Notes), Q(m.RecordedBy)));
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        Status($"Exported CSV to {dialog.FileName}");
    }

    // ───────────────────────── Website ─────────────────────────

    private bool _publishing;

    /// <summary>Files the site pages reference, taken from the app's own Assets.</summary>
    private static readonly (string SitePath, string Resource)[] SiteAssets =
    {
        (WebSite.AssetPaths.Logo, "Assets/logo.png"),
        (WebSite.AssetPaths.Background, "Assets/background.jpg"),
        (WebSite.AssetPaths.Font, "Assets/Fonts/Marcellus-Regular.ttf"),
        (WebSite.AssetPaths.Icon, "Assets/fnf.ico"),
        (WebSite.AssetPaths.MatchesArt, "Assets/mascots.png"),
        (WebSite.AssetPaths.StatsArt, "Assets/mascots-stats.png"),
    };

    private static byte[] ReadResource(string path)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/{path}"))
                   ?? throw new FileNotFoundException("Missing app resource", path);
        using var stream = info.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private void RefreshPublishUi()
    {
        var repo = SitePublisher.ParseRepo(Settings.PublishRepo);
        bool ready = repo != null && PublishToken.Exists;
        PublishBarButton.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        TokenStateText.Text = PublishToken.Exists
            ? "A token is saved on this PC. Leave the box empty to keep it, or paste a new one to replace it."
            : "No token saved yet.";
        if (string.IsNullOrEmpty(PublishStatusText.Text))
            PublishStatusText.Text = repo is null ? ""
                : $"Site: {SitePublisher.SiteUrl(repo.Value.Owner, repo.Value.Repo)}" +
                  (Settings.LastPublishedAt is DateTime t ? $"\nLast published {t:ddd MMM d, h:mm tt}." : "\nNot published yet.");
    }

    /// <summary>Saves the repo, token and auto-publish choice. False (with a message) if the repo name isn't usable.</summary>
    private bool SavePublishSettings()
    {
        var text = PublishRepoBox.Text.Trim();
        var repo = SitePublisher.ParseRepo(text);
        if (text.Length > 0 && repo is null)
        {
            Status("The repo should look like owner/name, e.g. Hooee71/fnf-stats.", error: true);
            return false;
        }
        Settings.PublishRepo = repo is { } r ? $"{r.Owner}/{r.Repo}" : null;
        PublishRepoBox.Text = Settings.PublishRepo ?? "";
        Settings.AutoPublish = AutoPublishCheck.IsChecked == true;
        if (PublishTokenBox.Password.Trim().Length > 0)
        {
            try { PublishToken.Save(PublishTokenBox.Password); }
            catch (Exception ex) { Status("Couldn't save the token: " + ex.Message, error: true); return false; }
            PublishTokenBox.Clear();
        }
        if (!Persist()) return false;
        PublishStatusText.Text = "";
        RefreshPublishUi();
        return true;
    }

    private void SavePublish_Click(object sender, RoutedEventArgs e)
    {
        if (SavePublishSettings()) Status("Website settings saved.");
    }

    private void ForgetToken_Click(object sender, RoutedEventArgs e)
    {
        try { PublishToken.Clear(); }
        catch (Exception ex) { Status("Couldn't remove the token: " + ex.Message, error: true); return; }
        PublishTokenBox.Clear();
        RefreshPublishUi();
        Status("Token removed from this PC.");
    }

    private void OpenSite_Click(object sender, RoutedEventArgs e)
    {
        if (SitePublisher.ParseRepo(Settings.PublishRepo) is { } r) OpenUrl(SitePublisher.SiteUrl(r.Owner, r.Repo));
        else Status("Set the website's repo first.", error: true);
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        // From the Settings tab, publish whatever is typed there (saving it first).
        if (ReferenceEquals(sender, PublishNowButton) && !SavePublishSettings()) return;
        await PublishSiteAsync(auto: false);
    }

    /// <summary>
    /// Builds the site from the current data and pushes it to GitHub as one commit. Automatic
    /// publishes stay quiet unless they fail.
    /// </summary>
    private async Task PublishSiteAsync(bool auto)
    {
        if (_publishing) return;
        var repo = SitePublisher.ParseRepo(Settings.PublishRepo);
        var token = PublishToken.Load();
        if (repo is null || token is null)
        {
            if (!auto) Status("Set up the website (repo and token) in Settings first.", error: true);
            return;
        }

        _publishing = true;
        PublishNowButton.IsEnabled = PublishBarButton.IsEnabled = false;
        PublishBarButton.Content = "Publishing…";
        if (!auto) Status("Publishing the site…");
        try
        {
            var files = WebSite.Build(_store.Data, DateTime.Now)
                               .ToDictionary(kv => kv.Key, kv => Encoding.UTF8.GetBytes(kv.Value));
            foreach (var (sitePath, resource) in SiteAssets) files[sitePath] = ReadResource(resource);

            using var http = SitePublisher.CreateClient(_currentVersion, token);
            var result = await SitePublisher.PublishAsync(http, repo.Value.Owner, repo.Value.Repo, files,
                $"Update site: {AllMatches.Count} games ({DateTime.Now:yyyy-MM-dd HH:mm})");

            Settings.LastPublishedAt = DateTime.Now;
            Persist();
            var what = result.Changed ? "Site published" : "Site already up to date";
            PublishStatusText.Text = $"{what} {DateTime.Now:h:mm tt}: {result.SiteUrl}" +
                                     (result.Changed ? "\nGitHub usually shows the change within a minute or two." : "") +
                                     (result.PagesNote is null ? "" : "\n" + result.PagesNote);
            if (result.PagesNote != null) Status(result.PagesNote, error: true);
            else if (!auto) Status($"{what}: {result.SiteUrl}");
        }
        catch (Exception ex)
        {
            var msg = ex is PublishException ? ex.Message : "Publish failed: " + ex.Message;
            PublishStatusText.Text = msg;
            Status(auto ? "Auto-publish failed: " + msg : msg, error: true);
        }
        finally
        {
            _publishing = false;
            PublishNowButton.IsEnabled = PublishBarButton.IsEnabled = true;
            PublishBarButton.Content = "Publish site";
        }
    }
}
