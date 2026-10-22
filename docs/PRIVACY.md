# FNF Arena Tracker — Privacy Policy

_Last updated: October 22, 2026_

FNF Arena Tracker ("the app") is a free, open-source Windows app for keeping track of World of Warcraft 2v2 arena games. It is made by an independent developer (GitHub: [Hooee71](https://github.com/Hooee71)). Its source code is public at <https://github.com/Hooee71/FNFTracker>, so anything described here can be checked against the code.

**The short version:** the app has no accounts, no ads, no analytics and no telemetry. Your data stays on your PC. The only times it sends anything over the internet are the features described below, and the developer never receives any of your data.

## What the app stores on your PC

The app keeps its data in your Windows user folder (`%LOCALAPPDATA%\FNFTracker`):

- **Match records:** date and time, win or loss, map, rating and MMR, match length, damage and healing, your notes and tags, and the class, spec and in-game character name of each player in the match: you, your teammate and your opponents.
- **Settings:** your character name, region and home realm, the location of your World of Warcraft folder, window layout, and the website and sync addresses you entered.
- **GitHub access token (only if you use Publish):** saved in a separate file, encrypted with Windows' built-in data protection so only your Windows account can read it. It is never included in your match data.

The app reads the data file written by its companion World of Warcraft addon in your WoW folder. When you click **Install / update addon**, it copies the addon's files into your WoW `AddOns` folder. Nothing else on your PC is read or changed.

## When the app uses the internet

Each of these happens only as described:

1. **Publishing your website (off until you set it up).** When you publish, the app sends your match records and stats, including the in-game character names in them, to the GitHub repository you chose, using the token you provided. GitHub then shows them as a **public website** that anyone with the address can see. Don't publish anything you don't want to be public.
2. **Syncing from a website (only if you enter a site address).** The app downloads the published match data from the address you entered and adds it to your records.
3. **Update checks (GitHub download only; can be turned off in Settings).** The copy downloaded from GitHub asks GitHub whether a newer version has been released, and downloads it if you choose to update. No personal data is sent with this request. The Microsoft Store version doesn't do this: the Store installs its updates.
4. **Player look-up links (only when you click one).** Clicking a player opens their profile on check-pvp.fr in your web browser. The link contains that character's region, realm and name.

Like any internet request, these connections let the service on the other end (GitHub or check-pvp.fr) see your IP address and basic request details. Those services handle that under their own policies: see the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) and check-pvp.fr's own terms.

## What the app does not do

- It does not collect analytics, crash reports or usage statistics.
- It does not show ads or use advertising IDs.
- It does not send any data to the developer.
- It does not sell or share your data with anyone. The only data that leaves your PC is what you choose to publish, and it goes only where you send it.
- It does not read your Battle.net account, password or anything outside the files described above.

## Your choices and control

- **See your data:** everything is in the app and in the files in `%LOCALAPPDATA%\FNFTracker`.
- **Edit or delete:** you can edit or delete any match in the app. To remove everything, delete the `%LOCALAPPDATA%\FNFTracker` folder (or uninstall the app and then delete that folder).
- **Take down your public website:** delete the files or the repository on GitHub, or turn off GitHub Pages for it. Remember that other people who already synced from your site keep a copy until their app next syncs.
- **Remove your GitHub token:** clear it in the app's Settings and revoke it on GitHub under **Settings → Developer settings → Personal access tokens**.
- **Turn off update checks (GitHub download):** use the option in the app's Settings.

## Children

The app is not directed at children under 13 and does not knowingly collect information from them.

## Changes to this policy

If the app's data handling changes, this page will be updated and the date at the top will change. The full history of changes is visible in the GitHub repository.

## Contact

Questions or concerns: open an issue at <https://github.com/Hooee71/FNFTracker/issues>.
