# FNFTracker

Friday Night Fights Arena tracker with Warcraft addon.

A small Windows app for logging 2v2 arena games on Thursday and Friday nights and seeing what's working.
Everything is stored locally in one JSON file. There is no server and no login.

## Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```
dotnet run --project FnfTracker.App
```

Or open `FNFTracker.sln` in Visual Studio 2026 and press F5.

## The FNF addon

`Addon\FNFTracker` is a small WoW addon that records each arena game automatically:
both enemy specs and names, map, result, length, rating change, MMR, damage and healing.

**Install:** in the app, go to Settings, set your WoW folder, and click **Install / update addon**.
(Or copy `Addon\FNFTracker` into `World of Warcraft\_retail_\Interface\AddOns\`.)
Only one of you needs it, since you're in the same games, but it's fine if you both run it.

**How games get into the app:** WoW only writes addon data to disk when you log out, exit or `/reload`.
The app imports at that moment if it's open, and again every time it starts. Settings shows the last import.
Games you already typed in by hand are recognised and not doubled up.

**In game:** `/fnf` lists the last few games the addon has recorded. Unrated 2v2s are imported with a
`skirmish` tag; 3v3 and Solo Shuffle games are ignored.

**Is it safe?** Yes. It only listens for events and reads the end-of-match scoreboard through
Blizzard's addon API. It never casts, clicks, queues or sends anything. The app only reads the saved
file on disk; it never touches the running game.

**If WoW says the addon is out of date:** after a patch, tick "Load out of date AddOns" on the
AddOns screen, or update the `## Interface:` line in `FNFTracker.toc`.
In game, `/dump (select(4, GetBuildInfo()))` prints the current number.

## Getting the app (for your partner)

Download `FNFTracker.exe` from the [latest release](https://github.com/Hooee71/FNFTracker/releases/latest),
put it in a folder of its own (e.g. `C:\Games\FNFTracker`), and run it. Nothing else to install.
Windows may say "Windows protected your PC" the first time because the app isn't code-signed:
click **More info → Run anyway**.

The full walkthrough for players is in [docs/USER_GUIDE.md](docs/USER_GUIDE.md).

After that the app updates itself: when a new version is out, a banner at the bottom offers
**Update now**, which downloads it, checks it, and restarts into the new version. Match data is kept.
Settings → Updates can turn off the startup check or check by hand.

## Releasing a new version

Double-click `release.cmd` (or run it from a terminal). It checks you're on `main` with everything
committed and in sync with GitHub, shows what changed since the last release, suggests the next
version number, and pushes the tag. GitHub Actions then runs the tests, builds the .exe and publishes
the release; everyone's app offers it on the next launch.

The same thing by hand:

```
git tag v0.5.1
git push origin v0.5.1
```

Use the last number for fixes (`v0.5.1` → `v0.5.2`) and the middle one for new features
(`v0.5.2` → `v0.6.0`). To build an .exe locally instead, run `publish.cmd`.

## Using it

**Matches tab:** after each game, press Ctrl+W or Ctrl+L, pick the two enemy classes and specs, and press Ctrl+S.
Map, rating, length, tags and notes are optional. With the addon installed you can skip this entirely. Type in a class box to jump (e.g. "Ro" for Rogue).
"Same as last game" copies the previous opponents for a rematch. Click a row to edit or delete it.

**Our team** starts as your last game's team. If you play more than one comp, buttons for your recent
comps let you switch with one click. Addon-imported games always record both of your actual specs, read
from the scoreboard, with your own spec and your partner's (inspect data) as a backup if the scoreboard hides one.

If you leave **+/-** blank but fill in **Rating after**, the app works out the change from your last rated game.

**Stats tab:** record, streaks, rating line, and win rate broken down by enemy comp, class, spec,
comp type (double DPS vs. healer), map, game number in the night, game length, day of week, night, tag,
and opponents you've played more than once. Green means 60%+, red means 40% or less; anything under 3 games is grey.
Use **Show** to pick which games count: All time, This season, a Single night (◀ ▶ jump between
nights you played), Last 7 days, Last 30 days, or a Custom range. Set the season start in Settings.

A "night" runs 6 AM to 6 AM, so a Friday session that goes past midnight still counts as Friday.

**Settings tab:** your name and data tools:

- **Export for partner / Merge partner's file:** swap files to combine your logs. Games you both logged
  (same result and enemy classes within 10 minutes) are skipped.
- **Export CSV:** for spreadsheets.

## Fonts

Headings, tab names, table headers and the big stat numbers use WoW's UI font, Friz Quadrata, if it's
installed on the PC. Friz Quadrata is commercial, so it isn't bundled; without it the app uses
Marcellus, a free look-alike included under the SIL Open Font License (`FnfTracker.App/Assets/Fonts/OFL.txt`).
Tables and form fields stay in Segoe UI for readability.

## Data

`%LOCALAPPDATA%\FNFTracker\fnf-data.json`, with the previous version kept as `fnf-data.bak.json` on every save.

## Project layout

- `FnfTracker.Core`: models, class/spec/map lists (`GameData.cs`), storage, and all stats. No Windows dependencies.
- `FnfTracker.App`: the WPF app.
- `Addon\FNFTracker`: the WoW addon (also embedded in the app for one-click install).
- `tests/FnfTracker.Tests`: checks for stats, addon import and the updater (`dotnet run --project tests/FnfTracker.Tests`).
- `.github/workflows/release.yml`: builds and publishes releases from version tags.

When Blizzard changes specs or the map pool, edit `FnfTracker.Core/GameData.cs`.

## Things to check after the first night with the addon

The addon was tested against a simulated WoW API, not in the live game. After your first
session, check that wins and losses, enemy specs, the rating change, and "Rating after" all match
what you saw in game. If anything is off, send the file
`World of Warcraft\_retail_\WTF\Account\<ACCOUNT>\SavedVariables\FNFTracker.lua`.

## License

GPL-3.0, see `LICENSE`. The bundled Marcellus font is under the SIL Open Font License (`FnfTracker.App/Assets/Fonts/OFL.txt`).
The realm-name table for check-pvp.fr links (`FnfTracker.Core/RealmNames.cs`) is generated from
[Check-PvP Assistant](https://github.com/Kirom/CheckPvPAssistant) by Kirom, MIT License.
