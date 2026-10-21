"""The focused healing suite must not accidentally launch the full suite."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import run_yixian_runtime_tests as runner


class StarHealingRunnerTests(unittest.TestCase):
    def test_focused_mode_launches_only_healing_and_preserves_normal_quit(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            game = root / 'game'
            plugin = game / 'Mods/yixian.sect/plugins/YixianSect.dll'
            plugin.parent.mkdir(parents=True)
            plugin.touch()
            argv = ['runner', '--game', str(game), '--star-healing-only', '--quit-after-tests']
            with patch.object(sys, 'argv', argv), patch.object(runner.sys, 'platform', 'linux'), \
                    patch.object(runner, 'ROOT', root), patch.object(runner, 'game_running', return_value=False), \
                    patch.object(runner.subprocess, 'run') as launch, \
                    patch.object(runner, 'wait_for_reports', return_value=0) as wait, \
                    patch.object(runner, 'collect_diagnostics'), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(), 0)
            command = launch.call_args.args[0]
            self.assertIn('-yixian-selftest', command)
            self.assertIn('-yixian-star-healing-selftest', command)
            self.assertIn('-yixian-selftest-quit', command)
            self.assertNotIn('-yixian-schools-selftest', command)
            self.assertNotIn('-yixian-fates-selftest', command)
            self.assertEqual(wait.call_args.args[1], ['runtime'])
            self.assertTrue(wait.call_args.kwargs['quit_after_tests'])
            self.assertEqual(wait.call_args.kwargs['expected_scope'], 'star-selected-healing')

    def test_old_dll_full_suite_report_cannot_pass_as_healing_specialist(self):
        for scope, expected in [('full', 5), ('star-selected-healing', 0)]:
            with self.subTest(scope=scope), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp); results = root / 'results'; results.mkdir(); out = root / 'out'; out.mkdir()
                report = dict(status='passed', scope=scope, cases=[], passed=4, failed=0)
                (results / 'runtime-new.json').write_text(json.dumps(report))
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    code = runner.wait_for_reports(results, ['runtime'], {'runtime': set()}, out,
                                                   is_running=lambda: True, expected_scope='star-selected-healing')
                self.assertEqual(code, expected)
                self.assertEqual(json.loads((out / 'results.json').read_text()), report)
                watchdog = json.loads((out / 'watchdog.json').read_text())
                self.assertEqual(watchdog['actual_scope'], scope)
                self.assertEqual(watchdog['outcome'], 'scope_mismatch' if expected else 'passed')

    def test_incompatible_scopes_are_rejected_before_launch(self):
        for flag in ['--include-fates', '--schools-only', '--costs-only', '--balance', '--headless']:
            with self.subTest(flag=flag), patch.object(sys, 'argv', ['runner', '--star-healing-only', flag]), \
                    patch.object(runner.subprocess, 'run') as launch, contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as result:
                    runner.main()
                self.assertEqual(result.exception.code, 2)
                launch.assert_not_called()
