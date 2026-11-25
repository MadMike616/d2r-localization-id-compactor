// SPDX-License-Identifier: MIT
// Copyright (c) 2026 MadMike
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace D2RLocalizationCompactor
{
    internal sealed class ModPaths
    {
        public InputSnapshot ModInfo;
        public string ModName, ContentFolder, SaveFolder, TablesFolder;
        public string ModError, SaveError, TablesError;
        public bool SaveFolderExists { get { return SaveFolder != null && Directory.Exists(SaveFolder); } }

        // Microsoft FOLDERID_SavedGames. Ask Windows instead of assembling a
        // user-profile path; the folder may be redirected to another drive.
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHGetKnownFolderPath([In] ref Guid id, uint flags, IntPtr token, out IntPtr path);
        public static string WindowsSavedGames()
        {
            Guid id = new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"); IntPtr path;
            int result = SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out path); // KF_FLAG_DONT_VERIFY
            try
            {
                if (result != 0) Marshal.ThrowExceptionForHR(result);
                string value = Marshal.PtrToStringUni(path);
                if (string.IsNullOrWhiteSpace(value)) throw new IOException("Windows did not return its Saved Games location.");
                return Path.GetFullPath(value);
            }
            finally { if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path); }
        }
        private static string MetadataPath(string location)
        {
            string folder = Path.GetFullPath(location);
            if (File.Exists(folder))
            {
                if (!Path.GetFileName(folder).Equals("modinfo.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select the mod folder or its modinfo.json.");
                return folder;
            }
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Mod folder does not exist: " + folder);
            // Only direct files in ancestors are checked. No drive, mod-list,
            // game-data or user-profile tree is recursively searched.
            for (DirectoryInfo current = new DirectoryInfo(folder); current != null; current = current.Parent)
            {
                string candidate = Path.Combine(current.FullName, "modinfo.json");
                if (File.Exists(candidate)) return candidate;
            }
            string[] children = Directory.GetDirectories(folder).Where(p => p.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.Combine(p, "modinfo.json")).Where(File.Exists).ToArray();
            if (children.Length > 1) throw new InvalidDataException("Several .mpq mod folders were found. Select the intended mod's .mpq folder explicitly.");
            if (children.Length == 1) return children[0];
            throw new FileNotFoundException("No modinfo.json was found above this location or in a direct .mpq child. Select the mod with --mod-folder or enter save paths manually.");
        }
        private static string Property(JsonValue root, string name, bool required)
        {
            if (root.Kind != "object") throw new InvalidDataException("modinfo.json must be an object.");
            var matches = root.Fields.Where(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Ambiguous " + name + " properties in modinfo.json.");
            if (matches.Length == 0) { if (!required) return null; throw new InvalidDataException("modinfo.json has no savepath. Save location will not be guessed from the mod name."); }
            if (matches[0].Value.Kind != "string") throw new InvalidDataException("modinfo.json " + name + " must be a string.");
            return matches[0].Value.Text;
        }
        public static ModPaths Discover(string location) { return DiscoverAt(location, null); }
        // The explicit known-folder argument is also used by tests with a
        // synthetic redirected Saved Games folder, without touching real saves.
        public static ModPaths DiscoverAt(string location, string knownSavedGames)
        {
            var result = new ModPaths();
            try
            {
                string metadata = MetadataPath(location);
                result.ModInfo = InputSnapshot.Read(metadata);
                JsonValue root = new JsonReader(File.ReadAllText(metadata, new UTF8Encoding(false, true)), metadata).Read();
                result.ModInfo.Verify();
                result.ContentFolder = Path.GetDirectoryName(metadata);
                result.ModName = Property(root, "name", false) ?? new DirectoryInfo(result.ContentFolder).Name;
                try
                {
                    string relative = Property(root, "savepath", true).Replace('/', Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.Any(c => c < 32) || relative.IndexOfAny(new[] { '*', '?', '"', '<', '>', '|' }) >= 0)
                        throw new InvalidDataException("Empty, absolute or nonstandard savepath: enter the actual save folder manually.");
                    // Do not silently reinterpret Win32-normalized names.
                    foreach (string part in relative.Split(Path.DirectorySeparatorChar))
                        if (part.Length > 0 && part != "." && part != ".." && (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)))
                            throw new InvalidDataException("Nonstandard savepath component: enter the actual save folder manually.");
                    string game = Path.GetFullPath(Path.Combine(knownSavedGames ?? WindowsSavedGames(), "Diablo II Resurrected"));
                    string save = Path.GetFullPath(Path.Combine(game, "mods", relative));
                    if (!save.Equals(game, StringComparison.OrdinalIgnoreCase) && !save.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("savepath leaves the D2R save tree. Enter the actual save folder manually.");
                    result.SaveFolder = save;
                    if (!Directory.Exists(save)) result.SaveError = "The detected save folder does not exist. No folder will be created: " + save;
                    else ChronicleRepair.PlainPath(Path.Combine(save, ".path-check"));
                }
                catch (Exception error) { result.SaveError = error.Message; result.SaveFolder = null; }
                string tables = Path.Combine(result.ContentFolder, "data", "global", "excel");
                string[] required = { "armor.txt", "weapons.txt", "misc.txt", "itemtypes.txt", "itemstatcost.txt" };
                if (Directory.Exists(tables) && required.All(name => File.Exists(Path.Combine(tables, name)))) result.TablesFolder = tables;
                else result.TablesError = "The mod's data/global/excel folder does not contain all five parsing tables; select a complete effective table folder manually.";
            }
            catch (Exception error) { result.ModError = error.Message; result.ModInfo = null; }
            return result;
        }
        public string RequireSaveFolder()
        {
            if (ModInfo == null) throw new InvalidDataException(ModError ?? "Could not detect this mod. Select it with --mod-folder or enter save paths manually.");
            if (!SaveFolderExists) throw new InvalidDataException(SaveError ?? ModError ?? "Could not detect the save folder. Use --save-file or --save-folder.");
            ModInfo.Verify(); return SaveFolder;
        }
        public string DefaultBackupRoot() { return ContentFolder == null ? null : Engine.DefaultBackupRoot(Path.Combine(ContentFolder, "data")); }
        public static string[] Saves(string folder, bool stashOnly)
        {
            ChronicleRepair.PlainPath(Path.Combine(folder, ".path-check"));
            return Directory.GetFiles(folder).Where(p => Path.GetExtension(p).Equals(".d2i", StringComparison.OrdinalIgnoreCase) || (!stashOnly && Path.GetExtension(p).Equals(".d2s", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public void Print()
        {
            if (ModInfo == null) { Console.WriteLine("Automatic mod detection unavailable: " + ModError); return; }
            Console.WriteLine("\nDetected mod: " + Engine.Json(ModName));
            Console.WriteLine("modinfo.json: " + ModInfo.Path);
            Console.WriteLine("Save folder: " + (SaveFolder ?? "Unavailable"));
            if (SaveError != null) Console.WriteLine("  " + SaveError);
            Console.WriteLine("Mod tables: " + (TablesFolder ?? "Unavailable"));
            if (TablesError != null) Console.WriteLine("  " + TablesError);
        }
    }
}
