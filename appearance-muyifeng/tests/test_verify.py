import argparse
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('appearance_verify', Path(__file__).resolve().parents[1] / 'scripts/verify.py')
verify = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify)


class VerificationLauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / 'game'
        self.game.mkdir()
        (self.game / 'ReincarnationSquad.exe').touch()
        self.proton = self.root / 'proton'
        self.proton.touch()
        self.args = argparse.Namespace(game=self.game, timeout=180, proton=self.proton,
            compat_data=self.root / 'test-prefix', wine=None, wine_prefix=None)

    def test_linux_uses_proton_with_separate_prefix_and_graphics(self):
        command, env = verify.launch_settings(self.args, self.root / 'evidence', platform='linux')
        self.assertEqual(command[:3], [str(self.proton), 'run', str(self.game / 'ReincarnationSquad.exe')])
        self.assertIn('-muyifeng-verify', command)
        self.assertIn('-muyifeng-quit', command)
        self.assertNotIn('-nographics', command)
        self.assertEqual(env['STEAM_COMPAT_DATA_PATH'], str(self.root / 'test-prefix'))
        self.assertFalse(self.args.compat_data.exists())

    def test_keyposes_is_a_graphical_partial_run(self):
        self.args.keyposes = True
        command, _ = verify.launch_settings(self.args, self.root / 'evidence', platform='linux')
        self.assertIn('-muyifeng-keyposes', command)
        self.assertNotIn('-muyifeng-static', command)
        self.assertNotIn('-nographics', command)

    def test_main_game_prefix_is_rejected_before_launch(self):
        self.args.compat_data = verify.STEAM / 'steamapps/compatdata/5241190'
        with self.assertRaisesRegex(ValueError, 'dedicated test'):
            verify.launch_settings(self.args, self.root, platform='linux')

    def test_mac_wine_path_preserved(self):
        wine = self.root / 'Steam.app/Contents/SharedSupport/wine/bin/wine'
        wine.parent.mkdir(parents=True)
        wine.touch()
        self.args.wine = wine
        self.args.wine_prefix = self.root / 'wine-prefix'
        command, env = verify.launch_settings(self.args, self.root, platform='darwin')
        self.assertEqual(command[0], str(wine))
        self.assertEqual(env['WINEPREFIX'], str(self.args.wine_prefix))
        self.assertIn('renderer/dxmt/wine', env['WINEDLLPATH_DXMT'])

    def prepare_source(self):
        source = self.root / 'mod/appearance.muyifeng'
        (source / 'plugins').mkdir(parents=True)
        (source / 'plugins/MuyifengAppearance.dll').touch()
        (source / 'mod.json').write_text(json.dumps({'author': 'test-author'}))
        return source

    def authorize_fixture(self):
        (self.game / 'Mods').mkdir()
        (self.game / 'Mods/enabled.json').write_text(json.dumps({'packages': ['appearance.muyifeng']}))
        (self.game / 'mod-code-consent.json').write_text(json.dumps({'version': 2, 'packages': {'appearance.muyifeng': {'author': 'test-author'}}}))

    def test_preflight_does_not_self_enable_or_grant_code_consent(self):
        self.prepare_source()
        before = sorted(self.game.rglob('*'))
        with patch.object(verify, 'ROOT', self.root), patch.object(verify, 'game_running', return_value=False):
            with self.assertRaisesRegex(ValueError, 'Enable appearance'):
                verify.preflight(self.args)
        self.assertEqual(before, sorted(self.game.rglob('*')))

    def test_wrong_code_author_is_rejected_without_installing(self):
        self.prepare_source()
        self.authorize_fixture()
        consent = self.game / 'mod-code-consent.json'
        consent.write_text(json.dumps({'version': 2, 'packages': {'appearance.muyifeng': {'author': 'wrong-author'}}}))
        before = consent.read_bytes()
        with patch.object(verify, 'ROOT', self.root), patch.object(verify, 'game_running', return_value=False):
            with self.assertRaisesRegex(ValueError, 'explicit code consent'):
                verify.preflight(self.args)
        self.assertEqual(before, consent.read_bytes())
        self.assertFalse((self.game / 'Mods/appearance.muyifeng').exists())

    def test_authorized_preflight_is_read_only(self):
        source = self.prepare_source()
        self.authorize_fixture()
        before = {str(p.relative_to(self.game)): p.read_bytes() for p in self.game.rglob('*') if p.is_file()}
        with patch.object(verify, 'ROOT', self.root), patch.object(verify, 'game_running', return_value=False):
            result, _, _ = verify.preflight(self.args)
        self.assertEqual(result, source)
        self.assertEqual(before, {str(p.relative_to(self.game)): p.read_bytes() for p in self.game.rglob('*') if p.is_file()})
        self.assertFalse(self.args.compat_data.exists())

    def test_running_target_is_not_overwritten(self):
        with patch.object(verify, 'game_running', return_value=True):
            with self.assertRaisesRegex(ValueError, 'close it normally'):
                verify.preflight(self.args)
        self.assertFalse((self.game / 'Mods').exists())

    def test_isolated_candidate_is_selected_without_changing_authorization(self):
        source = self.prepare_source()
        self.authorize_fixture()
        (source / 'mod.json').write_text(json.dumps({'id': 'appearance.muyifeng', 'author': 'test-author'}))
        self.args.package = source
        before = (self.game / 'mod-code-consent.json').read_bytes()
        with patch.object(verify, 'ROOT', self.root / 'unused-shared-build'), patch.object(verify, 'game_running', return_value=False):
            selected, _, _ = verify.preflight(self.args)
        self.assertEqual(selected, source)
        self.assertEqual((self.game / 'mod-code-consent.json').read_bytes(), before)

    def test_wrong_isolated_package_is_rejected_before_install(self):
        source = self.prepare_source()
        (source / 'mod.json').write_text(json.dumps({'id': 'another.package', 'author': 'test-author'}))
        self.args.package = source
        with patch.object(verify, 'game_running', return_value=False):
            with self.assertRaisesRegex(ValueError, 'isolated candidate'):
                verify.preflight(self.args)
        self.assertFalse((self.game / 'Mods').exists())


if __name__ == '__main__':
    unittest.main()
