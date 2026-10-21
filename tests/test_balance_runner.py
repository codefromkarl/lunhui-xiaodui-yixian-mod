"""Independent-copy process tracking must not mistake the player's game for a probe."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import run_yixian_runtime_tests as runner


class BalanceRunnerTests(unittest.TestCase):
    def test_scoped_process_ignores_other_installation(self):
        listing = subprocess.CompletedProcess([], 0, stdout='C:\\Steam\\ReincarnationSquad.exe\n/Users/test/other/ReincarnationSquad.exe\n')
        with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=listing):
            self.assertTrue(runner.game_running())
            self.assertFalse(runner.game_running(Path('/Users/test/probe'), Path('/Users/test/prefix')))

    def test_scoped_process_accepts_wine_z_drive(self):
        executable = Path('/Users/test/probe/ReincarnationSquad.exe')
        for name in [str(executable), 'Z:\\Users\\test\\probe\\ReincarnationSquad.exe']:
            listing = subprocess.CompletedProcess([], 0, stdout=name+'\n')
            with patch.object(runner.sys, 'platform', 'darwin'), patch.object(runner.subprocess, 'run', return_value=listing):
                self.assertTrue(runner.game_running(executable.parent))

    def test_balance_artifact_export_does_not_require_visuals(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); results=root/'results'; results.mkdir(); out=root/'out'; out.mkdir()
            (results/'runtime-new.json').write_text(json.dumps(dict(status='passed',cases=[],passed=1,failed=0)))
            artifact=dict(rows=[dict(status='completed')])
            (results/'balance-runtime-new.json').write_text(json.dumps(artifact))
            with patch.object(runner, 'game_running', return_value=True):
                self.assertEqual(runner.wait_for_reports(results,['runtime'],{'runtime':set()},out),0)
            self.assertEqual(json.loads((out/'balance-results.json').read_text()),artifact)

    def test_early_exit_keeps_partial_matrix_without_counting_it_as_a_pass(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); results=root/'results'; results.mkdir(); out=root/'out'; out.mkdir()
            (results/'runtime-new.json').write_text(json.dumps(dict(status='running',scope='balance',activeCase='balance_matrix')))
            artifact=dict(rows=[dict(status='completed')])
            (results/'balance-runtime-new.json').write_text(json.dumps(artifact))
            (results/'balance-active.json').write_text(json.dumps(dict(report='runtime-new.json',seed=91001,card='last-card')))
            with patch.object(runner, 'game_running', side_effect=[True,False,False]), patch.object(runner.time,'sleep'):
                self.assertEqual(runner.wait_for_reports(results,['runtime'],{'runtime':set()},out),4)
            self.assertEqual(json.loads((out/'balance-progress.json').read_text()),artifact)
            self.assertEqual(json.loads((out/'balance-active.json').read_text())['card'],'last-card')
            self.assertFalse((out/'results.json').exists())
