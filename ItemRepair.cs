// SPDX-License-Identifier: MIT
// Copyright (c) 2026 MadMike
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using D2SSharp;
using D2SSharp.Data;
using D2SSharp.Enums;
using D2SSharp.Model;

namespace D2RLocalizationCompactor
{
    internal sealed class InputSnapshot
    {
        public string Path;
        public byte[] Bytes;
        public bool Generated;
        public static InputSnapshot Read(string path)
        {
            path = System.IO.Path.GetFullPath(path); ChronicleRepair.PlainPath(path);
            return new InputSnapshot { Path = path, Bytes = File.ReadAllBytes(path) };
        }
        public void Verify()
        {
            if (Generated) return;
            ChronicleRepair.PlainPath(Path);
            if (!File.ReadAllBytes(Path).SequenceEqual(Bytes)) throw new IOException("Input changed after preview: " + Path);
        }
    }
    internal sealed class RepairTables : IExternalData
    {
        public readonly List<InputSnapshot> Inputs = new List<InputSnapshot>();
        private readonly HashSet<int> definedStats = new HashSet<int>();
        private readonly TxtFileExternalData data;
        public RepairTables(string folder)
        {
            folder = System.IO.Path.GetFullPath(folder);
            var required = new Dictionary<string, string[]> {
                { "itemstatcost.txt", new[] { "Stat", "CSvSigned", "CSvBits", "CSvParam", "Save Bits", "Save Param Bits", "Save Add", "ValShift" } },
                { "itemtypes.txt", new[] { "Code", "Equiv1", "Equiv2", "VarInvGfx" } },
                { "armor.txt", new[] { "code", "type", "compactsave", "stackable", "quest", "questdiffcheck" } },
                { "weapons.txt", new[] { "code", "type", "compactsave", "stackable", "quest", "questdiffcheck" } },
                { "misc.txt", new[] { "code", "type", "compactsave", "stackable", "quest", "questdiffcheck" } }
            };
            foreach (var table in required)
            {
                string[] matches = Directory.GetFiles(folder).Where(p => System.IO.Path.GetFileName(p).Equals(table.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                ItemRepair.Require(matches.Length == 1, "Select the mod's complete data/global/excel folder. Required table missing/ambiguous: " + table.Key);
                InputSnapshot input = InputSnapshot.Read(matches[0]); Inputs.Add(input);
                string text = new UTF8Encoding(false, true).GetString(input.Bytes).TrimStart('\uFEFF');
                using (var reader = new StringReader(text))
                {
                    string first = reader.ReadLine(); ItemRepair.Require(first != null, "Empty game table: " + table.Key);
                    string[] columns = first.Split('\t'); var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < columns.Length; ++i)
                    {
                        string name = columns[i].Trim(); if (name.Length == 0) continue;
                        ItemRepair.Require(!header.ContainsKey(name), "Duplicate column in " + table.Key + ": " + name); header.Add(name, i);
                    }
                    foreach (string name in table.Value) ItemRepair.Require(header.ContainsKey(name), "Missing column in " + table.Key + ": " + name);
                    string line; int row = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Upstream uses nonempty physical lines for stat row IDs.
                        if (line.Length == 0) continue;
                        string[] values = line.Split('\t');
                        if (table.Key == "itemstatcost.txt" && Value(values, header["Stat"]).Length > 0) definedStats.Add(row);
                        foreach (string name in table.Value.Where(n => !new[] { "Stat", "code", "type", "Equiv1", "Equiv2" }.Contains(n, StringComparer.OrdinalIgnoreCase)))
                        {
                            string value = Value(values, header[name]); if (value.Length == 0) continue;
                            int number; bool boolean = name.Equals("CSvSigned", StringComparison.OrdinalIgnoreCase);
                            if (boolean && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))) continue;
                            ItemRepair.Require(int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number), "Invalid numeric field in " + table.Key + ", row " + row + ": " + name);
                            bool byteField = new[] { "VarInvGfx", "compactsave", "stackable", "quest", "questdiffcheck" }.Contains(name);
                            int maximum = name == "Save Add" ? int.MaxValue : byteField ? 255 : boolean ? 1 : name == "ValShift" ? 31 : 32;
                            ItemRepair.Require((name == "Save Add" || number >= 0) && number <= maximum, "Unsupported bit width/value in " + table.Key + ", row " + row + ": " + name);
                        }
                        ++row;
                    }
                }
            }
            data = new TxtFileExternalData(folder, 105);
            foreach (InputSnapshot input in Inputs) input.Verify();
        }
        private static string Value(string[] row, int at) { return at < row.Length ? row[at].Trim() : ""; }
        public StatInfo GetStatInfo(StatId id, uint version)
        {
            ItemRepair.Require(definedStats.Contains((int)id), "Save references an undefined stat ID: " + (int)id);
            StatInfo info = data.GetStatInfo(id, version);
            ItemRepair.Require(id != StatId.NumSockets || info.SaveBits <= 8, "Unsupported socket-count bit width in the selected mod tables.");
            return info;
        }
        public int GetItemIndex(uint code, uint version) { return data.GetItemIndex(code, version); }
        public ItemTypeInfo GetItemInfo(int index, uint version) { return data.GetItemInfo(index, version); }
    }
    internal sealed class ItemIdPatch
    {
        public int BitOffset, Width;
        public uint OldId, NewId;
        public string Key, Location, ItemCode, Kind;
    }
    internal sealed class SaveMigrationRules
    {
        public HashSet<uint> BeforeRuneIds;
        public Func<uint, string, uint> ResolveSource;
        public Action<uint, string> ValidateRune;
        public bool AllowRuneAliases;
    }
    internal sealed class ItemSave
    {
        public InputSnapshot Input;
        public byte[] Output;
        public bool Character;
        public int Items, Runewords, AlreadyCurrent, UnchangedMapped;
        public readonly List<uint> Unmapped = new List<uint>();
        public readonly List<ItemIdPatch> Changes = new List<ItemIdPatch>();
    }
    internal sealed class ItemRepairPlan
    {
        public InputSnapshot Mapping;
        public RepairTables Tables;
        public string SaveFolder, BackupRoot, Backup;
        public bool IncludeChronicle;
        public InputSnapshot DetectedModInfo;
        public bool SaveOnly;
        public Action VerifyContext;
        public readonly Dictionary<string, InputSnapshot> ContextInputs = new Dictionary<string, InputSnapshot>();
        public readonly List<ItemSave> Saves = new List<ItemSave>();
        public int Changes { get { return Saves.Sum(s => s.Changes.Count); } }
    }
    internal static class ItemRepair
    {
        public static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        private static uint U32(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
        private static int U16(byte[] b, int at) { return BitConverter.ToUInt16(b, at); }
        private static string[] SaveFiles(string folder)
        {
            return Directory.GetFiles(folder).Where(p => new[] { ".d2s", ".d2i" }.Contains(System.IO.Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static uint ReadBits(byte[] bytes, int bit, int width)
        {
            Require(bit >= 0 && bit <= bytes.Length * 8 - width, "Item field extends outside the save.");
            uint result = 0; for (int i = 0; i < width; ++i) result |= (uint)((bytes[(bit + i) / 8] >> ((bit + i) % 8)) & 1) << i;
            return result;
        }
        private static void PutBits(byte[] bytes, int bit, int width, uint value)
        {
            for (int i = 0; i < width; ++i) { int at = (bit + i) / 8, shift = (bit + i) % 8; bytes[at] = (byte)((bytes[at] & ~(1 << shift)) | (int)((value >> i) & 1) << shift); }
        }
        private static void Items(ItemSave save, IEnumerable<Item> items, string location, int baseBits, Dictionary<uint, RuneMapping> mapping, HashSet<uint> destinations, SaveMigrationRules rules)
        {
            int index = 0;
            foreach (Item item in items)
            {
                string label = location + " #" + (++index) + " (" + item.Position.Mode + ", " + item.Position.StorePage + ", " + item.Position.BodyLocation + ")";
                ++save.Items;
                if (item.RunewordId.HasValue)
                {
                    ++save.Runewords; uint oldId = item.RunewordId.Value; RuneMapping row;
                    if (rules != null && rules.ValidateRune != null) rules.ValidateRune(oldId, label);
                    Require(item.RunewordIdBitOffset.HasValue, "The parser did not record a runeword ID field.");
                    int bit = baseBits + item.RunewordIdBitOffset.Value;
                    Require(ReadBits(save.Input.Bytes, bit, 16) == oldId, "Parsed runeword field disagrees with original save bytes.");
                    if (mapping.TryGetValue(oldId, out row))
                    {
                        if (oldId == row.NewId) ++save.UnchangedMapped;
                        else save.Changes.Add(new ItemIdPatch { BitOffset = bit, Width = 16, OldId = oldId, NewId = row.NewId, Key = row.Key, Location = label, ItemCode = item.ItemCodeString, Kind = "item" });
                    }
                    else if (rules != null && !rules.BeforeRuneIds.Contains(oldId)) throw new InvalidDataException("Saved runeword ID " + oldId + " is absent from the current pre-compaction runeword table. Restore matching original JSON or use advanced repair with its historical mapping first.");
                    else if (destinations.Contains(oldId)) ++save.AlreadyCurrent;
                    else save.Unmapped.Add(oldId);
                }
                if (rules != null && item.ChronicleId.HasValue)
                {
                    uint oldId = item.ChronicleId.Value, newId = rules.ResolveSource(oldId, label);
                    if (oldId != newId)
                    {
                        Require(item.ChronicleIdBitOffset.HasValue, "The parser did not record the item's Chronicle source ID field.");
                        save.Changes.Add(new ItemIdPatch { BitOffset = baseBits + item.ChronicleIdBitOffset.Value, Width = 16, OldId = oldId, NewId = newId, Key = "", Location = label, ItemCode = item.ItemCodeString, Kind = "item-source" });
                    }
                }
                Items(save, item.Sockets.Where(s => s != null), label + "/socket", baseBits, mapping, destinations, rules);
            }
        }
        public static ItemRepairPlan Build(IEnumerable<string> selected, string folder, string mapping, string tables, string backup, bool includeChronicle)
        {
            return BuildPrepared(selected, folder, InputSnapshot.Read(mapping), tables, backup, includeChronicle, null);
        }
        public static ItemRepairPlan BuildPrepared(IEnumerable<string> selected, string folder, InputSnapshot mapping, string tables, string backup, bool includeChronicle, SaveMigrationRules rules)
        {
            var plan = new ItemRepairPlan { Mapping = mapping, Tables = new RepairTables(tables), IncludeChronicle = includeChronicle,
                SaveFolder = folder == null ? null : System.IO.Path.GetFullPath(folder) };
            Dictionary<uint, RuneMapping> ids = ChronicleRepair.ReadMappingPrepared(plan.Mapping.Bytes, rules != null, rules != null && rules.AllowRuneAliases); var destinations = new HashSet<uint>(ids.Values.Select(r => r.NewId));
            string[] files = (plan.SaveFolder == null ? selected.Select(System.IO.Path.GetFullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() : SaveFiles(plan.SaveFolder));
            Require(files.Length > 0, "No .d2s or .d2i saves selected.");
            Require(files.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length, "A save file was selected more than once.");
            Require(files.Select(System.IO.Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length, "Selected saves have duplicate filenames. Repair their folders separately so backups remain unambiguous.");
            plan.BackupRoot = System.IO.Path.GetFullPath(backup ?? Engine.DefaultBackupRoot(System.IO.Path.GetDirectoryName(files[0])));
            Engine.ValidateBackupRoot(System.IO.Path.GetDirectoryName(plan.Tables.Inputs[0].Path), plan.BackupRoot);
            foreach (string file in files)
            {
                try
                {
                    Engine.ValidateBackupRoot(System.IO.Path.GetDirectoryName(file), plan.BackupRoot);
                    var save = new ItemSave { Input = InputSnapshot.Read(file), Character = System.IO.Path.GetExtension(file).Equals(".d2s", StringComparison.OrdinalIgnoreCase) };
                    Require(save.Character || System.IO.Path.GetExtension(file).Equals(".d2i", StringComparison.OrdinalIgnoreCase), "Item repair accepts only .d2s or .d2i save files.");
                    byte[] b = save.Input.Bytes; Require(b.Length <= 128 * 1024 * 1024, "Save exceeds the supported 128 MiB limit.");
                    if (save.Character)
                    {
                        Require(b.Length >= 833, "Uninitialized or truncated character save. Enter that character in game and save it normally before repairing; short/new-character headers are not edited.");
                        Require(U32(b, 0) == 0xAA55AA55 && U32(b, 4) == 105, "Unsupported character magic/version. Item repair supports save version 105 only.");
                        Require(U32(b, 8) == b.Length && ChecksumCalculator.Verify(b), "Character size/checksum validation failed. The save will not be edited.");
                        D2Save parsed = D2Save.Read(b, plan.Tables);
                        Require(parsed.TrailingData.Length == 0, "Unparsed character trailing data: this save layout is unsupported.");
                        Items(save, parsed.Items, "Player", 0, ids, destinations, rules);
                        int corpse = 0; foreach (Corpse body in parsed.Corpses) Items(save, body.Items, "Corpse " + (++corpse), 0, ids, destinations, rules);
                        if (parsed.MercItems != null) Items(save, parsed.MercItems.Items, "Mercenary", 0, ids, destinations, rules);
                        if (parsed.IronGolem != null && parsed.IronGolem.GolemItem != null) Items(save, new[] { parsed.IronGolem.GolemItem }, "Iron golem", 0, ids, destinations, rules);
                    }
                    else
                    {
                        int at = 0, tab = 0; bool chronicle = false;
                        while (at < b.Length)
                        {
                            Require(b.Length - at >= 64, "Truncated stash header.");
                            Require(U32(b, at) == 0xAA55AA55 && U32(b, at + 4) == 2 && U32(b, at + 8) == 105, "Unsupported stash layout. Item repair supports format 2 / save version 105 only.");
                            int size = U16(b, at + 16), kind = b[at + 20]; ++tab;
                            Require(size >= 68 && size <= b.Length - at && kind <= 2, "Invalid stash page size/type.");
                            // Bound the parser to a single validated page so an
                            // item cannot consume bytes belonging to another tab.
                            D2StashTab parsed = D2StashTab.Read(b.AsSpan(at, size), plan.Tables);
                            Require(parsed.ReadBodyEndBytePosition == size, "Stash page has unparsed or overrunning item data.");
                            if (kind == 2)
                            {
                                Require(parsed.Chronicle.Version == 1 && parsed.Chronicle.TrailingData.Length == 64, "Unsupported Chronicle layout in the selected stash.");
                                chronicle = true;
                            }
                            else Items(save, parsed.Items, "Stash tab " + tab + " (" + parsed.TabType + ")", at * 8, ids, destinations, rules);
                            at += size;
                        }
                        Require(tab > 0, "Empty shared-stash save.");
                        if (includeChronicle && chronicle)
                        {
                            ChroniclePlan history = ChronicleRepair.BuildPrepared(file, plan.Mapping, plan.BackupRoot, rules);
                            Require(history.Original.SequenceEqual(b) && history.MappingBytes.SequenceEqual(plan.Mapping.Bytes), "Save/mapping changed while preparing the repair.");
                            foreach (ChroniclePatch p in history.Changes) save.Changes.Add(new ItemIdPatch { BitOffset = p.Offset * 8, Width = 32, OldId = p.OldId, NewId = p.NewId, Key = p.Key, Location = "Chronicle history", ItemCode = "", Kind = "chronicle" });
                            foreach (ChroniclePatch p in history.SourceChanges) save.Changes.Add(new ItemIdPatch { BitOffset = p.Offset * 8, Width = 16, OldId = p.OldId, NewId = p.NewId, Key = "", Location = "Chronicle discovery source", ItemCode = "", Kind = "chronicle-source" });
                            save.Unmapped.AddRange(history.Unmapped);
                        }
                    }
                    save.Output = (byte[])b.Clone(); var bits = new HashSet<int>();
                    foreach (ItemIdPatch patch in save.Changes)
                    {
                        Require(ReadBits(b, patch.BitOffset, patch.Width) == patch.OldId, "Patch does not match original save.");
                        for (int i = 0; i < patch.Width; ++i) Require(bits.Add(patch.BitOffset + i), "Overlapping repair fields.");
                        PutBits(save.Output, patch.BitOffset, patch.Width, patch.NewId);
                    }
                    if (save.Character && save.Changes.Count > 0) ChecksumCalculator.Update(save.Output);
                    byte[] reverse = (byte[])save.Output.Clone();
                    foreach (ItemIdPatch patch in save.Changes) PutBits(reverse, patch.BitOffset, patch.Width, patch.OldId);
                    if (save.Character) Array.Copy(b, 12, reverse, 12, 4);
                    Require(reverse.SequenceEqual(b), "Internal error: bytes outside planned ID fields/checksum changed.");
                    plan.Saves.Add(save);
                }
                catch (Exception error) { throw new InvalidDataException(System.IO.Path.GetFileName(file) + ": " + error.Message + " No saves were edited.", error); }
            }
            StillMatches(plan); return plan;
        }
        public static void Preview(ItemRepairPlan plan)
        {
            Console.WriteLine("\nITEM REPAIR - EDITS SAVE FILES (.d2s / .d2i) IN PLACE.");
            Console.WriteLine("Close the game and save editors. Mapping and tables MUST match this mod and save ID history.");
            Console.WriteLine("Mapping: " + plan.Mapping.Path + "\nMod tables: " + System.IO.Path.GetDirectoryName(plan.Tables.Inputs[0].Path));
            Console.WriteLine("Chronicle history: " + (plan.IncludeChronicle ? "ALSO REPAIR matching IDs in selected stashes" : "LEFT UNCHANGED (use --include-chronicle to repair too)"));
            Console.WriteLine("Backup root: " + plan.BackupRoot);
            foreach (ItemSave save in plan.Saves)
            {
                Console.WriteLine("\nSAVE FILE: " + save.Input.Path);
                Console.WriteLine("  {0} parsed items; {1} runewords; {2} ID changes; {3} already-current; {4} identity mappings.", save.Items, save.Runewords, save.Changes.Count, save.AlreadyCurrent, save.UnchangedMapped);
                foreach (ItemIdPatch p in save.Changes) Console.WriteLine("  {0}: {1} -> {2}, key={3}, code={4}, bit={5}, {6}", p.Kind, p.OldId, p.NewId, Engine.Json(p.Key), p.ItemCode, p.BitOffset, p.Location);
                Console.WriteLine("  IDs absent from mapping, LEFT UNCHANGED (valid vanilla or unresolved): " + string.Join(", ", save.Unmapped.Distinct().OrderBy(v => v)));
            }
            Console.WriteLine("\n{0} selected saves; {1} fields will change. All selected saves are backed up before the first edit.", plan.Saves.Count, plan.Changes);
            Console.WriteLine("Changes: saved runeword name IDs (16 bits), optional Chronicle history IDs (32 bits), and character checksums (4 bytes at offset 0x0C). No item recreation or stat rerolling.");
            Console.WriteLine("Stats, sockets, runes, item seeds, positions, gold, quests and other bytes stay unchanged. This does not update runeword properties/recipes or fix unknown IDs. A wrong mapping/tables can damage saves.");
        }
        private static void StillMatches(ItemRepairPlan plan)
        {
            if (plan.VerifyContext != null) plan.VerifyContext();
            foreach (InputSnapshot input in plan.ContextInputs.Values) input.Verify();
            if (plan.DetectedModInfo != null) plan.DetectedModInfo.Verify();
            plan.Mapping.Verify(); foreach (InputSnapshot input in plan.Tables.Inputs) input.Verify();
            if (plan.SaveFolder != null && !SaveFiles(plan.SaveFolder).SequenceEqual(plan.Saves.Select(s => s.Input.Path), StringComparer.OrdinalIgnoreCase)) throw new IOException("The selected save-folder file list changed after preview.");
            foreach (ItemSave save in plan.Saves) save.Input.Verify();
        }
        private static void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Backup/staging verification failed: " + path);
        }
        private static void Report(ItemRepairPlan plan, string status, string message)
        {
            var report = new { tool = "D2R Localization ID Compactor", version = "1.6.1", action = plan.SaveOnly ? "EDIT SAVE FILES ONLY" : "Runeword item SAVE FILE repair", status = status, message = message, utc = DateTime.UtcNow.ToString("o"),
                mappingFile = plan.Mapping.Path, mappingSha256 = Engine.Hash(plan.Mapping.Bytes), includeChronicle = plan.IncludeChronicle,
                detectedModInfo = plan.DetectedModInfo == null ? null : new { path = plan.DetectedModInfo.Path, sha256 = Engine.Hash(plan.DetectedModInfo.Bytes) },
                tables = plan.Tables.Inputs.Select(t => new { path = t.Path, sha256 = Engine.Hash(t.Bytes) }).ToArray(),
                context = plan.ContextInputs.Select(t => new { backupName = t.Key, path = t.Value.Path, sha256 = Engine.Hash(t.Value.Bytes) }).ToArray(),
                saves = plan.Saves.Select(s => new { path = s.Input.Path, originalSha256 = Engine.Hash(s.Input.Bytes), outputSha256 = Engine.Hash(s.Output), parsedItems = s.Items, runewords = s.Runewords,
                    checksumUpdated = s.Character && s.Changes.Count > 0, unmappedIdsLeftUnchanged = s.Unmapped,
                    changes = s.Changes.Select(p => new { bitOffset = p.BitOffset, width = p.Width, kind = p.Kind, key = p.Key, itemCode = p.ItemCode, location = p.Location, oldId = p.OldId, newId = p.NewId }).ToArray() }).ToArray() };
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "repair-report.json"), Engine.Json(report), new UTF8Encoding(false));
        }
        public static void Apply(ItemRepairPlan plan)
        {
            StillMatches(plan);
            if (plan.Changes == 0) { Console.WriteLine("No mapped IDs need repair. No saves were edited or backed up."); return; }
            foreach (ItemSave save in plan.Saves) Engine.ValidateBackupRoot(System.IO.Path.GetDirectoryName(save.Input.Path), plan.BackupRoot);
            Engine.ValidateBackupRoot(System.IO.Path.GetDirectoryName(plan.Tables.Inputs[0].Path), plan.BackupRoot);
            plan.Backup = System.IO.Path.Combine(plan.BackupRoot, (plan.SaveOnly ? "Save-only-" : "Item-repair-") + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string originals = System.IO.Path.Combine(plan.Backup, "originals"), tables = System.IO.Path.Combine(plan.Backup, "mod-tables");
            Directory.CreateDirectory(originals); Directory.CreateDirectory(tables);
            // Complete, verified backups of EVERY selected save precede even
            // staging/replacing the first one. Also retain all repair inputs.
            foreach (ItemSave save in plan.Saves) WriteNew(System.IO.Path.Combine(originals, System.IO.Path.GetFileName(save.Input.Path)), save.Input.Bytes);
            foreach (InputSnapshot input in plan.Tables.Inputs) WriteNew(System.IO.Path.Combine(tables, System.IO.Path.GetFileName(input.Path)), input.Bytes);
            WriteNew(System.IO.Path.Combine(plan.Backup, "old-to-new-ids.csv"), plan.Mapping.Bytes);
            if (plan.Mapping.Generated) { plan.Mapping.Path = System.IO.Path.Combine(plan.Backup, "old-to-new-ids.csv"); plan.Mapping.Generated = false; }
            if (plan.ContextInputs.Count > 0)
            {
                string context = System.IO.Path.Combine(plan.Backup, "context"); Directory.CreateDirectory(context);
                foreach (var input in plan.ContextInputs)
                {
                    Require(System.IO.Path.GetFileName(input.Key) == input.Key, "Invalid context backup name.");
                    WriteNew(System.IO.Path.Combine(context, input.Key), input.Value.Bytes);
                }
            }
            if (plan.DetectedModInfo != null) WriteNew(System.IO.Path.Combine(plan.Backup, "detected-modinfo.json"), plan.DetectedModInfo.Bytes);
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "RESTORE.txt"), "THIS OPERATION EDITED SAVE FILES.\r\nClose the game and save editors before restoring.\r\nCopy each original from originals over its exact destination:\r\n" + string.Join("\r\n", plan.Saves.Select(s => System.IO.Path.GetFileName(s.Input.Path) + " -> " + s.Input.Path)) + "\r\nRestoring recovers the pre-repair save, including its old IDs. Use matching localization IDs.\r\nOther saves and game data were not edited. Keep this backup until in-game testing is complete.\r\n", new UTF8Encoding(false));
            Report(plan, "Prepared", "All selected saves, mapping and mod tables backed up and byte-verified. No saves edited yet.");
            Console.WriteLine("Verified SAVE FILE backups: " + originals);
            var temps = new Dictionary<ItemSave, string>(); var committed = new List<ItemSave>();
            try
            {
                foreach (ItemSave save in plan.Saves.Where(s => s.Changes.Count > 0))
                {
                    string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(save.Input.Path), ".item-repair-" + Guid.NewGuid().ToString("N") + ".tmp");
                    temps.Add(save, temp); WriteNew(temp, save.Output);
                }
                StillMatches(plan);
                foreach (ItemSave save in plan.Saves.Where(s => s.Changes.Count > 0))
                {
                    if (plan.VerifyContext != null) plan.VerifyContext();
                    save.Input.Verify(); File.Replace(temps[save], save.Input.Path, null, true); committed.Add(save);
                    if (!File.ReadAllBytes(save.Input.Path).SequenceEqual(save.Output)) throw new IOException("Repaired save verification failed: " + save.Input.Path);
                }
                if (plan.VerifyContext != null) plan.VerifyContext();
                Report(plan, "Complete", "Only listed ID bit fields and necessary character checksums changed. All outputs verified.");
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (ItemSave save in committed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!File.ReadAllBytes(save.Input.Path).SequenceEqual(save.Output)) throw new IOException("Concurrent edit; recovery left current save untouched.");
                        WriteNew(temps[save], save.Input.Bytes); File.Replace(temps[save], save.Input.Path, null, true); save.Input.Verify();
                    }
                    catch (Exception recovery) { failures.Add(save.Input.Path + ": " + recovery.Message); }
                }
                string recoveryMessage = failures.Count == 0 ? "Any committed save changes were restored and verified." : "Automatic recovery incomplete: " + string.Join(" | ", failures);
                try { Report(plan, failures.Count == 0 ? "RolledBack" : "RecoveryRequired", error.Message + " " + recoveryMessage); } catch { }
                throw new IOException(error.Message + "\n" + recoveryMessage + "\nVerified originals: " + originals, error);
            }
            finally { foreach (string temp in temps.Values) { try { if (File.Exists(temp)) File.Delete(temp); } catch { } } }
            Console.WriteLine("SAVE FILE EDIT COMPLETE: " + plan.Changes + " ID fields repaired. Verified originals/report: " + plan.Backup);
        }
    }
}
