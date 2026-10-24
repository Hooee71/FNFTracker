using FnfTracker.Core;
public static class ImportCheck {
  public static int Run(string svPath) {
    int fails = 0;
    void Check(bool ok, string what) { System.Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    var root = Path.Combine(Path.GetTempPath(), "fnf-wow-" + Guid.NewGuid());
    var sv = Path.Combine(root, "_retail_", "WTF", "Account", "STEVE123", "SavedVariables");
    Directory.CreateDirectory(sv);
    File.Copy(svPath, Path.Combine(sv, "FNFTracker.lua"));
    Check(AddonImport.FindSavedVariables(root).Count == 1, "finds file under _retail_");
    Check(AddonImport.FindSavedVariables(Path.Combine(root, "_retail_")).Count == 1, "finds file when pointed at _retail_");

    var store = new DataStore(Path.Combine(root, "data.json")); store.Load();
    // A game typed by hand right after game 1 (no specs) should be recognised as the same game.
    var recs = AddonImport.ReadRecords(File.ReadAllText(svPath));
    var g1 = AddonImport.ToMatch(recs[0], out _)!;
    store.Upsert(new Match { PlayedAt = g1.PlayedAt.AddMinutes(1), Won = true,
       Enemies = new() { new PlayerSlot { Class = "Druid" }, new PlayerSlot { Class = "Warrior" } } });

    var r = AddonImport.ImportInto(store, root);
    System.Console.WriteLine("  " + r.Summary());
    Check(r.Added == 1 && r.AlreadyHad == 1 && r.NotTwoVTwo == 1, $"added {r.Added}, had {r.AlreadyHad}, non-2v2 {r.NotTwoVTwo}, unusable {r.Unusable}");
    var loss = store.Data.Matches.Single(m => m.Source == "addon");
    Check(!loss.Won, "loss detected from winner/team");
    Check(loss.Map == "Black Rook Hold" && loss.DurationSeconds == 512, "map + duration");
    Check(loss.EnemyCompKey == "Discipline Priest + Frost Mage", "hidden mage spec filled from prep: " + loss.EnemyCompKey);
    Check(loss.Enemies.Any(e => e.Name == "Zuljïn-Kel'Thas") && loss.Enemies.Any(e => e.Name == "Holy\"Quote"), "unicode + escaped names");
    Check(loss.RatingAfter == 1823 && loss.RatingChange == -11, $"rating {loss.RatingAfter} ({loss.RatingChange})");
    Check(loss.OurMmr == 1810 && loss.EnemyMmr == 1795, "mmr");
    Check(loss.OurCompKey == "Discipline Priest + Subtlety Rogue", "our comp " + loss.OurCompKey);
    Check(g1.EnemyCompKey == "Arms Warrior + Restoration Druid" && g1.Won && g1.RatingAfter == 1834, "game 1 mapping");

    var again = AddonImport.ImportInto(store, root);
    Check(again.Added == 0 && again.AlreadyHad == 0 && again.NotTwoVTwo == 0, "second import is silent: " + again.Summary());
    store.Delete(loss.Id);
    Check(AddonImport.ImportInto(store, root).Added == 0, "deleted game does not come back");

    // Live 2v2 scoreboards report prematchMMR = 0 for everyone; team MMR comes from C_PvP.GetTeamInfo.
    const string players0 = "[\"players\"] = {" +
      "{ [\"name\"] = \"Me\", [\"class\"] = \"MONK\", [\"spec\"] = \"Mistweaver\", [\"team\"] = 0, [\"isMe\"] = true, [\"mmrBefore\"] = 0 }," +
      "{ [\"name\"] = \"Pal\", [\"class\"] = \"PALADIN\", [\"spec\"] = \"Retribution\", [\"team\"] = 0, [\"mmrBefore\"] = 0 }," +
      "{ [\"name\"] = \"Foe-A\", [\"class\"] = \"ROGUE\", [\"spec\"] = \"Subtlety\", [\"team\"] = 1, [\"mmrBefore\"] = 0 }," +
      "{ [\"name\"] = \"Foe-B\", [\"class\"] = \"MAGE\", [\"spec\"] = \"Fire\", [\"team\"] = 1, [\"mmrBefore\"] = 0 } }";
    LuaTable Rec(string extra) => AddonImport.ReadRecords(
      "FNFTrackerDB = { [\"matches\"] = { { [\"id\"] = \"x\", [\"recordedBy\"] = \"Me-Realm\", [\"rated\"] = true, [\"myTeam\"] = 0, [\"winner\"] = 0, [\"endedAt\"] = 1792714618, " +
      players0 + extra + " } } }")[0];
    var withTeams = AddonImport.ToMatch(Rec(", [\"teams\"] = { { [\"faction\"] = 0, [\"mmr\"] = 1955 }, { [\"faction\"] = 1, [\"mmr\"] = 1748 } }"), out _)!;
    Check(withTeams.OurMmr == 1955 && withTeams.EnemyMmr == 1748, $"team mmr from GetTeamInfo: ours {withTeams.OurMmr}, theirs {withTeams.EnemyMmr}");
    var damp = AddonImport.ToMatch(Rec(", [\"dampening\"] = 54"), out _)!;
    Check(damp.DampeningPct == 54 && withTeams.DampeningPct == null, $"dampening imported when the addon read it: {damp.DampeningPct}");
    var dampMs = new List<Match> { new() { Won = true, DampeningPct = 32 }, new() { Won = false, DampeningPct = 66 }, new() { Won = false, DampeningPct = 71 }, new() { Won = true } };
    var byDamp = Stats.ByDampening(dampMs);
    Check(byDamp.Select(r => $"{r.Label} {r.Record}").SequenceEqual(new[] { "Under 40% 1-0", "60-79% 0-2" }), "dampening buckets: " + string.Join(", ", byDamp.Select(r => $"{r.Label} {r.Record}")));
    {
      // Dampening estimate vs. the 8 real addon readings from Oct 23 (length s, healers in match, reading %).
      PlayerSlot S(string cls, string spec) => new() { Class = cls, Spec = spec };
      Match G(int secs, int healers, int? read = null) => new() { DurationSeconds = secs, DampeningPct = read,
        OurTeam = new() { healers >= 1 ? S("Priest", "Discipline") : S("Hunter", "Beast Mastery"), S("Hunter", "Beast Mastery") },
        Enemies = new() { healers >= 2 ? S("Druid", "Restoration") : S("Mage", "Arcane"), S("Rogue", "Subtlety") } };
      var real = new[] { (114, 2, 51), (228, 2, 63), (90, 1, 29), (167, 2, 57), (167, 2, 57), (252, 2, 66), (82, 2, 48), (85, 2, 49) };
      var off = real.Select(r => Stats.EstimateDampening(G(r.Item1, r.Item2))!.Value - r.Item3).ToList();
      Check(off.All(d => Math.Abs(d) <= 1) && off.Count(d => d == 0) == 7, "estimate vs. 8 real readings, diffs: " + string.Join(",", off));
      Check(Stats.EstimateDampening(G(200, 0)) == null, "no estimate with no healers (unconfirmed)");
      var noSpec = G(200, 2); noSpec.Enemies[1].Spec = "";
      Check(Stats.EstimateDampening(noSpec) == null && Stats.EstimateDampening(new Match()) == null, "no estimate without all specs or a length");
      Check(Stats.DampeningText(G(167, 2)) == "Dampening ~57%" && Stats.DampeningText(G(167, 2, read: 58)) == "Dampening 58%", "reading beats estimate; estimate marked ~");
      Check(Stats.EstimateDampening(G(1200, 2)) == 100, "capped at 100%");
      var tbl = Stats.ByDampening(new[] { G(90, 1), G(252, 2, 66) });
      Check(tbl.Select(r => $"{r.Label} {r.Games}").SequenceEqual(new[] { "Under 40% 1", "60-79% 1" }), "table uses readings and estimates");
    }
    var oldAddon = AddonImport.ToMatch(Rec(""), out _)!;
    Check(oldAddon.OurMmr == null && oldAddon.EnemyMmr == null, $"0 mmr from old addon is not recorded: ours {oldAddon.OurMmr}, theirs {oldAddon.EnemyMmr}");
    var zeroData = new FnfData(); zeroData.Matches.Add(new Match { OurMmr = 0, EnemyMmr = 0 }); zeroData.Matches.Add(new Match { EnemyMmr = 1800 });
    Check(DataStore.ClearZeroMmr(zeroData) && zeroData.Matches[0].EnemyMmr == null && zeroData.Matches[0].OurMmr == null && zeroData.Matches[1].EnemyMmr == 1800, "saved 0 mmr cleared on load");

    // Player ratings: imported, all-zero (hidden) boards give none, and older imports get them backfilled.
    {
      string Board(int a, int b, int c, int d) =>
        $"[\"players\"] = {{ {{ [\"name\"] = \"Me\", [\"class\"] = \"MONK\", [\"team\"] = 0, [\"isMe\"] = true, [\"rating\"] = {a} }}, " +
        $"{{ [\"name\"] = \"Pal\", [\"class\"] = \"PALADIN\", [\"team\"] = 0, [\"rating\"] = {b} }}, " +
        $"{{ [\"name\"] = \"Booster-A\", [\"class\"] = \"ROGUE\", [\"team\"] = 1, [\"rating\"] = {c} }}, " +
        $"{{ [\"name\"] = \"Buyer-B\", [\"class\"] = \"SHAMAN\", [\"team\"] = 1, [\"rating\"] = {d} }} }}";
      string Db(string id, string board) =>
        $"FNFTrackerDB = {{ [\"matches\"] = {{ {{ [\"id\"] = \"{id}\", [\"recordedBy\"] = \"Me-Realm\", [\"rated\"] = true, [\"myTeam\"] = 0, [\"winner\"] = 0, [\"endedAt\"] = 1792714618, {board} }} }} }}";
      var carried = AddonImport.ToMatch(AddonImport.ReadRecords(Db("c1", Board(1960, 1955, 1597, 672)))[0], out _)!;
      Check(carried.Enemies.Select(e => e.Rating).SequenceEqual(new int?[] { 1597, 672 }) && Stats.EnemyRatingGap(carried) == 925, $"enemy ratings imported, gap {Stats.EnemyRatingGap(carried)}");
      var hidden = AddonImport.ToMatch(AddonImport.ReadRecords(Db("h1", Board(0, 0, 0, 0)))[0], out _)!;
      Check(hidden.Enemies.All(e => e.Rating == null) && Stats.EnemyRatingGap(hidden) == null, "all-zero (hidden) ratings are not recorded");
      var freshBoost = AddonImport.ToMatch(AddonImport.ReadRecords(Db("z1", Board(1960, 1955, 1800, 0)))[0], out _)!;
      Check(freshBoost.Enemies[1].Rating == 0 && Stats.EnemyRatingGap(freshBoost) == 1800, "a real 0-rated enemy counts when the board isn't hidden");

      // Backfill: a game imported before ratings were kept gets them on the next import, and nothing else changes.
      var bRoot = Path.Combine(Path.GetTempPath(), "fnf-bf-" + Guid.NewGuid());
      var bSv = Path.Combine(bRoot, "WTF", "Account", "X", "SavedVariables"); Directory.CreateDirectory(bSv);
      File.WriteAllText(Path.Combine(bSv, "FNFTracker.lua"), Db("c1", Board(1960, 1955, 1597, 672)));
      var bStore = new DataStore(Path.Combine(bRoot, "d.json")); bStore.Load();
      Check(AddonImport.ImportInto(bStore, bRoot).Added == 1, "backfill setup import");
      var stored = bStore.Data.Matches.Single();
      foreach (var p in stored.Enemies.Concat(stored.OurTeam)) p.Rating = null;   // as saved by an older app
      stored.Enemies[0].Name = "Booster-A";                                         // unchanged
      var bf = AddonImport.ImportInto(bStore, bRoot);
      Check(bf.Backfilled == 1 && bf.Added == 0 && bf.Changed && Stats.EnemyRatingGap(bStore.Data.Matches.Single()) == 925, "older game backfilled: " + bf.Summary());
      Check(AddonImport.ImportInto(bStore, bRoot).Backfilled == 0, "backfill runs once");
      Directory.Delete(bRoot, true);

      var jf = Stats.JerkFactor(new[] { carried, freshBoost, hidden,
        new Match { Won = true, Enemies = new() { new PlayerSlot { Class = "Mage", Rating = 1900 }, new PlayerSlot { Class = "Priest", Rating = 2000 } } },
        new Match { Won = false, Enemies = new() { new PlayerSlot { Class = "Mage", Rating = 1500 }, new PlayerSlot { Class = "Priest", Rating = 2000 } } },
        new Match { Won = false, Enemies = new() { new PlayerSlot { Class = "Mage", Rating = 1501 }, new PlayerSlot { Class = "Priest", Rating = 2000 } } },
        new Match { Won = false, Enemies = new() { new PlayerSlot { Class = "Mage", Rating = 1799 }, new PlayerSlot { Class = "Priest", Rating = 2000 } } },
        new Match { Won = false, Enemies = new() { new PlayerSlot { Class = "Mage", Rating = 1800 }, new PlayerSlot { Class = "Priest", Rating = 2000 } } } });
      Check(jf.Select(r => $"{r.Label}: {r.Games}").SequenceEqual(new[] { "Clearly buying wins for gold: 3", "Questionable association: 2", "Most likely friends or guildmates: 2" }),
        "jerk factor tiers (500 carry, 499/201 questionable, 200/100 friends): " + string.Join(", ", jf.Select(r => $"{r.Label}: {r.Games}")));
      Check(Stats.JerkFactor(new[] { hidden }).Count == 0, "jerk factor empty without ratings");
    }

    // Duplicate detection. Tonight's real pair: back-to-back wins vs Priest + Death Knight 9 minutes
    // apart were treated as one game, so the second (Nagrand) was never imported.
    PlayerSlot P(string spec, string cls) => new() { Spec = spec, Class = cls };
    var at = new DateTime(2026, 10, 22, 21, 47, 0);
    var empyrean = new Match { PlayedAt = at, Won = true, Map = "Empyrean Domain", Source = "addon",
      ExternalId = "Sackpuncher-Sargeras/20261022-214243-4967", Enemies = new() { P("Discipline", "Priest"), P("Unholy", "Death Knight") } };
    var nagrand = new Match { PlayedAt = at.AddMinutes(9), Won = true, Map = "Nagrand Arena", Source = "addon",
      ExternalId = "Sackpuncher-/20261022-215314-7061", Enemies = new() { P("Holy", "Priest"), P("Unholy", "Death Knight") } };
    Check(!DataStore.IsSameGame(empyrean, nagrand), "back-to-back games vs same classes are different games");
    var rematch = new Match { PlayedAt = at.AddMinutes(8), Won = true, Map = "Empyrean Domain", Source = "addon",
      ExternalId = "Sackpuncher-Sargeras/20261022-215000-1111", Enemies = new() { P("Discipline", "Priest"), P("Unholy", "Death Knight") } };
    Check(!DataStore.IsSameGame(empyrean, rematch), "same addon never merges its own two games, even same map + specs");
    var partnerCopy = new Match { PlayedAt = at.AddMinutes(1), Won = true, Map = "Empyrean Domain", Source = "addon",
      ExternalId = "Corpsehmper-Sargeras/20261022-214245-2222", Enemies = new() { P("Discipline", "Priest"), P("Unholy", "Death Knight") } };
    Check(DataStore.IsSameGame(empyrean, partnerCopy), "partner's addon copy of the same game still matches");
    var partnerOther = new Match { PlayedAt = at.AddMinutes(9), Won = true, Map = "Nagrand Arena", Source = "addon",
      ExternalId = "Corpsehmper-Sargeras/20261022-215316-3333", Enemies = new() { P("Holy", "Priest"), P("Unholy", "Death Knight") } };
    Check(!DataStore.IsSameGame(empyrean, partnerOther), "partner's copy of a different game (other map) does not match");
    var byHand = new Match { PlayedAt = at.AddMinutes(2), Won = true, Enemies = new() { new PlayerSlot { Class = "Priest" }, new PlayerSlot { Class = "Death Knight" } } };
    Check(DataStore.IsSameGame(empyrean, byHand), "hand-entered game without map/specs still matches the addon copy");

    // Parser edge cases
    var g = LuaSavedVariablesReader.Parse("X = {\n \"a\\\\b\\n\", -- [1]\n -5.5e2, -- comment\n true, [4] = false, [\"k\"] = { },\n}\nY = -inf\n");
    var x = (LuaTable)g["X"]!;
    var items = x.Items.ToList();
    Check((string)items[0]! == "a\\b\n" && (double)items[1]! == -550 && (bool)items[2]! && items.Count == 4 && !(bool)items[3]! && x.Table("k") != null, "parser edge cases");
    Check(double.IsNegativeInfinity((double)g["Y"]!), "-inf");
    Check(LuaSavedVariablesReader.Parse("S = \"\\195\\175\"")["S"] as string == "ï", "\\ddd utf-8 bytes");
    Directory.Delete(root, true);
    return fails;
  }
}
