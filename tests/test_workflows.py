"""Three-menu workflow integration tests. Uses only synthetic mod/save fixtures."""
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
import uuid

from test_item_repair import checksum, patch_bits


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.root = ARGS.work / (self._testMethodName + '-' + uuid.uuid4().hex[:8])
        shutil.copytree(SEED, self.root)
        self.saves, self.tables = self.root / 'saves', self.root / 'tables'
        self.content = self.root / 'Any Mod/CompletelyDifferent.mpq'
        self.target = self.content / 'data/local/lng/strings'
        self.target.mkdir(parents=True)
        self.metadata = self.content / 'modinfo.json'
        self.metadata.write_text('{"name":"Arbitrary Name","savepath":"Nonexistent-Workflow-Unit-Tests/"}', encoding='utf-8')
        self.backups = self.root / 'external-backups'
        self.rows('item-modifiers.json', [(50002, 'Modifier')])
        self.rows('item-names.json', [(50001, 'Name')])
        self.rows('item-runes.json', [(53506, 'CustomWordA'), (53505, 'CustomWordB'), (20581, 'Renamed native slot')])
        self.rows('ui.json', [(50000, 'Source')])
        (self.target / 'layout.json').write_text('{"widget":"untouched"}', encoding='utf-8')
        (self.root / 'old-to-new-ids.csv').unlink()  # Combined mode must create its own mapping.
        self.snapshot()

    def rows(self, file, pairs):
        (self.target / file).write_text(json.dumps([{'id': i, 'Key': k, 'enUS': k} for i, k in pairs], indent=2), encoding='utf-8')

    def snapshot(self):
        self.original = {p: p.read_bytes() for folder in [self.target, self.saves] if folder.exists() for p in folder.iterdir() if p.is_file()}

    def run_tool(self, mode='3', *options, tables=True):
        command = [str(ARGS.exe), '--MoDe', mode, '--target', str(self.target), '--mod-folder', str(self.content),
                   '--backup-folder', str(self.backups)]
        if mode.lower() in ['3', 'compact and edit saves', 'compact-and-edit-saves']:
            command += ['--save-folder', str(self.saves)]
            if tables:
                command += ['--mod-tables', str(self.tables)]
        return subprocess.run(command + list(options), stdin=subprocess.DEVNULL, capture_output=True,
                              encoding='utf-8-sig', timeout=35)

    def inspect(self, path):
        result = subprocess.run([str(ARGS.fixtures), 'inspect', str(path), str(self.tables)],
                                capture_output=True, encoding='utf-8-sig', timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def fixture(self, command):
        result = subprocess.run([str(ARGS.fixtures), command, str(self.root)], capture_output=True, encoding='utf-8-sig', timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def unchanged(self, saves_only=False):
        for path, before in self.original.items():
            if not saves_only or path.parent == self.saves:
                self.assertEqual(path.read_bytes(), before, path.name)

    def report(self):
        path, = self.backups.glob('Compact-and-edit-saves-*/workflow-report.json')
        return path, json.loads(path.read_text(encoding='utf-8'))

    def id(self, file, index=0):
        return json.loads((self.target / file).read_text())[index]['id']

    def test_standard_compacts_all_without_editing_any_saves(self):
        result = self.run_tool('sTaNdArD cOmP', '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual([self.id(f) for f in ['item-modifiers.json', 'item-names.json', 'item-runes.json', 'ui.json']], [30000, 31000, 32000, 33000])
        self.assertEqual(self.id('item-runes.json', 2), 20581)
        self.unchanged(saves_only=True)
        self.assertFalse(list(self.backups.glob('Compact-and-edit-saves-*')))
        for path, before in self.original.items():
            if path.parent == self.target:
                self.assertTrue(any(p.read_bytes() == before for p in self.backups.rglob(path.name)), path.name)

    def test_skip_runewords_preserves_file_and_buffers_without_save_edits(self):
        self.rows('item-runes.json', [(30500, 'Near start'), (53506, 'CustomWordA'), (53505, 'CustomWordB')])
        self.snapshot()
        result = self.run_tool('SkIp RuNeWoRdS', '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.target / 'item-runes.json').read_bytes(), self.original[self.target / 'item-runes.json'])
        for file in ['item-modifiers.json', 'item-names.json', 'ui.json']:
            self.assertGreater(self.id(file), 31500)
            self.assertFalse(52505 <= self.id(file) <= 54506)
        self.unchanged(saves_only=True)

    def test_combined_preview_creates_no_files_or_backups(self):
        before = set(self.root.rglob('*'))
        result = self.run_tool('3', '--dry-run')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('no CSV selection is needed', result.stdout)
        self.assertEqual(set(self.root.rglob('*')), before)
        self.unchanged()

    def test_combined_migrates_all_locations_sources_and_history_with_backups(self):
        words = {p.name: self.inspect(p) for p in self.saves.iterdir()}
        result = self.run_tool('CoMpAcT aNd EdIt SaVeS', '--ApPlY', '--YeS')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report_path, report = self.report()
        self.assertEqual(report['status'], 'Complete')
        self.assertEqual(len(report['files']), len(self.original))
        kinds = {f['kind'] for s in report['saveChanges'] for f in s['fields']}
        self.assertEqual(kinds, {'item', 'item-source', 'chronicle', 'chronicle-source'})
        for entry in report['files']:
            path = Path(entry['path'])
            self.assertEqual((report_path.parent / 'originals' / entry['group'] / path.name).read_bytes(), self.original[path])
        mapping = {53506: 32000, 53505: 32001}
        for name, before_words in words.items():
            expected = bytearray(self.original[self.saves / name])
            changes = next(s['fields'] for s in report['saveChanges'] if Path(s['path']).name == name)
            for field in changes:
                patch_bits(expected, field['bitOffset'], field['width'], field['newId'])
            if name.endswith('.d2s') and changes:
                struct.pack_into('<I', expected, 12, checksum(expected))
            actual = (self.saves / name).read_bytes()
            self.assertEqual(actual, expected, name + ': unrelated bytes changed')
            for word in before_words:
                word['id'] = mapping.get(word['id'], word['id'])
                word['source'] = mapping.get(word['source'], word['source'])
            self.assertEqual(self.inspect(self.saves / name), before_words, 'Non-ID item properties changed')
        rows = list(csv.DictReader((report_path.parent / 'old-to-new-ids.csv').read_text(encoding='utf-8-sig').splitlines()))
        self.assertEqual({int(r['OldID']): int(r['NewID']) for r in rows}, {50002: 30000, 50001: 31000, **mapping, 50000: 33000})
        for table in self.tables.iterdir():
            self.assertEqual((report_path.parent / 'mod-tables' / table.name).read_bytes(), table.read_bytes())
        self.assertEqual((report_path.parent / 'modinfo.json').read_bytes(), self.metadata.read_bytes())
        self.assertIn('Restore BOTH groups', (report_path.parent / 'RESTORE.txt').read_text())
        self.assertFalse(list(self.root.rglob('.compact-save-*.tmp')))

    def test_broken_save_stops_json_and_save_edits_before_backups(self):
        (self.saves / 'BStash.d2i').write_bytes(b'invalid')
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.unchanged()
        self.assertFalse(self.backups.exists())

    def test_historical_stat_width_mismatch_stops_entire_workflow(self):
        self.fixture('add-legacy-stat-charm')
        table = self.tables / 'itemstatcost.txt'
        lines = table.read_text().splitlines()
        row = lines[399].split('\t'); row[4] = '7'; lines[399] = '\t'.join(row)
        table.write_text('\n'.join(lines) + '\n')
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('504', result.stderr)
        self.unchanged()
        self.assertFalse(self.backups.exists())

    def test_failed_save_write_rolls_back_json_and_earlier_save(self):
        file = self.saves / 'BStash.d2i'
        file.chmod(stat.S_IREAD)
        try:
            result = self.run_tool('3', '--apply', '--yes')
        finally:
            file.chmod(stat.S_IWRITE | stat.S_IREAD)
        self.assertNotEqual(result.returncode, 0)
        self.unchanged()
        report_path, report = self.report()
        self.assertEqual(report['status'], 'RolledBack')
        for entry in report['files']:
            self.assertEqual((report_path.parent / 'originals' / entry['group'] / Path(entry['path']).name).read_bytes(), self.original[Path(entry['path'])])

    def test_failed_json_write_rolls_back_json_and_leaves_saves_untouched(self):
        file = self.target / 'item-runes.json'
        file.chmod(stat.S_IREAD)
        try:
            result = self.run_tool('3', '--apply', '--yes')
        finally:
            file.chmod(stat.S_IWRITE | stat.S_IREAD)
        self.assertNotEqual(result.returncode, 0)
        self.unchanged()
        self.assertEqual(self.report()[1]['status'], 'RolledBack')

    def test_missing_save_profile_needs_no_tables_and_is_not_created(self):
        shutil.rmtree(self.saves)
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes', tables=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(self.saves.exists())
        self.assertEqual(self.id('item-runes.json'), 32000)
        self.assertEqual(self.report()[1]['status'], 'Complete')

    def test_no_custom_runewords_still_migrates_saved_localization_sources(self):
        self.fixture('native-only-input')
        self.rows('item-runes.json', [(20581, 'Native')])
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report = self.report()[1]
        fields = [f for s in report['saveChanges'] for f in s['fields']]
        self.assertTrue(fields)
        self.assertEqual({f['kind'] for f in fields}, {'item-source', 'chronicle-source'})
        self.assertEqual({f['newId'] for f in fields}, {32000})
        self.assertEqual({w['id'] for p in self.saves.iterdir() for w in self.inspect(p)}, {20581})

    def test_fresh_mapping_handles_overlapping_before_and_after_ids_once(self):
        self.fixture('swap-input-rune-ids')
        self.rows('item-runes.json', [(32001, 'CustomWordA'), (32000, 'CustomWordB'), (20581, 'Native')])
        before = self.inspect(self.saves / 'ACharacter.d2s')
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        for word in before:
            word['id'] = {32001: 32000, 32000: 32001}.get(word['id'], word['id'])
            word['source'] = {32001: 32000, 32000: 32001}.get(word['source'], word['source'])
        self.assertEqual(self.inspect(self.saves / 'ACharacter.d2s'), before)

    def test_preexisting_stale_runeword_is_rejected_without_guessing(self):
        self.rows('item-runes.json', [(60009, 'CustomWordA'), (60008, 'CustomWordB')])
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('pre-compaction runeword table', result.stderr.lower())
        self.unchanged()
        self.assertFalse(self.backups.exists())

    def test_duplicate_old_runeword_ids_are_rejected_before_changes(self):
        self.rows('item-runes.json', [(53506, 'CustomWordA'), (53506, 'CustomWordB')])
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('duplicate old IDs', result.stderr)
        self.unchanged()
        self.assertFalse(self.backups.exists())

    def test_ambiguous_saved_source_id_is_rejected_before_changes(self):
        self.rows('ui.json', [(53506, 'Unrelated source collision')])
        self.snapshot()
        result = self.run_tool('3', '--apply', '--yes')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Ambiguous saved localization source', result.stderr)
        self.unchanged()

    def test_cli_requires_valid_mode_and_explicit_write_authorization(self):
        for mode, flags in [('', ['--dry-run']), ('unknown', ['--dry-run']), ('1', []), ('2', ['--yes']), ('3', ['--apply']), ('3', ['--mapping', 'unused'])]:
            with self.subTest(mode=mode, flags=flags):
                result = self.run_tool(mode, *flags)
                self.assertNotEqual(result.returncode, 0)
                self.unchanged()
                self.assertFalse(self.backups.exists())

    def test_menu_blank_reprompts_and_has_exactly_four_modes(self):
        result = subprocess.run([str(HELPER), str(ASSEMBLY), 'menu'], input='\n\nSkIp RuNeWoRdS\n',
                                capture_output=True, encoding='utf-8-sig', timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('RESULT=SkipRunewords', result.stdout)
        self.assertEqual(result.stdout.count('Choose 1, 2, 3 or 4 (no default):'), 3)
        self.assertIn('1 / STANDARD COMP', result.stdout)
        self.assertIn('2 / SKIP RUNEWORDS', result.stdout)
        self.assertIn('3 / COMPACT AND EDIT SAVES', result.stdout)
        self.assertIn('4 / EDIT SAVE FILES ONLY', result.stdout)

    def test_final_confirmation_requires_explicit_case_insensitive_yes_or_no(self):
        cases = [('y\n', True, 1), ('Y\n', True, 1), ('yes\n', True, 1), (' YeS \n', True, 1),
                 ('n\n', False, 1), ('N\n', False, 1), (' nO \n', False, 1),
                 ('\n \nmaybe\nn\n', False, 4), ('APPLY\nn\n', False, 2), ('', False, 1), ('\n', False, 2)]
        for answer, expected, prompts in cases:
            with self.subTest(answer=answer):
                result = subprocess.run([str(HELPER), str(ASSEMBLY), 'confirm'], input=answer,
                                        capture_output=True, encoding='utf-8', timeout=20)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn('RESULT=' + str(expected), result.stdout)
                self.assertEqual(result.stdout.count('Continue? [Y/N] (no default):'), prompts)
                self.unchanged()

    def test_save_json_metadata_table_or_file_list_change_invalidates_combined_plan(self):
        for selected in [self.target / 'ui.json', self.saves / 'ACharacter.d2s', self.metadata,
                         self.tables / 'weapons.txt', self.saves / 'Added.d2s']:
            with self.subTest(selected=selected.name):
                existed = selected.exists()
                before = selected.read_bytes() if existed else b''
                result = subprocess.run([str(HELPER), str(ASSEMBLY), 'race', str(self.target), str(self.saves),
                                         str(self.tables), str(self.backups), str(self.metadata), str(selected)],
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
    parser.add_argument('--fixtures', type=Path, required=True, help='Fixture executable built by test_item_repair.py')
    parser.add_argument('--work', type=Path, required=True)
    ARGS = parser.parse_args()
    ARGS.exe, ARGS.fixtures = ARGS.exe.resolve(strict=True), ARGS.fixtures.resolve(strict=True)
    ARGS.work = ARGS.work.resolve(); ARGS.work.mkdir(parents=True, exist_ok=True)
    SEED = ARGS.work / 'synthetic-seed'
    subprocess.run([str(ARGS.fixtures), 'generate', str(SEED)], check=True, capture_output=True, timeout=20)
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
  var workflow=asm.GetType(ns+"Workflow");
  if(args[1]=="menu") { Console.WriteLine("RESULT="+workflow.GetMethod("Choose").Invoke(null,null)); return 0; }
  if(args[1]=="confirm") { Console.WriteLine("RESULT="+asm.GetType(ns+"Program").GetMethod("Confirm",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Confirm the displayed plan."})); return 0; }
  var reference=asm.GetType(ns+"Reference").GetMethod("Load").Invoke(null,new object[]{null});
  var mode=Enum.Parse(asm.GetType(ns+"ItemRunesMode"),"Compact");
  var localization=asm.GetType(ns+"Engine").GetMethod("Build").Invoke(null,new object[]{args[2],reference,30000,args[5],mode});
  var metadata=Activator.CreateInstance(asm.GetType(ns+"InputSnapshot"));
  metadata.GetType().GetField("Path").SetValue(metadata,args[6]); metadata.GetType().GetField("Bytes").SetValue(metadata,File.ReadAllBytes(args[6]));
  var plan=workflow.GetMethod("Build").Invoke(null,new object[]{localization,args[3],args[4],metadata});
  File.AppendAllText(args[7],"external change");
  try { workflow.GetMethod("Apply").Invoke(null,new[]{plan}); }
  catch(TargetInvocationException error) { if(error.InnerException is IOException) { Console.WriteLine(error.InnerException.Message); return 0; } throw; }
  return 2;
 }
}
''', encoding='utf-8')
    os.environ['DOTNET_CLI_HOME'] = str(ARGS.work / '.dotnet-home')
    os.environ['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    build = subprocess.run(['dotnet', 'build', str(helper / 'Probe.csproj'), '-c', 'Release', '-o', str(helper / 'output'),
                            '-p:UseSharedCompilation=false', '-nodeReuse:false'], capture_output=True, encoding='utf-8-sig', timeout=45)
    if build.returncode:
        raise RuntimeError(build.stdout + build.stderr)
    HELPER = helper / 'output/Probe.exe'
    unittest.main(argv=[sys.argv[0], '-v'])
