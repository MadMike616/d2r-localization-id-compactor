# D2R Localization ID Compactor

A portable Windows utility by **MadMike** for compacting custom Diablo II: Resurrected localization IDs while preserving vanilla entries and file formatting. Current version: **1.6.1**.

**SAVE FILE EDIT WARNING: Option 3, COMPACT AND EDIT SAVES, overwrites character `.d2s` and shared-stash `.d2i` save files in place, as well as localization JSON.** It migrates saved runeword IDs, Chronicle runeword history and known saved Chronicle drop-source localization IDs to match the same compaction. All original JSON and saves are backed up and verified before the first edit. Read the scope and recovery instructions below. **Option 4, EDIT SAVE FILES ONLY, also overwrites character/shared-stash saves, using current runeword IDs and verified history while leaving active JSON unchanged.** Options 1 and 2 do not edit saves.

**[Download the latest published release](https://github.com/MadMike616/d2r-localization-id-compactor/releases/latest)**

## Run: four choices, no default

1. Close Diablo II: Resurrected, D2RLoader and save editors. Copy the EXE into the mod's loose `data/local/lng/strings` folder. It can also run elsewhere; select the mod/target folder when prompted.
2. Double-click the EXE and explicitly select one of the four choices below. A blank answer asks again; nothing is selected automatically. Numbers and names ignore letter case.
3. Review paths, proposed IDs, save changes if applicable, and the backup location. At the final confirmation, enter **Y/YES** to continue or **N/NO** to cancel, in any letter case. There is **no default**: blank or invalid answers ask again, and end of input cancels. The prompt still states whether JSON, saves, or both will be edited.

| Choice | What it does | Save files |
|---|---|---|
| **1 / STANDARD COMP** | Compacts all recognized localization JSON, including custom IDs in `item-runes.json`. Recommended for mods with no saves. | **No save edits. Existing saves may stop working.** |
| **2 / SKIP RUNEWORDS** | Compacts other localization JSON, leaves `item-runes.json` byte-for-byte unchanged, and reserves 1000 IDs on both sides of its existing IDs. | **No save edits. Preserves existing runeword IDs for existing saves.** |
| **3 / COMPACT AND EDIT SAVES** | Compacts all recognized localization JSON, automatically creates its mapping, then migrates all direct character/shared-stash saves in the mod's save folder. | **EDITS SAVE FILES. Includes physical runewords, Chronicle history and known saved drop-source strings.** |
| **4 / EDIT SAVE FILES ONLY** | Reads current IDs in `item-runes.json`, matches old saved IDs using backup history, and repairs all direct character/shared-stash saves. Active JSON is unchanged. | **EDITS SAVE FILES. Physical runewords, Chronicle runeword history and required checksums only.** |

**All four options make verified backups before editing any active files. No option is the default.** A run with no required changes makes no new originals backup. Option 3 validates the entire JSON/save batch before making changes. Option 4 validates the entire selected save batch and its historical ID matches. One unsupported save stops either operation before any active file is edited.

Option 2 preserves the runeword IDs currently in the file. It does not undo an earlier compaction, fix already stale saves, or migrate other saved numeric string references. Preserving runewords cannot guarantee that unrelated save problems will disappear.

Requires **64-bit Windows 10/11**. The self-contained EXE includes its runtime and vanilla ID/key reference. No runtime installation, additional DLLs, Python or D2RLoader is needed to run it. Options 3 and 4 additionally require the mod's effective game-data tables when saves exist. Runtime extraction may use the operating system's temporary cache; previews do not write save, backup or game-data files. This is a standalone utility, independent of any particular mod name.

## Automatic paths and mapping

Inside a mod, the EXE finds the nearest ancestor `modinfo.json`, or a single direct `.mpq` child when it is in the outer mod folder. Otherwise select the mod folder once or use `--mod-folder`. It does not search every installed mod or scan user-profile trees.

The utility reads **`savepath`**, rather than guessing from the mod's display/archive name. Windows supplies the registered **Saved Games** location, including redirected folders. The profile resolves to `Diablo II Resurrected/mods/<savepath>`. Relative paths such as `../` may explicitly resolve to the D2R save root. Invalid/missing metadata, absolute paths, paths escaping the D2R save tree and ambiguous archives require manual selection. Custom launcher overrides absent from `modinfo.json` must be entered manually.

**Options 3 and 4 process every direct `.d2s` and `.d2i` file in that one profile**, including multiple softcore/hardcore/seasonal stash files. It does not recurse into subfolders or edit other profiles or companion files such as `.d2rl` and `.abs`. An empty/missing profile needs no save migration and is not created. Review the resolved location before confirming.

The mod's `data/global/excel` folder is detected when all five required tables are present: **`armor.txt`, `weapons.txt`, `misc.txt`, `itemtypes.txt`, `itemstatcost.txt`**. These must be the complete, effective tables matching the saves, including their historical binary layouts. If the mod contains only overrides, extract missing vanilla tables with **Ladik's Casc Viewer** into a separate working folder, overlay the mod's versions, and select that folder. The utility reads these tables and never edits them.

**Option 3 generates and uses its old-to-new mapping automatically. Option 4 automatically looks for prior mappings and rune-table snapshots in this mod's backup folder. Neither requires selecting an individual CSV.** Copies of the exact inputs used are kept in the backup. Advanced command-line repair can still select a particular historical mapping; see below.

Read-only path detection:

```powershell
.\D2R-Localization-ID-Compactor.exe --detect-paths --mod-folder "<mod-folder>"
```

## Option 3: exactly what is edited in saves

**COMPACT AND EDIT SAVES overwrites save files.** It patches these known saved localization references affected by the current compaction:

- Physical runeword items' **16-bit saved runeword name IDs**: player inventory, equipment/alternate equipment, cube, personal stash, cursor, corpses, mercenaries, saved iron golems, shared-stash normal/advanced tabs and socketed children.
- **32-bit Chronicle runeword discovery IDs**, even when the physical item no longer exists.
- **16-bit Chronicle drop-source localization IDs**, both on physical items and in set/unique/runeword discovery history, when the source refers to a string being moved. Set/unique discovery item numbers are table-row IDs and stay unchanged.
- The **four-byte character checksum at offset `0x0C`**, recalculated when a `.d2s` file changes.

Only those exact fields/checksums are patched in the original bytes. Saves are parsed to locate fields; items are not recreated and the whole save is not reserialized. **Stats/rolls, runeword properties, sockets/runes, seeds, durability, personalization, positions, timestamps, gold, character progress and unrelated data retain their original bytes.** Unchanged saves are backed up with the batch but not rewritten.

This does not apply new recipes/properties, reroll items, create missing items, migrate unrelated item-stat IDs, repair general corruption or guarantee character loading. References in arbitrary plugin data and unrecognized save formats are not migrated.

Saves must match the **current pre-compaction JSON IDs**. A runeword absent from that table stops option 3 instead of guessing which historical ID it represents. Restore matching original JSON or first perform advanced recovery with a verified old-to-current mapping. Duplicate old runeword IDs and ambiguous saved source IDs also stop migration. Overlapping before/after IDs are handled in one pass using this fresh plan.

Supported layouts are **save version 105**, modern stash **format 2**, and Chronicle **version 1 with 64 trailing bytes**. Unsupported versions/layouts, truncated or short creation-only characters, invalid sizes/checksums, unknown items/stats, malformed tables and unparsed character tails stop the **entire combined operation before edits**. A short new character may need to be entered and saved normally in game first. Mods that change the binary save layout are unsupported.

A stat-decoding failure can indicate different historical stat widths between a save and the tables; it does not prove corruption. The error identifies the failing stat and, when available, the previous stat's position/widths. The utility never guesses widths or remaps unrelated stat IDs to force parsing.

## Option 4: EDIT SAVE FILES ONLY — OVERWRITES SAVES

This option **does not edit any active localization JSON**. It reads the current numeric IDs and exact string keys in `item-runes.json`, then migrates physical runeword items and Chronicle runeword discovery history in **all direct `.d2s` and `.d2i` saves** in the detected mod profile. Only matching **16-bit item runeword IDs**, **32-bit Chronicle runeword discovery IDs**, and required **character checksums at `0x0C`** change. Item properties, rolls, sockets, source IDs, timestamps and unrelated bytes stay unchanged. It does not fix unrelated item-stat layouts or other save corruption.

**Current IDs alone cannot identify an old saved runeword.** Saves store numeric runeword IDs, not localization keys. The utility automatically reads `old-to-new-ids.csv` and `item-runes.json` snapshots under this mod's `Localization-ID-Backups` folder. It matches their exact keys to the current rune table. Both historical sides of a mapping become aliases of the current key; the destination always comes from **today's file**, not an outdated CSV destination. This supports multiple earlier compactions and a mixture of old/already migrated saves. Native IDs remain supported, including edited native string slots.

Keep this history with the mod. The interactive preview shows the detected history location and allows overriding it; `--history-folder` selects another backup folder on the command line. Only use verified history from **this same mod**. The search is confined to that folder, with limits on depth, file count and input size; it does not scan other installed mods or user-profile trees. Prior `Save-only-*` backups are excluded from automatic history discovery because they introduce no new string identities.

A saved ID with no matching history, a removed key, or an ID reused for different runewords is **not guessed**. If any selected save uses an unresolved/conflicting ID, the entire batch stops before edits. A current ID can also be ambiguous if the same number historically identified another runeword; its number alone cannot prove which save generation it came from. Use verified matching history rather than deleting conflicting evidence to force the batch through. With no stale IDs, no historical mapping is required.

The same save-version, parsing-table and whole-batch validation restrictions documented for option 3 apply. A stat-layout mismatch or unsupported character stops all saves from being written. Preview first; answering **Y/YES** at the final save-edit prompt authorizes overwriting the selected saves. No option is selected by default.

Before the first save replacement, **all selected original saves**, including unchanged ones, are backed up and byte-verified, together with the current rune table, every historical input used, the resolved alias mapping, the five parsing tables and detected metadata:

```text
<backup-root>/Save-only-<timestamp-and-unique-suffix>/
  originals/              COMPLETE ORIGINAL selected .d2s/.d2i SAVE FILES
  context/                Read-only current rune table and history snapshots
  mod-tables/             Exact five parsing tables
  old-to-new-ids.csv       Resolved old/current aliases used for this repair
  detected-modinfo.json   Metadata used for detected paths, when applicable
  repair-report.json      Save/input hashes, exact changed fields and status
  RESTORE.txt             Exact original-save destinations
```

Changes to current `item-runes.json`, history inputs/file list, saves/file list, metadata or tables after preview invalidate the plan. A save-write failure triggers restoration of already committed saves; incomplete recovery is reported. A crash/power loss can interrupt the batch, so retain the verified originals.

**Undo option 4:** close the game/editors and restore the original saves from `originals` to their exact destinations in `RESTORE.txt`. Active JSON was not edited. **Do not copy the `context` snapshots over your active JSON as part of this undo.** Restoring saves returns their pre-repair IDs (which may already have been stale) and reverts later progress.

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
- **Option 2 (SKIP RUNEWORDS)** leaves `item-runes.json` byte-for-byte unchanged. Its IDs and the 1000 numbers below and above each are reserved against assignment to other custom entries. Vanilla IDs in other files stay fixed.
- **Options 1 and 3** also renumber custom entries in `item-runes.json`. Option 1 does not edit saves; option 3 migrates their supported saved references to match the same compaction.
- Custom IDs are consecutive within each file, in existing entry order.
- The next file with custom entries starts at the smallest multiple of **1000** that is at least **300** higher than the preceding final ID. An ending ID of `54700` leads to `55000`; `54701` leads to `56000`.
- In option 2, overlapping safety buffers merge. If a proposed consecutive range touches one, that entire range moves to the next safe multiple of 1000. This can also move the first range above the requested starting ID. Distant buffers leave the space between them available.
- Custom IDs are restricted to **65534** or below for compatibility with native 16-bit fields. Overflowing plans are rejected.
- Existing vanilla duplicate/override IDs are preserved. The bundled reference includes vanilla's shared IDs `27956` and `28082`.
- Vanilla ID `251780` is a known outlier: it is excluded from the ordinary maximum calculation but remains reserved. The bundled reference's ordinary maximum is **28138**.

If a single record occupies a vanilla slot in its original filename, its ID is preserved conservatively even when the key was renamed. This cannot distinguish an intentional vanilla edit from an unrelated custom record reusing the slot. Ambiguous duplicate vanilla slots require manual review.

## Backups and recovery

The preview shows the full backup root. It is **outside game data**, normally `<mod-folder>/Localization-ID-Backups`, beside the containing `.mpq`/`data` folder. For an arbitrary standalone target, it is beside that folder. `--backup-folder` can select another safe root; destinations inside game data, selected save folders or the vanilla reference tree are rejected.

Options **1 and 2** back up and verify **all scanned JSON**, including skipped files, before replacing the first JSON:

```text
<backup-root>/<timestamp-and-unique-suffix>/
  originals/                Original scanned JSON
  old-to-new-ids.csv         Complete custom ID mapping
  preserved-vanilla.csv      Preserved entries/reasons
  preserved-item-runes.csv   Entries protected by option 2
  manifest.json             Hashes, ranges and operation status
```

Option **3** makes **one combined backup** and verifies **all JSON and all selected saves before any active edit**, plus the exact parsing inputs:

```text
<backup-root>/Compact-and-edit-saves-<timestamp-and-unique-suffix>/
  originals/localization/   All original scanned JSON, including skipped files
  originals/saves/          COMPLETE ORIGINAL .d2s/.d2i SAVES, including unchanged files
  mod-tables/               Exact five parsing tables, when saves exist
  modinfo.json              Detected metadata, when available
  old-to-new-ids.csv         Mapping generated by this same compaction
  workflow-report.json      Original/output hashes, save field changes and status
  RESTORE.txt               Exact destinations and joint restoration instructions
```

Inputs and the save-file list are rechecked before committing. Changes after preview invalidate the plan. JSON is installed first, then matching saves. Individual replacements are atomic on supported filesystems such as NTFS; the whole multi-file operation cannot be atomic. Write failure triggers restoration of committed files in **both groups**, with incomplete recovery reported. A crash/power loss can interrupt a batch; verified originals and the restore guide remain available.

**Undo options 1/2:** close the game/editors and restore the original JSON into the target folder. **Undo option 3: close the game/editors and restore BOTH original JSON and original saves to the destinations in `RESTORE.txt`. Restore both groups together so their IDs match.** Restoring saves also reverts progress made after the backup. Keep backups until characters, runewords and stash tabs have been checked in game.

Versions 1.0.0/1.0.1 placed backups inside strings, where a recursive game scan can try loading their manifests as localization. Verified legacy backups are relocated intact to `Migrated-legacy-*` outside game data when applying a plan. Unknown/incomplete folders require manual review. Preservation mode never reverses an earlier renumbering; recover matching files first.

Static HTML snapshots, binary tables and numeric references in other plugins/files are not automatically refreshed. Review these separately using the generated mapping.

## Command line

Option names, mode names and confirmations ignore letter case; paths and localization keys retain their spelling. Select a mode explicitly:

```powershell
# Preview standard compaction without writing files.
.\D2R-Localization-ID-Compactor.exe --mode 1 --target "<mod-strings>" --dry-run

# Preserve runewords; verified backups, then edit JSON only.
.\D2R-Localization-ID-Compactor.exe --mode "SKIP RUNEWORDS" --target "<mod-strings>" --apply --yes

# Preview JSON plus all saves, using detected paths and automatic mapping.
.\D2R-Localization-ID-Compactor.exe --mode 3 --mod-folder "<mod-folder>" --dry-run

# EDIT JSON AND SAVES, with combined backups and no confirmation prompt.
.\D2R-Localization-ID-Compactor.exe --mode "COMPACT AND EDIT SAVES" --mod-folder "<mod-folder>" --apply --yes
```

Names also accept `standard`, `skip-runewords`, `compact-and-edit-saves`, `save-only`, or `1`, `2`, `3`, `4`. Common overrides: `--target`, `--vanilla`, `--backup-folder`; `--start` applies only to modes 1–3. The default first custom ID is **30000** with the bundled reference, or above a newer reference's maximum. Modes 3 and 4 can override detection with `--save-folder` and `--mod-tables`; each processes that entire direct save folder. Mode 4 also accepts `--history-folder` and does not accept `--start`. No manual mapping or individual-save selection is accepted with `--mode`.

`--dry-run` previews only. `--apply --yes` explicitly authorizes the selected workflow, including save edits in modes 3 and 4. Legacy JSON-only automation using `--item-runes-mode preserve|compact` remains supported; it never edits saves and cannot be combined with `--mode`. Use `--help` for all options. Exit code `0` means completion, preview, cancellation or no changes needed; `1` means validation/write failure.

Save-only preview and application, with automatically detected current strings, saves, tables and history:

```powershell
.\D2R-Localization-ID-Compactor.exe --mode 4 --mod-folder "<mod-folder>" --dry-run

# EDIT SAVE FILES ONLY; verified backups, then overwrite supported saves.
.\D2R-Localization-ID-Compactor.exe --mode "EDIT SAVE FILES ONLY" --mod-folder "<mod-folder>" --apply --yes
```

If historical backups were stored elsewhere, add `--history-folder "<same-mod-backups>"`. No individual CSV selection is necessary. The original active JSON is never rewritten by mode 4.

## Advanced historical recovery — EDITS SAVE FILES

These separate command-line actions remain available for earlier migrations; they are **not additional main menu choices** and never compact JSON. They require the correct historical **`old-to-new-ids.csv`**: a text table identifying strings and their old/new numeric IDs. It must belong to the same mod and directly map the IDs in these saves to the mod's current IDs. A wrong mapping can assign the wrong runeword. Do not concatenate successive mappings. Ambiguous/overlapping mappings are rejected.

**Physical runeword items, optionally with Chronicle history:**

```powershell
.\D2R-Localization-ID-Compactor.exe --repair-items --mod-folder "<mod-folder>" --auto-saves --mapping "<historical-backup>/old-to-new-ids.csv" --include-chronicle --dry-run
.\D2R-Localization-ID-Compactor.exe --repair-items --mod-folder "<mod-folder>" --auto-saves --mapping "<historical-backup>/old-to-new-ids.csv" --include-chronicle --apply --yes
```

This **edits all direct `.d2s`/`.d2i` saves** in the detected profile. Alternatively, `--save-folder` selects a profile or repeated `--save-file` selects specific files; do not combine scopes. `--mod-tables` overrides detection. Only mapped physical runeword IDs, necessary character checksums, and (with `--include-chronicle`) Chronicle runeword history change. This advanced action **does not migrate drop-source IDs**. Unmapped IDs are reported and left unchanged. Observe the table/layout requirements above.

**Chronicle runeword history only, one shared-stash save:**

```powershell
.\D2R-Localization-ID-Compactor.exe --repair-chronicle --save-file "<shared-stash.d2i>" --mapping "<historical-backup>/old-to-new-ids.csv" --dry-run
.\D2R-Localization-ID-Compactor.exe --repair-chronicle --save-file "<shared-stash.d2i>" --mapping "<historical-backup>/old-to-new-ids.csv" --apply --yes
```

This **overwrites one `.d2i` save**, changing matching 32-bit runeword discovery IDs only. Physical items, characters, set/unique discoveries, sources and timestamps stay unchanged. No parsing tables are needed. `--auto-saves --mod-folder` is supported only if the profile has exactly one direct `.d2i` file.

Both advanced actions verify backups of **all selected saves**, mapping, tables if used, and detected metadata if used before editing any save. Backups are under `Item-repair-*` or `Chronicle-repair-*`, with `originals`, a report and `RESTORE.txt`. Restore saves with matching localization IDs to undo. The whole selected batch is validated first, and failed replacement triggers a rollback attempt. `--apply --yes` bypasses the final **Y/N** confirmation. Close the game/editors before applying.

## Build and test

Build with the **.NET 10 SDK** and Windows x64 runtime packs. `build.ps1` publishes an optimized self-contained EXE outside the source tree. Runtime version/notices are pinned. MIT-licensed D2SSharp source is vendored at a pinned revision; no upstream game-data tables or personal saves are distributed.

```powershell
.\build.ps1
python tests/test_compactor.py --exe "<built-executable>" --work "<tests>/json"
python tests/test_chronicle_repair.py --exe "<built-executable>" --work "<tests>/chronicle"
python tests/test_item_repair.py --exe "<built-executable>" --work "<tests>/items"
python tests/test_mod_paths.py --exe "<built-executable>" --work "<tests>/paths"
python tests/test_workflows.py --exe "<built-executable>" --fixtures "<tests>/items/artifacts/bin/Fixtures/release/Fixtures.exe" --work "<tests>/workflows"
python tests/test_save_only.py --exe "<built-executable>" --fixtures "<tests>/items/artifacts/bin/Fixtures/release/Fixtures.exe" --work "<tests>/save-only"
```

Python is needed only for development tests; fixtures are synthetic. See [README.txt](README.txt) and [CHANGELOG.txt](CHANGELOG.txt) for packaged documentation and history.

## License

[MIT](LICENSE). Copyright (c) 2026 MadMike. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for parser/runtime attribution. Run `--licenses` to display embedded notices even when only the EXE is copied.
