"""Install the selected accepted artifact, never an unrelated shared rebuild."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import install
import run_yixian_runtime_tests as runner


class FrozenInstallTests(unittest.TestCase):
    def test_selected_source_is_checked_installed_and_previous_package_retained(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); source = root / 'accepted'; game = root / 'game'
            target = game / 'Mods/yixian.sect'
            (source / 'plugins').mkdir(parents=True)
            (source / 'plugins/YixianSect.dll').write_bytes(b'accepted binary')
            (source / 'mod.json').write_text(json.dumps(dict(id='yixian.sect', version='test', assets={})))
            (target / 'plugins').mkdir(parents=True)
            (target / 'plugins/YixianSect.dll').write_bytes(b'previous binary')
            (target / 'TestResults').mkdir()
            (target / 'TestResults/retained.json').write_text('{}')
            retired = ['content/professions.json', 'content/alchemy.json', 'content/cardlevels.json',
                       'icons/professions/alchemy.png', 'icons/alchemy/healing.png']
            for relative in retired:
                path = target / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'retired feature')
            (target / 'content/local-notes.json').write_text('{}')
            game.mkdir(parents=True, exist_ok=True)
            (game / 'ReincarnationSquad.exe').touch()
            argv = ['install', '--game', str(game), '--source', str(source), '--apply']
            with patch.object(sys, 'argv', argv), patch.object(install, 'ROOT', root), \
                    patch.object(install, 'game_running', return_value=False), \
                    patch.object(install.subprocess, 'run') as check, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(install.main(), 0)
            self.assertEqual(check.call_args.args[0][-2:], ['--mod-root', str(source)])
            self.assertEqual((target / 'plugins/YixianSect.dll').read_bytes(), b'accepted binary')
            receipt = json.loads(next((root / 'logs/installation').glob('*/receipt.json')).read_text())
            self.assertEqual((Path(receipt['backup']) / 'yixian.sect/plugins/YixianSect.dll').read_bytes(), b'previous binary')
            self.assertFalse((target / 'TestResults/retained.json').exists())
            self.assertFalse((target / 'content/local-notes.json').exists())
            self.assertTrue((Path(receipt['backup']) / 'yixian.sect/TestResults/retained.json').is_file())
            self.assertEqual(receipt['packages'][0]['removed_files'], sorted(retired+['TestResults/retained.json','content/local-notes.json']))
            for relative in retired:
                self.assertFalse((target / relative).exists())
                self.assertEqual((Path(receipt['backup']) / 'yixian.sect' / relative).read_bytes(), b'retired feature')

    def test_selected_source_files_are_not_pruned(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); source = root / 'accepted'; game = root / 'game'
            (source / 'plugins').mkdir(parents=True)
            (source / 'plugins/YixianSect.dll').write_bytes(b'accepted binary')
            (source / 'content').mkdir()
            (source / 'content/professions.json').write_text('{}')
            (source / 'mod.json').write_text(json.dumps(dict(id='yixian.sect', version='test', assets={}, content=['content/professions.json'])))
            game.mkdir(parents=True, exist_ok=True)
            (game / 'ReincarnationSquad.exe').touch()
            argv = ['install', '--game', str(game), '--source', str(source), '--apply']
            with patch.object(sys, 'argv', argv), patch.object(install, 'ROOT', root), \
                    patch.object(install, 'game_running', return_value=False), \
                    patch.object(install.subprocess, 'run'), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(install.main(), 0)
            self.assertTrue((game / 'Mods/yixian.sect/content/professions.json').is_file())

    def test_focused_cultivation_launch_requires_matching_scope_and_normal_quit(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); game = root / 'game'
            plugin = game / 'Mods/yixian.sect/plugins/YixianSect.dll'
            plugin.parent.mkdir(parents=True); plugin.touch()
            argv = ['runner', '--game', str(game), '--cultivation-only', '--quit-after-tests']
            with patch.object(sys, 'argv', argv), patch.object(runner.sys, 'platform', 'linux'), \
                    patch.object(runner, 'ROOT', root), patch.object(runner, 'game_running', return_value=False), \
                    patch.object(runner.subprocess, 'run') as launch, \
                    patch.object(runner, 'wait_for_reports', return_value=0) as wait, \
                    patch.object(runner, 'collect_diagnostics'), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(), 0)
            self.assertIn('-yixian-cultivation-selftest', launch.call_args.args[0])
            self.assertIn('-yixian-selftest-quit', launch.call_args.args[0])
            self.assertEqual(wait.call_args.kwargs['expected_scope'], 'five-cultivation')
            self.assertEqual(wait.call_args.args[1], ['runtime'])


if __name__ == '__main__':
    unittest.main()

class CleanInstallTransactionTests(unittest.TestCase):
    def fixture(self, root):
        from runtime_package import PLUGINS
        game = root / 'game'; game.mkdir(); (game / 'ReincarnationSquad.exe').touch()
        sources = {}
        for slug, dll in PLUGINS.items():
            p = root / slug; (p / 'plugins').mkdir(parents=True)
            (p / dll).write_bytes(b'new')
            (p / 'mod.json').write_text(json.dumps(dict(id=slug, version='test', assets={})))
            if slug == 'appearance.muyifeng': (p / 'appearance.json').write_text('{}')
            target = game / 'Mods' / slug; target.mkdir(parents=True)
            (target / 'old.txt').write_bytes(b'old')
            sources[slug] = p
        (game / 'Mods/enabled.json').write_bytes(b'keep enabled')
        (game / 'mod-code-consent.json').write_bytes(b'keep consent')
        return game, sources

    def test_two_package_update_cleans_old_files_and_keeps_activation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); game, sources = self.fixture(root)
            with patch.object(install, 'game_running', return_value=False):
                result = install.install_packages(game, sources, root / 'backups')
            self.assertTrue(result['installed'])
            for slug in sources:
                self.assertFalse((game / 'Mods' / slug / 'old.txt').exists())
                self.assertEqual((Path(result['backup']) / slug / 'old.txt').read_bytes(), b'old')
            self.assertEqual((game / 'Mods/enabled.json').read_bytes(), b'keep enabled')
            self.assertEqual((game / 'mod-code-consent.json').read_bytes(), b'keep consent')

    def test_second_switch_failure_rolls_back_both_packages(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); game, sources = self.fixture(root)
            replace = install.os.replace
            def fail_second(source, target):
                if Path(source).name == 'appearance.muyifeng' and '.yixian-install-' in str(source):
                    raise OSError('simulated second-package switch failure')
                return replace(source, target)
            with patch.object(install, 'game_running', return_value=False), patch.object(install.os, 'replace', side_effect=fail_second):
                with self.assertRaisesRegex(OSError, 'simulated'):
                    install.install_packages(game, sources, root / 'backups')
            for slug in sources:
                self.assertEqual([p.name for p in (game / 'Mods' / slug).iterdir()], ['old.txt'])
                self.assertEqual((game / 'Mods' / slug / 'old.txt').read_bytes(), b'old')
            self.assertFalse(list(game.glob('.yixian-install-*')))

    def test_game_started_during_staging_cancels_without_replacing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); game, sources = self.fixture(root)
            with patch.object(install, 'game_running', side_effect=[False, True]):
                with self.assertRaisesRegex(ValueError, '准备期间'):
                    install.install_packages(game, sources, root / 'backups')
            for slug in sources: self.assertTrue((game / 'Mods' / slug / 'old.txt').exists())
