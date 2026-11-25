"""Save-only option tests; all character/stash data is synthetic."""
from pathlib import Path
import argparse
import csv
import json
import os
import shutil
import stat
import struct
import subprocess
import sys
import unittest
import test_workflows as workflows
from test_item_repair import checksum, patch_bits


class SaveOnlyTests(unittest.TestCase):
    rows = workflows.WorkflowTests.rows
    snapshot = workflows.WorkflowTests.snapshot
    unchanged = workflows.WorkflowTests.unchanged
    inspect = workflows.WorkflowTests.inspect
    fixture = workflows.WorkflowTests.fixture

    def setUp(self):
        workflows.WorkflowTests.setUp(self)
        self.history = self.content.parent / 'Localization-ID-Backups'
        self.rows('item-runes.json', [(60009, 'CustomWordA'), (60008, 'CustomWordB'), (20581, 'Renamed native slot')])
        self.mapping(self.history / 'first/old-to-new-ids.csv', [(53506, 34008, 'CustomWordA'), (53505, 34007, 'CustomWordB')])
        self.mapping(self.history / 'second/old-to-new-ids.csv', [(34008, 60009, 'CustomWordA'), (34007, 60008, 'CustomWordB')])
        self.snapshot()

    def mapping(self, path, rows):
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('w', encoding='utf-8', newline='') as stream:
            writer = csv.writer(stream); writer.writerow(['File', 'EntryIndex', 'Key', 'OldID', 'NewID'])
            for old, new, key in rows:
                writer.writerow(['item-runes.json', 0, key, old, new])

    def run_tool(self, *options, mode='4', tables=True):
        command = [str(ARGS.exe), '--MoDe', mode, '--target', str(self.target), '--mod-folder', str(self.content),
                   '--save-folder', str(self.saves), '--backup-folder', str(self.backups)]
        if tables:
            command += ['--mod-tables', str(self.tables)]
        return subprocess.run(command + list(options), stdin=subprocess.DEVNULL, capture_output=True, encoding='utf-8-sig', timeout=35)

    def report(self):
        path, = self.backups.glob('Save-only-*/repair-report.json')
        return path, json.loads(path.read_text(encoding='utf-8'))

    def json_unchanged(self):
        for path, before in self.original.items():
            if path.parent == self.target:
                self.assertEqual(path.read_bytes(), before, path.name)

    def test_preview_detects_history_automatically_and_writes_nothing(self):
        before = set(self.root.rglob('*'))
        result = self.run_tool('--dry-run', mode='eDiT sAvE fIlEs OnLy')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('2 historical inputs', result.stdout)
        self.assertIn('11 fields will change', result.stdout)
        self.assertIn('Active JSON stays byte-for-byte unchanged', result.stdout)
        self.assertEqual(set(self.root.rglob('*')), before)
        self.unchanged()

    def test_all_saves_items_and_history_repaired_to_current_json_with_verified_backups(self):
        words = {p.name: self.inspect(p) for p in self.saves.iterdir()}
        result = self.run_tool('--ApPlY', '--YeS')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        path, report = self.report()
        self.assertEqual(report['action'], 'EDIT SAVE FILES ONLY')
        self.assertEqual(report['status'], 'Complete')
        self.assertTrue(report['includeChronicle'])
        self.assertEqual(sum(len(s['changes']) for s in report['saves']), 11)
        self.assertEqual({f['kind'] for s in report['saves'] for f in s['changes']}, {'item', 'chronicle'})
        mapping = {53506: 60009, 53505: 60008}
        for save in report['saves']:
            file = Path(save['path']); before = self.original[file]; expected = bytearray(before)
            self.assertEqual((path.parent / 'originals' / file.name).read_bytes(), before)
            for field in save['changes']:
                patch_bits(expected, field['bitOffset'], field['width'], field['newId'])
            if file.suffix == '.d2s' and save['changes']:
                struct.pack_into('<I', expected, 12, checksum(expected))
            self.assertEqual(file.read_bytes(), expected, 'Unrelated save bytes changed')
            for word in words[file.name]:
                word['id'] = mapping.get(word['id'], word['id'])
            self.assertEqual(self.inspect(file), words[file.name], 'Stats, sources or other item data changed')
        self.json_unchanged()
        self.assertEqual(len(report['context']), 3)
        for entry in report['context']:
            self.assertEqual((path.parent / 'context' / entry['backupName']).read_bytes(), Path(entry['path']).read_bytes())
        for table in self.tables.iterdir():
            self.assertEqual((path.parent / 'mod-tables' / table.name).read_bytes(), table.read_bytes())
        self.assertFalse(list(self.saves.glob('*.tmp')))

    def test_mixed_old_and_current_characters_and_second_apply_are_idempotent(self):
        first = self.run_tool('--apply', '--yes')
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        repaired = {p: p.read_bytes() for p in self.saves.iterdir()}
        (self.saves / 'OldCharacter.d2s').write_bytes(self.original[self.saves / 'ACharacter.d2s'])
        second = self.run_tool('--apply', '--yes')
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        for p, before in repaired.items():
            self.assertEqual(p.read_bytes(), before)
        self.assertEqual((self.saves / 'OldCharacter.d2s').read_bytes(), repaired[self.saves / 'ACharacter.d2s'])
        before = set(self.backups.rglob('*'))
        third = self.run_tool('--apply', '--yes')
        self.assertEqual(third.returncode, 0, third.stdout + third.stderr)
        self.assertIn('No mapped IDs need repair', third.stdout)
        self.assertEqual(set(self.backups.rglob('*')), before)
        self.json_unchanged()

    def test_backup_root_can_also_be_history_without_self_invalidating_the_plan(self):
        self.backups = self.history
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        second = self.run_tool('--apply', '--yes')
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        self.assertEqual(len(list(self.history.glob('Save-only-*'))), 1)
        self.json_unchanged()

    def test_original_json_backups_identify_old_ids_without_any_csv(self):
        shutil.rmtree(self.history)
        old = self.history / 'originals/item-runes.json'; old.parent.mkdir(parents=True)
        old.write_text(json.dumps([{'id': 53506, 'Key': 'CustomWordA', 'enUS': 'A'}, {'id': 53505, 'Key': 'CustomWordB', 'enUS': 'B'}]))
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.report()[1]['status'], 'Complete')
        self.json_unchanged()

    def test_missing_history_cannot_identify_stale_numeric_ids_and_edits_nothing(self):
        shutil.rmtree(self.history)
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('numeric IDs, not string keys', result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_current_native_ids_need_no_history_and_no_rewrite(self):
        self.fixture('native-only-input')
        self.rows('item-runes.json', [(20581, 'Native renamed slot')])
        shutil.rmtree(self.history); self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_reused_current_id_is_ambiguous_in_mixed_old_and_new_saves(self):
        self.rows('item-runes.json', [(60009, 'CustomWordA'), (60008, 'CustomWordB'), (53506, 'Different word')])
        self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('conflicting or removed identities', result.stderr)
        self.assertIn('53506', result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_conflicting_history_alias_is_rejected_when_used(self):
        self.mapping(self.history / 'conflicting/old-to-new-ids.csv', [(53506, 60008, 'CustomWordB')])
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('conflicting or removed identities', result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_removed_runeword_key_is_not_guessed(self):
        self.rows('item-runes.json', [(60009, 'CustomWordA')]); self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('CustomWordB (key absent from current file)', result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_invalid_unrelated_json_does_not_block_save_only_mode(self):
        (self.target / 'ui.json').write_bytes(b'invalid unrelated JSON'); self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.json_unchanged()

    def test_duplicate_current_ids_or_keys_stop_before_saves_are_touched(self):
        for pairs in [[(60009, 'CustomWordA'), (60009, 'CustomWordB')], [(60009, 'CustomWordA'), (60008, 'CustomWordA')]]:
            self.rows('item-runes.json', pairs); self.snapshot()
            result = self.run_tool('--apply', '--yes')
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('duplicate IDs or keys', result.stderr)
            self.unchanged(); self.assertFalse(self.backups.exists())

    def test_bad_second_save_stops_before_any_edits_or_backup_creation(self):
        (self.saves / 'BStash.d2i').write_bytes(b'invalid'); self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_failed_second_save_write_restores_first_and_has_all_originals(self):
        file = self.saves / 'BStash.d2i'; file.chmod(stat.S_IREAD)
        try:
            result = self.run_tool('--apply', '--yes')
        finally:
            file.chmod(stat.S_IREAD | stat.S_IWRITE)
        self.assertNotEqual(result.returncode, 0)
        self.unchanged()
        path, report = self.report(); self.assertEqual(report['status'], 'RolledBack')
        for save in report['saves']:
            file = Path(save['path']); self.assertEqual((path.parent / 'originals' / file.name).read_bytes(), self.original[file])

    def test_manual_history_override_uses_current_ids_not_csv_destinations(self):
        custom = self.root / 'custom-history'; self.history.rename(custom)
        result = self.run_tool('--history-folder', str(custom), '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual({f['newId'] for s in self.report()[1]['saves'] for f in s['changes']}, {60009, 60008})
        self.json_unchanged()

    def test_empty_profile_needs_no_tables_and_is_not_created(self):
        shutil.rmtree(self.saves)
        result = self.run_tool('--apply', '--yes', tables=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(self.saves.exists()); self.assertFalse(self.backups.exists()); self.json_unchanged()

    def test_malformed_history_csv_aborts_before_save_edit(self):
        (self.history / 'first/old-to-new-ids.csv').write_bytes(b'File,Key,OldID,NewID\nitem-runes.json,"unterminated,53506,60009')
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Unterminated quoted CSV', result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_quoted_unicode_and_export_escaped_keys_match_without_changing_json(self):
        key = '=Word,"Å"\nSecond line'
        self.rows('item-runes.json', [(60009, key), (60008, 'CustomWordB'), (20581, 'Native renamed slot')])
        shutil.rmtree(self.history)
        self.mapping(self.history / 'original/old-to-new-ids.csv', [(53506, 34008, "'" + key), (53505, 34007, 'CustomWordB')])
        self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual({f['newId'] for s in self.report()[1]['saves'] for f in s['changes']}, {60008, 60009})
        self.json_unchanged()

    def test_csv_escape_collision_is_ambiguous_and_json_keys_are_literal(self):
        self.rows('item-runes.json', [(60009, '=Word'), (60008, "'=Word"), (20581, 'Native')])
        shutil.rmtree(self.history)
        self.mapping(self.history / 'original/old-to-new-ids.csv', [(53506, 34008, "'=Word"), (53505, 34007, "'=Word")])
        self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('conflicting or removed identities', result.stderr)
        self.unchanged(); self.assertFalse(self.backups.exists())

    def test_cli_rejects_ignored_or_conflicting_options_and_implicit_writes(self):
        for flags in [[], ['--apply'], ['--yes'], ['--apply', '--yes', '--start', '30000'], ['--mapping', 'unused'], ['--save-file', 'unused'], ['--history-folder', str(self.root / 'absent')]]:
            with self.subTest(flags=flags):
                result = self.run_tool(*flags)
                self.assertNotEqual(result.returncode, 0)
                self.unchanged(); self.assertFalse(self.backups.exists())

    def test_current_json_history_table_or_save_changes_after_preview_abort(self):
        selected_files = [self.target / 'item-runes.json', self.history / 'first/old-to-new-ids.csv',
                          self.history / 'added/old-to-new-ids.csv', self.tables / 'weapons.txt', self.metadata,
                          self.saves / 'ACharacter.d2s', self.saves / 'Added.d2s']
        for selected in selected_files:
            with self.subTest(selected=selected.name):
                existed = selected.exists(); before = selected.read_bytes() if existed else b''
                result = subprocess.run([str(HELPER), str(ASSEMBLY), str(self.target), str(self.saves), str(self.tables),
                                         str(self.backups), str(self.history), str(self.metadata), str(selected)],
                                        capture_output=True, encoding='utf-8-sig', timeout=25)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(selected.read_bytes(), before + b'external change')
                self.assertFalse(self.backups.exists())
                if existed:
                    selected.write_bytes(before)
                else:
                    selected.unlink()
                self.unchanged()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--fixtures', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    ARGS = parser.parse_args()
    ARGS.exe = ARGS.exe.resolve(strict=True); ARGS.fixtures = ARGS.fixtures.resolve(strict=True)
    ARGS.work = ARGS.work.resolve(); ARGS.work.mkdir(parents=True, exist_ok=True)
    workflows.ARGS = ARGS; workflows.SEED = ARGS.work / 'synthetic-seed'
    subprocess.run([str(ARGS.fixtures), 'generate', str(workflows.SEED)], check=True, capture_output=True, timeout=20)
    ASSEMBLY = ARGS.exe.parent.parent / 'artifacts/bin/D2RLocalizationCompactor/release_win-x64/D2R-Localization-ID-Compactor.dll'
    helper = ARGS.work / 'probe'; helper.mkdir(exist_ok=True)
    (helper / 'Probe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    (helper / 'Program.cs').write_text(r'''
using System;
using System.IO;
using System.Reflection;
class Check {
 static int Main(string[] args) {
  var asm=Assembly.LoadFrom(args[0]); string ns="D2RLocalizationCompactor.";
  var reference=asm.GetType(ns+"Reference").GetMethod("Load").Invoke(null,new object[]{null});
  var metadata=Activator.CreateInstance(asm.GetType(ns+"InputSnapshot"));
  metadata.GetType().GetField("Path").SetValue(metadata,args[6]); metadata.GetType().GetField("Bytes").SetValue(metadata,File.ReadAllBytes(args[6]));
  var engine=asm.GetType(ns+"SaveOnly");
  var plan=engine.GetMethod("Build").Invoke(null,new object[]{args[1],args[2],args[3],args[4],args[5],metadata,reference});
  Directory.CreateDirectory(Path.GetDirectoryName(args[7])); File.AppendAllText(args[7],"external change");
  try { engine.GetMethod("Apply").Invoke(null,new[]{plan}); }
  catch(TargetInvocationException error) { if(error.InnerException is IOException) { Console.WriteLine(error.InnerException.Message); return 0; } throw; }
  return 2;
 }
}
''', encoding='utf-8')
    os.environ['DOTNET_CLI_HOME'] = str(ARGS.work / '.dotnet-home')
    os.environ['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'; os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    build = subprocess.run(['dotnet', 'build', str(helper / 'Probe.csproj'), '-c', 'Release', '-o', str(helper / 'output'),
                            '-p:UseSharedCompilation=false', '-nodeReuse:false'], capture_output=True, encoding='utf-8-sig', timeout=45)
    if build.returncode:
        raise RuntimeError(build.stdout + build.stderr)
    HELPER = helper / 'output/Probe.exe'
    unittest.main(argv=[sys.argv[0], '-v'])
