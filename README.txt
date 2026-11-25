D2R Localization ID Compactor 1.1.0
Author: MadMike
License: MIT

Copy D2R-Localization-ID-Compactor.exe into a mod's loose
data/local/lng/strings folder and double-click it. The executable is the only
file needed to run the tool. Windows with .NET Framework 4.x is required
(included with Windows 10 and Windows 11). Python, PowerShell scripts and
D2RLoader are not required by the executable.

Close the game before applying a plan.

1. Accept the folder containing the executable, or paste another target folder.
2. Accept BUILTIN for the included vanilla D2R 3.4 ID/key reference, or paste
   the path to an original vanilla strings folder extracted with Ladik's Casc
   Viewer. Use a separate, unmodified extraction for the reference.
3. Accept the suggested backup folder outside game data, or choose another safe folder.
4. Choose the first custom ID. The default is 30000 with the built-in reference.
5. If item-runes.json is present, choose PRESERVE for existing characters/shared
   stashes, or COMPACT for a clean mod with no saves. There is no default.
6. Review the file ranges, skipped files and preserved vanilla duplicates.
7. Type apply in any letter case to proceed. Leading/trailing spaces are accepted.
   Entering anything else cancels without writing files.

WHAT GETS PROCESSED

Every direct .json file in the target folder is examined, regardless of its
filename. Subfolders are not scanned. Files with these names come first:
  item-gems.json
  item-modifiers.json
  item-nameaffixes.json
  item-names.json
  item-runes.json
  skills.json
  ui.json
All additional filenames follow in alphabetical order, ignoring case.

A localization table is an array of objects with a nonnegative integer id,
a nonempty Key and at least one language string (such as enUS or deDE).
Other valid JSON documents, empty arrays and key-only localization tables
without numeric IDs are skipped and listed in the preview. A partially
malformed localization table or invalid JSON stops the entire plan.

VANILLA AND CUSTOM ENTRIES

The bundled reference includes every vanilla 3.4 localization filename,
including files outside the seven names above. It contains only ID/key pairs,
with no vanilla translations. A fresh vanilla reference folder can replace it.

In a known vanilla filename, an entry occupying a vanilla ID slot is kept,
even when its translations or Key were edited. If a vanilla slot is duplicated
and exactly one record matches the original Key, that record is preserved
and the conflicting custom entries are renumbered. Ambiguous duplicates stop
the plan for manual review.

In an additional filename, a matching vanilla ID AND Key is preserved as a
native-string override. All other entries are custom, including entries whose
old IDs are below the chosen starting ID. Matching keys ignore case.

This rule cannot tell whether a single renamed record in a vanilla slot was
an intentional vanilla edit or an unrelated custom record reusing that slot.
Such records are preserved conservatively and recorded in preserved-vanilla.csv.

Original vanilla duplicates and native overrides stay unchanged. In particular,
the built-in 3.4 reference preserves the shared vanilla IDs 27956 and 28082.
The unusual vanilla ID 251780 is excluded only when calculating the ordinary
vanilla maximum; its definition is still preserved and its number reserved.
All other vanilla IDs count toward the maximum. With this reference the ordinary
maximum is 28138. The starting ID must exceed the reference's ordinary maximum.

ITEM-RUNES MODE

PRESERVE keeps item-runes.json byte-for-byte unchanged. Other custom IDs must
avoid every ID in that file and the 1000 numbers on either side. Overlapping
buffers merge; space between distant buffers remains available. Vanilla IDs
in other files stay fixed. Each consecutive custom range moves as a whole to
the next safe multiple of 1000 if it would touch a buffer. This also applies
to the first range, which can then begin above the requested starting ID.

COMPACT renumbers custom item-runes.json entries too, without those buffers.
Use it only for a clean mod with no existing character or shared-stash saves.
Completed runeword items can retain numeric name IDs; changing those IDs can
break references stored in existing items.

The choice is required on every run when item-runes.json contains numeric IDs.
It is not saved as a default. Mode values and option names ignore letter case.

If an earlier version already renumbered item-runes.json and saves exist,
close the game and restore that file from the original backup BEFORE choosing
PRESERVE. Preserve protects the IDs currently in the selected file; it does
not undo earlier compaction or migrate saved items. Test character loading
and shared-stash items after any localization changes.

Custom IDs are consecutive within each file, in existing entry order. The next
file with custom entries starts at the smallest multiple of 1000 at least 300
higher than the previous final ID:
  Last 54700 -> next 55000 (300 apart)
  Last 54701 -> next 56000 (55000 would be only 299 apart)
Files with no custom records consume no custom range.

The tool restricts custom IDs to 0-65534 for compatibility with native 16-bit
fields; an overflowing plan is rejected before anything is changed.

ONLY NUMERIC ID DIGITS ARE CHANGED

Keys, translations, comments, whitespace, escape spelling, line endings, BOMs
and entry order are retained. JSON comments and trailing commas are supported.
UTF-8 with or without a BOM and UTF-16 with a BOM are supported. UTF-32 and
invalid character encodings are rejected. JSON links/reparse points are rejected.

BACKUPS AND RECOVERY

Before replacing any active file, every scanned JSON file (including skipped
files) is backed up and its SHA-256 verified. Backups and JSON reports are kept
OUTSIDE the game-data tree. For a normal mod layout, the suggested location is:
  <mod-folder>/Localization-ID-Backups/<timestamp-and-unique-suffix>/originals/

For a target inside a .mpq directory, the backup root is placed beside that
.mpq directory. For a data/ layout without a .mpq wrapper, it is placed beside
data/. For another standalone folder, it is placed beside the selected folder:
  Localization-ID-Backups/<timestamp-and-unique-suffix>/originals/

The preview prints the complete destination. An explicit --backup-folder can
override it, but destinations within localization/game data or the vanilla
reference tree are rejected. D2RLoader reads JSON recursively under strings;
placing a backup manifest there prevents string-table initialization.

Versions 1.0.0 and 1.0.1 placed backups inside the target strings folder. Run
1.0.2 or later and apply the preview to relocate verified old compactor backups
outside game data. This relocation is offered even if IDs are already compacted.
Old backup files are preserved together in a Migrated-legacy-* folder. Automatic
relocation requires source and destination to be on the same drive. Unknown or
incomplete old backup folders require manual review/moving.

The same backup folder contains:
  old-to-new-ids.csv       File, record index, Key, old ID and new ID
  preserved-vanilla.csv   Preserved records and the classification reason
  preserved-item-runes.csv  Records protected by Preserve mode
  manifest.json          Original/output hashes, ranges and completion status

Temporary outputs are staged on the target volume. Files are replaced using
the filesystem's atomic replacement operation. If a write fails, the tool
attempts to roll back files already changed and reports any rollback failure.
The volume must support Windows File.Replace (for example, NTFS).

Concurrent edits and added/removed JSON files invalidate the preview and abort.
The tool never intentionally overwrites a concurrent edit during rollback.
A completed run verifies the written hashes. A run needing no ID changes does
not create another originals backup; legacy backups can still be relocated.

To restore, close the game and copy the entire contents of the chosen
originals folder back into the target strings folder, replacing those files.
Keep the report and backup until in-game behavior has been checked.

DEPENDENCIES OUTSIDE THESE JSON FILES

The tool does not inspect or update DLLs, binary game tables, numeric references
in other files or static HTML database snapshots. Key-based lookups retain their
keys. A mod/plugin with fixed numeric localization references needs a separate
review and corresponding updates using the generated mapping. Test the mod
afterward. The tool does not launch D2RLoader or the game.

COMMAND LINE

Preview only; writes no files:
  "D2R-Localization-ID-Compactor.exe" --target "<strings-folder>" --item-runes-mode preserve --dry-run

Apply with mandatory backups and no interactive prompt:
  "D2R-Localization-ID-Compactor.exe" --target "<strings-folder>" --item-runes-mode preserve --start 30000 --apply --yes

Use another vanilla extraction:
  "D2R-Localization-ID-Compactor.exe" --target "<mod-strings>" --vanilla "<vanilla-strings>" --item-runes-mode preserve --dry-run

Select a backup root outside game data:
  "D2R-Localization-ID-Compactor.exe" --target "<mod-strings>" --backup-folder "<external-backups>" --item-runes-mode preserve --apply --yes

For a clean mod without saves, use --item-runes-mode compact instead.
The option is mandatory when item-runes.json contains numeric IDs. For folders
without that file, it can be omitted because it has no effect.

Exit code 0 means successful completion, preview, cancellation or no changes
needed. Exit code 1 means validation/write failure. CLI runs do not pause.
The executable also supports --help. It stores no machine-specific settings.
Command-line option names are case-insensitive; for example, --DRY-RUN,
--Dry-Run and --dry-run are equivalent. Paths and localization keys keep their
original spelling.

BUILD FROM SOURCE

Run build.ps1 from Windows PowerShell or PowerShell with a .NET Framework 4.x
C# compiler installed. The supplied vanilla-3.4.json.gz reference is embedded
as an assembly resource. Build output defaults to a sibling build directory,
outside this source tree. The executable is an optimized, non-debug build.

The integration tests use Python only as a development/test dependency:
  python tests/test_compactor.py --exe "<built-executable>" --work "<test-folder>"
