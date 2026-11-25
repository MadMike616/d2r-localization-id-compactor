// Synthetic fixtures only. Never opens installed/user game saves.
using System.Text.Json;
using D2SSharp.Data;
using D2SSharp.Enums;
using D2SSharp.Model;

class ItemFixtures
{
    static TxtFileExternalData Data(string folder) => new(folder, 105);
    static Item Rune() => new() { Flags = ItemFlags.CompactSave | ItemFlags.Identified, Version = 7,
        ItemCodeString = "r01", Position = new() { Mode = ItemMode.Socketed, StorePage = StorePage.Null } };
    static Item Word(ushort id, StorePage store = StorePage.Inventory, ItemMode mode = ItemMode.Stored) => new() {
        Flags = ItemFlags.Runeword | ItemFlags.Socketed | ItemFlags.Identified | ItemFlags.Personalized | ItemFlags.Chronicle,
        Version = 7, ItemCodeString = "swd", ItemSeed = 53506, ItemLevel = 55, Quality = ItemQuality.Normal,
        Position = new() { Mode = mode, StorePage = store, BodyLocation = mode == ItemMode.Equipped ? BodyLocation.RightArm : 0, InvX = 1, InvY = 2 },
        RunewordId = id, PersonalizedName = "Fixture", MaxDurability = 60, Durability = 40,
        ChronicleId = 53506, ChronicleTimestamp = 53505, ChronicleRecipients = [],
        Sockets = [Rune(), Rune()], Stats = [new Stat { Id = StatId.Strength, Value = 53506 }],
        RunewordStats = [new Stat { Id = StatId.Strength, Value = 53505 }]
    };
    static void Tables(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"itemtypes.txt"), "Code\tEquiv1\tEquiv2\tVarInvGfx\nitem\t\t\t0\nweap\titem\t\t0\narmo\titem\t\t0\nrune\titem\t\t0\n");
        string header = "code\ttype\tcompactsave\tstackable\tquest\tquestdiffcheck\n";
        File.WriteAllText(Path.Combine(folder,"weapons.txt"), header + "swd\tweap\t0\t0\t0\t0\n");
        File.WriteAllText(Path.Combine(folder,"armor.txt"), header + "cap\tarmo\t0\t0\t0\t0\n");
        File.WriteAllText(Path.Combine(folder,"misc.txt"), header + "r01\trune\t1\t0\t0\t0\n");
        var lines = new List<string> { "Stat\tCSvSigned\tCSvBits\tCSvParam\tSave Bits\tSave Param Bits\tSave Add\tValShift" };
        for (int i = 0; i < 511; ++i) lines.Add($"fixture{i}\t0\t32\t0\t{(i == 194 ? 4 : i == 72 || i == 73 ? 8 : 32)}\t0\t0\t0");
        File.WriteAllLines(Path.Combine(folder,"itemstatcost.txt"), lines);
    }
    static void Generate(string root)
    {
        string tables = Path.Combine(root,"tables"), saves = Path.Combine(root,"saves"); Tables(tables); Directory.CreateDirectory(saves);
        var data = Data(tables);
        var save = new D2Save(); save.Character.Preview.GameVersion = GameVersion.Expansion; save.Character.Preview.Name = "Fixture";
        save.Character.SkillHotkeys = Enumerable.Range(0, 16).Select(_ => new Skill()).ToArray();
        save.Skills.SkillLevels = new byte[30];
        save.Character.MercData.Seed = 123; save.MercItems = new(); save.IronGolem = new();
        save.Items.Add(Word(53506)); save.Items.Add(Word(53505, StorePage.Stash)); save.Items.Add(Word(53506, StorePage.Cube));
        save.Items.Add(Word(53505, StorePage.Null, ItemMode.Equipped)); save.Items.Add(Word(53506, StorePage.Null, ItemMode.OnCursor));
        save.Items.Add(Word(20581)); // valid native ID absent from custom mapping
        save.Corpses.Add(new Corpse { Items = [Word(53506)] }); save.MercItems.Items.Add(Word(53505, StorePage.Null, ItemMode.Equipped));
        save.IronGolem.GolemItem = Word(53506);
        File.WriteAllBytes(Path.Combine(saves,"ACharacter.d2s"), save.ToBytes(data));
        var stash = new D2StashSave {
            new() { StashFormat = 2, SaveVersion = 105, Season = 4, TabType = StashTabType.Normal, Gold = 53506, Items = [Word(53506), Word(20581)] },
            new() { StashFormat = 2, SaveVersion = 105, Season = 7, TabType = StashTabType.AdvancedStash, Items = [Word(53505)] },
            new() { StashFormat = 2, SaveVersion = 105, TabType = StashTabType.Chronicle, Chronicle = new() {
                SetEntries = [new() { ItemId = 53506, Source = 53505, Timestamp = 53506 }],
                RunewordEntries = [new() { ItemId = 53506, Source = 0, Timestamp = 1234 }], TrailingData = new byte[64] } }
        };
        File.WriteAllBytes(Path.Combine(saves,"BStash.d2i"), stash.ToBytes(data));
        save.Items.Clear(); save.Corpses.Clear(); save.MercItems.Items.Clear(); save.IronGolem.GolemItem = null;
        File.WriteAllBytes(Path.Combine(saves,"CUnchanged.d2s"), save.ToBytes(data));
        File.WriteAllText(Path.Combine(root,"old-to-new-ids.csv"), "File,EntryIndex,Key,OldID,NewID\nitem-runes.json,0,CustomWordA,53506,34008\nitem-runes.json,1,CustomWordB,53505,34007\nui.json,0,Unrelated,53506,36000\n");
    }
    static void Walk(IEnumerable<Item> items, string location, int start, List<object> words)
    {
        int index = 0;
        foreach (var item in items) {
            string label = location + "/" + index++;
            if (item.RunewordId.HasValue) words.Add(new { location = label, id = item.RunewordId, bit = start + item.RunewordIdBitOffset, code = item.ItemCodeString,
                source = item.ChronicleId, seed = item.ItemSeed, stats = item.Stats, runewordStats = item.RunewordStats, durability = item.Durability, personalized = item.PersonalizedName, sockets = item.Sockets.Count });
            Walk(item.Sockets.Where(i => i != null)!, label + "/socket", start, words);
        }
    }
    static void AddLegacyStatCharm(string root)
    {
        // Deliberately create a save/table-history mismatch in private fixtures.
        // The old save stores stat 398 in ten bits. A later test changes the
        // table to seven bits, which makes the next nine-bit ID decode as 504.
        string folder = Path.Combine(root, "tables"), file = Path.Combine(root, "saves", "ACharacter.d2s");
        string statFile = Path.Combine(folder, "itemstatcost.txt");
        var rows = File.ReadAllLines(statFile).Take(401).ToArray();
        var fields = rows[399].Split('\t'); fields[4] = "10"; rows[399] = string.Join('\t', fields);
        File.WriteAllLines(statFile, rows);
        File.AppendAllText(Path.Combine(folder, "itemtypes.txt"), "char\titem\t\t3\nscha\tchar\t\t3\n");
        File.AppendAllText(Path.Combine(folder, "misc.txt"), "cm1\tscha\t0\t0\t0\t0\n");
        var data = Data(folder); var save = D2Save.Read(File.ReadAllBytes(file), data);
        save.Items.Insert(1, new Item { Flags = ItemFlags.Identified | ItemFlags.JustSaved, Version = 7, ItemCodeString = "cm1",
            ItemSeed = 12345, ItemLevel = 99, Quality = ItemQuality.Magic, VariableGfxId = 0,
            QualityData = new MagicQualityData { PrefixId = 0, SuffixId = 2 }, Position = new() { Mode = ItemMode.Stored, StorePage = StorePage.Inventory },
            Stats = [new Stat { Id = (StatId)398, Value = 3 }] });
        File.WriteAllBytes(file, save.ToBytes(data));
    }
    static void Inspect(string file, string folder)
    {
        var words = new List<object>(); byte[] b = File.ReadAllBytes(file); var data = Data(folder);
        if (file.EndsWith(".d2s")) {
            var save = D2Save.Read(b,data); Walk(save.Items,"player",0,words);
            foreach(var corpse in save.Corpses) Walk(corpse.Items,"corpse",0,words);
            if(save.MercItems != null) Walk(save.MercItems.Items,"merc",0,words);
            if(save.IronGolem?.GolemItem is Item golem) Walk([golem],"golem",0,words);
        } else {
            int at=0, index=0;
            while(at<b.Length) { int size=BitConverter.ToUInt16(b,at+16); var tab=D2StashTab.Read(b.AsSpan(at,size),data); Walk(tab.Items,"tab"+index++,at*8,words); at+=size; }
        }
        Console.WriteLine(JsonSerializer.Serialize(words));
    }
    static void RewriteInputIds(string root, bool nativeOnly)
    {
        var data = Data(Path.Combine(root, "tables"));
        ushort Id(ushort id) => nativeOnly ? (ushort)20581 : id == 53506 ? (ushort)32001 : id == 53505 ? (ushort)32000 : id;
        ushort Source(ushort id) => nativeOnly ? (ushort)50000 : id == 53506 ? (ushort)32001 : id == 53505 ? (ushort)32000 : id;
        void Items(IEnumerable<Item> items) {
            foreach (Item item in items) { if(item.RunewordId.HasValue) item.RunewordId=Id(item.RunewordId.Value);
                if(item.ChronicleId.HasValue) item.ChronicleId=Source(item.ChronicleId.Value); Items(item.Sockets.Where(i=>i!=null)!); }
        }
        foreach (string file in Directory.GetFiles(Path.Combine(root, "saves"))) {
            if(file.EndsWith(".d2s")) { var save=D2Save.Read(File.ReadAllBytes(file),data); Items(save.Items);
                foreach(var corpse in save.Corpses) Items(corpse.Items); if(save.MercItems!=null) Items(save.MercItems.Items);
                if(save.IronGolem?.GolemItem is Item golem) Items([golem]); File.WriteAllBytes(file,save.ToBytes(data));
            } else { var stash=D2StashSave.Read(File.ReadAllBytes(file),data); foreach(var tab in stash) { Items(tab.Items);
                if(tab.Chronicle!=null) {
                    foreach(var entries in new[] { tab.Chronicle.SetEntries, tab.Chronicle.UniqueEntries, tab.Chronicle.RunewordEntries })
                        for(int i=0;i<entries.Count;i++) { var entry=entries[i]; if(ReferenceEquals(entries,tab.Chronicle.RunewordEntries)) entry.ItemId=Id((ushort)entry.ItemId);
                            if(entry.Source!=0) entry.Source=Source(entry.Source); entries[i]=entry; }
                }
                } File.WriteAllBytes(file,stash.ToBytes(data)); }
        }
    }
    static int Main(string[] args) {
        if(args[0]=="generate") Generate(args[1]);
        else if(args[0]=="add-legacy-stat-charm") AddLegacyStatCharm(args[1]);
        else if(args[0]=="swap-input-rune-ids") RewriteInputIds(args[1],false);
        else if(args[0]=="native-only-input") RewriteInputIds(args[1],true);
        else Inspect(args[1],args[2]); return 0;
    }
}
