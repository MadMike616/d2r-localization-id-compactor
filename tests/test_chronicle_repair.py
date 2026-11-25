"""Integration tests for explicit, backed-up Chronicle SAVE FILE edits.

All fixtures are created under --work. No installed game or user saves are used.
"""
from pathlib import Path
import argparse
import codecs
import csv
import hashlib
import io
import json
import os
import stat
import struct
import subprocess
import sys
import unittest
import uuid


def page(kind, payload, season=0):
    header = bytearray(range(64))
    struct.pack_into('<IIIIHHB', header, 0, 0xAA55AA55, 2, 105, 12345,
                     64 + len(payload), season, kind)
    return bytes(header) + payload


def chronicle(runes, sets=(53506,), uniques=(53505,)):
    payload = struct.pack('<IHHHH', 0xC0EAEDC0, 1, len(sets), len(uniques), len(runes))
    payload += b'RESERVED'
    # IDs and source fields deliberately repeat mapped numbers outside RW ItemId.
    payload += b''.join(struct.pack('<IHI', number, 53506, 53505)
                        for number in (*sets, *uniques, *runes))
    payload += (struct.pack('<I', 53506) * 16)  # opaque preserved padding
    return page(2, payload)


class ChronicleRepairTests(unittest.TestCase):
    def setUp(self):
        self.root = ARGS.work / (self._testMethodName + '-' + uuid.uuid4().hex[:8])
        self.folder = self.root / 'saves & Unicode 測試'
        self.folder.mkdir(parents=True)
        self.save = self.folder / 'ArbitraryModSharedStash.D2I'
        self.mapping = self.root / 'old-to-new-ids.csv'
        self.backups = self.root / 'Localization-ID-Backups'
        self.item_page = page(0, b'JM\x01\x00Opaque item bytes:' + struct.pack('<I', 53506), 4)
        self.advanced_page = page(1, b'JM\x00\x00' + struct.pack('<I', 53505), 7)
        self.original = self.item_page + self.advanced_page + chronicle((53506, 20581, 53505, 53562, 50000))
        self.save.write_bytes(self.original)
        self.write_mapping()

    def write_mapping(self, rows=None, header=None):
        header = header or ['File', 'EntryIndex', 'Key', 'OldID', 'NewID']
        rows = rows if rows is not None else [
            ['ui.json', 0, 'UnrelatedUI', 53506, 36000],
            ['item-runes.json', 0, 'RuneA', 53506, 34008],
            ['ITEM-RUNES.JSON', 1, 'RuneB', 53505, 34007],
            ['item-runes.json', 2, 'RuneC', 53562, 34054],
            ['item-runes.json', 3, 'AlreadyUnchanged', 50000, 50000],
        ]
        text = io.StringIO(newline='')
        writer = csv.writer(text)
        writer.writerow(header)
        writer.writerows(rows)
        self.mapping.write_text(text.getvalue(), encoding='utf-8-sig', newline='')

    def run_tool(self, *options):
        command = [str(ARGS.exe), '--RePaIr-ChRoNiClE', '--SaVe-FiLe', str(self.save),
                   '--MaPpInG', str(self.mapping)] + list(options)
        return subprocess.run(command, stdin=subprocess.DEVNULL, capture_output=True,
                              encoding='utf-8-sig', errors='strict', timeout=30)

    def reports(self):
        return list(self.backups.glob('Chronicle-repair-*/repair-report.json'))

    def assert_no_edit(self, result, message=None):
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        if message:
            self.assertIn(message.lower(), result.stderr.lower())
        self.assertEqual(self.save.read_bytes(), self.original)
        self.assertFalse(self.backups.exists())

    def test_preview_has_no_writes_and_explicit_save_warning(self):
        before = set(self.root.rglob('*'))
        result = self.run_tool('--DrY-RuN')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('THIS OPTION EDITS A SAVE FILE', result.stdout)
        self.assertIn(str(self.save), result.stdout)
        self.assertIn('3 runeword history IDs will change', result.stdout)
        self.assertIn('20581', result.stdout)
        self.assertIn('Unmapped IDs may be valid vanilla IDs', result.stdout)
        self.assertEqual(self.save.read_bytes(), self.original)
        self.assertEqual(set(self.root.rglob('*')), before)

    def test_apply_backup_and_only_runeword_id_fields_change(self):
        other = self.folder / 'Character.d2s'
        other.write_bytes(b'Leave character save untouched')
        mapping_bytes = self.mapping.read_bytes()
        result = self.run_tool('--ApPlY', '--YeS')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        repaired = self.save.read_bytes()
        expected = bytearray(self.original)
        base = len(self.item_page) + len(self.advanced_page) + 64 + 20 + 2 * 10
        for index, target in [(0, 34008), (2, 34007), (3, 34054)]:
            struct.pack_into('<I', expected, base + index * 10, target)
        self.assertEqual(repaired, expected)
        self.assertEqual(other.read_bytes(), b'Leave character save untouched')
        report_path, = self.reports()
        backup = report_path.parent
        self.assertEqual((backup / 'originals' / self.save.name).read_bytes(), self.original)
        self.assertEqual((backup / 'old-to-new-ids.csv').read_bytes(), mapping_bytes)
        report = json.loads(report_path.read_text(encoding='utf-8'))
        self.assertEqual(report['status'], 'Complete')
        self.assertEqual(report['version'], '1.6.1')
        self.assertEqual(report['pages'], 3)
        self.assertEqual(report['runewordDiscoveries'], 5)
        self.assertEqual(report['unchangedMapped'], 1)
        self.assertEqual(report['unmappedIdsLeftUnchanged'], [20581])
        self.assertEqual(report['originalSha256'], hashlib.sha256(self.original).hexdigest())
        self.assertEqual(report['repairedSha256'], hashlib.sha256(repaired).hexdigest())
        self.assertEqual(report['mappingSha256'], hashlib.sha256(mapping_bytes).hexdigest())
        self.assertEqual([p['offset'] for p in report['changes']], [base, base + 20, base + 30])
        self.assertIn(str(self.save), (backup / 'RESTORE.txt').read_text(encoding='utf-8'))
        self.assertFalse(list(self.folder.glob('*.tmp')))

    def test_second_apply_is_noop_without_another_backup(self):
        first = self.run_tool('--apply', '--yes')
        self.assertEqual(first.returncode, 0, first.stderr)
        repaired = self.save.read_bytes()
        files = set(self.root.rglob('*'))
        second = self.run_tool('--apply', '--yes')
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertIn('3 already match mapping destinations', second.stdout)
        self.assertIn('save was not edited', second.stdout)
        self.assertEqual(self.save.read_bytes(), repaired)
        self.assertEqual(set(self.root.rglob('*')), files)

    def test_earlier_csv_headers_and_quoted_multiline_unicode_key(self):
        self.write_mapping([['item-runes.json', 0, 'Rune, "quoted"\r\n測試', 53506, 34008]],
                           ['file', 'entry_index', 'key', 'old_id', 'new_id'])
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(self.reports()[0].read_text(encoding='utf-8'))
        self.assertEqual(report['changes'][0]['key'], 'Rune, "quoted"\r\n測試')
        self.assertEqual(len(report['changes']), 1)

    def test_csv_utf16_both_byte_orders(self):
        original_csv = self.mapping.read_text(encoding='utf-8-sig')
        for encoding, bom in [('utf-16-le', codecs.BOM_UTF16_LE), ('utf-16-be', codecs.BOM_UTF16_BE)]:
            with self.subTest(encoding=encoding):
                self.mapping.write_bytes(bom + original_csv.encode(encoding))
                result = self.run_tool('--dry-run')
                self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.save.read_bytes(), self.original)
        self.assertFalse(self.backups.exists())

    def test_invalid_or_ambiguous_mappings_reject_before_writes(self):
        samples = [
            ([['item-runes.json', 0, 'A', 53506, 34000], ['item-runes.json', 1, 'B', 53506, 34001]], 'Ambiguous'),
            ([['item-runes.json', 0, 'A', 53506, 34000], ['item-runes.json', 1, 'B', 53505, 34000]], 'Ambiguous'),
            ([['item-runes.json', 0, 'A', 53506, 34000], ['item-runes.json', 1, 'A', 53505, 34001]], 'Ambiguous'),
            ([['item-runes.json', 0, 'A', 53506, 53505], ['item-runes.json', 1, 'B', 53505, 34001]], 'overlapping'),
            ([['item-runes.json', 0, 'A', 53506, 65535]], 'NewID'),
            ([['item-runes.json', 0, 'A', -1, 34000]], 'OldID'),
            ([['item-runes.json', 0, '', 53506, 34000]], 'Ambiguous'),
            ([['ui.json', 0, 'A', 53506, 34000]], 'no item-runes.json'),
            ([], 'no item-runes.json'),
        ]
        for rows, error in samples:
            with self.subTest(rows=rows):
                self.write_mapping(rows)
                self.assert_no_edit(self.run_tool('--apply', '--yes'), error)

    def test_malformed_csv_rejects_before_writes(self):
        for text in ['', 'File,Key,OldID\n', 'File,Key,OldID,NewID,old_id\n',
                     'File,Key,OldID,NewID\n"item-runes.json,A,53506,34000',
                     'File,Key,OldID,NewID\n"item-runes.json"bad,A,53506,34000',
                     'File,Key,OldID,NewID\nitem-runes.json,A,53506',
                     'File,Key,OldID,NewID\nitem-runes.json,A"B,53506,34000']:
            with self.subTest(text=text):
                self.mapping.write_text(text, encoding='utf-8')
                self.assert_no_edit(self.run_tool('--apply', '--yes'))

    def test_bad_save_boundaries_versions_and_counts_reject_before_writes(self):
        base = len(self.item_page) + len(self.advanced_page)
        cases = []
        for offset, fmt, value in [(0, '<I', 0), (4, '<I', 3), (8, '<I', 104),
                                   (16, '<H', 0), (20, '<B', 3), (64, '<B', 0),
                                   (base + 64, '<I', 0), (base + 68, '<H', 2),
                                   (base + 74, '<H', 65535)]:
            broken = bytearray(self.original)
            struct.pack_into(fmt, broken, offset, value)
            cases.append(bytes(broken))
        cases += [self.original[:-1], self.original[:30], self.original + b'tail']
        padding = bytearray(self.original[:-1])
        struct.pack_into('<H', padding, base + 16, len(padding) - base)
        cases.append(bytes(padding))
        for i, broken in enumerate(cases):
            with self.subTest(case=i):
                self.save.write_bytes(broken)
                result = self.run_tool('--apply', '--yes')
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertEqual(self.save.read_bytes(), broken)
                self.assertFalse(self.backups.exists())

    def test_collision_with_unmapped_existing_discovery_is_rejected(self):
        self.write_mapping([['item-runes.json', 0, 'A', 53506, 20581]])
        self.assert_no_edit(self.run_tool('--apply', '--yes'), 'colliding')

    def test_no_chronicle_and_character_extension_are_rejected(self):
        self.save.write_bytes(self.item_page)
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn('No supported Chronicle', result.stderr)
        self.assertEqual(self.save.read_bytes(), self.item_page)
        self.assertFalse(self.backups.exists())
        self.save = self.folder / 'Character.d2s'
        self.save.write_bytes(self.original)
        self.assert_no_edit(self.run_tool('--apply', '--yes'), '.d2i')

    def test_multiple_chronicle_tabs_and_empty_history(self):
        self.save.write_bytes(self.item_page + chronicle((53506,)) + chronicle((53505,)))
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(self.reports()[0].read_text(encoding='utf-8'))
        self.assertEqual(report['chronicleTabs'], 2)
        self.assertEqual(len(report['changes']), 2)
        self.save.write_bytes(self.item_page + chronicle((), sets=(), uniques=()))
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('No mapped Chronicle IDs', result.stdout)
        self.assertEqual(len(self.reports()), 1)

    def test_backup_inside_save_or_game_tree_is_rejected(self):
        self.assert_no_edit(self.run_tool('--backup-folder', str(self.folder / 'backups'), '--apply', '--yes'), 'outside')
        game = self.root / 'mod.mpq' / 'data' / 'saves'
        game.mkdir(parents=True)
        self.save = game / self.save.name
        self.save.write_bytes(self.original)
        self.assert_no_edit(self.run_tool('--backup-folder', str(game.parent / 'backups'), '--apply', '--yes'), 'outside')

    def test_backup_creation_failure_never_changes_save(self):
        blocked = self.root / 'not-a-directory'
        blocked.write_text('unchanged', encoding='ascii')
        self.assert_no_edit(self.run_tool('--backup-folder', str(blocked), '--apply', '--yes'))
        self.assertEqual(blocked.read_text(encoding='ascii'), 'unchanged')

    def test_failed_replacement_has_verified_backup_and_no_temporary_files(self):
        self.save.chmod(stat.S_IREAD)
        try:
            result = self.run_tool('--apply', '--yes')
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertEqual(self.save.read_bytes(), self.original)
            report_path, = self.reports()
            self.assertEqual((report_path.parent / 'originals' / self.save.name).read_bytes(), self.original)
            self.assertEqual(json.loads(report_path.read_text(encoding='utf-8'))['status'], 'FailedBeforeSaveEdit')
            self.assertFalse(list(self.folder.glob('*.tmp')))
        finally:
            self.save.chmod(stat.S_IREAD | stat.S_IWRITE)

    def test_cli_requires_explicit_action_and_write_authorization(self):
        for options in [(), ('--apply',), ('--yes',), ('--dry-run', '--apply', '--yes'),
                        ('--target', str(self.folder), '--dry-run'), ('--start', '30000', '--dry-run')]:
            with self.subTest(options=options):
                self.assert_no_edit(self.run_tool(*options))
        result = subprocess.run([str(ARGS.exe), '--save-file', str(self.save), '--mapping', str(self.mapping), '--apply', '--yes'],
                                stdin=subprocess.DEVNULL, capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assert_no_edit(result, '--repair-chronicle')

    def test_changes_after_preview_abort_without_overwriting_or_backups(self):
        # Exercise the preview/apply boundary directly, with an intervening
        # external edit, rather than relying on timing a background process.
        runner = self.root / 'ConcurrentEdit.cs'
        runner.write_text(r'''
using System;
using System.IO;
using System.Reflection;
class ConcurrentEdit {
    static int Main(string[] args) {
        Type repair = Assembly.LoadFrom(args[0]).GetType("D2RLocalizationCompactor.ChronicleRepair", true);
        object plan = repair.GetMethod("Build").Invoke(null, new object[] { args[1], args[2], args[3] });
        File.AppendAllText(args[4], "external edit");
        try { repair.GetMethod("Apply").Invoke(null, new[] { plan }); }
        catch (TargetInvocationException error) {
            if (error.InnerException is IOException && error.InnerException.Message.Contains("changed after preview")) return 0;
            Console.Error.WriteLine(error.InnerException); return 2;
        }
        return 1;
    }
}
''', encoding='utf-8')
        project = self.root / 'ConcurrentEdit.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                           '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', encoding='utf-8')
        helper = self.root / 'helper/ConcurrentEdit.exe'
        build = subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '-o', str(helper.parent),
                                '-p:UseSharedCompilation=false', '-nodeReuse:false'],
                               capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
        for selected in [self.save, self.mapping]:
            with self.subTest(selected=selected.name):
                self.save.write_bytes(self.original)
                self.write_mapping()
                before = selected.read_bytes()
                result = subprocess.run([str(helper), str(ARGS.assembly), str(self.save), str(self.mapping),
                                         str(self.backups), str(selected)], capture_output=True,
                                        encoding='utf-8-sig', timeout=30)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(selected.read_bytes(), before + b'external edit')
                if selected == self.mapping:
                    self.assertEqual(self.save.read_bytes(), self.original)
                self.assertFalse(self.backups.exists())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--assembly', type=Path, help='Unbundled build DLL for the preview/apply boundary test')
    ARGS = parser.parse_args()
    ARGS.exe = ARGS.exe.resolve(strict=True)
    ARGS.work = ARGS.work.resolve()
    ARGS.work.mkdir(parents=True, exist_ok=True)
    ARGS.assembly = ARGS.assembly or ARGS.exe.parent.parent / 'artifacts/bin/D2RLocalizationCompactor/release_win-x64/D2R-Localization-ID-Compactor.dll'
    ARGS.assembly = ARGS.assembly.resolve(strict=True)
    os.environ['DOTNET_CLI_HOME'] = str(ARGS.work / '.dotnet-home')
    os.environ['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    unittest.main(argv=[sys.argv[0], '-v'])
