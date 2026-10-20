"""Regression tests for real failure paths, without starting or terminating a game."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import run_yixian_runtime_tests as runner


class WatchdogTests(unittest.TestCase):
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

    def test_timeout_has_evidence_without_killing_process(self):
        with patch.object(runner, 'game_running', return_value=True):
            code = runner.wait_for_reports(self.results, ['runtime'], {'runtime': set()}, self.out, timeout=0)
        self.assertEqual(code, 3)
        self.assertTrue(json.loads((self.out / 'watchdog.json').read_text())['game_running_at_finish'])

    def test_failure_logs_are_copied(self):
        steam = Path(self.temp.name) / 'steam'
        persistent = steam / 'steamapps/compatdata/5241190/pfx/drive_c/users/steamuser/AppData/LocalLow/yuyuyi/轮回小队'
        (persistent / 'ModLogs').mkdir(parents=True)
        (persistent / 'Player.log').write_text('fatal example')
        (persistent / 'ModLogs/latest.log').write_text('last mod hook')
        with patch.object(runner, 'STEAM', steam): runner.collect_diagnostics(self.out)
        self.assertEqual((self.out / 'Player.log').read_text(), 'fatal example')
        self.assertEqual((self.out / 'ModLogs.log').read_text(), 'last mod hook')


if __name__ == '__main__': unittest.main()
