using FnfTracker.Core;
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
PlayerSlot P(string c, string s, string? n = null) => new() { Class = c, Spec = s, Name = n };
var us = new List<PlayerSlot> { P("Rogue","Subtlety"), P("Priest","Discipline") };
var fri = new DateTime(2026,10,16,21,0,0); // Friday 9pm
var ms = new List<Match>();
int r = 1800;
for (int i = 0; i < 12; i++) {
  bool won = i % 3 != 0;
  r += won ? 12 : -10;
  ms.Add(new Match { PlayedAt = fri.AddMinutes(i*15), Won = won, Map = i%2==0 ? "Nagrand Arena" : "Mugambala",
    OurTeam = us, Enemies = i < 6 ? new(){ P("Warrior","Arms","Bonk-Stormrage"), P("Druid","Restoration") } : new(){ P("Mage","Frost"), P("Rogue","Assassination") },
    RatingAfter = r, DurationSeconds = 120 + i*45, Tags = i==0 ? new(){"Bad opener"} : new() });
}
ms.Add(new Match { PlayedAt = new DateTime(2026,10,17,0,30,0), Won = true, Map="Mugambala", OurTeam = us, Enemies = new(){ P("Mage","Frost"), P("Rogue","Assassination") } });
var sum = Stats.Summarize(ms);
Check(sum.Games==13 && sum.Wins==9, $"summary {sum.Wins}-{sum.Losses}");
Check(sum.StreakText=="W3", "current streak " + sum.StreakText);
Check(sum.BestWinStreak==3, "best streak " + sum.BestWinStreak);
Check(sum.Nights==1, "nights (games past midnight stay Friday) " + sum.Nights);
Check(ms.Last().PlayedAt.Day==17 && ms.Last().Night.DayOfWeek==DayOfWeek.Friday, "after-midnight game counts as Friday");
var comps = Stats.ByEnemyComp(ms);
Check(comps.Count==2 && comps.Any(c => c.Label=="Arms Warrior + Restoration Druid" && c.Games==6), "comp key sorted: " + string.Join(" | ", comps.Select(c=>$"{c.Label} {c.Record}")));
Check(Stats.ByCompType(ms).Select(c=>c.Label).OrderBy(x=>x, StringComparer.Ordinal).SequenceEqual(new[]{"DPS + Healer","Double DPS"}), "comp types");
Check(Stats.ByGameNumber(ms).Select(g=>g.Label).SequenceEqual(new[]{"Games 1-5","Games 6-10","Games 11-15"}), "game number buckets " + string.Join(",", Stats.ByGameNumber(ms).Select(g=>$"{g.Label}:{g.Record}")));
Check(Stats.RepeatOpponents(ms).Single().Games==6, "repeat opponent");
Check(Stats.ByDuration(ms).First().Label=="Under 3 min", "duration order " + string.Join(",", Stats.ByDuration(ms).Select(d=>d.Label)));
Check(Stats.ByTag(ms).Single().Label=="bad opener", "tags normalized");
foreach (var (t, exp) in new[]{("4:32",272),("4m32s",272),("6",360),("272",272),("4.5",270),("5m",300)})
  Check(Stats.ParseDuration(t)==exp, $"duration '{t}' -> {Stats.ParseDuration(t)}");
Check(Stats.FormatDuration(272)=="4:32", "format duration");
// store round trip + merge dedupe
var path = Path.Combine(Path.GetTempPath(), "fnf-test-" + Guid.NewGuid() + ".json");
var store = new DataStore(path); store.Load();
foreach (var m in ms) store.Upsert(m);
store.Save(); store.Save(); // second save exercises File.Replace + backup
var again = new DataStore(path); again.Load();
Check(again.Data.Matches.Count==13, "round trip");
{
  // Files saved before Default team was removed still load.
  var oldPath = Path.Combine(Path.GetTempPath(), "fnf-old-" + Guid.NewGuid() + ".json");
  File.WriteAllText(oldPath, "{\"Settings\":{\"MyName\":\"Steve\",\"DefaultTeam\":[{\"Class\":\"Rogue\",\"Spec\":\"Subtlety\"},{}]},\"Matches\":[]}");
  var old = new DataStore(oldPath); old.Load();
  Check(old.Data.Settings.MyName == "Steve", "old file with DefaultTeam loads");
  File.Delete(oldPath);
}
Check(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)+".bak.json")), "backup written");
var friend = new FnfData();
friend.Matches.Add(new Match { PlayedAt = ms[3].PlayedAt.AddMinutes(1), Won = ms[3].Won, Enemies = new(){ P("Druid","Restoration"), P("Warrior","Arms") } }); // same game logged by friend
friend.Matches.Add(new Match { PlayedAt = fri.AddDays(6), Won = true, Enemies = new(){ P("Hunter","Marksmanship"), P("Shaman","Restoration") } });
friend.Matches.Add(ms[0]); // same Id
var res = again.MergeFrom(friend);
Check(res.Added==1 && res.Duplicates==2, $"merge added {res.Added}, dupes {res.Duplicates}");
Check(Stats.Summarize(Stats.Filter(again.Data.Matches, fri.AddDays(1)).ToList()).Games==1, "season filter counts by night");
{
  var mm = new List<Match> {
    new() { PlayedAt = fri, Won = true, EnemyMmr = 1750 },
    new() { PlayedAt = fri.AddMinutes(5), Won = false, EnemyMmr = 1910 },
    new() { PlayedAt = fri.AddMinutes(10), Won = true, EnemyMmr = 1820 },
    new() { PlayedAt = fri.AddMinutes(15), Won = true },
  };
  var o = Stats.OpponentMmrSummary(mm);
  Check(o.Games == 3 && o.Highest == 1910 && o.Lowest == 1750 && o.Average == 1827 && o.WonVsHighest == false && o.WonVsLowest == true, $"opponent mmr {o}");
  Check(Stats.OpponentMmrSummary(new List<Match>()).Highest == null, "opponent mmr empty");
}
{
  var today = new DateOnly(2026, 10, 22);
  var (a, b) = StatsRange.Resolve(RangeKind.SingleNight, today, null, new DateOnly(2026,10,16), null, null);
  Check(a == b && a == new DateOnly(2026,10,16), "single night");
  (a, b) = StatsRange.Resolve(RangeKind.Last7Days, today, null, null, null, null);
  Check(a == new DateOnly(2026,10,16) && b == today, $"last 7 days {a}..{b}");
  (a, b) = StatsRange.Resolve(RangeKind.Last30Days, today, null, null, null, null);
  Check(a == new DateOnly(2026,9,23), $"last 30 days {a}");
  (a, b) = StatsRange.Resolve(RangeKind.Custom, today, null, null, new DateOnly(2026,10,20), new DateOnly(2026,10,1));
  Check(a == new DateOnly(2026,10,1) && b == new DateOnly(2026,10,20), "custom swapped");
  (a, b) = StatsRange.Resolve(RangeKind.Season, today, new DateTime(2026,9,1), null, null, null);
  Check(a == new DateOnly(2026,9,1) && b == null, "season");
  Check(StatsRange.Resolve(RangeKind.Season, today, null, null, null, null) == (null, null), "season unset = all");
  Check(StatsRange.CurrentNight(new DateTime(2026,10,17,1,30,0)) == new DateOnly(2026,10,16), "1:30am is previous night");
  var games = new List<Match> {
    new() { PlayedAt = new DateTime(2026,10,9,21,0,0) },
    new() { PlayedAt = new DateTime(2026,10,16,21,0,0) },
    new() { PlayedAt = new DateTime(2026,10,17,0,45,0) },   // still Fri 16th night
    new() { PlayedAt = new DateTime(2026,10,17,21,0,0) },
  };
  Check(StatsRange.Apply(games, new DateOnly(2026,10,16), new DateOnly(2026,10,16)).Count() == 2, "single night includes after-midnight game");
  Check(StatsRange.LatestNight(games) == new DateOnly(2026,10,17), "latest night");
  Check(StatsRange.StepNight(games, new DateOnly(2026,10,16), -1) == new DateOnly(2026,10,9), "step back skips empty nights");
  Check(StatsRange.StepNight(games, new DateOnly(2026,10,17), +1) == null, "no later night");
  Check(StatsRange.Describe(new DateOnly(2026,10,16), new DateOnly(2026,10,16)) == "Fri Oct 16", "describe single");
  Check(StatsRange.Describe(null, null) == "All games", "describe all");
}
{
  // Our team: pre-fill from the last game, quick picks for recent comps.
  var ret = new List<PlayerSlot> { P("Paladin","Retribution","Stevo"), P("Priest","Discipline","Buddy") };
  var dh  = new List<PlayerSlot> { P("Paladin","Retribution"), P("Shaman","Restoration") };
  var t = new List<Match> {
    new() { PlayedAt = fri, OurTeam = ret }, new() { PlayedAt = fri.AddMinutes(5), OurTeam = ret },
    new() { PlayedAt = fri.AddMinutes(10), OurTeam = ret }, new() { PlayedAt = fri.AddMinutes(15), OurTeam = dh },
    new() { PlayedAt = fri.AddMinutes(20), OurTeam = new() { P("Paladin","") , P("Shaman","Restoration") } }, // spec unknown
  };
  var last = Stats.LastTeam(t);
  Check(Comp.Key(last) == "Paladin + Restoration Shaman", "last team is newest game: " + Comp.Key(last));
  Check(last.All(p => p.Name == null && p.Damage == null), "last team carries class/spec only");
  Check(Stats.LastTeam(new List<Match>()).Count == 0, "no games, no pre-fill");
  var picks = Stats.RecentTeams(t).Select(Comp.Key).ToList();
  Check(picks.SequenceEqual(new[] { "Discipline Priest + Retribution Paladin", "Restoration Shaman + Retribution Paladin" }), "recent teams: " + string.Join(" | ", picks));
}
{
  // check-pvp.fr profile links
  Check(CheckPvp.ProfileUrl("Bonk-Area52", null, "us") == "https://check-pvp.fr/us/Area%2052/Bonk", "link cross-realm: " + CheckPvp.ProfileUrl("Bonk-Area52", null, "us"));
  Check(CheckPvp.ProfileUrl("Treehug", "Illidan", "us") == "https://check-pvp.fr/us/Illidan/Treehug", "link same realm uses home realm");
  Check(CheckPvp.ProfileUrl("Treehug", null, "us") == null, "no realm, no link");
  Check(CheckPvp.ProfileUrl(null, "Illidan", "us") == null && CheckPvp.ProfileUrl("  ", "Illidan", "us") == null, "no name, no link");
  Check(CheckPvp.ProfileUrl("Bonk-Illidan", "Stormrage", "EU") == "https://check-pvp.fr/eu/Illidan/Bonk", "own realm beats home realm; region lowercased");
  Check(CheckPvp.ProfileUrl("Bonk-Illidan", null, "xx")!.StartsWith("https://check-pvp.fr/us/"), "unknown region falls back to us");
  Check(CheckPvp.DisplayRealm("Blade'sEdge") == "Blade's Edge", "realm table: " + CheckPvp.DisplayRealm("Blade'sEdge"));
  Check(CheckPvp.DisplayRealm("AeriePeak") == "Aerie Peak", "aerie peak");
  Check(CheckPvp.DisplayRealm("area 52") == "Area 52" && CheckPvp.DisplayRealm("AREA52") == "Area 52", "loose realm match");
  Check(CheckPvp.DisplayRealm("MadeUpRealm9") == "Made Up Realm 9", "unknown realm spaced out: " + CheckPvp.DisplayRealm("MadeUpRealm9"));
  Check(CheckPvp.ProfileUrl("Ñoño-Ragnaros", null, "eu") == "https://check-pvp.fr/eu/Ragnaros/%C3%91o%C3%B1o", "non-ASCII name encoded");
  Check(CheckPvp.RealmOf("Stevo-Area52") == "Area52" && CheckPvp.RealmOf("Steve") == null, "realm of recordedBy");
}
fails += ImportCheck.Run(Path.Combine(AppContext.BaseDirectory, "TestData", "SavedVariables-sample.lua"));
fails += UpdaterCheck.Run();
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
