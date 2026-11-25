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
    internal sealed class RuneMapping
    {
        public string Key;
        public uint OldId, NewId;
    }
    internal sealed class ChroniclePatch
    {
        public int Offset;
        public string Key;
        public uint OldId, NewId;
    }
    internal sealed class ChroniclePlan
    {
        public string Save, Mapping, BackupRoot, Backup;
        public byte[] Original, Output, MappingBytes;
        public int Pages, Chronicles, Runewords, UnchangedMapped, AlreadyCurrent;
        public readonly List<uint> Unmapped = new List<uint>();
        public readonly List<ChroniclePatch> Changes = new List<ChroniclePatch>();
        public readonly List<ChroniclePatch> SourceChanges = new List<ChroniclePatch>();
        public InputSnapshot DetectedModInfo;
    }

    // Format references (no third-party implementation copied):
    // https://github.com/ResurrectedTrader/D2SSharp/blob/main/src/D2SSharp/Model/D2StashTab.cs
    // https://github.com/ResurrectedTrader/D2SSharp/blob/main/src/D2SSharp/Model/ChronicleSection.cs
    // Only validated Chronicle runeword ItemId fields are patched. Item streams,
    // set/unique discoveries, source IDs, timestamps and padding stay opaque.
    internal static class ChronicleRepair
    {
        private static uint U32(byte[] b, int at) { return (uint)b[at] | ((uint)b[at + 1] << 8) | ((uint)b[at + 2] << 16) | ((uint)b[at + 3] << 24); }
        private static int U16(byte[] b, int at) { return b[at] | (b[at + 1] << 8); }
        private static void Put32(byte[] b, int at, uint value) { for (int i = 0; i < 4; ++i) b[at + i] = (byte)(value >> (8 * i)); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        public static void PlainPath(string path)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Save and mapping files must not be links/reparse points: " + path);
            for (DirectoryInfo current = new DirectoryInfo(Path.GetDirectoryName(path)); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Save and mapping paths must not use links/reparse points: " + current.FullName);
        }
        private static IEnumerable<List<string>> Csv(string text)
        {
            int at = 0;
            while (at < text.Length)
            {
                var row = new List<string>();
                for (;;)
                {
                    var field = new StringBuilder();
                    if (at < text.Length && text[at] == '"')
                    {
                        ++at; bool closed = false;
                        while (at < text.Length)
                        {
                            char c = text[at++];
                            if (c != '"') { field.Append(c); continue; }
                            if (at < text.Length && text[at] == '"') { field.Append('"'); ++at; }
                            else { closed = true; break; }
                        }
                        Require(closed, "Unterminated quoted CSV field.");
                        Require(at == text.Length || text[at] == ',' || text[at] == '\r' || text[at] == '\n', "Unexpected text after a quoted CSV field.");
                    }
                    else
                    {
                        while (at < text.Length && text[at] != ',' && text[at] != '\r' && text[at] != '\n')
                        { Require(text[at] != '"', "Unexpected quote in an unquoted CSV field."); field.Append(text[at++]); }
                    }
                    row.Add(field.ToString());
                    if (at < text.Length && text[at] == ',') { ++at; continue; }
                    if (at < text.Length && text[at] == '\r') { ++at; if (at < text.Length && text[at] == '\n') ++at; }
                    else if (at < text.Length && text[at] == '\n') ++at;
                    yield return row; break;
                }
            }
        }
        public static Dictionary<uint, RuneMapping> ReadMapping(byte[] bytes)
        {
            return ReadMappingPrepared(bytes, false);
        }
        public static List<RuneMapping> ReadHistoryRows(byte[] bytes)
        {
            string text;
            if (bytes.Length >= 4 && ((bytes[0] == 255 && bytes[1] == 254 && bytes[2] == 0 && bytes[3] == 0) || (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 254 && bytes[3] == 255))) throw new InvalidDataException("UTF-32 mapping CSV is unsupported.");
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) text = new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) text = new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
            else
            {
                int bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                text = new UTF8Encoding(false, true).GetString(bytes, bom, bytes.Length - bom);
            }
            List<List<string>> rows = Csv(text).Where(r => r.Count != 1 || !string.IsNullOrWhiteSpace(r[0])).ToList();
            Require(rows.Count > 0, "The mapping CSV is empty.");
            var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows[0].Count; ++i)
            {
                string name = rows[0][i].Trim().Replace("_", "");
                Require(name.Length > 0 && !header.ContainsKey(name), "The mapping CSV has empty or duplicate column names."); header.Add(name, i);
            }
            foreach (string name in new[] { "File", "Key", "OldID", "NewID" }) Require(header.ContainsKey(name), "The mapping CSV is missing column " + name + ". Use old-to-new-ids.csv from the relevant compaction.");
            var result = new List<RuneMapping>();
            foreach (List<string> row in rows.Skip(1))
            {
                Require(row.Count == rows[0].Count, "The mapping CSV has an inconsistent column count.");
                if (!row[header["File"]].Trim().Equals("item-runes.json", StringComparison.OrdinalIgnoreCase)) continue;
                uint oldId, newId;
                Require(uint.TryParse(row[header["OldID"]], NumberStyles.None, CultureInfo.InvariantCulture, out oldId), "Invalid OldID in an item-runes.json mapping row.");
                Require(uint.TryParse(row[header["NewID"]], NumberStyles.None, CultureInfo.InvariantCulture, out newId) && newId <= Engine.MaximumId, "Invalid NewID in an item-runes.json mapping row (maximum 65534).");
                string key = row[header["Key"]];
                Require(!string.IsNullOrWhiteSpace(key), "Ambiguous item-runes.json mapping: empty key.");
                result.Add(new RuneMapping { Key = key, OldId = oldId, NewId = newId });
            }
            return result;
        }
        public static Dictionary<uint, RuneMapping> ReadMappingPrepared(byte[] bytes, bool freshCompaction, bool allowAliases = false)
        {
            var result = new Dictionary<uint, RuneMapping>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var targets = new HashSet<uint>();
            foreach (RuneMapping row in ReadHistoryRows(bytes))
            {
                Require(!result.ContainsKey(row.OldId) && (allowAliases || (keys.Add(row.Key) && targets.Add(row.NewId))), "Ambiguous item-runes.json mapping: keys, old IDs and new IDs must each be unique.");
                result.Add(row.OldId, row);
            }
            Require(freshCompaction || result.Count > 0, "The CSV contains no item-runes.json mapping rows. A Preserve run does not provide a runeword migration; select the mapping from the run that changed those IDs.");
            foreach (RuneMapping row in result.Values)
                Require(freshCompaction || row.OldId == row.NewId || !result.ContainsKey(row.NewId), "Ambiguous overlapping old/new runeword ID ranges. This repair cannot distinguish an old ID from an already migrated ID; no save will be edited. A verified migration needs manual review.");
            return result;
        }
        public static ChroniclePlan Build(string save, string mapping, string backup)
        {
            return BuildPrepared(save, InputSnapshot.Read(mapping), backup, null);
        }
        public static ChroniclePlan BuildPrepared(string save, InputSnapshot mapping, string backup, SaveMigrationRules rules)
        {
            save = Path.GetFullPath(save);
            Require(Path.GetExtension(save).Equals(".d2i", StringComparison.OrdinalIgnoreCase), "Select a .d2i shared-stash SAVE FILE. Character .d2s files are not supported.");
            PlainPath(save); mapping.Verify();
            var plan = new ChroniclePlan { Save = save, Mapping = mapping.Path, Original = File.ReadAllBytes(save), MappingBytes = mapping.Bytes,
                BackupRoot = Path.GetFullPath(backup ?? Engine.DefaultBackupRoot(Path.GetDirectoryName(save))) };
            Engine.ValidateBackupRoot(Path.GetDirectoryName(save), plan.BackupRoot);
            Dictionary<uint, RuneMapping> ids = ReadMappingPrepared(plan.MappingBytes, rules != null, rules != null && rules.AllowRuneAliases);
            var destinations = new HashSet<uint>(ids.Values.Select(r => r.NewId));
            byte[] b = plan.Original; plan.Output = (byte[])b.Clone();
            int page = 0;
            while (page < b.Length)
            {
                Require(b.Length - page >= 64, "Truncated shared-stash page header.");
                Require(U32(b, page) == 0xAA55AA55, "Unknown shared-stash page magic at offset " + page + ".");
                Require(U32(b, page + 4) == 2 && U32(b, page + 8) == 105, "Unsupported shared-stash format/version. Only modern format 2, save version 105 is supported.");
                int size = U16(b, page + 16), kind = b[page + 20];
                Require(size >= 68 && size <= b.Length - page, "Invalid or truncated shared-stash page size.");
                Require(kind <= 2, "Unsupported shared-stash tab type.");
                ++plan.Pages;
                if (kind != 2) Require(b[page + 64] == 'J' && b[page + 65] == 'M', "Missing stored-item section marker.");
                else
                {
                    Require(size >= 84, "Truncated Chronicle header.");
                    int body = page + 64;
                    Require(U32(b, body) == 0xC0EAEDC0 && U16(b, body + 4) == 1, "Unsupported Chronicle magic/version. Only Chronicle version 1 is supported.");
                    int sets = U16(b, body + 6), uniques = U16(b, body + 8), runes = U16(b, body + 10);
                    Require(sets + uniques + runes <= (size - 84) / 10, "Chronicle discovery counts overrun the page.");
                    int end = body + 20 + (sets + uniques + runes) * 10;
                    Require(page + size - end == 64, "Unsupported Chronicle trailing-data layout (expected 64 preserved bytes).");
                    ++plan.Chronicles; plan.Runewords += runes;
                    if (rules != null)
                        for (int entry = 0; entry < sets + uniques + runes; ++entry)
                        {
                            int at = body + 20 + entry * 10 + 4; uint oldId = (uint)U16(b, at), newId = rules.ResolveSource(oldId, "Chronicle source at byte " + at);
                            if (oldId != newId) plan.SourceChanges.Add(new ChroniclePatch { Offset = at, OldId = oldId, NewId = newId, Key = "" });
                        }
                    var finalIds = new HashSet<uint>();
                    for (int entry = 0; entry < runes; ++entry)
                    {
                        int at = body + 20 + (sets + uniques + entry) * 10;
                        uint oldId = U32(b, at), newId = oldId; RuneMapping row;
                        if (rules != null && rules.ValidateRune != null) rules.ValidateRune(oldId, "Chronicle discovery at byte " + at);
                        if (ids.TryGetValue(oldId, out row))
                        {
                            newId = row.NewId;
                            if (oldId == newId) ++plan.UnchangedMapped;
                            else
                            {
                                plan.Changes.Add(new ChroniclePatch { Offset = at, Key = row.Key, OldId = oldId, NewId = newId });
                                Put32(plan.Output, at, newId);
                            }
                        }
                        else if (rules != null && !rules.BeforeRuneIds.Contains(oldId)) throw new InvalidDataException("Chronicle runeword ID " + oldId + " is absent from the current pre-compaction runeword table. Recover its historical mapping before combined compaction.");
                        else if (destinations.Contains(oldId)) ++plan.AlreadyCurrent;
                        else plan.Unmapped.Add(oldId);
                        Require(finalIds.Add(newId), "Duplicate or colliding Chronicle runeword IDs after mapping. No save will be edited.");
                    }
                }
                page += size;
            }
            Require(plan.Chronicles > 0, "No supported Chronicle tab was found in this shared-stash save.");
            // Reversing the complete patch list must recover the exact input.
            byte[] reverse = (byte[])plan.Output.Clone();
            foreach (ChroniclePatch patch in plan.Changes) Put32(reverse, patch.Offset, patch.OldId);
            Require(reverse.SequenceEqual(plan.Original), "Internal validation failed: an unrelated save byte changed.");
            return plan;
        }
        public static void Preview(ChroniclePlan plan)
        {
            Console.WriteLine("\nCHRONICLE REPAIR - THIS OPTION EDITS A SAVE FILE (.d2i).");
            Console.WriteLine("Close the game and all save editors before applying. Use the mapping for THIS mod and the IDs recorded in THIS save.");
            Console.WriteLine("Save file: " + plan.Save);
            Console.WriteLine("Mapping:   " + plan.Mapping);
            Console.WriteLine("Backup root (outside the save/game-data folder): " + plan.BackupRoot);
            Console.WriteLine("{0} pages; {1} Chronicle tabs; {2} runeword discoveries.", plan.Pages, plan.Chronicles, plan.Runewords);
            foreach (ChroniclePatch patch in plan.Changes) Console.WriteLine("  Offset 0x{0:X}: {1} -> {2}  Key={3}", patch.Offset, patch.OldId, patch.NewId, Engine.Json(patch.Key));
            Console.WriteLine("{0} runeword history IDs will change. {1} mapped IDs are unchanged; {2} already match mapping destinations.", plan.Changes.Count, plan.UnchangedMapped, plan.AlreadyCurrent);
            Console.WriteLine("{0} IDs absent from the mapping stay unchanged: {1}", plan.Unmapped.Count, string.Join(", ", plan.Unmapped.Distinct().OrderBy(v => v)));
            Console.WriteLine("Unmapped IDs may be valid vanilla IDs or unresolved stale IDs; they are not inferred or validated.");
            Console.WriteLine("Only Chronicle runeword discovery IDs change. Stored runeword ITEMS, character saves, set/unique history, sources and timestamps are not repaired.");
            Console.WriteLine("A wrong mod/history mapping can assign the wrong discoveries. This is not a general save repair or a guarantee that characters will load.");
        }
        private static void StillMatches(ChroniclePlan plan)
        {
            if (plan.DetectedModInfo != null) plan.DetectedModInfo.Verify();
            PlainPath(plan.Save); PlainPath(plan.Mapping);
            if (!File.ReadAllBytes(plan.Save).SequenceEqual(plan.Original) || !File.ReadAllBytes(plan.Mapping).SequenceEqual(plan.MappingBytes))
                throw new IOException("The save or mapping changed after preview. Close the game/editor and run the tool again.");
        }
        private static void WriteNew(string path, byte[] bytes)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Backup/staging verification failed: " + path);
        }
        private static void Report(ChroniclePlan plan, string status, string message)
        {
            var report = new { tool = "D2R Localization ID Compactor", version = "1.6.1", action = "Chronicle runeword ID SAVE FILE repair", utc = DateTime.UtcNow.ToString("o"),
                status = status, message = message, saveFile = plan.Save, mappingFile = plan.Mapping,
                detectedModInfo = plan.DetectedModInfo == null ? null : new { path = plan.DetectedModInfo.Path, sha256 = Engine.Hash(plan.DetectedModInfo.Bytes) },
                originalSha256 = Engine.Hash(plan.Original), repairedSha256 = Engine.Hash(plan.Output), mappingSha256 = Engine.Hash(plan.MappingBytes),
                pages = plan.Pages, chronicleTabs = plan.Chronicles, runewordDiscoveries = plan.Runewords,
                changes = plan.Changes.Select(p => new { offset = p.Offset, key = p.Key, oldId = p.OldId, newId = p.NewId }).ToArray(),
                unchangedMapped = plan.UnchangedMapped, alreadyCurrent = plan.AlreadyCurrent, unmappedIdsLeftUnchanged = plan.Unmapped.ToArray() };
            File.WriteAllText(Path.Combine(plan.Backup, "repair-report.json"), Engine.Json(report), new UTF8Encoding(false));
        }
        public static void Apply(ChroniclePlan plan)
        {
            StillMatches(plan);
            if (plan.Changes.Count == 0) { Console.WriteLine("No mapped Chronicle IDs need repair. No save file was edited."); return; }
            Engine.ValidateBackupRoot(Path.GetDirectoryName(plan.Save), plan.BackupRoot);
            if (plan.DetectedModInfo != null) Engine.ValidateBackupRoot(Path.Combine(Path.GetDirectoryName(plan.DetectedModInfo.Path), "data"), plan.BackupRoot);
            plan.Backup = Path.Combine(plan.BackupRoot, "Chronicle-repair-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string originals = Path.Combine(plan.Backup, "originals"); Directory.CreateDirectory(originals);
            WriteNew(Path.Combine(originals, Path.GetFileName(plan.Save)), plan.Original);
            WriteNew(Path.Combine(plan.Backup, "old-to-new-ids.csv"), plan.MappingBytes);
            if (plan.DetectedModInfo != null) WriteNew(Path.Combine(plan.Backup, "detected-modinfo.json"), plan.DetectedModInfo.Bytes);
            File.WriteAllText(Path.Combine(plan.Backup, "RESTORE.txt"), "THIS OPERATION EDITED A SAVE FILE.\r\nClose the game and all save editors.\r\nTo undo ONLY this repair, copy the file from originals back over:\r\n" + plan.Save + "\r\nKeep the backup until loading and Chronicle discoveries have been checked.\r\nThis restores the pre-repair save, including its old Chronicle IDs. Use matching localization IDs.\r\nOther saves and localization JSON were not changed by this repair.\r\n", new UTF8Encoding(false));
            Report(plan, "Prepared", "Original save and selected mapping backed up and byte-verified. No save file has been edited yet.");
            Console.WriteLine("Verified original SAVE FILE backup: " + Path.Combine(originals, Path.GetFileName(plan.Save)));
            string temp = Path.Combine(Path.GetDirectoryName(plan.Save), ".chronicle-repair-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool committed = false;
            try
            {
                WriteNew(temp, plan.Output); StillMatches(plan);
                File.Replace(temp, plan.Save, null, true); committed = true;
                if (!File.ReadAllBytes(plan.Save).SequenceEqual(plan.Output)) throw new IOException("Repaired-save verification failed.");
                Report(plan, "Complete", "Only the listed Chronicle runeword history ID fields changed. The repaired save was byte-verified.");
            }
            catch (Exception error)
            {
                string status = "FailedBeforeSaveEdit", recovery = "No save replacement completed.";
                if (committed)
                {
                    try
                    {
                        if (!File.ReadAllBytes(plan.Save).SequenceEqual(plan.Output)) throw new IOException("Concurrent edit; rollback left the current save untouched.");
                        WriteNew(temp, plan.Original); File.Replace(temp, plan.Save, null, true);
                        if (!File.ReadAllBytes(plan.Save).SequenceEqual(plan.Original)) throw new IOException("Restored-save verification failed.");
                        status = "RolledBack"; recovery = "The original save was restored and verified.";
                    }
                    catch (Exception rollback) { status = "RecoveryRequired"; recovery = "Automatic recovery failed: " + rollback.Message; }
                }
                try { Report(plan, status, error.Message + " " + recovery); } catch { }
                throw new IOException(error.Message + "\n" + recovery + "\nVerified original save: " + Path.Combine(originals, Path.GetFileName(plan.Save)), error);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
            Console.WriteLine("SAVE FILE EDIT COMPLETE: " + plan.Changes.Count + " Chronicle runeword history IDs repaired.\nSave file: " + plan.Save + "\nBackup and repair report: " + plan.Backup);
        }
    }
}
