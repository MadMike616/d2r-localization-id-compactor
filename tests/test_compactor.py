from pathlib import Path
import argparse
import codecs
import csv
import hashlib
import gzip
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import unittest
import uuid


def row(number, key, text='Translated text'):
    return {'id': number, 'Key': key, 'enUS': text}


class CompactorTests(unittest.TestCase):
    def setUp(self):
        self.root = ARGS.work / (self._testMethodName + '-' + uuid.uuid4().hex[:8])
        self.target = self.root / 'strings & Unicode 測試'
        self.reference = self.root / 'vanilla'
        self.backup_root = self.root / 'Localization-ID-Backups'
        self.target.mkdir(parents=True)
        self.reference.mkdir()
        self.write('item-gems.json', [row(10, 'StockGem')], self.reference)
        self.write('ui.json', [row(20, 'StockUI')], self.reference)
        self.write('commands.json', [row(251780, 'D2CommandsFriendsNoInviteTo')], self.reference)

    def write(self, name, data, directory=None):
        path = (directory or self.target) / name
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
        return path

    def run_tool(self, *args, builtin=False, rune_mode='compact'):
        command = [str(ARGS.exe), '--target', str(self.target)]
        if not builtin:
            command += ['--vanilla', str(self.reference)]
        if rune_mode is not None:
            command += ['--item-runes-mode', rune_mode]
        return subprocess.run(command + list(args), stdin=subprocess.DEVNULL, capture_output=True,
                              encoding='utf-8-sig', errors='strict', timeout=50)

    def apply(self, *args, builtin=False, rune_mode='compact'):
        result = self.run_tool(*args, '--apply', '--yes', builtin=builtin, rune_mode=rune_mode)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result

    def snapshot(self):
        return {p.name: p.read_bytes() for p in self.target.iterdir() if p.is_file()}

    def manifests(self):
        return list(self.backup_root.glob('*/manifest.json'))

    def ids(self, name):
        return [r['id'] for r in json.loads((self.target / name).read_text(encoding='utf-8-sig'))]

    def test_additional_filenames_native_edits_and_complete_backups(self):
        self.write('item-gems.json', [row(10, 'RenamedVanillaGem', 'Edited vanilla')])
        self.write('item-modifiers.json', [row(5, 'CustomModifier')])
        self.write('ui.json', [row(20, 'StockUI', 'Changed vanilla UI'), row(5, 'CustomUI')])
        self.write('a-extra.json', [row(5, 'ExtraA'), row(10, 'StockGem', 'Native override')])
        self.write('z-extra.JSON', [row(5, 'ExtraZ')])
        self.write('layout.json', {'id': 5, 'Key': 'NotLocalization', 'type': 'Widget'})
        self.write('key-only.json', [{'Key': 'PluginString', 'enUS': 'No numeric ID'}])
        before = self.snapshot()
        result = self.apply()
        self.assertEqual(self.ids('item-gems.json'), [10])
        self.assertEqual(self.ids('item-modifiers.json'), [30000])
        self.assertEqual(self.ids('ui.json'), [20, 31000])
        self.assertEqual(self.ids('a-extra.json'), [32000, 10])
        self.assertEqual(self.ids('z-extra.JSON'), [33000])
        self.assertEqual((self.target / 'layout.json').read_bytes(), before['layout.json'])
        self.assertIn('Skipped key-only.json', result.stdout)
        manifest_path, = self.manifests()
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        self.assertEqual(manifest['status'], 'Complete')
        self.assertEqual(manifest['changedIds'], 4)
        for name, contents in before.items():
            self.assertEqual((manifest_path.parent / 'originals' / name).read_bytes(), contents)
        with (manifest_path.parent / 'old-to-new-ids.csv').open(encoding='utf-8-sig', newline='') as stream:
            mapping = list(csv.DictReader(stream))
        self.assertEqual([r['File'] for r in mapping], ['item-modifiers.json', 'ui.json', 'a-extra.json', 'z-extra.JSON'])
        self.assertEqual({int(r['OldID']) for r in mapping}, {5})

    def test_dry_run_has_no_writes(self):
        self.write('other.json', [row(1, 'Custom')])
        before = self.snapshot()
        result = self.run_tool('--dry-run')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('30000-30000', result.stdout)
        self.assertEqual(self.snapshot(), before)
        self.assertFalse(self.backup_root.exists())

    def test_mixed_case_options_apply_and_preserve_values(self):
        self.write('MixedCase.json', [row(5, 'KeepMyCase', 'Keep This Text')])
        result = subprocess.run([str(ARGS.exe), '--TaRgEt', str(self.target),
                                 '--VaNiLlA', str(self.reference), '--StArT', '31000',
                                 '--ApPlY', '--YeS'], stdin=subprocess.DEVNULL,
                                capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads((self.target / 'MixedCase.json').read_text(encoding='utf-8')),
                         [row(31000, 'KeepMyCase', 'Keep This Text')])
        self.assertEqual(len(self.manifests()), 1)

    def test_case_insensitive_preview_and_help_options(self):
        self.write('other.json', [row(5, 'Custom')])
        before = self.snapshot()
        result = self.run_tool('--DrY-RuN')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('Preview only', result.stdout)
        result = subprocess.run([str(ARGS.exe), '--HeLp'], stdin=subprocess.DEVNULL,
                                capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('Options:', result.stdout)
        self.assertEqual(self.snapshot(), before)

    def test_rounding_exactly_300_is_allowed(self):
        self.write('alpha.json', [row(1000 + i, 'A' + str(i)) for i in range(701)])
        self.write('zeta.json', [row(3, 'Z')])
        result = self.run_tool('--dry-run')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertRegex(result.stdout, r'zeta\.json\s+31000-31000')

    def test_rounding_299_skips_a_thousand(self):
        self.write('alpha.json', [row(1000 + i, 'A' + str(i)) for i in range(702)])
        self.write('zeta.json', [row(3, 'Z')])
        result = self.run_tool('--dry-run')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertRegex(result.stdout, r'zeta\.json\s+32000-32000')

    def test_json_comments_trailing_commas_escapes_and_id_position(self):
        text = ('[\r\n // Credits remain\r\n {"Key":"K\\\"x", "enUS":"http://example/ \\\"id\\\": 123 中文", '
                '"metadata":{"id":123,"items":[1,2,],}, "id" : 5,},\r\n]\r\n')
        raw = codecs.BOM_UTF8 + text.encode('utf-8')
        path = self.target / 'custom.json'
        path.write_bytes(raw)
        self.apply()
        self.assertEqual(path.read_bytes(), raw.replace(b'"id" : 5', b'"id" : 30000'))

    def test_utf16_both_byte_orders(self):
        for encoding, bom, name in [('utf-16-le', codecs.BOM_UTF16_LE, 'alpha.json'),
                                    ('utf-16-be', codecs.BOM_UTF16_BE, 'beta.json')]:
            raw = bom + '[{"enUS":"Hełm/腰帶", "id":5, "Key":"Custom"}]\r\n'.encode(encoding)
            (self.target / name).write_bytes(raw)
        self.apply()
        for encoding, bom, name, number in [('utf-16-le', codecs.BOM_UTF16_LE, 'alpha.json', 30000),
                                            ('utf-16-be', codecs.BOM_UTF16_BE, 'beta.json', 31000)]:
            expected = bom + ('[{"enUS":"Hełm/腰帶", "id":' + str(number) + ', "Key":"Custom"}]\r\n').encode(encoding)
            self.assertEqual((self.target / name).read_bytes(), expected)

    def test_duplicate_custom_ids_are_all_moved(self):
        self.write('extra.json', [row(5, 'A'), row(5, 'B')])
        self.apply()
        self.assertEqual(self.ids('extra.json'), [30000, 30001])

    def test_exact_native_record_wins_a_conflicting_slot(self):
        self.write('item-gems.json', [row(10, 'StockGem', 'Edited native text'), row(10, 'Custom')])
        self.apply()
        self.assertEqual(self.ids('item-gems.json'), [10, 30000])

    def test_ambiguous_native_duplicate_is_rejected(self):
        self.write('item-gems.json', [row(10, 'RenamedA'), row(10, 'RenamedB')])
        before = self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertIn('Ambiguous duplicate vanilla slot 10', result.stderr)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), [])

    def test_malformed_json_cannot_partially_apply(self):
        self.write('alpha.json', [row(5, 'ValidCustom')])
        (self.target / 'zeta.json').write_text('[{"id": 6, "Key": "Broken", "enUS": "x"}', encoding='utf-8')
        before = self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), [])

    def test_mixed_invalid_localization_is_not_silently_skipped(self):
        self.write('extra.json', [row(5, 'Good'), {'id': '6', 'Key': 'Bad', 'enUS': 'x'}])
        before = self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertIn('Invalid localization record 2', result.stderr)
        self.assertEqual(self.snapshot(), before)

    def test_duplicate_properties_rejected(self):
        (self.target / 'extra.json').write_text('[{"id":5,"id":6,"Key":"A","enUS":"x"}]', encoding='utf-8')
        result = self.run_tool('--dry-run')
        self.assertEqual(result.returncode, 1)
        self.assertIn('Duplicate property: id', result.stderr)

    def test_invalid_utf8_rejected_without_any_writes(self):
        self.write('alpha.json', [row(5, 'Good')])
        (self.target / 'beta.json').write_bytes(b'[{"id":6,"Key":"Bad","enUS":"\xff"}]')
        before = self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), [])

    def test_executable_can_be_copied_alone(self):
        portable = self.target / ARGS.exe.name
        shutil.copyfile(ARGS.exe, portable)
        self.write('additional.json', [row(5, 'StandaloneCustom')])
        before = self.snapshot()
        result = subprocess.run([str(portable), '--dry-run'], cwd=self.root,
                                stdin=subprocess.DEVNULL, capture_output=True,
                                encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Built-in vanilla D2R 3.4', result.stdout)
        self.assertIn('30000-30000', result.stdout)
        self.assertEqual(self.snapshot(), before)

    def test_all_vanilla_filenames_with_custom_additions(self):
        first = ['item-gems.json', 'item-modifiers.json', 'item-nameaffixes.json',
                 'item-names.json', 'item-runes.json', 'skills.json', 'ui.json']
        if ARGS.full_vanilla:
            stock = {p.name: json.loads(p.read_text(encoding='utf-8-sig')) for p in ARGS.full_vanilla.glob('*.json')}
        else:
            profile = json.loads(gzip.decompress((Path(__file__).resolve().parents[1] / 'vanilla-3.4.json.gz').read_bytes()))
            stock = {name: [row(number, key, 'Reference fixture text') for number, key in entries] for name, entries in profile.items()}
        files = sorted(stock, key=lambda name: (first.index(name) if name in first else len(first), name.casefold()))
        for index, name in enumerate(files):
            self.write(name, stock[name] + [row(60000 + index, 'CustomFor_' + Path(name).stem)])
        self.apply(builtin=True)
        self.assertEqual(len(files), 21)
        for index, name in enumerate(files):
            after = json.loads((self.target / name).read_text(encoding='utf-8-sig'))
            self.assertEqual(after[:-1], stock[name], name)
            self.assertEqual(after[-1]['id'], 30000 + index * 1000, name)

    def test_preserves_original_vanilla_duplicate_groups(self):
        self.write('item-modifiers.json', [row(10, 'NativeModifier')], self.reference)
        self.write('item-gems.json', [row(10, 'StockGem')])
        self.write('item-modifiers.json', [row(10, 'NativeModifier')])
        self.write('extra.json', [row(5, 'Custom')])
        result = self.apply()
        self.assertIn('Vanilla duplicate/override ID groups preserved: 1', result.stdout)
        self.assertEqual(self.ids('item-gems.json'), [10])
        self.assertEqual(self.ids('item-modifiers.json'), [10])

    def test_start_inside_vanilla_range_is_rejected(self):
        self.write('extra.json', [row(5, 'Custom')])
        before = self.snapshot()
        result = self.run_tool('--start', '20', '--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertEqual(self.snapshot(), before)

    def test_range_overflow_is_rejected_before_writing(self):
        self.write('extra.json', [row(1000 + i, 'A' + str(i)) for i in range(536)])
        before = self.snapshot()
        result = self.run_tool('--start', '65000', '--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertIn('exceed 65534', result.stderr)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), [])

    def test_item_runes_mode_is_required_without_an_automatic_default(self):
        self.write('item-runes.json', [row(29001, 'Runeword238')])
        self.write('other.json', [row(5, 'Custom')])
        before = self.snapshot()
        for options in [('--dry-run',), ('--apply', '--yes')]:
            result = self.run_tool(*options, rune_mode=None)
            self.assertEqual(result.returncode, 1, result.stdout)
            self.assertIn('--item-runes-mode preserve', result.stderr)
            self.assertEqual(self.snapshot(), before)
            self.assertEqual(self.manifests(), [])

    def test_preserve_item_runes_keeps_bytes_and_reserves_both_clusters(self):
        text = '[\r\n // Keep this file exactly\r\n {"id":29001,"Key":"Shortcut","enUS":"短路"},\r\n {"id":29003,"Key":"Sunder","enUS":"Sunder"},\r\n {"id":53500,"Key":"Distortion","enUS":"Distortion"},\r\n]\r\n'
        raw = codecs.BOM_UTF16_LE + text.encode('utf-16-le')
        runes = self.target / 'ITEM-RUNES.JSON'
        runes.write_bytes(raw)
        self.write('item-modifiers.json', [row(5, 'CustomModifier')])
        self.write('other.json', [row(6, 'CustomOther')])
        result = self.apply('--ItEm-RuNeS-MoDe', 'pReSeRvE')
        self.assertEqual(runes.read_bytes(), raw)
        self.assertEqual(self.ids('item-modifiers.json'), [31000])
        self.assertEqual(self.ids('other.json'), [32000])
        self.assertIn('28001-30003', result.stdout)
        self.assertIn('52500-54500', result.stdout)
        manifest_path, = self.manifests()
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        self.assertEqual(manifest['itemRunesMode'], 'preserve')
        self.assertEqual(manifest['itemRunesBuffer'], 1000)
        self.assertEqual(manifest['reservedItemRunesIntervals'],
                         [{'first': 28001, 'last': 30003}, {'first': 52500, 'last': 54500}])
        with (manifest_path.parent / 'preserved-item-runes.csv').open(encoding='utf-8-sig', newline='') as stream:
            self.assertEqual([int(r['ID']) for r in csv.DictReader(stream)], [29001, 29003, 53500])
        with (manifest_path.parent / 'old-to-new-ids.csv').open(encoding='utf-8-sig', newline='') as stream:
            self.assertNotIn('ITEM-RUNES.JSON', {r['File'] for r in csv.DictReader(stream)})
        before = self.snapshot()
        result = self.apply(rune_mode='preserve')
        self.assertIn('No ID changes are needed', result.stdout)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(len(self.manifests()), 1)

    def test_item_runes_buffer_includes_exactly_1000_on_either_side(self):
        self.write('item-runes.json', [row(32000, 'CustomWord')])
        self.write('extra.json', [row(5, 'Custom')])
        for start, expected in [(30999, 30999), (31000, 34000), (32000, 34000),
                                (33000, 34000), (33001, 33001)]:
            with self.subTest(start=start):
                result = self.run_tool('--start', str(start), '--dry-run', rune_mode='preserve')
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertRegex(result.stdout, rf'extra\.json\s+{expected}-{expected}')
        self.assertFalse(self.backup_root.exists())

    def test_whole_consecutive_range_moves_past_item_runes_buffer(self):
        self.write('item-runes.json', [row(32000, 'CustomWord')])
        self.write('item-names.json', [row(100 + i, 'Name' + str(i)) for i in range(1001)])
        self.apply(rune_mode='preserve')
        self.assertEqual(self.ids('item-names.json'), list(range(34000, 35001)))
        self.assertEqual(self.ids('item-runes.json'), [32000])

    def test_later_file_obeys_spacing_and_moves_after_merged_buffers(self):
        self.write('item-runes.json', [row(33000, 'One'), row(33500, 'Two')])
        self.write('item-modifiers.json', [row(5, 'Modifier')])
        self.write('skills.json', [row(6, 'Skill')])
        self.apply('--start', '31000', rune_mode='preserve')
        self.assertEqual(self.ids('item-modifiers.json'), [31000])
        self.assertEqual(self.ids('skills.json'), [35000])

    def test_item_runes_buffer_overflow_stops_before_any_writes(self):
        self.write('item-runes.json', [row(65534, 'LastWord')])
        self.write('extra.json', [row(5, 'Custom')])
        before = self.snapshot()
        result = self.run_tool('--start', '65000', '--apply', '--yes', rune_mode='preserve')
        self.assertEqual(result.returncode, 1)
        self.assertIn('item-runes safety buffers', result.stderr)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), [])

    def test_compact_item_runes_is_explicit_and_does_not_reserve_buffers(self):
        self.write('item-runes.json', [row(29001, 'CustomWord')])
        self.write('skills.json', [row(5, 'CustomSkill')])
        result = self.apply('--ITEM-RUNES-MODE', 'CoMpAcT')
        self.assertEqual(self.ids('item-runes.json'), [30000])
        self.assertEqual(self.ids('skills.json'), [31000])
        self.assertIn('NO existing character or shared-stash saves', result.stdout)
        manifest_path, = self.manifests()
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        self.assertEqual(manifest['itemRunesMode'], 'compact')
        self.assertEqual(manifest['reservedItemRunesIntervals'], [])

    def test_invalid_item_runes_mode_is_rejected(self):
        self.write('item-runes.json', [row(29001, 'CustomWord')])
        before = self.snapshot()
        result = self.run_tool('--apply', '--yes', rune_mode='automatic')
        self.assertEqual(result.returncode, 1)
        self.assertIn('mode must be PRESERVE', result.stderr)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), [])

    def test_second_run_is_idempotent_and_ignores_backups(self):
        self.write('extra.json', [row(5, 'A'), row(6, 'B')])
        self.apply()
        before = self.snapshot()
        manifests = self.manifests()
        result = self.apply()
        self.assertIn('No ID changes are needed', result.stdout)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.manifests(), manifests)

    def test_backup_json_is_outside_recursive_game_string_scan(self):
        self.target = self.root / 'Arbitrary Mod' / 'UnrelatedName.MPQ' / 'data' / 'local' / 'lng' / 'strings'
        self.target.mkdir(parents=True)
        self.write('extra.json', [row(5, 'Custom')])
        self.apply()
        backup = self.root / 'Arbitrary Mod' / 'Localization-ID-Backups'
        manifest, = backup.glob('*/manifest.json')
        self.assertEqual(json.loads(manifest.read_text(encoding='utf-8'))['status'], 'Complete')
        # D2RLoader scans recursively: every JSON remaining under strings must
        # be an actual string-table array, with no manifest or original copies.
        loaded = list(self.target.rglob('*.json'))
        self.assertEqual([p.name for p in loaded], ['extra.json'])
        self.assertTrue(all(isinstance(json.loads(p.read_text(encoding='utf-8')), list) for p in loaded))

    def test_data_layout_without_mpq_uses_mod_root_for_backups(self):
        self.target = self.root / 'Different Mod' / 'data' / 'local' / 'lng' / 'strings'
        self.target.mkdir(parents=True)
        self.write('extra.json', [row(5, 'Custom')])
        self.apply()
        self.assertEqual(len(list((self.root / 'Different Mod' / 'Localization-ID-Backups').glob('*/manifest.json'))), 1)
        self.assertEqual(len(list((self.root / 'Different Mod' / 'data').rglob('*.json'))), 1)

    def test_backup_override_inside_game_data_is_rejected(self):
        self.target = self.root / 'Mod' / 'Mod.mpq' / 'data' / 'local' / 'lng' / 'strings'
        self.target.mkdir(parents=True)
        self.write('extra.json', [row(5, 'Custom')])
        before = self.snapshot()
        unsafe = self.root / 'Mod' / 'Mod.mpq' / 'data' / 'OtherBackups'
        result = self.run_tool('--backup-folder', str(unsafe), '--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertIn('outside the localization/game-data tree', result.stderr)
        self.assertEqual(self.snapshot(), before)
        self.assertFalse(unsafe.exists())

    def test_explicit_external_backup_folder(self):
        self.write('extra.json', [row(5, 'Custom')])
        external = self.root / 'Explicit Backups'
        self.apply('--BaCkUp-FoLdEr', str(external))
        self.assertEqual(len(list(external.glob('*/manifest.json'))), 1)
        self.assertFalse(self.backup_root.exists())

    def test_legacy_backups_are_relocated_even_for_already_compacted_ids(self):
        active = self.write('extra.json', [row(30000, 'Custom')])
        before = active.read_bytes()
        legacy = self.target / 'Localization-ID-Backups'
        old_run = legacy / 'old-run'
        (old_run / 'originals').mkdir(parents=True)
        original = json.dumps([row(5, 'Custom')]).encode('utf-8')
        (old_run / 'originals' / 'extra.json').write_bytes(original)
        (old_run / 'manifest.json').write_text(json.dumps({'tool': 'D2R Localization ID Compactor', 'version': '1.0.1'}), encoding='utf-8')
        result = self.run_tool('--dry-run')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('Old backups inside game data will be relocated', result.stdout)
        self.assertTrue(legacy.exists())
        self.assertFalse(self.backup_root.exists())
        result = self.apply()
        self.assertIn('Moved old backups out of game data', result.stdout)
        self.assertFalse(legacy.exists())
        moved, = self.backup_root.glob('Migrated-legacy-*')
        self.assertEqual((moved / 'old-run' / 'originals' / 'extra.json').read_bytes(), original)
        self.assertEqual(active.read_bytes(), before)
        self.assertEqual([p.name for p in self.target.rglob('*.json')], ['extra.json'])

    def test_unrecognized_legacy_folder_requires_manual_review(self):
        self.write('extra.json', [row(5, 'Custom')])
        old_run = self.target / 'Localization-ID-Backups' / 'old-run'
        old_run.mkdir(parents=True)
        (old_run / 'manifest.json').write_text(json.dumps({'tool': 'Another application'}), encoding='utf-8')
        before = self.snapshot()
        result = self.run_tool('--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertIn('not owned by this utility', result.stderr)
        self.assertTrue(old_run.exists())
        self.assertEqual(self.snapshot(), before)

    def test_backup_override_cannot_pollute_vanilla_reference(self):
        self.write('extra.json', [row(5, 'Custom')])
        result = self.run_tool('--backup-folder', str(self.reference / 'Backups'), '--apply', '--yes')
        self.assertEqual(result.returncode, 1)
        self.assertIn('outside the vanilla reference tree', result.stderr)
        self.assertFalse((self.reference / 'Backups').exists())

    @unittest.skipUnless(os.name == 'nt', 'Windows File.Replace/read-only behavior')
    def test_failed_second_commit_rolls_back_first(self):
        self.write('alpha.json', [row(5, 'A')])
        readonly = self.write('beta.json', [row(6, 'B')])
        before = self.snapshot()
        readonly.chmod(stat.S_IREAD)
        try:
            result = self.run_tool('--apply', '--yes')
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertEqual(self.snapshot(), before)
            manifest_path, = self.manifests()
            manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
            self.assertEqual(manifest['status'], 'RolledBack')
            self.assertEqual(sorted(p.name for p in (manifest_path.parent / 'originals').glob('*.json')), ['alpha.json', 'beta.json'])
            self.assertFalse(list(self.target.glob('*.tmp')))
        finally:
            readonly.chmod(stat.S_IREAD | stat.S_IWRITE)

    def test_reference_fixture_replay(self):
        if ARGS.replay_backup and ARGS.expected_manifest:
            expected = json.loads(ARGS.expected_manifest.read_text(encoding='utf-8'))
            for p in ARGS.replay_backup.glob('*.json'):
                (self.target / p.name).write_bytes(p.read_bytes())
        else:
            self.write('item-gems.json', [row(11073, 'GemXp1', 'Helms/Belts:'), row(50004, 'CustomGemNote')])
            self.write('skills.json', [row(50004, 'CustomSkillName')])
            expected = {'files': {}, 'checks': {'total_renumbered': 2, 'preserved_vanilla_duplicate_exceptions': {}}}
            for name, new_id in [('item-gems.json', 30000), ('skills.json', 31000)]:
                output = (self.target / name).read_bytes().replace(b'"id": 50004', ('"id": ' + str(new_id)).encode('ascii'))
                expected['files'][name] = {'after_sha256': hashlib.sha256(output).hexdigest()}
        self.apply(builtin=True)
        for name, info in expected['files'].items():
            self.assertEqual(hashlib.sha256((self.target / name).read_bytes()).hexdigest(), info['after_sha256'], name)
        manifest_path, = self.manifests()
        current = json.loads(manifest_path.read_text(encoding='utf-8'))
        self.assertEqual(current['changedIds'], expected['checks']['total_renumbered'])
        self.assertEqual(set(current['preservedVanillaDuplicateGroups']), set(expected['checks']['preserved_vanilla_duplicate_exceptions']))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--replay-backup', type=Path)
    parser.add_argument('--expected-manifest', type=Path)
    parser.add_argument('--full-vanilla', type=Path)
    ARGS = parser.parse_args()
    ARGS.exe = ARGS.exe.resolve(strict=True)
    ARGS.work = ARGS.work.resolve()
    ARGS.work.mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[sys.argv[0], '-v'])
