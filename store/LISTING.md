# Microsoft Store listing text

Copy-paste text for Partner Center → your app → Store listings → English. Keep Blizzard artwork,
logos and game screenshots out of the listing: use screenshots of the app itself.

## Product name

FNF Arena Tracker

## Short description

Keep score of your 2v2 arena nights. FNF Arena Tracker records every game automatically through a small companion addon, then shows what's working: your win rate against each enemy comp, map, class and spec, and how your rating moves night by night.

## Description

FNF Arena Tracker is a simple, free tracker for World of Warcraft 2v2 arena partners.

Install the companion addon from inside the app with one click. From then on, every rated 2v2 game you play is recorded automatically: both teams' classes, specs and names, the map, result, match length, rating change, MMR, damage and healing. Log out or /reload and the games appear in the app. Nothing to type in.

See what's working:
• Win rate against every enemy comp, class and spec
• Results by map and by night, with your season record
• Rating history game by game, plus how you do against higher and lower MMR
• Notes and tags on any game, and easy fixes for any game that needs editing

Share it with your partner and friends:
• Publish your Matches and Stats pages to your own free public website on GitHub Pages
• Friends can follow your site, and their copy of the app stays in sync with yours

Private by design: no account, no ads, no tracking. Your games are kept in one file on your PC, and nothing leaves it unless you choose to publish.

The addon only reads the end-of-match scoreboard through Blizzard's standard addon API. It never automates anything in the game, and the app never touches the running game.

FNF Arena Tracker is a fan-made app and is not affiliated with or endorsed by Blizzard Entertainment. World of Warcraft is a trademark of Blizzard Entertainment, Inc.

## Features (one per line)

Automatic game recording with a one-click companion addon
Win rate by enemy comp, healer and DPS spec, and map
Rating history and results by MMR gap
Notes, tags and per-game editing
Optional public stats website on GitHub Pages
Friends' copies sync from your published site
No account, no ads, no tracking

## Search terms (up to 7)

arena tracker
2v2 arena
pvp stats
arena stats
win rate tracker
arena comps
pvp tracker

## Category

Utilities & tools

## Privacy policy URL

https://github.com/Hooee71/FNFTracker/blob/main/docs/PRIVACY.md

## Submission notes (for the certification team)

The package declares the unvirtualizedResources capability with file-system write virtualization disabled so the app's data file stays at %LOCALAPPDATA%\FNFTracker\fnf-data.json. That's the same location used by the non-Store version, so existing users keep their records, and the app's "open data folder" button opens the real file. The app also writes its own companion addon into the user's World of Warcraft AddOns folder when the user clicks "Install / update addon".
