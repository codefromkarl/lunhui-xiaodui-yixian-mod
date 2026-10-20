"""Regression tests for real failure paths, without starting or terminating a game."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import run_yixian_runtime_tests as runner


class WatchdogTests(unittest.TestCase):
    def test_macos_detects_wine_game_with_spaces(self):
        result = subprocess.CompletedProcess([], 0, stdout='C:\\Program Files (x86)\\Steam\\Steam.exe\nC:\\Games\\轮回小队 Demo\\ReincarnationSquad.exe\n')
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=result):
            self.assertTrue(runner.game_running())

    def test_macos_ignores_other_processes(self):
        result = subprocess.CompletedProcess([], 0, stdout='C:\\Program Files (x86)\\Steam\\Steam.exe\n/usr/bin/python3\n')
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=result):
            self.assertFalse(runner.game_running())

    def test_macos_process_detection_fails_closed(self):
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, 'ps')):
            with self.assertRaises(subprocess.CalledProcessError):
                runner.game_running()

    def test_other_game_install_does_not_block_target_or_mask_its_exit(self):
        result = subprocess.CompletedProcess([], 0, stdout='/Games/Other Copy/ReincarnationSquad.exe\n')
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=result):
            self.assertTrue(runner.game_running())
            self.assertFalse(runner.game_running(Path('/Games/Target Copy')))
            self.assertTrue(runner.game_running(Path('/Games/Other Copy')))

    def test_windows_process_path_is_resolved_inside_target_wine_prefix(self):
        result = subprocess.CompletedProcess([], 0, stdout='C:\\Games\\轮回小队 Demo\\ReincarnationSquad.exe\n')
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=result):
            self.assertTrue(runner.game_running(Path('/Wine Prefix/drive_c/Games/轮回小队 Demo'), Path('/Wine Prefix')))
            self.assertFalse(runner.game_running(Path('/Games/Another Copy'), Path('/Wine Prefix')))

    def test_unresolvable_windows_path_still_blocks_install(self):
        result = subprocess.CompletedProcess([], 0, stdout='C:\\Games\\轮回小队 Demo\\ReincarnationSquad.exe\n')
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=result), patch.dict(runner.os.environ, {}, clear=True):
            self.assertTrue(runner.game_running(Path('/Games/Target')))

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.results = Path(self.temp.name) / 'results'; self.results.mkdir()
        self.out = Path(self.temp.name) / 'out'; self.out.mkdir()

    def write_report(self, name, status, active=None):
        row = dict(status=status, cases=[], passed=1, failed=0)
        if active: row['activeCase'] = active
        (self.results / name).write_text(json.dumps(row))

    def test_process_exit_preserves_in_progress_case(self):
        self.write_report('runtime-new.json', 'running', 'health_queue_boundary')
        with patch.object(runner, 'game_running', side_effect=[True, False, False]), patch.object(runner.time, 'sleep'):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out)
        self.assertEqual(code, 4)
        watchdog = json.loads((self.out / 'watchdog.json').read_text())
        self.assertEqual(watchdog['outcome'], 'process_exited')
        self.assertEqual(watchdog['active_cases']['runtime'], 'health_queue_boundary')
        self.assertTrue((self.out / 'runtime-progress.json').is_file())

    def test_all_suites_required_and_old_reports_not_reused(self):
        self.write_report('runtime-old.json', 'passed')
        self.write_report('runtime-new.json', 'passed')
        self.write_report('fates-new.json', 'failed')
        prior = {'runtime': {self.results / 'runtime-old.json'}, 'fates': set()}
        with patch.object(runner, 'game_running', return_value=True):
            code = runner.wait_for_reports(self.results, ['runtime', 'fates'], prior, self.out)
        self.assertEqual(code, 1)
        self.assertTrue((self.out / 'results.json').is_file())
        self.assertTrue((self.out / 'fate-results.json').is_file())
        self.assertEqual(json.loads((self.out / 'watchdog.json').read_text())['outcome'], 'assertion_failed')

    def test_exit_is_failure_even_with_complete_reports(self):
        self.write_report('runtime-new.json', 'passed')
        with patch.object(runner, 'game_running', return_value=False):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out)
        self.assertEqual(code, 4)

    def test_requested_exit_requires_matching_receipt_and_process_exit(self):
        self.write_report('runtime-new.json', 'passed')
        (self.results / 'quit-runtime-new.json').write_text(json.dumps(dict(status='quit_requested', reports={'runtime': 'runtime-new.json'})))
        with patch.object(runner, 'game_running', side_effect=[True, False, False]), patch.object(runner.time, 'sleep'):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, quit_after_tests=True)
        self.assertEqual(code, 0)
        self.assertEqual(json.loads((self.out / 'watchdog.json').read_text())['outcome'], 'passed_and_quit')
        self.assertTrue((self.out / 'quit-receipt.json').is_file())

    def test_exit_without_receipt_is_still_failure_in_quit_mode(self):
        self.write_report('runtime-new.json', 'passed')
        with patch.object(runner, 'game_running', return_value=False):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, quit_after_tests=True)
        self.assertEqual(code, 4)

    def test_stale_exit_receipt_is_not_reused(self):
        self.write_report('runtime-new.json', 'passed')
        (self.results / 'quit-runtime-old.json').write_text(json.dumps(dict(status='quit_requested', reports={'runtime': 'runtime-old.json'})))
        with patch.object(runner, 'game_running', return_value=False):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, quit_after_tests=True)
        self.assertEqual(code, 4)

    def test_quit_receipt_cannot_hide_an_incomplete_suite(self):
        self.write_report('runtime-new.json', 'passed')
        self.write_report('fates-new.json', 'running', 'unfinished')
        (self.results / 'quit-runtime-new.json').write_text(json.dumps(dict(status='quit_requested', reports={'runtime': 'runtime-new.json'})))
        with patch.object(runner, 'game_running', side_effect=[True, False, False]), patch.object(runner.time, 'sleep'):
            code = runner.wait_for_reports(self.results, ['runtime', 'fates'], {'runtime': set(), 'fates': set()}, self.out, quit_after_tests=True)
        self.assertEqual(code, 4)

    def test_clean_exit_does_not_hide_assertion_failure(self):
        self.write_report('runtime-new.json', 'failed')
        (self.results / 'quit-runtime-new.json').write_text(json.dumps(dict(status='quit_requested', reports={'runtime': 'runtime-new.json'})))
        with patch.object(runner, 'game_running', return_value=False):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, quit_after_tests=True)
        self.assertEqual(code, 1)
        self.assertEqual(json.loads((self.out / 'watchdog.json').read_text())['outcome'], 'assertion_failed_and_quit')

    def test_timeout_has_evidence_without_killing_process(self):
        with patch.object(runner, 'game_running', return_value=True):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, timeout=0)
        self.assertEqual(code, 3)
        self.assertTrue(json.loads((self.out / 'watchdog.json').read_text())['game_running_at_finish'])

    def test_direct_process_probe_is_not_masked_by_another_game(self):
        self.write_report('runtime-new.json', 'passed')
        (self.results / 'quit-runtime-new.json').write_text(json.dumps(dict(status='quit_requested', reports={'runtime': 'runtime-new.json'})))
        with patch.object(runner, 'game_running', return_value=True) as other_install:
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, quit_after_tests=True, is_running=lambda: False)
        self.assertEqual(code, 0)
        other_install.assert_not_called()
        self.assertFalse(json.loads((self.out / 'watchdog.json').read_text())['game_running_at_finish'])

    def test_failure_logs_are_copied(self):
        steam = Path(self.temp.name) / 'steam'
        persistent = steam / 'steamapps/compatdata/5241190/pfx/drive_c/users/steamuser/AppData/LocalLow/yuyuyi/轮回小队'
        (persistent / 'ModLogs').mkdir(parents=True)
        (persistent / 'Player.log').write_text('fatal example')
        (persistent / 'ModLogs/latest.log').write_text('last mod hook')
        with patch.object(runner, 'STEAM', steam): runner.collect_diagnostics(self.out)
        self.assertEqual((self.out / 'Player.log').read_text(), 'fatal example')
        self.assertEqual((self.out / 'ModLogs.log').read_text(), 'last mod hook')

    def test_explicit_macos_log_directory(self):
        persistent = Path(self.temp.name) / 'Wine User' / 'AppData' / 'LocalLow' / '轮回小队'
        persistent.mkdir(parents=True)
        (persistent / 'Player.log').write_text('Wine Unity log')
        runner.collect_diagnostics(self.out, persistent)
        self.assertEqual((self.out / 'Player.log').read_text(), 'Wine Unity log')

    def test_failed_wine_launch_does_not_wait_for_report_timeout(self):
        process = Mock()
        process.poll.return_value = 127
        with patch.object(runner, 'game_running', return_value=False):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, launch_process=process)
        self.assertEqual(code, 2)
        self.assertEqual(json.loads((self.out / 'watchdog.json').read_text())['outcome'], 'launch_failed')

    def test_sikarugir_library_path_preserves_existing_fallback(self):
        contents = Path(self.temp.name) / 'Game Wrapper.app' / 'Contents'
        wine = contents / 'SharedSupport' / 'wine' / 'bin' / 'wine'
        wine.parent.mkdir(parents=True)
        wine.touch()
        (contents / 'Frameworks').mkdir()
        with patch.dict(runner.os.environ, {'DYLD_FALLBACK_LIBRARY_PATH': '/existing/libs'}):
            env = runner.wine_environment(wine, Path(self.temp.name) / 'prefix')
        self.assertEqual(env['DYLD_FALLBACK_LIBRARY_PATH'], str((contents / 'Frameworks').resolve()) + ':/existing/libs')


if __name__ == '__main__': unittest.main()
