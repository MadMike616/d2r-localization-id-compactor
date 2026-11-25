using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("D2R Localization ID Compactor")]
[assembly: AssemblyDescription("Portable localization ID compactor with vanilla preservation and backups")]
[assembly: AssemblyCompany("MadMike")]
[assembly: AssemblyProduct("D2R Localization ID Compactor")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MadMike")]
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyInformationalVersion("1.0.2")]

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
        public bool Preserved;
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
    internal sealed class Plan
    {
        public string Target;
        public string BackupRoot, LegacyBackup;
        public Reference Reference;
        public int Start, Changed;
        public List<Document> Documents;
        public readonly List<Change> Mapping = new List<Change>();
        public readonly Dictionary<int, List<string>> VanillaDuplicates = new Dictionary<int, List<string>>();
        public string Backup;
    }

    internal static class Engine
    {
        public const int MaximumId = 65534;
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
        private static void ValidateBackupRoot(string target, string backup)
        {
            if (Within(backup, DataBoundary(target).FullName)) throw new InvalidDataException("The backup folder must be outside the localization/game-data tree. Choose a folder outside strings, data and any containing .mpq folder.");
            for (DirectoryInfo current = new DirectoryInfo(backup); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Backup paths must not use links/reparse points: " + current.FullName);
        }
        private static string FindLegacyBackup(string target)
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
        public static Plan Build(string target, Reference reference, int start, string backupFolder)
        {
            if (start <= reference.Maximum || start < 0 || start > MaximumId) throw new InvalidDataException("Starting ID must be above the vanilla maximum " + reference.Maximum + " and at most " + MaximumId + ".");
            string[] files = JsonFiles(target);
            if (files.Length == 0) throw new InvalidDataException("No .json files were found in the target folder.");
            string backupRoot = System.IO.Path.GetFullPath(backupFolder ?? DefaultBackupRoot(target));
            ValidateBackupRoot(target, backupRoot);
            if (reference.SourceFolder != null && Within(backupRoot, DataBoundary(reference.SourceFolder).FullName)) throw new InvalidDataException("The backup folder must be outside the vanilla reference tree.");
            var plan = new Plan { Target = target, Reference = reference, Start = start, BackupRoot = backupRoot,
                LegacyBackup = FindLegacyBackup(target), Documents = files.Select(Document.Read).ToList() };
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
            Console.WriteLine("\nFile                            Custom range          IDs changing");
            foreach (Document doc in plan.Documents)
                Console.WriteLine("{0,-31} {1,-21} {2}", doc.Name, doc.SkipReason != null ? "Skipped" : doc.First.HasValue ? doc.First + "-" + doc.Last : "Vanilla only", doc.Changed);
            foreach (Document doc in plan.Documents.Where(d => d.SkipReason != null)) Console.WriteLine("  Skipped " + doc.Name + ": " + doc.SkipReason);
            Console.WriteLine("\n" + plan.Mapping.Count + " custom records; " + plan.Changed + " IDs will change.");
            Console.WriteLine("Vanilla duplicate/override ID groups preserved: " + plan.VanillaDuplicates.Count);
            foreach (var pair in plan.VanillaDuplicates) Console.WriteLine("  " + pair.Key + ": " + string.Join("; ", pair.Value));
            Console.WriteLine("Each following custom range starts at a multiple of 1000 at least 300 above the preceding last ID.");
            Console.WriteLine("Only JSON ID numbers change. Fixed numeric references in plugins or other game files are not updated.");
        }
        private static string Csv(string value)
        {
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        private static void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        private static void SnapshotStillMatches(Plan plan)
        {
            string[] files = JsonFiles(plan.Target);
            if (!files.SequenceEqual(plan.Documents.Select(d => d.Path), StringComparer.OrdinalIgnoreCase)) throw new IOException("The target JSON file list changed after preview. Run the tool again.");
            foreach (Document doc in plan.Documents) if (!File.ReadAllBytes(doc.Path).SequenceEqual(doc.Original)) throw new IOException(doc.Name + " changed after preview. Run the tool again.");
        }
        private static object Manifest(Plan plan, string status, string message)
        {
            return new { tool = "D2R Localization ID Compactor", version = "1.0.2", utc = DateTime.UtcNow.ToString("o"),
                status = status, message = message, target = plan.Target, reference = plan.Reference.Description,
                startingId = plan.Start, minimumBoundaryDistance = 300, changedIds = plan.Changed, backupRoot = plan.BackupRoot,
                files = plan.Documents.Select(d => new { name = d.Name, originalSha256 = Hash(d.Original), outputSha256 = Hash(d.Output),
                    records = d.Records == null ? 0 : d.Records.Count, changingIds = d.Changed, firstCustomId = d.First, lastCustomId = d.Last, skipped = d.SkipReason }).ToArray(),
                preservedVanillaDuplicateGroups = plan.VanillaDuplicates.ToDictionary(p => p.Key.ToString(CultureInfo.InvariantCulture), p => p.Value) };
        }
        public static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 }.Serialize(value); }
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
            foreach (Document doc in plan.Documents.Where(d => d.Records != null)) foreach (Record row in doc.Records.Where(r => r.Preserved)) preserved.Append(Csv(doc.Name)).Append(',').Append(row.Index).Append(',').Append(Csv(row.Key)).Append(',').Append(row.Id).Append(',').Append(Csv(row.Reason)).Append("\r\n");
            File.WriteAllText(System.IO.Path.Combine(plan.Backup, "preserved-vanilla.csv"), preserved.ToString(), new UTF8Encoding(true));
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
        private static void Help()
        {
            Console.WriteLine("D2R Localization ID Compactor 1.0.2 - MadMike\n");
            Console.WriteLine("Double-click to choose a folder, preview the ranges, then type APPLY to proceed.");
            Console.WriteLine("The default target is the folder containing this executable.");
            Console.WriteLine("\nOptions:\n  --target FOLDER    Target folder (direct .json children only)\n  --vanilla FOLDER   Use a fresh vanilla strings extraction instead of built-in 3.4\n  --backup-folder FOLDER  Backup root outside localization/game data\n  --start NUMBER     First custom ID (default 30000, or above a newer reference)\n  --dry-run          Preview only; write nothing\n  --apply --yes      Apply without an interactive prompt; backups remain mandatory\n  --help            Show this help\n\nThe seven established filenames come first; additional filenames follow alphabetically.\nCustom IDs stop at 65534 for native 16-bit field compatibility.\nMalformed JSON, ambiguous vanilla duplicates, range overflow and concurrent edits abort.\nOnly the known vanilla outlier 251780 is excluded from the maximum calculation; it stays reserved.");
        }
        public static int Main(string[] args)
        {
            // The Framework compiler targets CLR 4. Enable the newer path
            // handling when hosted by current Windows .NET Framework runtimes.
            Type context = typeof(object).Assembly.GetType("System.AppContext");
            if (context != null)
            {
                MethodInfo setSwitch = context.GetMethod("SetSwitch", new[] { typeof(string), typeof(bool) });
                if (setSwitch != null)
                {
                    setSwitch.Invoke(null, new object[] { "Switch.System.IO.UseLegacyPathHandling", false });
                    setSwitch.Invoke(null, new object[] { "Switch.System.IO.BlockLongPaths", false });
                }
            }
            bool interactive = args.Length == 0 && !Console.IsInputRedirected;
            int exit = 0;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                string target = AppDomain.CurrentDomain.BaseDirectory, vanilla = null, backup = null; int? start = null;
                bool dry = false, apply = false, yes = false;
                for (int i = 0; i < args.Length; ++i)
                {
                    string arg = args[i].ToLowerInvariant();
                    if (arg == "--help" || arg == "-h" || arg == "/?") { Help(); return 0; }
                    if (arg == "--dry-run") dry = true;
                    else if (arg == "--apply") apply = true;
                    else if (arg == "--yes") yes = true;
                    else if (arg == "--target" || arg == "--vanilla" || arg == "--start" || arg == "--backup-folder")
                    {
                        if (++i >= args.Length) throw new ArgumentException("Missing value for " + arg);
                        if (arg == "--target") target = args[i]; else if (arg == "--vanilla") vanilla = args[i]; else if (arg == "--backup-folder") backup = args[i];
                        else { int parsed; if (!int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException("Invalid starting ID."); start = parsed; }
                    }
                    else throw new ArgumentException("Unknown option: " + arg + ". Use --help.");
                }
                if (dry && (apply || yes)) throw new ArgumentException("--dry-run cannot be combined with --apply or --yes.");
                if (yes && !apply) throw new ArgumentException("Use --apply together with --yes.");
                Console.WriteLine("D2R Localization ID Compactor 1.0.2 - MadMike");
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
                Plan plan = Engine.Build(target, reference, start.Value, backup); Engine.Preview(plan);
                if (dry) Console.WriteLine("\nPreview only. No files were written.");
                else if (plan.Changed == 0 && plan.LegacyBackup == null) Console.WriteLine("\nNo ID changes are needed.");
                else
                {
                    if (!yes)
                    {
                        if (Console.IsInputRedirected) throw new ArgumentException("Use --dry-run to preview, or --apply --yes to write with backups.");
                        Console.Write("\nType APPLY (any letter case) to relocate old backups if needed, back up scanned JSON and apply this plan: ");
                        if (!string.Equals((Console.ReadLine() ?? "").Trim(), "APPLY", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Cancelled. No files were written."); return 0; }
                    }
                    Engine.Apply(plan);
                }
            }
            catch (Exception error) { Console.Error.WriteLine("\nERROR: " + error.Message); exit = 1; }
            if (interactive) { Console.Write("\nPress Enter to close."); Console.ReadLine(); }
            return exit;
        }
    }
}
