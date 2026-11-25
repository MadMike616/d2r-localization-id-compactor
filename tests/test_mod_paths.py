"""Automatic mod/save discovery uses synthetic mod and save trees under --work."""
from pathlib import Path
import argparse
import json
import os
import subprocess
import sys
import unittest
import uuid
from test_chronicle_repair import page, chronicle


class ModPathTests(unittest.TestCase):
    def setUp(self):
        self.root = ARGS.work / (self._testMethodName + '-' + uuid.uuid4().hex[:8])
        self.mod = self.root / 'Different Mod Directory'
        self.content = self.mod / 'DifferentArchive.mpq'
        self.strings = self.content / 'data/local/lng/strings'
        self.strings.mkdir(parents=True)
        self.saved_games = self.root / 'Redirected Saved Games 測試'
        self.profile_name = 'Profile-With-Different-Name'
        self.profile = self.saved_games / 'Diablo II Resurrected/mods' / self.profile_name
        self.profile.mkdir(parents=True)
        self.metadata = self.content / 'modinfo.json'
        self.metadata.write_text(json.dumps({'name': 'Friendly Display Name', 'savepath': self.profile_name + '/'}), encoding='utf-8-sig')
        self.tables = self.content / 'data/global/excel'
        self.tables.mkdir(parents=True)
        (self.tables / 'itemtypes.txt').write_text('Code\tEquiv1\tEquiv2\tVarInvGfx\nitem\t\t\t0\n', encoding='utf-8')
        (self.tables / 'itemstatcost.txt').write_text('Stat\tCSvSigned\tCSvBits\tCSvParam\tSave Bits\tSave Param Bits\tSave Add\tValShift\n', encoding='utf-8')
        for name in ['armor.txt', 'weapons.txt', 'misc.txt']:
            (self.tables / name).write_text('code\ttype\tcompactsave\tstackable\tquest\tquestdiffcheck\n', encoding='utf-8')
        self.stash = self.profile / 'ModernSharedStashSoftCoreV2.d2i'
        self.original = page(0, b'JM\0\0') + chronicle((53506,))
        self.stash.write_bytes(self.original)
        self.mapping = self.root / 'old-to-new-ids.csv'
        self.mapping.write_text('File,Key,OldID,NewID\nitem-runes.json,CustomWord,53506,34008\n', encoding='utf-8')
        self.backups = self.root / 'Repair-Backups'

    def helper(self, action='discover', start=None, extra=()):
        result = subprocess.run([str(HELPER), str(ASSEMBLY), action, str(start or self.strings), str(self.saved_games),
                                 str(self.stash), str(self.mapping), str(self.tables), str(self.backups), *extra],
                                capture_output=True, encoding='utf-8-sig', timeout=30)
        return result

    def discover(self, start=None):
        result = self.helper(start=start)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return json.loads(result.stdout)

    def test_ancestor_metadata_and_redirected_known_folder_use_savepath(self):
        data = self.discover()
        self.assertEqual(Path(data['saveFolder']), self.profile)
        self.assertEqual(data['modName'], 'Friendly Display Name')
        self.assertEqual(Path(data['modInfo']), self.metadata)
        self.assertEqual(Path(data['tablesFolder']), self.tables)
        self.assertTrue(data['saveFolderExists'])

    def test_outer_mod_folder_and_metadata_file_are_supported(self):
        for start in [self.mod, self.content, self.metadata]:
            with self.subTest(start=start):
                self.assertEqual(Path(self.discover(start)['modInfo']), self.metadata)

    def test_nested_savepath_and_parent_path_to_vanilla_tree(self):
        for relative in ['Season 12/Hardcore/', 'Season 12\\Hardcore\\', '../']:
            with self.subTest(relative=relative):
                self.metadata.write_text(json.dumps({'savepath': relative}), encoding='utf-8')
                data = self.discover()
                expected = (self.saved_games / 'Diablo II Resurrected/mods' / relative.replace('\\', '/')).resolve()
                self.assertEqual(Path(data['saveFolder']), expected)

    def test_missing_invalid_or_unsafe_savepath_is_not_guessed(self):
        examples = [{}, {'savepath': ''}, {'savepath': 123}, {'savepath': 'E:/unrelated'},
                    {'savepath': '../../outside'}, {'savepath': 'Name '}, {'savepath': 'Name.'},
                    {'savepath': r'\\server\share'}, {'savepath': 'bad\nname'},
                    {'savepath': 'One', 'SavePath': 'Two'}]
        for metadata in examples:
            with self.subTest(metadata=metadata):
                metadata['name'] = self.profile_name  # no fallback from name
                self.metadata.write_text(json.dumps(metadata), encoding='utf-8')
                data = self.discover()
                self.assertIsNone(data['saveFolder'])
                self.assertFalse(data['saveFolderExists'])
                self.assertTrue(data['saveError'])

    def test_absent_profile_is_reported_without_creating_it(self):
        name = 'Not-Created-' + uuid.uuid4().hex
        self.metadata.write_text(json.dumps({'savepath': name}), encoding='utf-8')
        data = self.discover()
        self.assertFalse(data['saveFolderExists'])
        self.assertIn('does not exist', data['saveError'])
        self.assertFalse(Path(data['saveFolder']).exists())

    def test_partial_tables_do_not_supply_a_false_default(self):
        (self.tables / 'armor.txt').unlink()
        data = self.discover()
        self.assertIsNone(data['tablesFolder'])
        self.assertIn('five', data['tablesError'])

    def test_malformed_metadata_or_multiple_archives_do_not_pick_a_mod(self):
        self.metadata.write_text('{broken', encoding='utf-8')
        self.assertIsNone(self.discover()['modInfo'])
        self.metadata.write_text('{}', encoding='utf-8')
        other = self.mod / 'Second.mpq'; other.mkdir(); (other / 'modinfo.json').write_text('{}')
        data = self.discover(self.mod)
        self.assertIsNone(data['modInfo'])
        self.assertIn('Several', data['modError'])

    def test_auto_chronicle_requires_one_stash_and_items_use_the_profile(self):
        result = self.helper('auto-chronicle')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(str(self.stash), result.stdout)
        self.assertIn('Preview only', result.stdout)
        result = self.helper('auto-items')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Using detected mod tables', result.stdout)
        self.assertIn('1 selected saves', result.stdout)
        second = self.profile / 'OtherStash.d2i'; second.write_bytes(self.original)
        result = self.helper('auto-chronicle')
        self.assertEqual(result.returncode, 1)
        self.assertIn('exactly one', result.stderr)
        self.assertEqual(self.stash.read_bytes(), self.original)
        self.assertFalse(self.backups.exists())

    def test_picker_lists_saves_and_accepts_number_or_manual_case_insensitively(self):
        second = self.profile / 'ZSecond.d2i'; second.write_bytes(self.original)
        result = self.helper('pick', extra=('0\n2\n',))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(str(second), result.stdout)
        self.assertIn('DETECTED=True', result.stdout)
        result = self.helper('pick', extra=('mAnUaL\n' + str(second) + '\n',))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('DETECTED=False', result.stdout)

    def test_metadata_changes_abort_both_repair_engines_before_writing(self):
        original_metadata = self.metadata.read_bytes()
        for action in ['race-items', 'race-chronicle']:
            with self.subTest(action=action):
                self.metadata.write_bytes(original_metadata)
                result = self.helper(action)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(self.stash.read_bytes(), self.original)
                self.assertFalse(self.backups.exists())

    def test_detected_metadata_is_archived_with_a_successful_repair(self):
        result = self.helper('apply-items')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report_path, = self.backups.glob('Item-repair-*/repair-report.json')
        report = json.loads(report_path.read_text(encoding='utf-8'))
        self.assertEqual(report['status'], 'Complete')
        self.assertEqual(Path(report['detectedModInfo']['path']), self.metadata)
        self.assertEqual((report_path.parent / 'detected-modinfo.json').read_bytes(), self.metadata.read_bytes())
        self.assertEqual((report_path.parent / 'originals' / self.stash.name).read_bytes(), self.original)

    def test_cli_can_detect_tables_for_explicit_mod_and_save_without_writes(self):
        before = set(self.root.rglob('*'))
        result = subprocess.run([str(ARGS.exe), '--repair-items', '--mod-folder', str(self.mod),
                                 '--save-file', str(self.stash), '--mapping', str(self.mapping),
                                 '--include-chronicle', '--dry-run'], stdin=subprocess.DEVNULL,
                                capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('Using detected mod tables', result.stdout)
        self.assertEqual(set(self.root.rglob('*')), before)
        self.assertEqual(self.stash.read_bytes(), self.original)

    def test_backup_inside_detected_game_data_is_rejected(self):
        result = subprocess.run([str(ARGS.exe), '--repair-items', '--mod-folder', str(self.mod),
                                 '--save-file', str(self.stash), '--mapping', str(self.mapping),
                                 '--include-chronicle', '--backup-folder', str(self.strings / 'backups'), '--apply', '--yes'],
                                stdin=subprocess.DEVNULL, capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertIn('outside', result.stderr)
        self.assertEqual(self.stash.read_bytes(), self.original)
        self.assertFalse((self.strings / 'backups').exists())

    def test_cli_rejects_conflicting_auto_and_explicit_scopes(self):
        for action in ['--repair-items', '--repair-chronicle']:
            result = subprocess.run([str(ARGS.exe), action, '--auto-saves', '--save-file', str(self.stash),
                                     '--mapping', str(self.mapping), '--dry-run'], stdin=subprocess.DEVNULL,
                                    capture_output=True, encoding='utf-8-sig', timeout=30)
            self.assertEqual(result.returncode, 1, result.stdout)
            self.assertIn('OR explicit', result.stderr)

    def test_exe_inside_mod_detects_context_without_mod_argument(self):
        import shutil
        profile_name = 'Detection-Probe-' + uuid.uuid4().hex
        self.metadata.write_text(json.dumps({'name': 'Arbitrary Mod', 'savepath': profile_name + '/'}), encoding='utf-8')
        copied = self.strings / 'Copied Utility.exe'
        shutil.copyfile(ARGS.exe, copied)
        before = set(self.root.rglob('*'))
        result = subprocess.run([str(copied), '--DeTeCt-PaThS'], stdin=subprocess.DEVNULL,
                                capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 1)  # unique real profile is absent
        self.assertIn(str(self.metadata), result.stdout)
        self.assertIn(profile_name, result.stdout)
        self.assertIn(str(self.tables), result.stdout)
        self.assertIn('No folder will be created', result.stdout)
        self.assertEqual(set(self.root.rglob('*')), before)

    def test_path_detection_command_never_accepts_apply(self):
        result = subprocess.run([str(ARGS.exe), '--detect-paths', '--mod-folder', str(self.mod), '--apply', '--yes'],
                                stdin=subprocess.DEVNULL, capture_output=True, encoding='utf-8-sig', timeout=30)
        self.assertEqual(result.returncode, 1)
        self.assertIn('standalone read-only', result.stderr)
        self.assertEqual(self.stash.read_bytes(), self.original)
        self.assertFalse(self.backups.exists())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    ARGS = parser.parse_args(); ARGS.exe = ARGS.exe.resolve(strict=True); ARGS.work = ARGS.work.resolve()
    ARGS.work.mkdir(parents=True, exist_ok=True)
    ASSEMBLY = ARGS.exe.parent.parent / 'artifacts/bin/D2RLocalizationCompactor/release_win-x64/D2R-Localization-ID-Compactor.dll'
    os.environ['DOTNET_CLI_HOME'] = str(ARGS.work / '.dotnet-home')
    os.environ['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'; os.environ['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    helper = ARGS.work / 'helper'; helper.mkdir(exist_ok=True)
    (helper / 'Probe.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
    (helper / 'Program.cs').write_text(r'''
using System.Reflection;
using System.Text.Json;
var asm=Assembly.LoadFrom(args[0]); var type=asm.GetType("D2RLocalizationCompactor.ModPaths",true);
object context=type.GetMethod("DiscoverAt").Invoke(null,new object[]{args[2],args[3]});
object Field(string name)=>type.GetField(name).GetValue(context);
var metadata=Field("ModInfo"); string metadataPath=metadata==null?null:(string)metadata.GetType().GetField("Path").GetValue(metadata);
if(args[1]=="discover") {
 Console.WriteLine(JsonSerializer.Serialize(new { modInfo=metadataPath,modName=Field("ModName"),modError=Field("ModError"),saveFolder=Field("SaveFolder"),saveError=Field("SaveError"),
  saveFolderExists=type.GetProperty("SaveFolderExists").GetValue(context),tablesFolder=Field("TablesFolder"),tablesError=Field("TablesError") })); return 0;
}
try {
 var program=asm.GetType("D2RLocalizationCompactor.Program",true);
 if(args[1]=="auto-chronicle") { program.GetMethod("Repair",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{null,args[5],args[7],false,true,false,context,true}); return 0; }
 if(args[1]=="auto-items") { program.GetMethod("RepairItems",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{new List<string>(),null,args[5],null,args[7],true,false,true,false,context,true,true}); return 0; }
 if(args[1]=="pick") {
  Console.SetIn(new StringReader(args[8])); object[] call={context,true,false};
  object choice=program.GetMethod("PickSave",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,call);
  Console.WriteLine("\nCHOSEN="+choice+" DETECTED="+call[2]); return 0;
 }
 bool items=args[1].EndsWith("items"); var engine=asm.GetType("D2RLocalizationCompactor."+(items?"ItemRepair":"ChronicleRepair"),true);
 object plan=engine.GetMethod("Build").Invoke(null,items?new object[]{new[]{args[4]},null,args[5],args[6],args[7],true}:new object[]{args[4],args[5],args[7]});
 plan.GetType().GetField("DetectedModInfo").SetValue(plan,metadata);
 if(args[1].StartsWith("race-")) {
  File.AppendAllText(metadataPath,"external change");
  try { engine.GetMethod("Apply").Invoke(null,new[]{plan}); }
  catch(TargetInvocationException e) { if(e.InnerException is IOException && e.InnerException.Message.Contains("changed after preview")) return 0; throw; }
  return 2;
 }
 engine.GetMethod("Apply").Invoke(null,new[]{plan}); return 0;
} catch(TargetInvocationException e) { Console.Error.WriteLine(e.InnerException); return 1; }
''', encoding='utf-8')
    build = subprocess.run(['dotnet', 'build', str(helper / 'Probe.csproj'), '-c', 'Release', '-o', str(helper / 'output'),
                            '-p:UseSharedCompilation=false', '-nodeReuse:false'], capture_output=True, encoding='utf-8-sig', timeout=45)
    if build.returncode: raise RuntimeError(build.stdout + build.stderr)
    HELPER = helper / 'output/Probe.exe'
    unittest.main(argv=[sys.argv[0], '-v'])
