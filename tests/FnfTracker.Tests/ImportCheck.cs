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
    var oldAddon = AddonImport.ToMatch(Rec(""), out _)!;
    Check(oldAddon.OurMmr == null && oldAddon.EnemyMmr == null, $"0 mmr from old addon is not recorded: ours {oldAddon.OurMmr}, theirs {oldAddon.EnemyMmr}");
    var zeroData = new FnfData(); zeroData.Matches.Add(new Match { OurMmr = 0, EnemyMmr = 0 }); zeroData.Matches.Add(new Match { EnemyMmr = 1800 });
    Check(DataStore.ClearZeroMmr(zeroData) && zeroData.Matches[0].EnemyMmr == null && zeroData.Matches[0].OurMmr == null && zeroData.Matches[1].EnemyMmr == 1800, "saved 0 mmr cleared on load");

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
