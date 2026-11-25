# D2R Localization ID Compactor

A portable Windows utility by **MadMike** for compacting custom Diablo II: Resurrected localization IDs while preserving vanilla entries and the original file formatting.

**[Download the latest release](https://github.com/MadMike616/d2r-localization-id-compactor/releases/latest)**

## Run

1. Download the EXE or extract the release ZIP.
2. Close the game and copy `D2R-Localization-ID-Compactor.exe` into the mod's loose `data/local/lng/strings` folder. You can also run it elsewhere and enter the target folder when prompted.
3. Double-click the EXE. Accept the built-in vanilla **D2R 3.4** reference, or select an unmodified vanilla strings extraction. Ladik's Casc Viewer can extract a fresh reference.
4. Choose the first custom ID; the default is **30000** with the bundled reference.
5. Review the preview, then type `apply` in any letter case. The program backs up every scanned JSON before replacing any active file.

Requires Windows with .NET Framework 4.x. The executable contains its vanilla ID/key reference; Python, additional DLLs and D2RLoader are not required to run it. This is a standalone file utility.

## Files and ordering

Every direct `.json` file in the chosen folder is examined. Subfolders are not scanned. Recognized localization arrays are processed in this order:

1. `item-gems.json`
2. `item-modifiers.json`
3. `item-nameaffixes.json`
4. `item-names.json`
5. `item-runes.json`
6. `skills.json`
7. `ui.json`
8. All additional filenames, alphabetically and ignoring case.

Other valid JSON documents, empty arrays and key-only localization tables without numeric IDs are listed as skipped. Invalid JSON and partially malformed localization tables stop the plan before active files are changed.

## ID rules

- Vanilla records retain their IDs, including edited translations and renamed records occupying vanilla slots in the same filename.
- In an additional filename, a matching vanilla ID **and** key is preserved as an override.
- All other records are custom, including records whose old IDs are below the chosen starting number.
- Custom IDs are consecutive within each file, in existing entry order.
- The next file with custom entries starts at the smallest multiple of **1000** that is at least **300** higher than the preceding final ID. An ending ID of `54700` leads to `55000`; `54701` leads to `56000`.
- Custom IDs are restricted to **65534** or below for compatibility with native 16-bit fields. Overflowing plans are rejected.
- Existing vanilla duplicate/override IDs are preserved. The bundled reference includes vanilla's shared IDs `27956` and `28082`.
- Vanilla ID `251780` is a known outlier: it is excluded from the ordinary maximum calculation but remains reserved. The bundled reference's ordinary maximum is **28138**.

If a single record occupies a vanilla slot in its original filename, its ID is preserved conservatively even when the key was renamed. This cannot distinguish an intentional vanilla edit from an unrelated custom record reusing the slot. Ambiguous duplicate vanilla slots require manual review.

## Backups and preservation

Before any active replacement, the program writes and verifies backups of **all scanned JSON files**, including skipped files:

```text
Localization-ID-Backups/<timestamp-and-unique-suffix>/
  originals/              Original files
  old-to-new-ids.csv      Complete custom ID mapping
  preserved-vanilla.csv   Preserved records and classification reasons
  manifest.json          SHA-256 hashes, ranges and completion status
```

Only the planned numeric ID digits are replaced. Keys, translations, comments, whitespace, escape spelling, line endings, BOMs and entry order are retained. UTF-8 and UTF-16 with a BOM are supported; malformed encodings are rejected. JSON comments and trailing commas are accepted.

Outputs are staged and replaced atomically on supported Windows filesystems such as NTFS. A failed write triggers a rollback attempt, with any incomplete recovery reported alongside the backup location. Changes detected after the preview invalidate the plan. Repeating an already compacted plan does not create another backup.

To restore, close the game and copy all files from the selected `originals` folder back into the target folder.

The utility changes localization JSON only. Fixed numeric references in plugins, binary tables or other files need a separate review using the generated mapping. Static HTML snapshots are not refreshed. Test the mod afterward.

## Command line

Option names and the `apply` confirmation are case-insensitive. Paths and localization keys retain their original spelling.

Preview without writing files:

```powershell
.\D2R-Localization-ID-Compactor.exe --target "<mod-strings>" --dry-run
```

Apply with mandatory backups:

```powershell
.\D2R-Localization-ID-Compactor.exe --target "<mod-strings>" --start 30000 --apply --yes
```

Use a fresh vanilla extraction:

```powershell
.\D2R-Localization-ID-Compactor.exe --target "<mod-strings>" --vanilla "<vanilla-strings>" --dry-run
```

Use `--help` for all options. Exit code `0` means completion, preview, cancellation or no changes needed; `1` means validation/write failure.

## Build and test

The source uses the Windows .NET Framework C# compiler and no third-party packages. Build an optimized executable outside the source tree:

```powershell
.\build.ps1
```

Python is needed only for development tests. The default suite uses self-contained fixtures and the bundled ID/key reference:

```powershell
python tests/test_compactor.py --exe "<built-executable>" --work "<test-output-folder>"
```

See [README.txt](README.txt) for additional format and recovery details and [CHANGELOG.txt](CHANGELOG.txt) for version history.

## License

[MIT](LICENSE). Copyright (c) 2026 MadMike.
