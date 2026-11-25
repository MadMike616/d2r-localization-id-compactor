// SPDX-License-Identifier: MIT
// Copyright (c) 2026 MadMike
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace D2RLocalizationCompactor
{
    internal sealed class SaveOnlyPlan
    {
        public ItemRepairPlan Repair;
        public string SaveFolder, HistoryFolder, BackupRoot;
        public InputSnapshot Current, Metadata;
        public readonly List<InputSnapshot> History = new List<InputSnapshot>();
        public readonly Dictionary<uint, List<string>> Ambiguous = new Dictionary<uint, List<string>>();
        public int Aliases;
    }
    internal static class SaveOnly
    {
        private sealed class Target
        {
            public string Key;
            public uint Id;
            public bool Missing;
            public string Identity { get { return Key + "\0" + (Missing ? "missing" : Id.ToString(CultureInfo.InvariantCulture)); } }
        }
        private static string[] HistoryFiles(string folder)
        {
            if (!Directory.Exists(folder)) return new string[0];
            if (new DirectoryInfo(folder).Parent == null) throw new InvalidDataException("Select this mod's backup folder, not a drive root.");
            var result = new List<string>(); var pending = new Stack<Tuple<string, int>>(); pending.Push(Tuple.Create(folder, 0)); int directories = 0;
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                if (++directories > 10000 || next.Item2 > 32) throw new InvalidDataException("History search limit exceeded. Select a narrower backup folder with --history-folder.");
                ChronicleRepair.PlainPath(Path.Combine(next.Item1, ".history-path-check"));
                foreach (string file in Directory.GetFiles(next.Item1))
                    if (Path.GetFileName(file).Equals("old-to-new-ids.csv", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).Equals("item-runes.json", StringComparison.OrdinalIgnoreCase))
                    {
                        ChronicleRepair.PlainPath(file); result.Add(Path.GetFullPath(file));
                        if (result.Count > 5000) throw new InvalidDataException("Too many history inputs. Select a narrower --history-folder.");
                    }
                foreach (string child in Directory.GetDirectories(next.Item1))
                    if (!Path.GetFileName(child).StartsWith("Save-only-", StringComparison.OrdinalIgnoreCase)) pending.Push(Tuple.Create(child, next.Item2 + 1));
            }
            return result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        private static string RuneFile(string folder)
        {
            string[] files = Directory.GetFiles(folder).Where(p => Path.GetFileName(p).Equals("item-runes.json", StringComparison.OrdinalIgnoreCase)).ToArray();
            ItemRepair.Require(files.Length == 1, "EDIT SAVE FILES ONLY needs exactly one current item-runes.json in the selected strings folder."); return files[0];
        }
        private static string Quote(string value)
        {
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        private static string[] Saves(string folder) { return Directory.Exists(folder) ? ModPaths.Saves(folder, false) : new string[0]; }
        public static SaveOnlyPlan Build(string strings, string saveFolder, string tables, string backup, string historyFolder, InputSnapshot metadata, Reference reference)
        {
            strings = Path.GetFullPath(strings); string runeFile = RuneFile(strings); ChronicleRepair.PlainPath(runeFile);
            Document current = Document.Read(runeFile);
            ItemRepair.Require(current.Records != null && current.Records.Count > 0, "Current item-runes.json has no numeric localization entries.");
            ItemRepair.Require(current.Records.All(r => r.Id >= 0 && r.Id <= Engine.MaximumId), "Current item-runes.json has an ID outside the supported 0-65534 range.");
            ItemRepair.Require(!current.Records.GroupBy(r => r.Id).Any(g => g.Count() > 1) && !current.Records.GroupBy(r => r.Key, StringComparer.Ordinal).Any(g => g.Count() > 1), "Current item-runes.json has duplicate IDs or keys; save identities are ambiguous.");
            var plan = new SaveOnlyPlan { Current = new InputSnapshot { Path = runeFile, Bytes = current.Original }, Metadata = metadata,
                SaveFolder = Path.GetFullPath(saveFolder), BackupRoot = Path.GetFullPath(backup ?? Engine.DefaultBackupRoot(strings)),
                HistoryFolder = Path.GetFullPath(historyFolder ?? Engine.DefaultBackupRoot(strings)) };
            if (historyFolder != null && !Directory.Exists(plan.HistoryFolder)) throw new DirectoryNotFoundException("Historical backup folder not found: " + plan.HistoryFolder);
            Engine.ValidateBackupRoot(strings, plan.BackupRoot); Engine.ValidateBackupRoot(plan.SaveFolder, plan.BackupRoot);
            var byKey = current.Records.ToDictionary(r => r.Key, r => new Target { Key = r.Key, Id = (uint)r.Id }, StringComparer.Ordinal);
            var byId = byKey.Values.ToDictionary(t => t.Id);
            var aliases = new Dictionary<uint, Dictionary<string, Target>>();
            Action<uint, Target> add = (id, target) => { Dictionary<string, Target> values; if (!aliases.TryGetValue(id, out values)) aliases.Add(id, values = new Dictionary<string, Target>(StringComparer.Ordinal)); values[target.Identity] = target; };
            foreach (Target target in byKey.Values) add(target.Id, target);
            Dictionary<int, HashSet<string>> native;
            if (!reference.Files.TryGetValue("item-runes.json", out native)) native = new Dictionary<int, HashSet<string>>();
            foreach (var row in native.Where(p => p.Key >= 0 && p.Key <= Engine.MaximumId))
            {
                uint id = (uint)row.Key;
                if (!byId.ContainsKey(id)) byId.Add(id, new Target { Key = row.Value.OrderBy(k => k, StringComparer.Ordinal).First(), Id = id });
                add(id, byId[id]);
            }
            Func<string, uint, Target> identify = (key, hint) =>
            {
                Target target;
                if (byKey.TryGetValue(key, out target)) return target;
                HashSet<string> nativeKeys;
                if (hint <= int.MaxValue && native.TryGetValue((int)hint, out nativeKeys) && nativeKeys.Contains(key) && byId.TryGetValue(hint, out target)) return target;
                return new Target { Key = key, Missing = true };
            };
            string[] historyFiles = HistoryFiles(plan.HistoryFolder); long bytes = 0;
            foreach (string file in historyFiles)
            {
                long length = new FileInfo(file).Length;
                ItemRepair.Require(length <= 256L * 1024 * 1024 - bytes, "Historical inputs exceed 256 MiB. Select a narrower --history-folder.");
                InputSnapshot input = InputSnapshot.Read(file); bytes += input.Bytes.LongLength;
                ItemRepair.Require(bytes <= 256L * 1024 * 1024, "Historical inputs exceed 256 MiB. Select a narrower --history-folder."); plan.History.Add(input);
                if (Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    foreach (RuneMapping row in ChronicleRepair.ReadHistoryRows(input.Bytes))
                    {
                        Target unescaped;
                        bool escaped = row.Key.Length > 1 && row.Key[0] == '\'' && "=+-@".IndexOf(row.Key[1]) >= 0 && byKey.TryGetValue(row.Key.Substring(1), out unescaped);
                        // CSV exports escape formula prefixes. Preserve both
                        // candidates if an actual apostrophe-prefixed key also
                        // exists; a numeric save cannot disambiguate them.
                        foreach (uint id in new[] { row.OldId, row.NewId })
                        {
                            Target raw = identify(row.Key, id);
                            if (!escaped || !raw.Missing) add(id, raw);
                            if (escaped) add(id, byKey[row.Key.Substring(1)]);
                        }
                    }
                else
                {
                    Document doc = Document.Read(file);
                    ItemRepair.Require(doc.Original.SequenceEqual(input.Bytes) && doc.Records != null, "Historical item-runes.json is invalid or changed while reading: " + file);
                    foreach (Record row in doc.Records) add((uint)row.Id, identify(row.Key, (uint)row.Id));
                }
            }
            var valid = new Dictionary<uint, Target>();
            foreach (var alias in aliases)
            {
                if (alias.Value.Count == 1 && !alias.Value.Values.Single().Missing) valid.Add(alias.Key, alias.Value.Values.Single());
                else plan.Ambiguous.Add(alias.Key, alias.Value.Values.Select(t => t.Key + (t.Missing ? " (key absent from current file)" : " -> " + t.Id)).OrderBy(t => t, StringComparer.Ordinal).ToList());
            }
            plan.Aliases = valid.Count;
            var csv = new StringBuilder("File,EntryIndex,Key,OldID,NewID\r\n");
            foreach (var alias in valid.OrderBy(p => p.Key)) csv.Append("item-runes.json,0,").Append(Quote(alias.Value.Key)).Append(',').Append(alias.Key.ToString(CultureInfo.InvariantCulture)).Append(',').Append(alias.Value.Id.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            var encoding = new UTF8Encoding(true);
            var mapping = new InputSnapshot { Path = Path.Combine(plan.BackupRoot, "automatically-resolved-runeword-ids.csv"), Bytes = encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray(), Generated = true };
            var rules = new SaveMigrationRules { AllowRuneAliases = true, BeforeRuneIds = new HashSet<uint>(valid.Keys), ResolveSource = (id, location) => id,
                ValidateRune = (id, location) =>
                {
                    List<string> choices;
                    if (plan.Ambiguous.TryGetValue(id, out choices)) throw new InvalidDataException("Saved runeword ID " + id + " at " + location + " has conflicting or removed identities in the selected history: " + string.Join("; ", choices) + ". No saves were edited. Select verified history for this mod; do not guess.");
                    if (!valid.ContainsKey(id)) throw new InvalidDataException("Saved runeword ID " + id + " at " + location + " cannot be identified from current item-runes.json or this mod's selected backup history. Old saves contain numeric IDs, not string keys. Supply matching historical backups with --history-folder. No saves were edited.");
                } };
            string[] files = Saves(plan.SaveFolder);
            if (files.Length > 0)
            {
                ItemRepair.Require(!string.IsNullOrWhiteSpace(tables), "Save-only editing needs the mod's five complete effective TXT tables.");
                plan.Repair = ItemRepair.BuildPrepared(null, plan.SaveFolder, mapping, tables, plan.BackupRoot, true, rules);
                plan.Repair.SaveOnly = true; plan.Repair.DetectedModInfo = metadata;
                plan.Repair.ContextInputs.Add("current-item-runes.json", plan.Current);
                for (int i = 0; i < plan.History.Count; ++i) plan.Repair.ContextInputs.Add("history-" + i.ToString("D4", CultureInfo.InvariantCulture) + Path.GetExtension(plan.History[i].Path), plan.History[i]);
                plan.Repair.VerifyContext = () => Verify(plan);
            }
            Verify(plan); return plan;
        }
        public static void Verify(SaveOnlyPlan plan)
        {
            plan.Current.Verify(); if (plan.Metadata != null) plan.Metadata.Verify();
            if (!string.Equals(RuneFile(Path.GetDirectoryName(plan.Current.Path)), plan.Current.Path, StringComparison.OrdinalIgnoreCase)) throw new IOException("Current item-runes.json selection changed after preview.");
            if (!HistoryFiles(plan.HistoryFolder).SequenceEqual(plan.History.Select(i => i.Path), StringComparer.OrdinalIgnoreCase)) throw new IOException("Historical backup file list changed after preview.");
            foreach (InputSnapshot input in plan.History) input.Verify();
            string[] expected = plan.Repair == null ? new string[0] : plan.Repair.Saves.Select(s => s.Input.Path).ToArray();
            if (!Saves(plan.SaveFolder).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase)) throw new IOException("The save-folder file list changed after preview.");
        }
        public static void Preview(SaveOnlyPlan plan)
        {
            Console.WriteLine("\n4 / EDIT SAVE FILES ONLY - EDITS ALL DIRECT .d2s/.d2i SAVES IN THIS PROFILE.");
            Console.WriteLine("Current runeword IDs: " + plan.Current.Path + "\nSave folder: " + plan.SaveFolder + "\nHistory folder: " + plan.HistoryFolder + "\nBackup root: " + plan.BackupRoot);
            Console.WriteLine("{0} historical inputs; {1} unambiguous ID aliases; {2} conflicting/removed aliases (a save using one aborts the batch).", plan.History.Count, plan.Aliases, plan.Ambiguous.Count);
            Console.WriteLine("The destination IDs come from current item-runes.json. History matches saved old IDs to exact string keys automatically.");
            if (plan.Repair != null) ItemRepair.Preview(plan.Repair); else Console.WriteLine("No direct .d2s/.d2i saves exist. No profile will be created.");
            Console.WriteLine("Active JSON stays byte-for-byte unchanged. Only saved runeword IDs, Chronicle runeword history, and necessary character checksums change. Drop-source fields stay unchanged.");
            Console.WriteLine("All selected saves, current item-runes.json, historical inputs and parsing tables are backed up and verified before any save edit.");
        }
        public static void Apply(SaveOnlyPlan plan)
        {
            Verify(plan);
            if (plan.Repair == null) { Console.WriteLine("No saves to edit. No files were changed."); return; }
            ItemRepair.Apply(plan.Repair); plan.Current.Verify();
        }
    }
}
