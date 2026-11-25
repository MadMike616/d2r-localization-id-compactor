using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[assembly: AssemblyTitle("D2R Localization ID Compactor")]
[assembly: AssemblyDescription("Localization ID compaction and optional Chronicle save ID repair with verified backups")]
[assembly: AssemblyCompany("MadMike")]
[assembly: AssemblyProduct("D2R Localization ID Compactor")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MadMike")]
[assembly: AssemblyVersion("1.6.1.0")]
[assembly: AssemblyFileVersion("1.6.1.0")]
[assembly: AssemblyInformationalVersion("1.6.1")]

namespace D2RLocalizationCompactor
{
    internal sealed class JsonValue
    {
        public string Kind, Text;
        public int Start, End;
        public long Integer;
        public bool IsInteger;
        public List<JsonValue> Items;
        public Dictionary<string, JsonValue> Fields;
        public JsonValue Field(string name) { JsonValue value; return Fields != null && Fields.TryGetValue(name, out value) ? value : null; }
    }

    // Parses game JSON with comments and trailing commas, retaining scalar
    // offsets. No serializer is used to rewrite the localization files.
    internal sealed class JsonReader
    {
        private readonly string source, filename;
        private int at;
        public JsonReader(string text, string name) { source = text; filename = name; }
        private Exception Error(string message)
        {
            int line = 1, column = 1;
            for (int i = 0; i < at && i < source.Length; ++i) { if (source[i] == '\n') { ++line; column = 1; } else ++column; }
            return new InvalidDataException(filename + " (" + line + ":" + column + "): " + message);
        }
        private void Space()
        {
            for (;;)
            {
                while (at < source.Length && (source[at] == ' ' || source[at] == '\t' || source[at] == '\r' || source[at] == '\n')) ++at;
                if (at + 1 >= source.Length || source[at] != '/') return;
                if (source[at + 1] == '/') { at += 2; while (at < source.Length && source[at] != '\n' && source[at] != '\r') ++at; }
                else if (source[at + 1] == '*') { int end = source.IndexOf("*/", at + 2, StringComparison.Ordinal); if (end < 0) throw Error("Unterminated comment."); at = end + 2; }
                else return;
            }
        }
        private string String()
        {
            if (at >= source.Length || source[at++] != '"') throw Error("Expected a string.");
            var result = new StringBuilder();
            while (at < source.Length)
            {
                char c = source[at++];
                if (c == '"') return result.ToString();
                if (c < 32) throw Error("Unescaped control character in a string.");
                if (c != '\\') { result.Append(c); continue; }
                if (at >= source.Length) throw Error("Unterminated escape.");
                switch (source[at++])
                {
                    case '"': result.Append('"'); break; case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break; case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break; case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                    case 'u':
                        if (at + 4 > source.Length) throw Error("Incomplete Unicode escape.");
                        ushort code;
                        if (!ushort.TryParse(source.Substring(at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw Error("Invalid Unicode escape.");
                        result.Append((char)code); at += 4; break;
                    default: throw Error("Invalid escape.");
                }
            }
            throw Error("Unterminated string.");
        }
        private JsonValue Value(int depth)
        {
            if (depth > 128) throw Error("JSON nesting is too deep.");
            Space(); if (at >= source.Length) throw Error("Expected a JSON value.");
            var value = new JsonValue { Start = at };
            char c = source[at];
            if (c == '{')
            {
                ++at; value.Kind = "object"; value.Fields = new Dictionary<string, JsonValue>(StringComparer.Ordinal); Space();
                if (at < source.Length && source[at] == '}') ++at;
                else for (;;)
                {
                    Space(); string key = String(); Space();
                    if (at >= source.Length || source[at++] != ':') throw Error("Expected ':'.");
                    if (value.Fields.ContainsKey(key)) throw Error("Duplicate property: " + key);
                    value.Fields.Add(key, Value(depth + 1)); Space();
                    if (at < source.Length && source[at] == '}') { ++at; break; }
                    if (at >= source.Length || source[at++] != ',') throw Error("Expected ',' or '}'.");
                    Space(); if (at < source.Length && source[at] == '}') { ++at; break; }
                }
            }
            else if (c == '[')
            {
                ++at; value.Kind = "array"; value.Items = new List<JsonValue>(); Space();
                if (at < source.Length && source[at] == ']') ++at;
                else for (;;)
                {
                    value.Items.Add(Value(depth + 1)); Space();
                    if (at < source.Length && source[at] == ']') { ++at; break; }
                    if (at >= source.Length || source[at++] != ',') throw Error("Expected ',' or ']'.");
                    Space(); if (at < source.Length && source[at] == ']') { ++at; break; }
                }
            }
            else if (c == '"') { value.Kind = "string"; value.Text = String(); }
            else if (c == '-' || (c >= '0' && c <= '9'))
            {
                value.Kind = "number"; int start = at;
                if (source[at] == '-') ++at;
                if (at >= source.Length) throw Error("Invalid number.");
                if (source[at] == '0') ++at;
                else { if (source[at] < '1' || source[at] > '9') throw Error("Invalid number."); while (at < source.Length && source[at] >= '0' && source[at] <= '9') ++at; }
                bool integer = true;
                if (at < source.Length && source[at] == '.') { integer = false; ++at; int begin = at; while (at < source.Length && source[at] >= '0' && source[at] <= '9') ++at; if (begin == at) throw Error("Invalid fraction."); }
                if (at < source.Length && (source[at] == 'e' || source[at] == 'E')) { integer = false; ++at; if (at < source.Length && (source[at] == '+' || source[at] == '-')) ++at; int begin = at; while (at < source.Length && source[at] >= '0' && source[at] <= '9') ++at; if (begin == at) throw Error("Invalid exponent."); }
                value.Text = source.Substring(start, at - start);
                value.IsInteger = integer && long.TryParse(value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value.Integer);
            }
            else
            {
                string literal = c == 't' ? "true" : c == 'f' ? "false" : c == 'n' ? "null" : "";
                if (literal.Length == 0 || at + literal.Length > source.Length || string.CompareOrdinal(source, at, literal, 0, literal.Length) != 0) throw Error("Invalid JSON value.");
                value.Kind = literal; at += literal.Length;
            }
            value.End = at; return value;
        }
        public JsonValue Read() { JsonValue result = Value(0); Space(); if (at != source.Length) throw Error("Unexpected content after the JSON value."); return result; }
    }

    internal sealed class Record
    {
        public string Key;
        public int Id, Index, Start, End;
        public bool Preserved, Native, ProtectedRune;
        public string Reason;
    }
    internal sealed class Document
    {
        public string Name, Path, Text, SkipReason;
        public byte[] Original, Output, Preamble;
        public Encoding Encoding;
        public List<Record> Records;
        public int? First, Last;
        public int Changed;
        public static Document Read(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("JSON links/reparse points are not supported: " + path);
            var doc = new Document { Path = path, Name = System.IO.Path.GetFileName(path), Original = File.ReadAllBytes(path) };
            byte[] b = doc.Original; int prefix = 0;
            if (b.Length >= 4 && ((b[0] == 255 && b[1] == 254 && b[2] == 0 && b[3] == 0) || (b[0] == 0 && b[1] == 0 && b[2] == 254 && b[3] == 255))) throw new InvalidDataException(doc.Name + ": UTF-32 is unsupported; use UTF-8 or UTF-16 with a BOM.");
            if (b.Length >= 3 && b[0] == 239 && b[1] == 187 && b[2] == 191) { doc.Encoding = new UTF8Encoding(false, true); prefix = 3; }
            else if (b.Length >= 2 && b[0] == 255 && b[1] == 254) { doc.Encoding = new UnicodeEncoding(false, false, true); prefix = 2; }
            else if (b.Length >= 2 && b[0] == 254 && b[1] == 255) { doc.Encoding = new UnicodeEncoding(true, false, true); prefix = 2; }
            else doc.Encoding = new UTF8Encoding(false, true);
            doc.Preamble = b.Take(prefix).ToArray();
            doc.Text = doc.Encoding.GetString(b, prefix, b.Length - prefix);
            doc.Records = Localization(new JsonReader(doc.Text, doc.Name).Read(), doc.Name, out doc.SkipReason);
            return doc;
        }
        public static List<Record> Localization(JsonValue root, string name, out string skip)
        {
            skip = null;
            if (root.Kind != "array") { skip = "Not a localization array"; return null; }
            if (root.Items.Count == 0) { skip = "Empty array"; return null; }
            Func<JsonValue, bool> hasLocale = v => v.Fields != null && v.Fields.Any(p => p.Key.Length == 4 && char.IsLower(p.Key[0]) && char.IsLower(p.Key[1]) && char.IsUpper(p.Key[2]) && char.IsUpper(p.Key[3]) && p.Value.Kind == "string");
            bool resembles = root.Items.Any(v => v.Kind == "object" && v.Field("Key") != null && hasLocale(v));
            if (!resembles) { skip = "Not a localization table"; return null; }
            if (root.Items.All(v => v.Kind == "object" && v.Field("Key") != null && v.Field("id") == null && hasLocale(v))) { skip = "Key-only localization; no numeric IDs"; return null; }
            var records = new List<Record>();
            foreach (JsonValue value in root.Items)
            {
                JsonValue id = value.Field("id"), key = value.Field("Key");
                if (value.Kind != "object" || key == null || key.Kind != "string" || key.Text.Length == 0 || id == null || id.Kind != "number" || !id.IsInteger || id.Integer < 0 || id.Integer > int.MaxValue || !hasLocale(value)) throw new InvalidDataException(name + ": Invalid localization record " + (records.Count + 1) + "; each record needs a nonnegative integer id, a nonempty Key and a translated string.");
                records.Add(new Record { Key = key.Text, Id = (int)id.Integer, Index = records.Count, Start = id.Start, End = id.End });
            }
            return records;
        }
        public byte[] Encode(string text) { byte[] content = Encoding.GetBytes(text); return Preamble.Concat(content).ToArray(); }
    }

    internal sealed class Reference
    {
        public string Description;
        public string SourceFolder;
        public readonly Dictionary<string, Dictionary<int, HashSet<string>>> Files = new Dictionary<string, Dictionary<int, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, HashSet<string>> Global = new Dictionary<int, HashSet<string>>();
        public int Maximum;
        public static Reference Load(string folder)
        {
            var result = new Reference();
            if (folder == null)
            {
                result.Description = "Built-in vanilla D2R 3.4 (ID/key pairs only)";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("vanilla-3.4.json.gz"))
                {
                    if (stream == null) throw new InvalidDataException("The embedded vanilla reference is missing.");
                    using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gzip, new UTF8Encoding(false, true)))
                    {
                        JsonValue root = new JsonReader(reader.ReadToEnd(), "Embedded reference").Read();
                        foreach (var file in root.Fields)
                            foreach (JsonValue pair in file.Value.Items) result.Add(file.Key, (int)pair.Items[0].Integer, pair.Items[1].Text);
                    }
                }
            }
            else
            {
                result.SourceFolder = folder;
                result.Description = "Vanilla extraction: " + folder;
                foreach (string path in Engine.JsonFiles(folder))
                {
                    Document doc = Document.Read(path); if (doc.Records == null) continue;
                    foreach (Record row in doc.Records) result.Add(doc.Name, row.Id, row.Key);
                }
                if (result.Global.Count == 0) throw new InvalidDataException("The reference folder contains no numeric localization tables.");
            }
            result.Maximum = result.Global.Keys.Where(id => id != 251780).DefaultIfEmpty(-1).Max();
            return result;
        }
        private void Add(string filename, int id, string key)
        {
            Dictionary<int, HashSet<string>> file;
            if (!Files.TryGetValue(filename, out file)) Files[filename] = file = new Dictionary<int, HashSet<string>>();
            HashSet<string> keys;
            if (!file.TryGetValue(id, out keys)) file[id] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            keys.Add(key);
            if (!Global.TryGetValue(id, out keys)) Global[id] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            keys.Add(key);
        }
    }

    internal sealed class Change
    {
        public string File, Key;
        public int Index, OldId, NewId;
    }
    internal enum ItemRunesMode { Unspecified, Preserve, Compact }
    internal sealed class IdRange
    {
        public long First, Last;
    }
    internal sealed class Plan
    {
        public string Target;
        public string BackupRoot, LegacyBackup;
        public Reference Reference;
        public int Start, Changed;
        public ItemRunesMode ItemRunesMode;
        public readonly List<IdRange> RuneBuffers = new List<IdRange>();
        public List<Document> Documents;
        public readonly List<Change> Mapping = new List<Change>();
        public readonly Dictionary<int, List<string>> VanillaDuplicates = new Dictionary<int, List<string>>();
        public string Backup;
        public bool EditSaves;
    }

    internal static class Engine
    {
        public const int MaximumId = 65534;
        public const int RuneBuffer = 1000;
        private static readonly string[] FirstFiles = { "item-gems.json", "item-modifiers.json", "item-nameaffixes.json", "item-names.json", "item-runes.json", "skills.json", "ui.json" };
        public static string[] JsonFiles(string folder)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder does not exist: " + folder);
            return Directory.GetFiles(folder).Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(p => Rank(System.IO.Path.GetFileName(p))).ThenBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToArray();
        }
        private static int Rank(string name) { for (int i = 0; i < FirstFiles.Length; ++i) if (string.Equals(name, FirstFiles[i], StringComparison.OrdinalIgnoreCase)) return i; return FirstFiles.Length; }
        private static bool Within(string path, string root)
        {
            string normalized = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
            string boundary = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
            return string.Equals(normalized, boundary, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(boundary + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        private static DirectoryInfo DataBoundary(string target)
        {
            var ancestors = new List<DirectoryInfo>();
            for (DirectoryInfo current = new DirectoryInfo(target); current != null; current = current.Parent) ancestors.Add(current);
            return ancestors.LastOrDefault(d => d.Name.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase))
                ?? ancestors.LastOrDefault(d => d.Name.Equals("data", StringComparison.OrdinalIgnoreCase))
                ?? ancestors.LastOrDefault(d => d.Name.Equals("strings", StringComparison.OrdinalIgnoreCase) || d.Name.Equals("strings-legacy", StringComparison.OrdinalIgnoreCase))
                ?? ancestors[0];
        }
        public static string DefaultBackupRoot(string target)
        {
            DirectoryInfo boundary = DataBoundary(target);
            if (boundary.Parent == null) throw new InvalidDataException("Choose a backup folder outside the target using --backup-folder.");
            return System.IO.Path.Combine(boundary.Parent.FullName, "Localization-ID-Backups");
        }
        public static void ValidateBackupRoot(string target, string backup)
        {
            if (Within(backup, DataBoundary(target).FullName)) throw new InvalidDataException("The backup folder must be outside the localization/game-data tree. Choose a folder outside strings, data and any containing .mpq folder.");
            for (DirectoryInfo current = new DirectoryInfo(backup); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Backup paths must not use links/reparse points: " + current.FullName);
        }
        public static string FindLegacyBackup(string target)
        {
            string root = System.IO.Path.Combine(target, "Localization-ID-Backups");
            if (!Directory.Exists(root)) return null;
            var directories = new Stack<string>(); directories.Push(root);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Legacy backup links cannot be migrated automatically: " + directory);
                foreach (string file in Directory.GetFiles(directory))
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Legacy backup links cannot be migrated automatically: " + file);
                foreach (string child in Directory.GetDirectories(directory)) directories.Push(child);
            }
            if (Directory.GetFiles(root).Length != 0) throw new InvalidDataException("Unrecognized files in the old backup folder; move it outside game data manually: " + root);
            foreach (string run in Directory.GetDirectories(root))
            {
                string manifest = System.IO.Path.Combine(run, "manifest.json");
                if (!File.Exists(manifest)) throw new InvalidDataException("Incomplete old backup folder; move it outside game data manually: " + root);
                JsonValue tool = new JsonReader(File.ReadAllText(manifest, Encoding.UTF8), manifest).Read().Field("tool");
                if (tool == null || tool.Kind != "string" || tool.Text != "D2R Localization ID Compactor") throw new InvalidDataException("The old backup folder is not owned by this utility; move it outside game data manually: " + root);
            }
            return root;
        }
        public static string Hash(byte[] value) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
        private static bool IsItemRunes(Document doc) { return string.Equals(doc.Name, "item-runes.json", StringComparison.OrdinalIgnoreCase); }
        private static void ReserveRunes(Plan plan)
        {
            if (plan.ItemRunesMode != ItemRunesMode.Preserve) return;
            foreach (Record row in plan.Documents.Where(IsItemRunes).Where(d => d.Records != null).SelectMany(d => d.Records).OrderBy(r => r.Id))
            {
                long first = Math.Max(0L, (long)row.Id - RuneBuffer), last = Math.Min((long)MaximumId, (long)row.Id + RuneBuffer);
                if (first > last) continue;
                IdRange previous = plan.RuneBuffers.LastOrDefault();
                if (previous != null && first <= previous.Last + 1) previous.Last = Math.Max(previous.Last, last);
                else plan.RuneBuffers.Add(new IdRange { First = first, Last = last });
            }
        }
        private static long SafeRangeStart(Plan plan, long cursor, int count)
        {
            if (count == 0) return cursor;
            foreach (IdRange buffer in plan.RuneBuffers)
            {
                if (cursor + count - 1 < buffer.First) break;
                if (cursor <= buffer.Last) cursor = ((buffer.Last + 1 + 999) / 1000) * 1000;
            }
            if (cursor + count - 1 > MaximumId) throw new InvalidDataException("The planned ranges exceed " + MaximumId + ", including any item-runes safety buffers. Reduce the starting ID or the number of entries. No files have been changed.");
            return cursor;
        }
        public static Plan Build(string target, Reference reference, int start, string backupFolder, ItemRunesMode itemRunesMode)
        {
            if (start <= reference.Maximum || start < 0 || start > MaximumId) throw new InvalidDataException("Starting ID must be above the vanilla maximum " + reference.Maximum + " and at most " + MaximumId + ".");
            string[] files = JsonFiles(target);
            if (files.Length == 0) throw new InvalidDataException("No .json files were found in the target folder.");
            string backupRoot = System.IO.Path.GetFullPath(backupFolder ?? DefaultBackupRoot(target));
            ValidateBackupRoot(target, backupRoot);
            if (reference.SourceFolder != null && Within(backupRoot, DataBoundary(reference.SourceFolder).FullName)) throw new InvalidDataException("The backup folder must be outside the vanilla reference tree.");
            var plan = new Plan { Target = target, Reference = reference, Start = start, BackupRoot = backupRoot,
                LegacyBackup = FindLegacyBackup(target), Documents = files.Select(Document.Read).ToList(), ItemRunesMode = itemRunesMode };
            if (plan.ItemRunesMode == ItemRunesMode.Unspecified && plan.Documents.Any(d => IsItemRunes(d) && d.Records != null))
                throw new InvalidDataException("Choose --item-runes-mode preserve for existing characters/shared stashes, or --item-runes-mode compact for a clean mod with no saves. No mode is selected automatically.");
            ReserveRunes(plan);
            var finalIds = new Dictionary<int, List<string>>();
            long cursor = start;
            foreach (Document doc in plan.Documents)
            {
                if (doc.Records == null) { doc.Output = doc.Original; continue; }
                Dictionary<int, HashSet<string>> nativeFile;
                reference.Files.TryGetValue(doc.Name, out nativeFile);
                foreach (var group in doc.Records.GroupBy(r => r.Id))
                {
                    HashSet<string> stockKeys;
                    if (nativeFile != null && nativeFile.TryGetValue(group.Key, out stockKeys))
                    {
                        List<Record> rows = group.ToList();
                        List<Record> exact = rows.Where(r => stockKeys.Contains(r.Key)).ToList();
                        if (rows.Count == 1) { rows[0].Preserved = true; rows[0].Reason = stockKeys.Contains(rows[0].Key) ? "Vanilla ID/key" : "Edited or renamed vanilla slot"; }
                        else if (exact.Count == 1) { exact[0].Preserved = true; exact[0].Reason = "Vanilla ID/key; conflicting custom record will move"; }
                        else throw new InvalidDataException(doc.Name + ": Ambiguous duplicate vanilla slot " + group.Key + ". Resolve these duplicate records before running the compactor.");
                    }
                    else foreach (Record row in group)
                    {
                        HashSet<string> globalKeys;
                        if (reference.Global.TryGetValue(row.Id, out globalKeys) && globalKeys.Contains(row.Key)) { row.Preserved = true; row.Reason = "Vanilla ID/key override in another file"; }
                    }
                }
                foreach (Record row in doc.Records)
                {
                    row.Native = row.Preserved;
                    if (plan.ItemRunesMode == ItemRunesMode.Preserve && IsItemRunes(doc))
                    {
                        row.ProtectedRune = true; row.Preserved = true;
                        row.Reason = row.Native ? row.Reason + "; item-runes ID preserved" : "Item-runes ID preserved for saved-item compatibility";
                    }
                }
                cursor = SafeRangeStart(plan, cursor, doc.Records.Count(r => !r.Preserved));
                foreach (Record row in doc.Records)
                {
                    int id = row.Id;
                    if (!row.Preserved)
                    {
                        if (cursor > MaximumId) throw new InvalidDataException("The planned ranges exceed " + MaximumId + ". Reduce the starting ID or the number of entries. No files have been changed.");
                        id = (int)cursor++;
                        if (reference.Global.ContainsKey(id)) throw new InvalidDataException("The planned custom range collides with reserved vanilla ID " + id + ".");
                        if (!doc.First.HasValue) doc.First = id;
                        doc.Last = id;
                        plan.Mapping.Add(new Change { File = doc.Name, Key = row.Key, Index = row.Index, OldId = row.Id, NewId = id });
                        if (id != row.Id) { ++doc.Changed; ++plan.Changed; }
                    }
                    List<string> uses;
                    if (!finalIds.TryGetValue(id, out uses)) finalIds[id] = uses = new List<string>();
                    uses.Add(doc.Name + " / " + row.Key);
                }
                if (doc.Last.HasValue) cursor = (((long)doc.Last.Value + 300 + 999) / 1000) * 1000;
                List<Change> edits = plan.Mapping.Where(r => r.File == doc.Name).ToList();
                var text = new StringBuilder(doc.Text.Length); int previous = 0;
                foreach (Change change in edits)
                {
                    Record row = doc.Records[change.Index];
                    text.Append(doc.Text, previous, row.Start - previous);
                    text.Append(change.NewId.ToString(CultureInfo.InvariantCulture)); previous = row.End;
                }
                text.Append(doc.Text, previous, doc.Text.Length - previous);
                string output = text.ToString(); doc.Output = doc.Encode(output);
                string skip;
                List<Record> checkedRows = Document.Localization(new JsonReader(output, doc.Name).Read(), doc.Name, out skip);
                if (checkedRows == null || checkedRows.Count != doc.Records.Count) throw new InvalidDataException("Output validation failed: " + doc.Name);
                var newIds = edits.ToDictionary(r => r.Index, r => r.NewId);
                for (int i = 0; i < checkedRows.Count; ++i)
                {
                    Record old = doc.Records[i], current = checkedRows[i]; int wanted;
                    if (!newIds.TryGetValue(i, out wanted)) wanted = old.Id;
                    if (old.Key != current.Key || current.Id != wanted) throw new InvalidDataException("Record validation failed: " + doc.Name);
                }
                // Offsets come directly from the parsed numeric value. Text
                // before/between/after those digit spans is copied verbatim.
                if (doc.Changed == 0 && !doc.Original.SequenceEqual(doc.Output)) throw new InvalidDataException("Unchanged-file byte validation failed: " + doc.Name);
            }
            if (!plan.Documents.Any(d => d.Records != null)) throw new InvalidDataException("No numeric localization tables were found in the target folder.");
            foreach (var pair in finalIds.Where(p => p.Value.Count > 1))
            {
                if (!reference.Global.ContainsKey(pair.Key)) throw new InvalidDataException("Custom ID collision: " + pair.Key);
                plan.VanillaDuplicates[pair.Key] = pair.Value;
            }
            return plan;
        }
        public static void Preview(Plan plan)
        {
            Console.WriteLine("\nTarget: " + plan.Target);
            Console.WriteLine("Reference: " + plan.Reference.Description);
            Console.WriteLine("Backup folder (outside game data): " + plan.BackupRoot);
            if (plan.LegacyBackup != null) Console.WriteLine("Old backups inside game data will be relocated: " + plan.LegacyBackup);
            Console.WriteLine("Vanilla maximum: " + plan.Reference.Maximum + " (251780 excluded from the maximum; its ID remains reserved)");
            if (plan.Documents.Any(d => IsItemRunes(d) && d.Records != null))
            {
                Console.WriteLine("item-runes mode: " + plan.ItemRunesMode.ToString().ToUpperInvariant());
                if (plan.ItemRunesMode == ItemRunesMode.Preserve)
                {
                    Console.WriteLine("item-runes.json stays unchanged. Other custom ranges avoid every item-runes ID and 1000 numbers on either side.");
                    foreach (IdRange buffer in plan.RuneBuffers) Console.WriteLine("  Reserved ID interval: " + buffer.First + "-" + buffer.Last);
                }
                else Console.WriteLine(plan.EditSaves ? "Runeword IDs will be migrated together with all supported saves in this mod's save profile." : "COMPACT is for a clean mod with NO existing character or shared-stash saves. Runeword name IDs stored in saved items can become invalid.");
            }
            Console.WriteLine("\nFile                            Custom range          IDs changing");
            foreach (Document doc in plan.Documents)
                Console.WriteLine("{0,-31} {1,-21} {2}", doc.Name, doc.SkipReason != null ? "Skipped" : doc.First.HasValue ? doc.First + "-" + doc.Last : "Preserved only", doc.Changed);
            foreach (Document doc in plan.Documents.Where(d => d.SkipReason != null)) Console.WriteLine("  Skipped " + doc.Name + ": " + doc.SkipReason);
            Console.WriteLine("\n" + plan.Mapping.Count + " custom records; " + plan.Changed + " IDs will change.");
            Console.WriteLine("Vanilla duplicate/override ID groups preserved: " + plan.VanillaDuplicates.Count);
            foreach (var pair in plan.VanillaDuplicates) Console.WriteLine("  " + pair.Key + ": " + string.Join("; ", pair.Value));
            Console.WriteLine("Each following custom range starts at a multiple of 1000 at least 300 above the preceding last ID.");
            if (plan.RuneBuffers.Count > 0) Console.WriteLine("A range intersecting an item-runes buffer moves as a whole to the next safe multiple of 1000, including the first range if needed.");
            Console.WriteLine("Only JSON ID numbers change. Fixed numeric references in plugins or other game files are not updated.");
        }
        private static string Csv(string value)
        {
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        public static byte[] MappingBytes(Plan plan)
        {
            var text = new StringBuilder("File,EntryIndex,Key,OldID,NewID\r\n");
            foreach (Change change in plan.Mapping) text.Append(Csv(change.File)).Append(',').Append(change.Index).Append(',').Append(Csv(change.Key)).Append(',').Append(change.OldId).Append(',').Append(change.NewId).Append("\r\n");
            return new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text.ToString())).ToArray();
        }
        private static void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public static void SnapshotStillMatches(Plan plan)
        {
            string[] files = JsonFiles(plan.Target);
            if (!files.SequenceEqual(plan.Documents.Select(d => d.Path), StringComparer.OrdinalIgnoreCase)) throw new IOException("The target JSON file list changed after preview. Run the tool again.");
            foreach (Document doc in plan.Documents) if (!File.ReadAllBytes(doc.Path).SequenceEqual(doc.Original)) throw new IOException(doc.Name + " changed after preview. Run the tool again.");
        }
        private static object Manifest(Plan plan, string status, string message)
        {
            return new { tool = "D2R Localization ID Compactor", version = "1.6.1", utc = DateTime.UtcNow.ToString("o"),
                status = status, message = message, target = plan.Target, reference = plan.Reference.Description,
                startingId = plan.Start, minimumBoundaryDistance = 300, changedIds = plan.Changed, backupRoot = plan.BackupRoot,
                itemRunesMode = plan.ItemRunesMode.ToString().ToLowerInvariant(), itemRunesBuffer = plan.ItemRunesMode == ItemRunesMode.Preserve ? RuneBuffer : 0,
                reservedItemRunesIntervals = plan.RuneBuffers.Select(r => new { first = r.First, last = r.Last }).ToArray(),
                files = plan.Documents.Select(d => new { name = d.Name, originalSha256 = Hash(d.Original), outputSha256 = Hash(d.Output),
                    records = d.Records == null ? 0 : d.Records.Count, changingIds = d.Changed, firstCustomId = d.First, lastCustomId = d.Last, skipped = d.SkipReason }).ToArray(),
                preservedVanillaDuplicateGroups = plan.VanillaDuplicates.ToDictionary(p => p.Key.ToString(CultureInfo.InvariantCulture), p => p.Value) };
        }
        public static string Json(object value) { return JsonSerializer.Serialize(value, new JsonSerializerOptions { MaxDepth = 256 }); }
        private static void SaveManifest(Plan plan, string status, string message)
        {
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "manifest.json"), Json(Manifest(plan, status, message)), new UTF8Encoding(false));
        }
        public static void Apply(Plan plan)
        {
            SnapshotStillMatches(plan);
            ValidateBackupRoot(plan.Target, plan.BackupRoot);
            string parent = plan.BackupRoot;
            if (plan.LegacyBackup != null)
            {
                if (FindLegacyBackup(plan.Target) != plan.LegacyBackup) throw new IOException("The legacy backup location changed after preview.");
                Directory.CreateDirectory(parent);
                string migrated = System.IO.Path.Combine(parent, "Migrated-legacy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try { Directory.Move(plan.LegacyBackup, migrated); }
                catch (IOException error) { throw new IOException("Could not relocate old backups. Choose a backup folder on the same drive, or move " + plan.LegacyBackup + " outside game data manually. No active localization files have been changed.", error); }
                Console.WriteLine("Moved old backups out of game data to: " + migrated);
                File.WriteAllText(System.IO.Path.Combine(migrated, "migration.txt"), "Original location: " + plan.LegacyBackup + Environment.NewLine + "Safe location: " + migrated + Environment.NewLine, new UTF8Encoding(false));
            }
            if (plan.Changed == 0) { Console.WriteLine("No ID changes are needed. No new originals backup was necessary."); return; }
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("The backup folder must not be a link/reparse point.");
            Directory.CreateDirectory(parent);
            plan.Backup = System.IO.Path.Combine(parent, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string originals = System.IO.Path.Combine(plan.Backup, "originals"); Directory.CreateDirectory(originals);
            // Every scanned JSON, including skipped files, is saved and hash
            // checked before even the first active file can be replaced.
            foreach (Document doc in plan.Documents)
            {
                string path = System.IO.Path.Combine(originals, doc.Name); WriteNew(path, doc.Original);
                if (Hash(File.ReadAllBytes(path)) != Hash(doc.Original)) throw new IOException("Backup verification failed: " + doc.Name);
            }
            var mapping = new StringBuilder("File,EntryIndex,Key,OldID,NewID\r\n");
            foreach (Change change in plan.Mapping) mapping.Append(Csv(change.File)).Append(',').Append(change.Index).Append(',').Append(Csv(change.Key)).Append(',').Append(change.OldId).Append(',').Append(change.NewId).Append("\r\n");
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "old-to-new-ids.csv"), mapping.ToString(), new UTF8Encoding(true));
            var preserved = new StringBuilder("File,EntryIndex,Key,ID,Reason\r\n");
            foreach (Document doc in plan.Documents.Where(d => d.Records != null)) foreach (Record row in doc.Records.Where(r => r.Native)) preserved.Append(Csv(doc.Name)).Append(',').Append(row.Index).Append(',').Append(Csv(row.Key)).Append(',').Append(row.Id).Append(',').Append(Csv(row.Reason)).Append("\r\n");
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "preserved-vanilla.csv"), preserved.ToString(), new UTF8Encoding(true));
            var runes = new StringBuilder("File,EntryIndex,Key,ID,Reason\r\n");
            foreach (Document doc in plan.Documents.Where(d => d.Records != null)) foreach (Record row in doc.Records.Where(r => r.ProtectedRune)) runes.Append(Csv(doc.Name)).Append(',').Append(row.Index).Append(',').Append(Csv(row.Key)).Append(',').Append(row.Id).Append(',').Append(Csv(row.Reason)).Append("\r\n");
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "preserved-item-runes.csv"), runes.ToString(), new UTF8Encoding(true));
            SaveManifest(plan, "Prepared", "All original JSON files backed up and verified. No active files changed yet.");
            Console.WriteLine("\nVerified backups: " + plan.Backup);
            var temps = new Dictionary<Document, string>(); var committed = new List<Document>();
            string operation = "staging outputs";
            try
            {
                foreach (Document doc in plan.Documents.Where(d => d.Changed > 0))
                {
                    operation = "staging " + doc.Name;
                    string temp = System.IO.Path.Combine(plan.Target, "." + doc.Name + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    temps.Add(doc, temp); WriteNew(temp, doc.Output);
                    if (Hash(File.ReadAllBytes(temp)) != Hash(doc.Output)) throw new IOException("Staging verification failed: " + doc.Name);
                }
                operation = "checking for concurrent edits"; SnapshotStillMatches(plan);
                foreach (Document doc in plan.Documents.Where(d => d.Changed > 0))
                {
                    operation = "replacing " + doc.Name;
                    if (!File.ReadAllBytes(doc.Path).SequenceEqual(doc.Original)) throw new IOException("Concurrent edit: " + doc.Name);
                    // Ignore failure to merge optional filesystem metadata;
                    // replacement of the actual file data remains atomic.
                    File.Replace(temps[doc], doc.Path, null, true); committed.Add(doc);
                    if (!File.ReadAllBytes(doc.Path).SequenceEqual(doc.Output)) throw new IOException("Installed-file verification failed: " + doc.Name);
                }
                operation = "recording completion";
                SaveManifest(plan, "Complete", "Only planned ID digit spans were changed. Backups and all written file hashes verified.");
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (Document doc in committed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!File.ReadAllBytes(doc.Path).SequenceEqual(doc.Output)) throw new IOException("Concurrent edit; automatic rollback left this file untouched.");
                        string restore = System.IO.Path.Combine(plan.Target, "." + doc.Name + "." + Guid.NewGuid().ToString("N") + ".restore.tmp");
                        temps[doc] = restore; WriteNew(restore, doc.Original); File.Replace(restore, doc.Path, null, true);
                        if (!File.ReadAllBytes(doc.Path).SequenceEqual(doc.Original)) throw new IOException("Rollback verification failed.");
                    }
                    catch (Exception rollback) { failures.Add(doc.Name + ": " + rollback.Message); }
                }
                string status = failures.Count == 0 ? "RolledBack" : "RollbackIncomplete";
                string detail = "Failed while " + operation + ": " + error.Message;
                try { SaveManifest(plan, status, detail + (failures.Count == 0 ? "" : " | " + string.Join(" | ", failures))); } catch { }
                throw new IOException(detail + "\n" + (failures.Count == 0 ? "Any committed changes were rolled back." : "Some changes could not be rolled back: " + string.Join(" | ", failures)) + "\nOriginal files: " + originals, error);
            }
            finally { foreach (string temp in temps.Values) { try { if (File.Exists(temp)) File.Delete(temp); } catch { } } }
            Console.WriteLine("Completed: " + plan.Changed + " IDs changed. Original files, mapping and verification manifest are in the backup folder.");
        }
    }

    internal static class Program
    {
        private static string Folder(string input) { return System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'))); }
        private static string Ask(string label, string value) { Console.Write(label + " [" + value + "]: "); string answer = Console.ReadLine(); return string.IsNullOrWhiteSpace(answer) ? value : answer.Trim(); }
        private static bool Confirm(string action)
        {
            for (;;)
            {
                Console.Write("\n" + action + " Continue? [Y/N] (no default): ");
                string answer = Console.ReadLine();
                if (answer == null) return false;
                answer = answer.Trim();
                if (answer.Equals("Y", StringComparison.OrdinalIgnoreCase) || answer.Equals("YES", StringComparison.OrdinalIgnoreCase)) return true;
                if (answer.Equals("N", StringComparison.OrdinalIgnoreCase) || answer.Equals("NO", StringComparison.OrdinalIgnoreCase)) return false;
                Console.WriteLine("Enter Y/YES or N/NO. There is no default.");
            }
        }
        private static ItemRunesMode ParseItemRunesMode(string value)
        {
            if (string.Equals(value.Trim(), "preserve", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1") return ItemRunesMode.Preserve;
            if (string.Equals(value.Trim(), "compact", StringComparison.OrdinalIgnoreCase) || value.Trim() == "2") return ItemRunesMode.Compact;
            throw new ArgumentException("Item-runes mode must be PRESERVE (existing characters/shared stashes) or COMPACT (clean mod with no saves).");
        }
        private static ItemRunesMode AskItemRunesMode()
        {
            Console.WriteLine("\nChoose how to handle item-runes.json (no default):");
            Console.WriteLine("  1 / PRESERVE: Existing characters or shared stashes. Keep item-runes IDs and reserve 1000 numbers on both sides.");
            Console.WriteLine("  2 / COMPACT:  Clean mod with no saves. Renumber custom item-runes IDs too.");
            for (;;)
            {
                Console.Write("Enter PRESERVE or COMPACT: "); string answer = Console.ReadLine();
                if (answer == null) throw new ArgumentException("No item-runes mode selected. No files were changed.");
                try { return ParseItemRunesMode(answer); }
                catch (ArgumentException error) { Console.WriteLine(error.Message); }
            }
        }
        private static void Help()
        {
            Console.WriteLine("D2R Localization ID Compactor 1.6.1 - MadMike\n");
            Workflow.Menu();
            Console.WriteLine("\n--mode standard|skip-runewords|compact-and-edit-saves|save-only (or 1, 2, 3, 4) selects the same workflow for automation.\nMode 3 generates and uses its mapping automatically, validates every save first,\nand backs up JSON plus saves together. A failure triggers recovery of both.\nThe --repair-items/--repair-chronicle commands below remain advanced recovery\ncommands for files compacted earlier; they are separate from the four main menu options.");
            Console.WriteLine("Double-click to select a workflow and preview it. Final confirmation: Y/YES\nto continue, N/NO to cancel (any letter case). No default; blank/invalid asks again.\nClose the game/loader before applying.");
            Console.WriteLine("The default target is the folder containing this executable.");
            Console.WriteLine("\nOptions:\n  --target FOLDER    Target folder (direct .json children only)\n  --vanilla FOLDER   Use a fresh vanilla strings extraction instead of built-in 3.4\n  --backup-folder FOLDER  Backup root outside localization/game data\n  --item-runes-mode preserve|compact  Legacy JSON-only CLI; never with --mode\n  --start NUMBER     First custom ID (default 30000, or above a newer reference)\n  --dry-run          Preview only; write nothing\n  --apply --yes      Apply without an interactive prompt; backups remain mandatory\n  --help            Show this help\n\nPRESERVE keeps item-runes.json unchanged with a 1000-ID buffer on both sides of every ID.\nCOMPACT renumbers its custom IDs too; use only for a clean mod with no character/shared-stash saves.\nOption names, mode values and Y/YES/N/NO confirmations ignore letter case.\nThe seven established filenames come first; additional filenames follow alphabetically.\nCustom IDs stop at 65534 for native 16-bit field compatibility.\nMalformed JSON, ambiguous vanilla duplicates, range overflow and concurrent edits abort.\nOnly the known vanilla outlier 251780 is excluded from the maximum calculation; it stays reserved.");
            Console.WriteLine("\nSeparate optional SAVE FILE EDIT action:\n  --repair-chronicle  Repair Chronicle runeword history in ONE .d2i shared-stash SAVE\n  --save-file FILE    The .d2i SAVE FILE to edit in place\n  --mapping FILE      old-to-new-ids.csv from the relevant item-runes compaction\nUse --dry-run to preview, or --apply --yes to EDIT THE SAVE with mandatory backups.\nDo not combine repair with --target, --vanilla, --start or --item-runes-mode.\nClose the game and save editors first. Only modern format 2 / save version 105,\nChronicle version 1 with 64 trailing bytes is supported. Ambiguous maps abort.\nThis repairs discovery history only, not stored items or character saves.\nFinal save-edit confirmation accepts Y/YES or N/NO (any letter case). See README before use.");
            Console.WriteLine("\nOptional ITEM SAVE FILE EDIT action:\n  --repair-items      Repair runeword items in .d2s characters / .d2i stashes\n  --save-file FILE    Select a save; repeat for multiple explicit files\n  --save-folder FOLDER  Instead, select ALL direct .d2s/.d2i files (no recursion)\n  --mod-tables FOLDER  This mod's complete effective data/global/excel tables\n  --mapping FILE     Matching old-to-new-ids.csv\n  --include-chronicle  Also repair Chronicle history in selected stashes\nUse --dry-run first, or --apply --yes to EDIT SAVES with mandatory backups.\nOnly save version 105 / modern stash format 2 is supported. ALL selected saves\nare validated and backed up before the first replacement. Only ID fields and\nnecessary character checksums change. Final confirmation: Y/YES or N/NO, with no default.\nUnknown IDs stay unchanged; invalid/ambiguous input aborts the whole batch.");
            Console.WriteLine("\nMode 4 / EDIT SAVE FILES ONLY:\n  Reads current item-runes.json and leaves active JSON unchanged.\n  Automatically reads this mod's Localization-ID-Backups mappings and rune snapshots\n  to identify old saved numeric IDs by exact string key. No CSV selection needed.\n  --history-folder FOLDER  Override the mod-local historical backup folder.\n  Unknown/reused/conflicting IDs stop the whole batch rather than being guessed.\n  Migrates physical runewords and Chronicle history; keeps drop-source IDs unchanged.\n  Final confirmation: Y/YES or N/NO; verifies all selected save/context backups first.");
            Console.WriteLine("\nWindows x64 portable EXE with bundled runtime. --licenses displays embedded software notices.");
            Console.WriteLine("\nAutomatic mod/save paths:\n  --mod-folder FOLDER  Select the mod if the EXE is stored elsewhere\n  --detect-paths       Show detected mod, save folder and mod-table folder; read only\n  --auto-saves         Use modinfo.json's detected save folder for a repair\n                      ITEMS: all direct .d2s/.d2i saves. Chronicle-only: one .d2i\n                      must exist, otherwise select --save-file explicitly.\nModes 3 and 4 select ALL direct saves in the detected profile.\nMode 3 generates its own mapping. Mode 4 resolves historical aliases by string key.\nSave/table folders can be overridden manually.\nWindows supplies the Saved Games location; no username/mod name is hardcoded.\nAdvanced repair commands require a CSV matching the save's ID history. Backups and explicit\nsave-edit confirmations remain mandatory. Changed modinfo.json aborts the plan.");
        }
        private static bool SamePath(string first, string second)
        {
            return first != null && second != null && string.Equals(Folder(first).TrimEnd('\\', '/'), Folder(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        private static void RunWorkflow(WorkflowMode mode, string target, bool explicitTarget, string vanilla, int? start, string backup,
            string saveFolder, string tables, string historyFolder, ModPaths paths, bool interactive, bool dry, bool yes)
        {
            string suggested = paths.ContentFolder == null ? target : System.IO.Path.Combine(paths.ContentFolder, "data", "local", "lng", "strings");
            if (!explicitTarget && Directory.Exists(suggested)) target = suggested;
            if (interactive && (!Directory.Exists(target) || !Directory.GetFiles(target).Any(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))) target = Ask("Target strings folder", "");
            target = Folder(target);
            ModPaths targetPaths = ModPaths.Discover(target);
            if (paths.ModInfo == null && targetPaths.ModInfo != null) paths = targetPaths;
            if ((mode == WorkflowMode.CompactAndEditSaves || mode == WorkflowMode.EditSaveFilesOnly) && targetPaths.ModInfo != null && paths.ModInfo != null && !SamePath(targetPaths.ModInfo.Path, paths.ModInfo.Path))
                throw new ArgumentException("The target JSON and detected save profile belong to different mods. Select matching --target and --mod-folder paths.");
            if (vanilla != null) vanilla = Folder(vanilla);
            if (SamePath(target, vanilla)) throw new ArgumentException("Target and vanilla reference must be different folders.");
            Reference reference = Reference.Load(vanilla);
            if (mode == WorkflowMode.EditSaveFilesOnly)
            {
                if (start.HasValue) throw new ArgumentException("Mode 4 does not assign JSON IDs; do not supply --start.");
                paths.Print();
                if (interactive && historyFolder == null)
                {
                    string detectedHistory = Engine.DefaultBackupRoot(target);
                    string selectedHistory = Ask("Historical backups folder (old mappings/rune snapshots)", detectedHistory);
                    if (!SamePath(selectedHistory, detectedHistory)) historyFolder = selectedHistory;
                }
                if (saveFolder == null) saveFolder = paths.SaveFolder;
                if (saveFolder == null && interactive) saveFolder = Ask("Mod save folder (automatic detection unavailable)", "");
                if (string.IsNullOrWhiteSpace(saveFolder)) throw new ArgumentException(paths.SaveError ?? paths.ModError ?? "Select the mod's save folder. No files were changed.");
                if (tables == null) tables = paths.TablesFolder;
                if (tables == null && Directory.Exists(Folder(saveFolder)) && ModPaths.Saves(Folder(saveFolder), false).Length > 0 && interactive)
                    tables = Ask("Complete effective mod-table folder (five TXT files)", "");
                SaveOnlyPlan saveOnly = SaveOnly.Build(target, Folder(saveFolder), string.IsNullOrWhiteSpace(tables) ? null : Folder(tables), backup == null ? null : Folder(backup), historyFolder == null ? null : Folder(historyFolder), paths.ModInfo, reference);
                SaveOnly.Preview(saveOnly);
                if (dry) { Console.WriteLine("\nPreview only. No files were written."); return; }
                if (!yes)
                {
                    if (Console.IsInputRedirected) throw new ArgumentException("Use --dry-run to preview, or --apply --yes to edit saves with mandatory backups.");
                    if (!Confirm("Back up all selected saves and EDIT SAVE FILES ONLY.")) { Console.WriteLine("Cancelled. No files were written."); return; }
                }
                Workflow.RequireGameClosed(); SaveOnly.Apply(saveOnly); return;
            }
            long suggestedStart = Math.Max(30000L, (((long)reference.Maximum + 1 + 999) / 1000) * 1000);
            if (!start.HasValue) { if (suggestedStart > Engine.MaximumId) throw new InvalidDataException("No supported custom ID range remains."); start = (int)suggestedStart; }
            Plan localization = Engine.Build(target, reference, start.Value, backup == null ? null : Folder(backup), mode == WorkflowMode.SkipRunewords ? ItemRunesMode.Preserve : ItemRunesMode.Compact);
            WorkflowPlan combined = null;
            if (mode == WorkflowMode.CompactAndEditSaves)
            {
                paths.Print();
                if (saveFolder == null) saveFolder = paths.SaveFolder;
                if (saveFolder == null && interactive) saveFolder = Ask("Mod save folder (automatic detection unavailable)", "");
                if (string.IsNullOrWhiteSpace(saveFolder)) throw new ArgumentException(paths.SaveError ?? paths.ModError ?? "Select the mod's save folder. No JSON or saves were changed.");
                if (tables == null) tables = paths.TablesFolder;
                if (tables == null && Directory.Exists(Folder(saveFolder)) && ModPaths.Saves(Folder(saveFolder), false).Length > 0 && interactive)
                    tables = Ask("Complete effective mod-table folder (five TXT files)", "");
                combined = Workflow.Build(localization, Folder(saveFolder), string.IsNullOrWhiteSpace(tables) ? null : Folder(tables), paths.ModInfo);
                Workflow.Preview(combined);
            }
            else
            {
                Console.WriteLine("\nSelected: " + (mode == WorkflowMode.Standard ? "STANDARD COMP" : "SKIP RUNEWORDS") + ". No save files will be edited.");
                Engine.Preview(localization);
            }
            if (dry) { Console.WriteLine("\nPreview only. No files were written."); return; }
            if (!yes)
            {
                if (Console.IsInputRedirected) throw new ArgumentException("Use --dry-run to preview, or --apply --yes to apply this workflow with mandatory backups.");
                if (!Confirm(mode == WorkflowMode.CompactAndEditSaves ? "Back up all inputs, compact JSON and EDIT SAVES." : "Back up scanned JSON and apply this compaction plan.")) { Console.WriteLine("Cancelled. No files were written."); return; }
            }
            Workflow.RequireGameClosed();
            if (combined != null) Workflow.Apply(combined); else Engine.Apply(localization);
        }
        private static string PickSave(ModPaths paths, bool stashOnly, out bool detected)
        {
            detected = false;
            if (paths.SaveFolderExists)
            {
                string[] files = ModPaths.Saves(paths.SaveFolder, stashOnly);
                if (files.Length > 0)
                {
                    Console.WriteLine("\nAvailable " + (stashOnly ? "shared-stash saves" : "character/shared-stash saves") + " in " + paths.SaveFolder + ":");
                    for (int i = 0; i < files.Length; ++i) Console.WriteLine("  {0}: {1}", i + 1, System.IO.Path.GetFileName(files[i]));
                    Console.WriteLine("  M / MANUAL: Enter a different save-file path");
                    for (;;)
                    {
                        Console.Write(files.Length == 1 ? "Choose save [1]: " : "Choose save number or MANUAL: ");
                        string raw = Console.ReadLine(); if (raw == null) throw new ArgumentException("No save selected. No files were changed.");
                        string answer = raw.Trim(); if (answer.Length == 0 && files.Length == 1) answer = "1";
                        if (answer.Equals("M", StringComparison.OrdinalIgnoreCase) || answer.Equals("MANUAL", StringComparison.OrdinalIgnoreCase)) break;
                        int number;
                        if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 1 && number <= files.Length)
                        { paths.ModInfo.Verify(); detected = true; return files[number - 1]; }
                        Console.WriteLine("Choose a listed save number, or MANUAL.");
                    }
                }
                else Console.WriteLine("No matching saves were found in the detected folder. Enter a different file manually.");
            }
            return Ask(stashOnly ? "Shared-stash SAVE FILE (.d2i)" : "SAVE FILE (.d2s or .d2i)", "");
        }
        private static void Repair(string save, string mapping, string backup, bool interactive, bool dry, bool yes, ModPaths paths, bool autoSaves)
        {
            Console.WriteLine("\nWARNING: CHRONICLE REPAIR EDITS A .d2i SHARED-STASH SAVE FILE IN PLACE.");
            Console.WriteLine("Close the game and all save editors. Read the README's SAVE FILE EDIT section before applying.");
            bool detected = false;
            if (autoSaves)
            {
                paths.Print(); string[] files = ModPaths.Saves(paths.RequireSaveFolder(), true);
                if (files.Length != 1) throw new ArgumentException("--auto-saves needs exactly one shared-stash .d2i for Chronicle-only repair; found " + files.Length + ". Choose --save-file explicitly or use the interactive numbered list.");
                save = files[0]; detected = true;
            }
            if (interactive)
            {
                paths.Print(); save = PickSave(paths, true, out detected);
                mapping = Ask("Matching old-to-new-ids.csv from the run that changed item-runes IDs", "");
            }
            if (string.IsNullOrWhiteSpace(save) || string.IsNullOrWhiteSpace(mapping)) throw new ArgumentException("Chronicle repair requires --save-file and --mapping (or their interactive paths).");
            save = Folder(save); mapping = Folder(mapping);
            string suggestedBackup = detected ? paths.DefaultBackupRoot() : Engine.DefaultBackupRoot(System.IO.Path.GetDirectoryName(save));
            if (interactive) backup = Ask("SAVE backup folder (outside the save/game-data folder)", suggestedBackup);
            else if (backup == null && detected) backup = suggestedBackup;
            ChroniclePlan plan = ChronicleRepair.Build(save, mapping, backup == null ? null : Folder(backup));
            if (detected)
            {
                paths.ModInfo.Verify(); plan.DetectedModInfo = paths.ModInfo;
                Engine.ValidateBackupRoot(System.IO.Path.Combine(paths.ContentFolder, "data"), plan.BackupRoot);
            }
            ChronicleRepair.Preview(plan);
            if (dry) Console.WriteLine("\nPreview only. No files were written; the save was not edited.");
            else if (plan.Changes.Count == 0) Console.WriteLine("\nNo mapped Chronicle IDs need repair. The save was not edited. Unmapped IDs are not validated.");
            else
            {
                if (!yes)
                {
                    if (Console.IsInputRedirected) throw new ArgumentException("Use --dry-run to preview, or --apply --yes to EDIT THE SAVE FILE with mandatory backups.");
                    if (!Confirm("Back up and EDIT the .d2i SAVE FILE shown above.")) { Console.WriteLine("Cancelled. No save file was edited."); return; }
                }
                ChronicleRepair.Apply(plan);
            }
        }
        private static void RepairItems(List<string> saves, string folder, string mapping, string tables, string backup, bool history, bool interactive, bool dry, bool yes, ModPaths paths, bool autoSaves, bool explicitMod)
        {
            Console.WriteLine("\nWARNING: ITEM REPAIR EDITS CHARACTER/STASH SAVE FILES IN PLACE. Close the game and save editors.");
            bool detected = false;
            if (autoSaves) { paths.Print(); folder = paths.RequireSaveFolder(); detected = true; }
            if (interactive)
            {
                paths.Print();
                string mode = Ask("Select FILE or FOLDER (all direct .d2s/.d2i saves)", "FILE");
                if (mode.Equals("FILE", StringComparison.OrdinalIgnoreCase)) saves.Add(Folder(PickSave(paths, false, out detected)));
                else if (mode.Equals("FOLDER", StringComparison.OrdinalIgnoreCase))
                {
                    folder = Folder(Ask("SAVE FOLDER (all direct saves will be inspected)", paths.SaveFolderExists ? paths.SaveFolder : ""));
                    detected = SamePath(folder, paths.SaveFolder);
                }
                else throw new ArgumentException("Choose FILE or FOLDER. No saves were edited.");
                mapping = Ask("Matching old-to-new-ids.csv", "");
                tables = Ask("This mod's data/global/excel folder (all five required TXT tables)", paths.TablesFolder ?? "");
                detected = detected || SamePath(tables, paths.TablesFolder);
                string choice = Ask("Also repair Chronicle history in selected stashes? YES or NO", "NO");
                if (choice.Equals("YES", StringComparison.OrdinalIgnoreCase)) history = true;
                else if (!choice.Equals("NO", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose YES or NO. No saves were edited.");
            }
            else if (tables == null && paths.TablesFolder != null && (detected || explicitMod || SamePath(folder, paths.SaveFolder) ||
                (saves.Count > 0 && saves.All(p => SamePath(System.IO.Path.GetDirectoryName(Folder(p)), paths.SaveFolder)))))
            { tables = paths.TablesFolder; detected = true; Console.WriteLine("Using detected mod tables: " + tables); }
            if ((saves.Count == 0 && folder == null) || (saves.Count > 0 && folder != null) || string.IsNullOrWhiteSpace(mapping) || string.IsNullOrWhiteSpace(tables))
                throw new ArgumentException("Item repair needs --save-file FILE (repeatable) OR --save-folder FOLDER, plus --mapping CSV and --mod-tables FOLDER.");
            saves = saves.Select(Folder).ToList(); if (folder != null) folder = Folder(folder);
            string suggestedBackup = detected ? paths.DefaultBackupRoot() : Engine.DefaultBackupRoot(folder ?? System.IO.Path.GetDirectoryName(saves[0]));
            if (interactive) backup = Ask("SAVE backup folder (outside all save/game-data folders)", suggestedBackup);
            else if (backup == null && detected) backup = suggestedBackup;
            ItemRepairPlan plan = ItemRepair.Build(saves, folder, Folder(mapping), Folder(tables), backup == null ? null : Folder(backup), history);
            if (detected)
            {
                paths.ModInfo.Verify(); plan.DetectedModInfo = paths.ModInfo;
                Engine.ValidateBackupRoot(System.IO.Path.Combine(paths.ContentFolder, "data"), plan.BackupRoot);
            }
            ItemRepair.Preview(plan);
            if (dry) Console.WriteLine("\nPreview only. No files were written; saves were not edited.");
            else if (plan.Changes == 0) Console.WriteLine("\nNo mapped IDs need repair. Saves were not edited. Unmapped IDs are not validated.");
            else
            {
                if (!yes)
                {
                    if (Console.IsInputRedirected) throw new ArgumentException("Use --dry-run to preview, or --apply --yes to EDIT THE SAVE FILES with mandatory backups.");
                    if (!Confirm("Back up ALL selected saves and EDIT the listed ID fields.")) { Console.WriteLine("Cancelled. No saves were edited."); return; }
                }
                ItemRepair.Apply(plan);
            }
        }
        public static int Main(string[] args)
        {
            bool interactive = args.Length == 0 && !Console.IsInputRedirected;
            int exit = 0;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                string target = AppDomain.CurrentDomain.BaseDirectory, vanilla = null, backup = null, mapping = null, saveFolder = null, modTables = null, modFolder = null, historyFolder = null; int? start = null;
                var saves = new List<string>();
                WorkflowMode workflowMode = WorkflowMode.Unspecified;
                ItemRunesMode itemRunesMode = ItemRunesMode.Unspecified;
                bool dry = false, apply = false, yes = false, repair = false, repairItems = false, history = false, localizationOptions = false, autoSaves = false, detectPaths = false;
                for (int i = 0; i < args.Length; ++i)
                {
                    string arg = args[i].ToLowerInvariant();
                    if (arg == "--help" || arg == "-h" || arg == "/?") { Help(); return 0; }
                    if (arg == "--licenses")
                    {
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        foreach (string name in assembly.GetManifestResourceNames().Where(n => !n.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal))
                            using (var reader = new StreamReader(assembly.GetManifestResourceStream(name))) Console.WriteLine("\n" + name + "\n" + reader.ReadToEnd());
                        return 0;
                    }
                    if (arg == "--dry-run") dry = true;
                    else if (arg == "--repair-chronicle") repair = true;
                    else if (arg == "--repair-items") repairItems = true;
                    else if (arg == "--include-chronicle") history = true;
                    else if (arg == "--auto-saves") autoSaves = true;
                    else if (arg == "--detect-paths") detectPaths = true;
                    else if (arg == "--apply") apply = true;
                    else if (arg == "--yes") yes = true;
                    else if (arg == "--target" || arg == "--vanilla" || arg == "--start" || arg == "--backup-folder" || arg == "--item-runes-mode" || arg == "--save-file" || arg == "--mapping" || arg == "--save-folder" || arg == "--mod-tables" || arg == "--mod-folder" || arg == "--history-folder" || arg == "--mode")
                    {
                        if (++i >= args.Length) throw new ArgumentException("Missing value for " + arg);
                        if (arg == "--target" || arg == "--vanilla" || arg == "--start" || arg == "--item-runes-mode") localizationOptions = true;
                        if (arg == "--target") target = args[i]; else if (arg == "--vanilla") vanilla = args[i]; else if (arg == "--backup-folder") backup = args[i];
                        else if (arg == "--save-file") saves.Add(args[i]); else if (arg == "--mapping") mapping = args[i];
                        else if (arg == "--save-folder") saveFolder = args[i]; else if (arg == "--mod-tables") modTables = args[i];
                        else if (arg == "--mod-folder") modFolder = args[i];
                        else if (arg == "--history-folder") historyFolder = args[i];
                        else if (arg == "--mode") workflowMode = Workflow.ParseMode(args[i]);
                        else if (arg == "--item-runes-mode") itemRunesMode = ParseItemRunesMode(args[i]);
                        else { int parsed; if (!int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException("Invalid starting ID."); start = parsed; }
                    }
                    else throw new ArgumentException("Unknown option: " + arg + ". Use --help.");
                }
                if (dry && (apply || yes)) throw new ArgumentException("--dry-run cannot be combined with --apply or --yes.");
                if (yes && !apply) throw new ArgumentException("Use --apply together with --yes.");
                if (detectPaths)
                {
                    if (apply || yes || repair || repairItems || autoSaves || localizationOptions || saves.Count > 0 || saveFolder != null || mapping != null || modTables != null || historyFolder != null || history || workflowMode != WorkflowMode.Unspecified)
                        throw new ArgumentException("--detect-paths is a standalone read-only action; combine it only with --mod-folder.");
                    ModPaths detectedPaths = ModPaths.Discover(modFolder == null ? AppDomain.CurrentDomain.BaseDirectory : Folder(modFolder)); detectedPaths.Print();
                    return detectedPaths.ModInfo != null && detectedPaths.SaveFolderExists ? 0 : 1;
                }
                if (workflowMode != WorkflowMode.Unspecified && (repair || repairItems || mapping != null || autoSaves || saves.Count > 0 || itemRunesMode != ItemRunesMode.Unspecified))
                    throw new ArgumentException("The four workflows resolve their own mapping and scope. Do not combine --mode with advanced repair/mapping/item-runes-mode options.");
                if (historyFolder != null && workflowMode != WorkflowMode.EditSaveFilesOnly) throw new ArgumentException("--history-folder is only for mode 4 / EDIT SAVE FILES ONLY.");
                if ((autoSaves || modFolder != null) && !repair && !repairItems && workflowMode == WorkflowMode.Unspecified) throw new ArgumentException("Use --mod-folder with --mode, a repair action, or --detect-paths.");
                if (autoSaves && (saves.Count > 0 || saveFolder != null)) throw new ArgumentException("Choose --auto-saves OR explicit --save-file/--save-folder paths.");
                if (repair && repairItems) throw new ArgumentException("Choose --repair-items (optionally --include-chronicle) OR --repair-chronicle.");
                if ((repair || repairItems) && localizationOptions) throw new ArgumentException("SAVE FILE repair is a separate action. Do not combine it with localization options.");
                if (!repair && !repairItems && (saves.Count > 0 || mapping != null)) throw new ArgumentException("Use --repair-chronicle or --repair-items to select a SAVE FILE EDIT action.");
                if (!repairItems && workflowMode != WorkflowMode.CompactAndEditSaves && workflowMode != WorkflowMode.EditSaveFilesOnly && (saveFolder != null || modTables != null || history)) throw new ArgumentException("Save/table options require mode 3, mode 4 or --repair-items.");
                if (repair && !autoSaves && saves.Count != 1) throw new ArgumentException("Chronicle repair needs exactly one --save-file, or --auto-saves.");
                Console.WriteLine("D2R Localization ID Compactor 1.6.1 - MadMike");
                if (interactive)
                {
                    workflowMode = Workflow.Choose();
                }
                ModPaths paths = repairItems || repair || workflowMode != WorkflowMode.Unspecified ? ModPaths.Discover(modFolder == null ? AppDomain.CurrentDomain.BaseDirectory : Folder(modFolder)) : null;
                if (interactive && paths != null && paths.ModInfo == null && (repair || repairItems || workflowMode == WorkflowMode.CompactAndEditSaves || workflowMode == WorkflowMode.EditSaveFilesOnly))
                {
                    Console.WriteLine("The EXE is not inside a recognized mod. Select its folder once to detect the save/table paths, or choose MANUAL.");
                    string selectedMod = Ask("Mod folder or MANUAL", "MANUAL");
                    if (!selectedMod.Equals("MANUAL", StringComparison.OrdinalIgnoreCase)) paths = ModPaths.Discover(Folder(selectedMod));
                }
                if (workflowMode != WorkflowMode.Unspecified) RunWorkflow(workflowMode, target, args.Any(a => a.Equals("--target", StringComparison.OrdinalIgnoreCase)), vanilla, start, backup, saveFolder, modTables, historyFolder, paths, interactive, dry, yes);
                else if (repairItems) RepairItems(saves, saveFolder, mapping, modTables, backup, history, interactive, dry, yes, paths, autoSaves, modFolder != null);
                else if (repair) Repair(saves.FirstOrDefault(), mapping, backup, interactive, dry, yes, paths, autoSaves);
                else
                {
                    if (interactive)
                    {
                        target = Ask("Target strings folder", target);
                        string referencePath = Ask("Vanilla strings folder, or BUILTIN for D2R 3.4", "BUILTIN");
                        if (!referencePath.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase)) vanilla = referencePath;
                    }
                    target = Folder(target); if (vanilla != null) vanilla = Folder(vanilla);
                    if (interactive) backup = Ask("Backup folder (must be outside game data)", Engine.DefaultBackupRoot(target));
                    if (backup != null) backup = Folder(backup);
                    if (vanilla != null && string.Equals(target.TrimEnd('\\', '/'), vanilla.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Target and vanilla reference must be different folders.");
                    Reference reference = Reference.Load(vanilla);
                    long suggested = Math.Max(30000L, (((long)reference.Maximum + 1 + 999) / 1000) * 1000);
                    if (interactive)
                    {
                        string answer = Ask("Starting custom ID", suggested.ToString(CultureInfo.InvariantCulture)); int parsed;
                        if (!int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException("Invalid starting ID."); start = parsed;
                    }
                    if (!start.HasValue) { if (suggested > Engine.MaximumId) throw new InvalidDataException("The reference leaves no supported custom ID range; review its outliers."); start = (int)suggested; }
                    if (interactive && Engine.JsonFiles(target).Any(p => string.Equals(System.IO.Path.GetFileName(p), "item-runes.json", StringComparison.OrdinalIgnoreCase))) itemRunesMode = AskItemRunesMode();
                    Plan plan = Engine.Build(target, reference, start.Value, backup, itemRunesMode); Engine.Preview(plan);
                    if (dry) Console.WriteLine("\nPreview only. No files were written.");
                    else if (plan.Changed == 0 && plan.LegacyBackup == null) Console.WriteLine("\nNo ID changes are needed.");
                    else
                    {
                        if (!yes)
                        {
                            if (Console.IsInputRedirected) throw new ArgumentException("Use --dry-run to preview, or --apply --yes to write with backups.");
                            if (!Confirm("Relocate old backups if needed, back up scanned JSON and apply this plan.")) { Console.WriteLine("Cancelled. No files were written."); return 0; }
                        }
                        Engine.Apply(plan);
                    }
                }
            }
            catch (Exception error) { Console.Error.WriteLine("\nERROR: " + error.Message); exit = 1; }
            if (interactive) { Console.Write("\nPress Enter to close."); Console.ReadLine(); }
            return exit;
        }
    }
}
