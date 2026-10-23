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
var toughDps = Stats.ToughestSpec(ms, healers: false);
Check(toughDps?.Label=="Arms Warrior" && toughDps.Record=="4-2", "toughest DPS " + toughDps?.Label + " " + toughDps?.Record);
Check(Stats.ToughestSpec(ms, healers: true)?.Label=="Restoration Druid", "toughest healer");
Check(Stats.ToughestSpec(ms.Take(2), healers: true)==null, "toughest needs 3+ games");
{
  // Toughest = most losses, not worst win rate; never a spec we haven't lost to.
  var t = new List<Match>();
  void G(bool won, string cls, string spec) => t.Add(new Match { PlayedAt = fri.AddMinutes(t.Count * 10), Won = won,
    Enemies = new() { P(cls, spec), P("Priest", "Holy") } });
  for (int i = 0; i < 4; i++) G(true, "Death Knight", "Unholy");                           // 4-0
  G(false, "Rogue", "Assassination"); G(false, "Rogue", "Assassination"); G(true, "Rogue", "Assassination"); // 1-2
  for (int i = 0; i < 3; i++) G(i < 2, "Warrior", "Arms");                                 // 2-1
  for (int i = 0; i < 6; i++) G(i < 3, "Mage", "Fire");                                    // 3-3
  var dps = Stats.ToughestSpec(t, healers: false);
  Check(dps?.Label == "Fire Mage" && dps.Record == "3-3", $"toughest DPS = most losses (3-3 beats 1-2): {dps?.Label} {dps?.Record}");
  var undefeated = t.Where(m => m.Enemies[0].Class == "Death Knight").ToList();
  Check(Stats.ToughestSpec(undefeated, healers: false) == null, "spec we've never lost to is never toughest");
  Check(Stats.ToughestSpec(t.Take(7), healers: false)?.Label == "Assassination Rogue", "3 games with losses qualifies");
  Check(Stats.ToughestSpec(t, healers: true)?.Label == "Holy Priest", "toughest healer");
}
foreach (var (t, exp) in new[]{("4:32",272),("4m32s",272),("6",360),("272",272),("4.5",270),("5m",300)})
  Check(Stats.ParseDuration(t)==exp, $"duration '{t}' -> {Stats.ParseDuration(t)}");
Check(Stats.FormatDuration(272)=="4:32", "format duration");
// damage / healing vs. the enemy team
{
  var a = new List<PlayerSlot> { new() { Damage = 600_000, Healing = 50_000 }, new() { Damage = 300_000, Healing = 400_000 } };
  var b = new List<PlayerSlot> { new() { Damage = 700_000, Healing = 0 }, new() { Damage = 500_000, Healing = 300_000 } };
  var ourDmg = Stats.TeamTotal(a, p => p.Damage); var theirDmg = Stats.TeamTotal(b, p => p.Damage);
  Check(ourDmg == 900_000 && theirDmg == 1_200_000, "team damage totals");
  Check(Stats.FormatPercentVs(Stats.PercentVs(ourDmg, theirDmg)) == "-25%", "behind on damage -> " + Stats.FormatPercentVs(Stats.PercentVs(ourDmg, theirDmg)));
  Check(Stats.FormatPercentVs(Stats.PercentVs(Stats.TeamTotal(a, p => p.Healing), Stats.TeamTotal(b, p => p.Healing))) == "+50%", "ahead on healing");
  Check(Stats.TeamTotal(new List<PlayerSlot> { P("Rogue","Subtlety") }, p => p.Damage) == null, "no scoreboard -> no total");
  Check(Stats.PercentVs(100, 0) == null && Stats.PercentVs(0, 0) == 0 && Stats.PercentVs(null, 5) == null, "zero / missing edges");
  Check(Stats.FormatPercentVs(0.4) == "0%" && Stats.FormatPercentVs(-0.6) == "-1%", "rounding");
  Check(Stats.FormatAmount(1_234_567) == "1.23M" && Stats.FormatAmount(4_120) == "4.1k", "compact amounts");

  // healer-count check, and the Stats-tab aggregates
  PlayerSlot S(string c, string sp, long d, long h) => new() { Class = c, Spec = sp, Damage = d, Healing = h };
  var ourTeam = new List<PlayerSlot> { S("Rogue","Subtlety",500,0), S("Priest","Discipline",200,400) };
  var dd = new List<PlayerSlot> { S("Warrior","Arms",800,50), S("Mage","Frost",600,50) };      // double DPS
  var heals = new List<PlayerSlot> { S("Warrior","Arms",600,50), S("Druid","Restoration",100,300) };
  var g1 = new Match { Won = false, OurTeam = ourTeam, Enemies = dd };      // 700 vs 1400 dmg
  var g2 = new Match { Won = true, OurTeam = ourTeam, Enemies = heals };    // 700 vs 700 dmg, 400 vs 350 heal
  var g3 = new Match { Won = true, OurTeam = us, Enemies = dd };           // no scoreboard
  Check(!Stats.HealingComparable(g1) && Stats.HealingComparable(g2), "healer counts decide healing comparability");
  Check(Stats.HealingComparable(new Match { OurTeam = ourTeam, Enemies = new() { P("Warrior","") , P("Mage","Frost") } }), "unknown spec -> assume comparable");
  var byRes = Stats.OutputByResult(new[] { g1, g2, g3 });
  Check(byRes.Select(r => r.Label).SequenceEqual(new[] { "All games", "Wins", "Losses" }), "output by result rows");
  Check(byRes[0].Games == 2 && byRes[0].DpsGames == 2 && byRes[0].HpsGames == 1, "manual game skipped, healing only like-for-like");
  Check(Stats.FormatPercentVs(byRes[0].DpsPct) == "-33%", "summed damage gap " + Stats.FormatPercentVs(byRes[0].DpsPct));   // 1400 vs 2100
  Check(Stats.FormatPercentVs(byRes[1].HpsPct) == "+14%" && byRes[2].HpsPct == null, "healing gap in wins only");
  var byComp = Stats.OutputByEnemyComp(new[] { g1, g2, g3 });
  Check(byComp.Count == 2 && byComp[0].Label == "Arms Warrior + Frost Mage" && byComp[0].DpsPct == -50, "comp table, worst gap first");
  Check(Stats.OutputByResult(new[] { g3 }).Count == 0, "no addon games -> empty");
}
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
fails += WebSiteCheck.Run();
fails += SyncCheck.Run();
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
