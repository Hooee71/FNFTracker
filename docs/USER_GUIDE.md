# FNF Arena Tracker: User Guide

FNF Arena Tracker logs our 2v2 arena games on Thursday and Friday nights and shows what's working
and what isn't: which comps beat us, which maps we lose on, whether we fall apart after game ten.

Everything stays on your own PC in one file. There's no account, no login and no server. If you
want, the app can also publish the Matches and Stats pages to a free public website (section 7).

---

## 1. Install

1. Download **FNFTracker.exe** from the
   [latest release](https://github.com/Hooee71/FNFTracker/releases/latest).
2. Make a folder of its own for it, for example `C:\Games\FNFTracker`, and move the .exe there.
   (Not *Program Files* and not your Desktop. The app updates itself by replacing the .exe, and it
   needs a folder it's allowed to write to.)
3. Double-click it.

The first time, Windows may show **"Windows protected your PC"**. The app isn't code-signed (that
costs money), which is why. Click **More info**, then **Run anyway**. You'll only see this once.

Nothing else needs installing. Pin it to the taskbar if you like.

---

## 2. First-time setup (two minutes)

Open the **Settings** tab.

1. **Your name.** Type your name and click **Save settings**. It's stamped on games you record, so
   when we combine our logs you can tell who recorded what.
   Below it, set **your realm and region** too: enemy names link to check-pvp.fr, and the scoreboard
   leaves the realm off enemies on your own realm, so the app needs yours to fill that in.
2. **WoW folder.** If WoW is in the usual place (`C:\Program Files (x86)\World of Warcraft`) this is
   already filled in. Otherwise click **Browse…** and pick the folder that *contains* `_retail_`
   (not `_retail_` itself).
3. **Install the addon.** Click **Install / update addon**. If WoW is open, type `/reload` in game.
   See the next section for what the addon does.
4. **Season start (optional).** Set this to the day the current PvP season began. The **This season**
   view on the Stats tab and the record at the top of the window then count only this season.

---

## 3. The addon: games record themselves

The FNF addon watches your arena games and records each one automatically: both enemies' specs and
names, map, win or loss, game length, Dampening at the end, rating change and MMR. With it installed you don't need to
type anything in.

**Only one of us needs it**, since we're in the same games. Running it on both PCs does no harm.

**When games show up in the app.** WoW only saves addon data to disk when you **log out, exit the
game or type `/reload`**. At that moment the app picks the games up automatically (if it's open),
and it also checks every time it starts. So the usual flow is: play your games, log out, and they're
there. Settings shows when the last import happened and how many games it found.

**In game,** type `/fnf` to see the last few games the addon has recorded.

**What it records:**

- Rated 2v2: yes.
- Unrated 2v2 (skirmish): yes, tagged `skirmish` so you can see or ignore them on the Stats tab.
- 3v3 and Solo Shuffle: ignored.

**Is it safe?** Yes. It only listens for game events and reads the end-of-match scoreboard through
Blizzard's normal addon API. It never casts, clicks, queues or sends anything. The app never touches
the running game; it only reads the file WoW saves.

**After a WoW patch,** WoW may mark the addon as out of date and not load it. On the character select
screen, open **AddOns** and tick **Load out of date AddOns**. A newer version of the app usually
brings an updated addon too; after updating, click **Install / update addon** again.

---

## 4. Matches tab

The list of games, newest first. Games come in from the addon on their own; there's no form for
typing them in.

Across the top of the window you'll see tonight's record and streak, and your overall record (or
season record, if you've set a season start).

### Fixing or deleting a game

Each row ends in two small icons: a **pencil** to edit the game and a **bin** to delete it. They're
faint until you point at the row.

- **Edit:** click the pencil, double-click the row, or select it and press **Enter**. The game opens in
  a pop-up with everything you can change: Win/Loss, both opponents (class, spec, Name-Realm), our
  team, map, rating after, +/-, their MMR, length, tags, notes and when it was played. **Save** (or
  Ctrl+S) keeps your changes; **Cancel** (or Esc) throws them away. If something doesn't make sense,
  like a rating that isn't a number, it says so at the bottom of the pop-up and nothing is saved until
  it's fixed.
- **Delete:** click the bin, or select the row and press **Del**. It asks first. A deleted addon game
  won't come back on the next import.

Editing never touches the scoreboard numbers behind the DPS/HPS column.

| Field | What to put |
|---|---|
| **Map** | Pick from the list or type it. |
| **Rating after** | Your 2v2 rating after the game, e.g. `1840`. |
| **+/-** | Rating change, e.g. `14` or `-11`. Clear it and the app works it out from your previous rated game. |
| **Length** | `4:32`, or `4.5` for four and a half minutes. |
| **Tags** | Short labels, comma separated: `bad opener, dampening, got trained`. Each tag gets its own win-rate line on the Stats tab. |
| **Notes** | Anything you want to remember. |
| **Played at** | `yyyy-MM-dd HH:mm`, e.g. `2026-10-23 21:40`. |

### Columns

**Night** and **#** (game number in that night), **Time**, **W/L**, **Enemies** (in class colours,
with names; click a name to open that player's check-pvp.fr profile), **Map**, **Rating**, **+/-**, **Their MMR** (addon games only), **DPS/HPS** and
**Notes**. Click a column header to sort by it.

**DPS/HPS** (addon games only) compares your team's total damage and healing with the enemy team's:
`DPS -18%` means you did 18% less damage than they did. Green when you were ahead, red when you were
behind, with a small bar that grows right (ahead) or left (behind) from a centre tick and fills at a
50% gap. Both teams play the same length of game, so the gap in totals is the same as the gap in DPS
and HPS. Hover the cell for the actual totals, per-second rates, game length and Dampening at the end. Sorting the column
sorts by the damage gap. (Game length is still recorded, and editable from the pencil.)

HPS turns grey when the teams brought a different number of healers: a healer comp out-heals double
DPS every time, so that number says nothing about who played better. Damage is always comparable.

---

## 5. Stats tab

### Choosing which games count

Use **Show** at the top:

- **All time**
- **This season**: from the season start set in Settings.
- **Single night**: one session. Use **◀ ▶** to jump between nights you actually played, or pick a
  date.
- **Last 7 days** / **Last 30 days**
- **Custom range**: pick a from and to date. Leave one blank for no limit on that side.

The app remembers your choice.

### What you'll see

- **Summary cards:** record, win rate, current streak, best win streak, and number of nights played.
- **Rating:** current and peak rating, plus a graph of rating over time. Each dot is a game and
  dashed lines separate nights.
- **MMR:** the highest, average and lowest enemy team MMR you've faced, and whether you won those
  games. This needs addon-recorded games.
- **Damage & healing vs. them** (addon games only): your team's total damage and healing compared
  with the enemy team's, for all games, wins and losses. `-20%` DPS means you did 20% less damage
  than them. Each figure comes from the summed totals over those games. Healing only counts games
  where both teams brought the same number of healers; hover an HPS figure to see how many games
  it's built from.
- **Enemy comps:** your record and win % against each comp, plus the same DPS / HPS comparison for
  the addon-recorded games against it.
- **Enemy healers** and **Enemy DPS:** your record against each class + spec, healers and DPS in
  separate tables.
- **Win-rate tables**, broken down by:
  comp type (double DPS vs. with a healer), map, game number in
  the night, game length, Dampening at the end (addon 0.1.6+ games), day of week, night, tag, and repeat opponents (players you've met more
  than once). When there's enough data you'll also see enemy MMR vs. yours, and our comps if we've
  played more than one.

**Colours:** green means 60% or better, red means 40% or worse. Anything with fewer than 3 games
stays grey, because two games doesn't tell you much.

### What counts as a "night"

A night runs from **6 AM to 6 AM**. A Friday session that goes past midnight still counts as Friday.

---

## 6. Sharing logs with your partner

If we've both been logging games (or one of us has games the other doesn't), we can combine them:

1. **You:** Settings, then **Export for partner…**. Save the file and send it over (Discord is fine).
2. **Them:** Settings, then **Merge partner's file…**, and pick the file.

It reports how many games were added and how many were skipped. A game counts as already there if
both logs have the same result against the same enemy classes within 10 minutes of each other, so
games we both recorded don't get doubled up. You can merge the same file twice without harm.

**Export CSV…** saves every game as a spreadsheet file if you want to dig in with Excel or Google
Sheets.

---

## 7. Public website

The app can publish the **Matches** and **Stats** pages as a website anyone can open, on your phone,
in Discord, wherever. It uses GitHub Pages, which is free. The site is just a copy of the pages:
it has every game with the enemy names (linked to check-pvp.fr), and the stats for this season,
all time, the last 30 days and the latest night. Your notes are left off. Friends who run the
app can also follow the site to get the games in their own copy (see below).

**Only one of us needs to do this.** Whoever publishes should be the one with the merged games.

### Setting it up (once, about five minutes)

1. **Make a repo for the site.** On GitHub, click **New repository**. Name it, for example,
   `fnf-stats`, set it to **Public**, and create it. (Leaving it empty is fine.)
2. **Make a token** so the app can update it. Go to GitHub **Settings → Developer settings →
   Personal access tokens → Fine-grained tokens → Generate new token**:
   - **Expiration:** up to a year (you'll need a new one when it runs out).
   - **Repository access:** *Only select repositories*, and pick the site repo.
   - **Permissions → Repository permissions:** **Contents**, **Pages** and **Administration**,
     each set to **Read and write**. (GitHub asks for Administration to switch Pages on; since the
     token only covers the site repo, it can't touch anything else.)
   - Generate it and copy the token (it starts with `github_pat_`).
3. **In the app,** Settings, under **Website**: type the repo as `yourname/fnf-stats`, paste the
   token, and click **Publish now**.

The first publish turns GitHub Pages on for you. The address is
`https://yourname.github.io/fnf-stats/`, and **Open site** takes you there. The very first time,
GitHub can take a few minutes before the page appears.

### Publishing

- **Publish site** at the bottom right of the window updates the website in one click. GitHub
  usually shows the change within a minute or two.
- Tick **Publish automatically when the addon brings in new games** and you won't need to click
  anything: log out of WoW and the site updates by itself.
- **Open pages refresh themselves.** A page left open checks every minute for a newer publish and
  reloads when there is one, keeping your filter, search, sort, scroll position and Stats range.

The token is stored encrypted on your PC, separately from your games, so it is **not** included when
you send your partner an export. **Forget token** removes it. If a publish says the token was
rejected, it has probably expired: make a new one and paste it in.

### Following a friend's site

Friends who run FNF Tracker can keep their copy in step with your site, with no GitHub account or
token. Each publish also puts the games on the site as data (`matches.json`, every game except your
notes), and their app reads that.

1. Send them your site address, e.g. `https://yourname.github.io/fnf-stats/`.
2. In their app, Settings, under **Follow a friend's site**: paste the address and click **Sync now**.
3. Leave **Sync every time the app starts** ticked and it stays up to date by itself.

What syncing does:

- **New games on the site** come in. If they already recorded the same game themselves (same result
  and enemy classes within 10 minutes), theirs is kept and the site's copy is skipped.
- **Your edits** replace their copy of that game, and **games you delete** are removed from theirs.
  Any notes they wrote on a synced game are kept.
- **Their own games** (from their addon) are never changed.
- If they **delete** a synced game, it stays deleted; if they **edit** one, the next sync puts your
  version back.
- **Stop following** asks whether to keep the synced games as their own or remove them.

If a partner used **Merge** on your exports before, their copies are recognised and simply start
following the site; nothing is doubled up. The site needs to be published once from this version of
the app (or later) before friends can sync from it.

---

## 8. Updates

The app checks for a new version when it starts. When one is available, a banner appears at the
bottom right:

- **Update now:** downloads it, checks it, and restarts into the new version. Your games and
  settings are kept.
- **What's new:** opens the release page.
- **Later:** hides the banner until next time.

In Settings, under **Updates**, you can see which version you're running, check by hand, or turn off
the check at startup.

If the update says Windows wouldn't let it replace itself, the .exe is in a protected folder. Move
it to a folder of its own like `C:\Games\FNFTracker` and try again.

---

## 9. Your data and backups

Your games live in one file:

```
%LOCALAPPDATA%\FNFTracker\fnf-data.json
```

Settings, then **Open data folder**, takes you straight there. Every time the app saves, it keeps
the previous version next to it as `fnf-data.bak.json`.

To back up, copy that folder somewhere. To move to a new PC, install the app there and copy
`fnf-data.json` into the same place (close the app first).

---

## 10. Troubleshooting

**Games from the addon aren't showing up.**

- Did you log out, exit or `/reload` after playing? WoW doesn't save addon data until then.
- Type `/fnf` in game. If it lists the games, the addon is working; log out and they'll come through.
  If it says nothing or the command doesn't exist, the addon isn't loaded. Check the AddOns list on
  character select (and tick **Load out of date AddOns** after a patch).
- In Settings, check the WoW folder is right, then click **Import now**. The text underneath says
  what it found.
- "Incomplete (left early?) skipped" means a game where the scoreboard never finished, usually
  because someone left before the end. Add that one by hand if you want it.

**"Set your WoW folder first."** Click **Browse…** in Settings and pick the folder that contains
`_retail_`.

**"Windows wouldn't let the app write to…" when installing the addon.** WoW is in a protected
folder. Either run FNFTracker once as administrator (right-click, **Run as administrator**) and
click **Install / update addon** again, or ask Steve for the `FNFTracker` addon folder and copy it
into `World of Warcraft\_retail_\Interface\AddOns\` yourself.

**"Couldn't read your data file."** The app stops saving so it can't make things worse. Close the
app, open the data folder, and either send `fnf-data.json` to Steve, or rename it to something like
`fnf-data.broken.json` and rename `fnf-data.bak.json` to `fnf-data.json` to go back one save.

**A game recorded by the addon looks wrong** (wrong result, specs, or rating). Fix it with the row's
pencil icon, and let Steve know. It helps him to have the file
`World of Warcraft\_retail_\WTF\Account\<your account>\SavedVariables\FNFTracker.lua`.

**The app looks plainer than Steve's.** The headings use WoW's own font if it's installed on your PC.
If it isn't, the app uses a close free look-alike. Nothing's broken.
