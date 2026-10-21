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
            argv = ['install', '--game', str(game), '--source', str(source), '--apply']
            with patch.object(sys, 'argv', argv), patch.object(install, 'ROOT', root), \
                    patch.object(install, 'game_running', return_value=False), \
                    patch.object(install.subprocess, 'run') as check, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(install.main(), 0)
            self.assertEqual(check.call_args.args[0][-2:], ['--mod-root', str(source)])
            self.assertEqual((target / 'plugins/YixianSect.dll').read_bytes(), b'accepted binary')
            receipt = json.loads(next((root / 'logs/installation').glob('*/receipt.json')).read_text())
            self.assertEqual((Path(receipt['backup']) / 'plugins/YixianSect.dll').read_bytes(), b'previous binary')
            self.assertTrue((target / 'TestResults/retained.json').is_file())
            self.assertTrue((target / 'content/local-notes.json').is_file())
            self.assertEqual(receipt['removed_retired_files'], sorted(retired))
            for relative in retired:
                self.assertFalse((target / relative).exists())
                self.assertEqual((Path(receipt['backup']) / relative).read_bytes(), b'retired feature')

    def test_selected_source_files_are_not_pruned(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); source = root / 'accepted'; game = root / 'game'
            (source / 'plugins').mkdir(parents=True)
            (source / 'plugins/YixianSect.dll').write_bytes(b'accepted binary')
            (source / 'content').mkdir()
            (source / 'content/professions.json').write_text('{}')
            (source / 'mod.json').write_text(json.dumps(dict(id='yixian.sect', version='test', assets={})))
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
