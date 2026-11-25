"""Runeword item repair tests; all saves and game tables are synthetic fixtures."""
from pathlib import Path
import argparse
import json
import os
import shutil
import stat
import struct
import subprocess
import sys
import unittest
import uuid


MAPPING = {53506: 34008, 53505: 34007}


def checksum(data):
    value = 0
    for at, byte in enumerate(data):
        value = (((value << 1) | (value >> 31)) + (0 if 12 <= at < 16 else byte)) & 0xffffffff
    return value


def patch_bits(data, bit, width, number):
    for i in range(width):
        at, shift = divmod(bit + i, 8)
        data[at] = (data[at] & ~(1 << shift)) | (((number >> i) & 1) << shift)


class ItemRepairTests(unittest.TestCase):
    def setUp(self):
        self.root = ARGS.work / (self._testMethodName + '-' + uuid.uuid4().hex[:8])
        shutil.copytree(SEED, self.root)
        self.saves, self.tables = self.root / 'saves', self.root / 'tables'
        self.mapping = self.root / 'old-to-new-ids.csv'
        self.backups = self.root / 'Localization-ID-Backups'
        self.original = {p.name: p.read_bytes() for p in self.saves.iterdir()}

    def run_tool(self, *options, files=None, exe=None):
        command = [str(exe or ARGS.exe), '--RePaIr-ItEmS', '--MaPpInG', str(self.mapping), '--MoD-TaBlEs', str(self.tables)]
        if files is None:
            command += ['--SaVe-FoLdEr', str(self.saves)]
        else:
            for file in files:
                command += ['--SaVe-FiLe', str(file)]
        return subprocess.run(command + list(options), stdin=subprocess.DEVNULL, capture_output=True,
                              encoding='utf-8-sig', errors='strict', timeout=35)

    def inspect(self, name):
        result = subprocess.run([str(FIXTURES), 'inspect', str(self.saves / name), str(self.tables)],
                                capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def reports(self):
        return list(self.backups.glob('Item-repair-*/repair-report.json'))

    def assert_unchanged(self):
        for name, before in self.original.items():
            self.assertEqual((self.saves / name).read_bytes(), before, name)

    def test_preview_and_case_insensitive_options_write_nothing(self):
        before = set(self.root.rglob('*'))
        result = self.run_tool('--InClUdE-ChRoNiClE', '--DrY-RuN')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('11 fields will change', result.stdout)
        for word in ['Equipped', 'Cube', 'Stash', 'Mercenary', 'Corpse', 'Iron golem', 'OnCursor', 'SAVE FILE']:
            self.assertIn(word, result.stdout)
        self.assertEqual(set(self.root.rglob('*')), before)
        self.assert_unchanged()

    def test_items_exact_bits_and_character_checksums_with_all_backups(self):
        words = {name: self.inspect(name) for name in self.original}
        self.assertTrue(any(w['bit'] % 8 for rows in words.values() for w in rows))
        result = self.run_tool('--ApPlY', '--YeS')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report_path, = self.reports()
        report = json.loads(report_path.read_text(encoding='utf-8'))
        self.assertEqual(report['status'], 'Complete')
        self.assertFalse(report['includeChronicle'])
        self.assertEqual(sum(len(s['changes']) for s in report['saves']), 10)
        for name, before in self.original.items():
            expected = bytearray(before)
            changed = False
            for word in words[name]:
                if word['id'] in MAPPING:
                    patch_bits(expected, word['bit'], 16, MAPPING[word['id']])
                    changed = True
            if name.endswith('.d2s') and changed:
                struct.pack_into('<I', expected, 12, checksum(expected))
            actual = (self.saves / name).read_bytes()
            self.assertEqual(actual, expected, name)
            self.assertEqual((report_path.parent / 'originals' / name).read_bytes(), before)
            after_words = self.inspect(name)
            for word in words[name]:
                word['id'] = MAPPING.get(word['id'], word['id'])
            self.assertEqual(after_words, words[name], 'Item stats/sockets/seeds/positions changed')
            if name.endswith('.d2s'):
                self.assertEqual(struct.unpack_from('<I', actual, 12)[0], checksum(actual))
        for table in self.tables.iterdir():
            self.assertEqual((report_path.parent / 'mod-tables' / table.name).read_bytes(), table.read_bytes())
        self.assertEqual((report_path.parent / 'old-to-new-ids.csv').read_bytes(), self.mapping.read_bytes())
        self.assertIn('ACharacter.d2s', (report_path.parent / 'RESTORE.txt').read_text(encoding='utf-8'))
        self.assertFalse(list(self.saves.glob('*.tmp')))

    def test_optional_chronicle_combines_with_items_and_is_idempotent(self):
        result = self.run_tool('--include-chronicle', '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report = json.loads(self.reports()[0].read_text(encoding='utf-8'))
        self.assertTrue(report['includeChronicle'])
        patches = [p for save in report['saves'] for p in save['changes']]
        self.assertEqual(sum(p['kind'] == 'item' for p in patches), 10)
        self.assertEqual(sum(p['kind'] == 'chronicle' for p in patches), 1)
        for save in report['saves']:
            name = Path(save['path']).name
            expected = bytearray(self.original[name])
            for patch in save['changes']:
                patch_bits(expected, patch['bitOffset'], patch['width'], patch['newId'])
            if save['checksumUpdated']:
                struct.pack_into('<I', expected, 12, checksum(expected))
            self.assertEqual((self.saves / name).read_bytes(), expected)
        before = {p.name: p.read_bytes() for p in self.saves.iterdir()}
        result = self.run_tool('--include-chronicle', '--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('No mapped IDs need repair', result.stdout)
        self.assertEqual(len(self.reports()), 1)
        self.assertEqual({p.name: p.read_bytes() for p in self.saves.iterdir()}, before)

    def test_one_file_and_repeated_explicit_file_selection(self):
        result = self.run_tool('--apply', '--yes', files=[self.saves / 'ACharacter.d2s'])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.saves / 'BStash.d2i').read_bytes(), self.original['BStash.d2i'])
        report_path, = self.reports()
        self.assertEqual(len(list((report_path.parent / 'originals').iterdir())), 1)
        result = self.run_tool('--apply', '--yes', files=[self.saves / 'BStash.d2i', self.saves / 'CUnchanged.d2s'])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(self.reports()), 2)

    def test_invalid_second_save_aborts_before_any_backup_or_edit(self):
        bad = self.saves / 'BStash.d2i'
        bad.write_bytes(bad.read_bytes()[:-1])
        broken = bad.read_bytes()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertEqual((self.saves / 'ACharacter.d2s').read_bytes(), self.original['ACharacter.d2s'])
        self.assertEqual(bad.read_bytes(), broken)
        self.assertFalse(self.backups.exists())

    def test_failed_second_replacement_rolls_back_first_and_backs_up_all(self):
        blocked = self.saves / 'BStash.d2i'
        blocked.chmod(stat.S_IREAD)
        try:
            result = self.run_tool('--apply', '--yes')
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assert_unchanged()
            report_path, = self.reports()
            self.assertEqual(json.loads(report_path.read_text(encoding='utf-8'))['status'], 'RolledBack')
            for name, before in self.original.items():
                self.assertEqual((report_path.parent / 'originals' / name).read_bytes(), before)
            self.assertFalse(list(self.saves.glob('*.tmp')))
        finally:
            blocked.chmod(stat.S_IREAD | stat.S_IWRITE)

    def test_bad_checksum_version_short_save_and_trailing_data_are_rejected(self):
        path = self.saves / 'ACharacter.d2s'
        cases = []
        bad = bytearray(self.original[path.name]); bad[-1] ^= 1; cases.append(bad)
        bad = bytearray(self.original[path.name]); struct.pack_into('<I', bad, 4, 104); cases.append(bad)
        cases.append(self.original[path.name][:403])
        bad = bytearray(self.original[path.name] + b'unknown tail')
        struct.pack_into('<I', bad, 8, len(bad)); struct.pack_into('<I', bad, 12, checksum(bad)); cases.append(bad)
        for data in cases:
            with self.subTest(length=len(data)):
                path.write_bytes(data)
                result = self.run_tool('--apply', '--yes')
                self.assertEqual(result.returncode, 1, result.stdout)
                self.assertEqual(path.read_bytes(), data)
                self.assertFalse(self.backups.exists())

    def test_missing_malformed_or_wrong_mod_tables_abort(self):
        target = self.tables / 'weapons.txt'
        original = target.read_bytes()
        for replacement in [b'code\ttype\n', original.replace(b'swd', b'bad'), original.replace(b'compactsave', b'type')]:
            with self.subTest(replacement=replacement):
                target.write_bytes(replacement)
                result = self.run_tool('--apply', '--yes')
                self.assertEqual(result.returncode, 1, result.stdout)
                self.assert_unchanged()
                self.assertFalse(self.backups.exists())
        target.unlink()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assert_unchanged()

    def test_historical_stat_width_mismatch_aborts_batch_without_guessing(self):
        fixture = subprocess.run([str(FIXTURES), 'add-legacy-stat-charm', str(self.root)],
                                 capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(fixture.returncode, 0, fixture.stdout + fixture.stderr)
        self.original = {p.name: p.read_bytes() for p in self.saves.iterdir()}
        stat_table = self.tables / 'itemstatcost.txt'
        matching = stat_table.read_bytes()
        rows = stat_table.read_text(encoding='utf-8-sig').splitlines()
        fields = rows[399].split('\t')
        self.assertEqual(fields[4], '10')
        fields[4] = '7'
        rows[399] = '\t'.join(fields)
        stat_table.write_text('\n'.join(rows) + '\n', encoding='utf-8')
        mismatched = stat_table.read_bytes()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1, result.stdout)
        for message in ['stat ID 504', 'Previous stat ID 398', '7 value bits',
                        'different stat layouts', 'No IDs or bit widths were guessed', 'No saves were edited']:
            self.assertIn(message, result.stderr)
        self.assert_unchanged()
        self.assertEqual(stat_table.read_bytes(), mismatched)
        self.assertFalse(self.backups.exists())
        self.assertFalse(list(self.saves.glob('*.tmp')))
        # With the explicit matching historical schema the very same save can
        # be parsed and migrated. No special-case stat IDs exist in production.
        stat_table.write_bytes(matching)
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report_path, = self.reports()
        report = json.loads(report_path.read_text(encoding='utf-8'))
        self.assertEqual(report['status'], 'Complete')
        self.assertEqual(sum(len(s['changes']) for s in report['saves']), 10)
        for save in report['saves']:
            name = Path(save['path']).name
            expected = bytearray(self.original[name])
            for patch in save['changes']:
                self.assertEqual(patch['kind'], 'item')
                self.assertEqual(patch['width'], 16)
                patch_bits(expected, patch['bitOffset'], patch['width'], patch['newId'])
            if save['checksumUpdated']:
                struct.pack_into('<I', expected, 12, checksum(expected))
            self.assertEqual((self.saves / name).read_bytes(), expected)
            self.assertEqual((report_path.parent / 'originals' / name).read_bytes(), self.original[name])

    def test_no_recursive_scan_and_other_user_files_are_untouched(self):
        sub = self.saves / 'other-save-folder'; sub.mkdir(); (sub / 'Bad.d2s').write_bytes(b'Not selected')
        other = self.saves / 'Character.d2rl'; other.write_bytes(b'Plugin companion data')
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(other.read_bytes(), b'Plugin companion data')
        self.assertEqual((sub / 'Bad.d2s').read_bytes(), b'Not selected')

    def test_exe_can_be_copied_alone_for_item_repair(self):
        standalone = self.root / 'portable'; standalone.mkdir()
        exe = standalone / 'Renamed Utility.exe'; shutil.copyfile(ARGS.exe, exe)
        result = self.run_tool('--apply', '--yes', exe=exe)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(list(standalone.iterdir()), [exe])

    def test_cli_rejects_ambiguous_scope_and_requires_explicit_write_flags(self):
        for extra in [[], ['--apply'], ['--yes'], ['--dry-run', '--apply', '--yes'],
                      ['--repair-chronicle', '--dry-run'], ['--target', str(self.saves), '--dry-run'],
                      ['--save-file', str(self.saves / 'ACharacter.d2s'), '--dry-run']]:
            with self.subTest(extra=extra):
                result = self.run_tool(*extra)
                self.assertEqual(result.returncode, 1, result.stdout)
                self.assert_unchanged()
                self.assertFalse(self.backups.exists())
        result = self.run_tool('--dry-run', files=[self.saves / 'ACharacter.d2s'] * 2)
        self.assertEqual(result.returncode, 1, result.stdout)

    def test_changes_after_preview_to_saves_mapping_tables_or_file_list_abort(self):
        helper = self.root / 'concurrent-helper'; helper.mkdir()
        (helper / 'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                                            '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', encoding='utf-8')
        (helper / 'Program.cs').write_text(r'''
using System;
using System.IO;
using System.Reflection;
class Check {
 static int Main(string[] a) {
  Type type=Assembly.LoadFrom(a[0]).GetType("D2RLocalizationCompactor.ItemRepair",true);
  object plan=type.GetMethod("Build").Invoke(null,new object[]{new string[0],a[1],a[2],a[3],a[4],false});
  File.AppendAllText(a[5],"external edit");
  try { type.GetMethod("Apply").Invoke(null,new[]{plan}); }
  catch(TargetInvocationException e) {
   if(e.InnerException is IOException && e.InnerException.Message.Contains("changed after preview")) return 0;
   Console.Error.WriteLine(e.InnerException); return 2;
  }
  return 1;
 }
}
''', encoding='utf-8')
        out = helper / 'output'
        build = subprocess.run(['dotnet', 'build', str(helper / 'Check.csproj'), '-c', 'Release', '-o', str(out),
                                '-p:UseSharedCompilation=false', '-nodeReuse:false'], capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
        assembly = ARGS.exe.parent.parent / 'artifacts/bin/D2RLocalizationCompactor/release_win-x64/D2R-Localization-ID-Compactor.dll'
        for selected in [self.saves / 'ACharacter.d2s', self.mapping, self.tables / 'weapons.txt', self.saves / 'Added.d2s']:
            with self.subTest(selected=selected.name):
                existed = selected.exists()
                before = selected.read_bytes() if existed else b''
                result = subprocess.run([str(out / 'Check.exe'), str(assembly), str(self.saves), str(self.mapping),
                                         str(self.tables), str(self.backups), str(selected)],
                                        capture_output=True, encoding='utf-8-sig', timeout=30)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(selected.read_bytes(), before + b'external edit')
                self.assertFalse(self.backups.exists())
                if existed:
                    selected.write_bytes(before)
                else:
                    selected.unlink()
        self.assert_unchanged()

    def test_licenses_remain_embedded_when_only_exe_is_copied(self):
        result = subprocess.run([str(ARGS.exe), '--LiCeNsEs'], capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('MadMike', result.stdout)
        self.assertIn('ResurrectedTrader', result.stdout)
        self.assertIn('.NET Foundation', result.stdout)
        self.assertGreater(len(result.stdout), 80000)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    ARGS = parser.parse_args()
    ARGS.exe = ARGS.exe.resolve(strict=True)
    ARGS.work = ARGS.work.resolve()
    ARGS.work.mkdir(parents=True, exist_ok=True)
    os.environ['DOTNET_CLI_HOME'] = str(ARGS.work / '.dotnet-home')
    os.environ['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    helper = ARGS.work / 'fixture-tool'; helper.mkdir(exist_ok=True)
    source = Path(__file__).resolve().parent
    project = helper / 'Fixtures.csproj'
    from xml.sax.saxutils import escape
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
                       f'<Compile Include="{escape(str(source / "ItemFixtures.cs"))}" />'
                       f'<ProjectReference Include="{escape(str(source.parent / "ThirdParty/D2SSharp/D2SSharp.csproj"))}" />'
                       '</ItemGroup></Project>', encoding='utf-8')
    build = subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--artifacts-path', str(ARGS.work / 'artifacts'),
                            '-p:UseSharedCompilation=false', '-nodeReuse:false'], capture_output=True, encoding='utf-8-sig', timeout=60)
    if build.returncode:
        raise RuntimeError(build.stdout + build.stderr)
    FIXTURES = ARGS.work / 'artifacts/bin/Fixtures/release/Fixtures.exe'
    SEED = ARGS.work / 'synthetic-seed'
    generate = subprocess.run([str(FIXTURES), 'generate', str(SEED)], capture_output=True, encoding='utf-8-sig', timeout=30)
    if generate.returncode:
        raise RuntimeError(generate.stdout + generate.stderr)
    unittest.main(argv=[sys.argv[0], '-v'])
