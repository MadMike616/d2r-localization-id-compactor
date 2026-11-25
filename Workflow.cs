// SPDX-License-Identifier: MIT
// Copyright (c) 2026 MadMike
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace D2RLocalizationCompactor
{
    internal enum WorkflowMode { Unspecified, Standard, SkipRunewords, CompactAndEditSaves, EditSaveFilesOnly }
    internal sealed class WorkflowFile
    {
        public InputSnapshot Input;
        public byte[] Output;
        public string Group;
    }
    internal sealed class WorkflowPlan
    {
        public Plan Localization;
        public ItemRepairPlan Saves;
        public InputSnapshot ModInfo, Mapping;
        public string SaveFolder, Backup;
        public readonly List<WorkflowFile> Files = new List<WorkflowFile>();
        public readonly List<InputSnapshot> Tables = new List<InputSnapshot>();
    }
    internal static class Workflow
    {
        public static WorkflowMode ParseMode(string value)
        {
            string mode = value.Trim().Replace('-', ' ').Replace('_', ' ').ToUpperInvariant();
            if (mode == "1" || mode == "STANDARD" || mode == "STANDARD COMP") return WorkflowMode.Standard;
            if (mode == "2" || mode == "SKIP" || mode == "SKIP RUNEWORDS") return WorkflowMode.SkipRunewords;
            if (mode == "3" || mode == "EDIT SAVES" || mode == "COMPACT AND EDIT SAVES") return WorkflowMode.CompactAndEditSaves;
            if (mode == "4" || mode == "SAVE ONLY" || mode == "EDIT SAVE FILES ONLY") return WorkflowMode.EditSaveFilesOnly;
            throw new ArgumentException("Choose 1 / STANDARD COMP, 2 / SKIP RUNEWORDS, 3 / COMPACT AND EDIT SAVES, or 4 / EDIT SAVE FILES ONLY. There is no default.");
        }
        public static void Menu()
        {
            Console.WriteLine("\n1 / STANDARD COMP");
            Console.WriteLine("  Compact all localization JSON. No saves edited; existing saves may not work.");
            Console.WriteLine("  Recommended for mods with no save files.");
            Console.WriteLine("2 / SKIP RUNEWORDS");
            Console.WriteLine("  Compact other localization JSON; leave item-runes.json unchanged.");
            Console.WriteLine("  No saves edited. Preserves existing runeword IDs and their 1000-ID buffers.");
            Console.WriteLine("3 / COMPACT AND EDIT SAVES");
            Console.WriteLine("  Compact all localization JSON and migrate the mod's character/shared-stash saves.");
            Console.WriteLine("  EDITS SAVE FILES. One unsupported save stops the operation before any edits.");
            Console.WriteLine("4 / EDIT SAVE FILES ONLY");
            Console.WriteLine("  Read current item-runes.json; repair all character/shared-stash saves in this profile.");
            Console.WriteLine("  EDITS SAVE FILES. JSON unchanged. Uses matching backup history to identify old IDs.");
            Console.WriteLine("All four options make verified backups before edits. NO DEFAULT OPTION.");
        }
        public static WorkflowMode Choose()
        {
            Menu();
            for (;;)
            {
                Console.Write("Choose 1, 2, 3 or 4 (no default): "); string value = Console.ReadLine();
                if (value == null) throw new ArgumentException("No option selected. No files were changed.");
                try { return ParseMode(value); } catch (ArgumentException error) { Console.WriteLine(error.Message); }
            }
        }
        private static string[] Saves(string folder) { return Directory.Exists(folder) ? ModPaths.Saves(folder, false) : new string[0]; }
        private static SaveMigrationRules Rules(Plan localization)
        {
            var targets = new Dictionary<uint, HashSet<uint>>();
            Action<uint, uint> add = (oldId, newId) => { HashSet<uint> values; if (!targets.TryGetValue(oldId, out values)) targets.Add(oldId, values = new HashSet<uint>()); values.Add(newId); };
            foreach (int id in localization.Reference.Global.Keys) add((uint)id, (uint)id);
            foreach (Document doc in localization.Documents.Where(d => d.Records != null))
            {
                var edits = localization.Mapping.Where(c => c.File == doc.Name).ToDictionary(c => c.Index);
                foreach (Record row in doc.Records) { Change edit; add((uint)row.Id, edits.TryGetValue(row.Index, out edit) ? (uint)edit.NewId : (uint)row.Id); }
            }
            var runeIds = new HashSet<uint>();
            Dictionary<int, HashSet<string>> native;
            if (localization.Reference.Files.TryGetValue("item-runes.json", out native)) foreach (int id in native.Keys) runeIds.Add((uint)id);
            foreach (Document doc in localization.Documents.Where(d => d.Name.Equals("item-runes.json", StringComparison.OrdinalIgnoreCase) && d.Records != null))
            {
                if (doc.Records.GroupBy(r => r.Id).Any(g => g.Count() > 1)) throw new InvalidDataException("Combined migration cannot resolve duplicate old IDs in item-runes.json. No JSON or saves were changed.");
                foreach (Record row in doc.Records) runeIds.Add((uint)row.Id);
            }
            return new SaveMigrationRules { BeforeRuneIds = runeIds, ResolveSource = (id, location) =>
            {
                if (id == 0 || id == 65535) return id;
                HashSet<uint> values;
                if (!targets.TryGetValue(id, out values)) return id; // not a known changed string
                if (values.Count != 1) throw new InvalidDataException("Ambiguous saved localization source ID " + id + " at " + location + ". Its old ID belongs to strings with different new IDs. No files were changed.");
                uint result = values.Single();
                if (result > Engine.MaximumId) throw new InvalidDataException("Saved source ID exceeds the native field range.");
                return result;
            } };
        }
        public static WorkflowPlan Build(Plan localization, string saveFolder, string tables, InputSnapshot metadata)
        {
            localization.EditSaves = true;
            var plan = new WorkflowPlan { Localization = localization, SaveFolder = Path.GetFullPath(saveFolder), ModInfo = metadata,
                Backup = Path.Combine(localization.BackupRoot, "Compact-and-edit-saves-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)) };
            Engine.ValidateBackupRoot(plan.SaveFolder, localization.BackupRoot);
            plan.Mapping = new InputSnapshot { Path = Path.Combine(plan.Backup, "old-to-new-ids.csv"), Bytes = Engine.MappingBytes(localization), Generated = true };
            SaveMigrationRules rules = Rules(localization);
            string[] files = Saves(plan.SaveFolder);
            if (files.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(tables)) throw new InvalidDataException("Save migration needs this mod's five effective game-data TXT tables. No JSON or saves were changed.");
                plan.Saves = ItemRepair.BuildPrepared(files, null, plan.Mapping, tables, localization.BackupRoot, true, rules);
                plan.Tables.AddRange(plan.Saves.Tables.Inputs);
                foreach (ItemSave save in plan.Saves.Saves) plan.Files.Add(new WorkflowFile { Input = save.Input, Output = save.Output, Group = "saves" });
            }
            foreach (Document doc in localization.Documents) plan.Files.Add(new WorkflowFile { Input = new InputSnapshot { Path = doc.Path, Bytes = doc.Original }, Output = doc.Output, Group = "localization" });
            if (metadata != null) Engine.ValidateBackupRoot(Path.Combine(Path.GetDirectoryName(metadata.Path), "data"), localization.BackupRoot);
            Verify(plan); return plan;
        }
        public static void Verify(WorkflowPlan plan)
        {
            Engine.SnapshotStillMatches(plan.Localization);
            if (plan.ModInfo != null) plan.ModInfo.Verify();
            foreach (InputSnapshot input in plan.Tables) input.Verify();
            plan.Mapping.Verify();
            string[] expected = plan.Files.Where(f => f.Group == "saves").Select(f => f.Input.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!Saves(plan.SaveFolder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase)) throw new IOException("The mod's save file list changed after preview. No files were changed.");
            foreach (WorkflowFile file in plan.Files) file.Input.Verify();
            if (Engine.FindLegacyBackup(plan.Localization.Target) != plan.Localization.LegacyBackup) throw new IOException("Legacy backup state changed after preview.");
        }
        public static void Preview(WorkflowPlan plan)
        {
            Engine.Preview(plan.Localization);
            Console.WriteLine("\nCOMPACT AND EDIT SAVES - JSON AND SAVE FILES WILL BE EDITED TOGETHER.");
            Console.WriteLine("Save profile: " + plan.SaveFolder + "\nCombined backup: " + plan.Backup);
            if (plan.Saves != null) ItemRepair.Preview(plan.Saves);
            else Console.WriteLine("No .d2s/.d2i saves exist in this profile. No save folder will be created.");
            Console.WriteLine("The mapping was generated from this JSON plan automatically; no CSV selection is needed.");
            Console.WriteLine("Known saved runeword IDs and Chronicle drop-source localization IDs migrate; item stats, rolls, sockets and table-row IDs do not.");
            Console.WriteLine("ALL scanned JSON and ALL selected saves are backed up and verified before the first replacement. A write failure triggers recovery of both groups.");
        }
        public static void RequireGameClosed()
        {
            foreach (string name in new[] { "D2R", "D2RLoader" })
            {
                Process[] processes = Process.GetProcessesByName(name);
                try { if (processes.Length > 0) throw new IOException("Close the game and D2RLoader before applying this workflow. No files were changed."); }
                finally { foreach (Process process in processes) process.Dispose(); }
            }
        }
        private static void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Backup/staging verification failed: " + path);
        }
        private static void Journal(WorkflowPlan plan, string status, string message)
        {
            var report = new { tool = "D2R Localization ID Compactor", version = "1.6.1", mode = "COMPACT AND EDIT SAVES", utc = DateTime.UtcNow.ToString("o"), status = status, message = message,
                saveFolder = plan.SaveFolder, stringsFolder = plan.Localization.Target,
                mappingSha256 = Engine.Hash(plan.Mapping.Bytes), modInfoSha256 = plan.ModInfo == null ? null : Engine.Hash(plan.ModInfo.Bytes),
                tables = plan.Tables.Select(t => new { path = t.Path, sha256 = Engine.Hash(t.Bytes) }).ToArray(),
                files = plan.Files.Select(f => new { path = f.Input.Path, group = f.Group, originalSha256 = Engine.Hash(f.Input.Bytes), outputSha256 = Engine.Hash(f.Output), changed = !f.Input.Bytes.SequenceEqual(f.Output) }).ToArray(),
                saveChanges = plan.Saves == null ? null : plan.Saves.Saves.Select(s => new { path = s.Input.Path,
                    fields = s.Changes.Select(p => new { bitOffset = p.BitOffset, width = p.Width, kind = p.Kind, oldId = p.OldId, newId = p.NewId, location = p.Location }).ToArray() }).ToArray() };
            File.WriteAllText(Path.Combine(plan.Backup, "workflow-report.json"), Engine.Json(report), new UTF8Encoding(false));
        }
        public static void Apply(WorkflowPlan plan)
        {
            Verify(plan);
            if (!plan.Files.Any(f => !f.Input.Bytes.SequenceEqual(f.Output)) && plan.Localization.LegacyBackup == null) { Console.WriteLine("No JSON or save changes are needed. No files were edited."); return; }
            if (Directory.Exists(plan.Backup)) throw new IOException("The planned backup run folder already exists. Run a fresh preview.");
            var originals = Path.Combine(plan.Backup, "originals");
            foreach (string group in new[] { "localization", "saves" }) Directory.CreateDirectory(Path.Combine(originals, group));
            foreach (WorkflowFile file in plan.Files) WriteNew(Path.Combine(originals, file.Group, Path.GetFileName(file.Input.Path)), file.Input.Bytes);
            Directory.CreateDirectory(Path.Combine(plan.Backup, "mod-tables"));
            foreach (InputSnapshot table in plan.Tables) WriteNew(Path.Combine(plan.Backup, "mod-tables", Path.GetFileName(table.Path)), table.Bytes);
            if (plan.ModInfo != null) WriteNew(Path.Combine(plan.Backup, "modinfo.json"), plan.ModInfo.Bytes);
            WriteNew(plan.Mapping.Path, plan.Mapping.Bytes); plan.Mapping.Generated = false;
            File.WriteAllText(Path.Combine(plan.Backup, "RESTORE.txt"), "THIS OPERATION EDITED LOCALIZATION JSON AND SAVE FILES.\r\nClose the game/loader/editors. Restore BOTH groups from originals to their exact destinations below.\r\n" + string.Join("\r\n", plan.Files.Select(f => Path.Combine("originals", f.Group, Path.GetFileName(f.Input.Path)) + " -> " + f.Input.Path)) + "\r\nDo not restore JSON without its matching saves, or saves without matching JSON.\r\nA crash/power loss can interrupt a multi-file operation; use the verified originals and workflow report to recover.\r\n", new UTF8Encoding(false));
            Journal(plan, "Prepared", "All JSON, saves, parsing tables, metadata and generated mapping backed up and verified. No active files edited yet.");
            Console.WriteLine("Verified combined backups: " + plan.Backup);
            var temps = new Dictionary<WorkflowFile, string>(); var committed = new List<WorkflowFile>();
            try
            {
                foreach (WorkflowFile file in plan.Files.Where(f => !f.Input.Bytes.SequenceEqual(f.Output)))
                {
                    string temp = Path.Combine(Path.GetDirectoryName(file.Input.Path), ".compact-save-" + Guid.NewGuid().ToString("N") + ".tmp"); temps.Add(file, temp); WriteNew(temp, file.Output);
                }
                Verify(plan);
                if (plan.Localization.LegacyBackup != null)
                {
                    // Both resolved locations were validated outside/inside the
                    // intended data tree by Engine before this operation.
                    string destination = Path.Combine(plan.Localization.BackupRoot, "Migrated-legacy-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(plan.Localization.LegacyBackup, destination);
                    Console.WriteLine("Moved old backups outside game data: " + destination);
                }
                foreach (WorkflowFile file in plan.Files.Where(f => !f.Input.Bytes.SequenceEqual(f.Output)).OrderBy(f => f.Group == "localization" ? 0 : 1))
                {
                    file.Input.Verify(); File.Replace(temps[file], file.Input.Path, null, true); committed.Add(file);
                    if (!File.ReadAllBytes(file.Input.Path).SequenceEqual(file.Output)) throw new IOException("Installed file verification failed: " + file.Input.Path);
                }
                Journal(plan, "Complete", "Compacted JSON and corresponding save fields installed and verified together.");
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (WorkflowFile file in committed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!File.ReadAllBytes(file.Input.Path).SequenceEqual(file.Output)) throw new IOException("Concurrent edit; recovery left this file untouched.");
                        WriteNew(temps[file], file.Input.Bytes); File.Replace(temps[file], file.Input.Path, null, true); file.Input.Verify();
                    }
                    catch (Exception recovery) { failures.Add(file.Input.Path + ": " + recovery.Message); }
                }
                string recoveryMessage = failures.Count == 0 ? "All committed JSON/save changes were restored and verified." : "Incomplete recovery: " + string.Join(" | ", failures);
                try { Journal(plan, failures.Count == 0 ? "RolledBack" : "RecoveryRequired", error.Message + " " + recoveryMessage); } catch { }
                throw new IOException(error.Message + "\n" + recoveryMessage + "\nCombined originals: " + originals, error);
            }
            finally { foreach (string temp in temps.Values) { try { if (File.Exists(temp)) File.Delete(temp); } catch { } } }
            Console.WriteLine("COMPLETE: localization JSON and supported save references migrated together. Backups: " + plan.Backup);
        }
    }
}
