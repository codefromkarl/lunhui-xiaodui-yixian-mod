"""A focused animation report must not be confused with old/full runtime evidence."""
import contextlib
import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import run_yixian_runtime_tests as runner

class DepthRunnerTests(unittest.TestCase):
    def test_focused_depth_uses_expected_scope_and_normal_quit(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);game=root/'game';plugin=game/'Mods/yixian.sect/plugins/YixianSect.dll';plugin.parent.mkdir(parents=True);plugin.touch()
            with patch.object(sys,'argv',['runner','--game',str(game),'--depth-effects-only','--quit-after-tests']), patch.object(runner.sys,'platform','linux'), patch.object(runner,'ROOT',root), patch.object(runner,'game_running',return_value=False), patch.object(runner.subprocess,'run') as launch, patch.object(runner,'wait_for_reports',return_value=0) as wait, patch.object(runner,'collect_diagnostics'), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(),0)
            command=launch.call_args.args[0]
            self.assertIn('-yixian-depth-selftest',command)
            self.assertIn('-yixian-selftest-quit',command)
            self.assertNotIn('-yixian-fates-selftest',command)
            self.assertEqual(wait.call_args.kwargs['expected_scope'],'sect-depth-effects')

    def test_focused_spirit_uses_specific_scope_and_does_not_run_fates(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);game=root/'game';plugin=game/'Mods/yixian.sect/plugins/YixianSect.dll';plugin.parent.mkdir(parents=True);plugin.touch()
            with patch.object(sys,'argv',['runner','--game',str(game),'--sword-scaling-only','--quit-after-tests']), patch.object(runner.sys,'platform','linux'), patch.object(runner,'ROOT',root), patch.object(runner,'game_running',return_value=False), patch.object(runner.subprocess,'run') as launch, patch.object(runner,'wait_for_reports',return_value=0) as wait, patch.object(runner,'collect_diagnostics'), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(),0)
            command=launch.call_args.args[0]
            self.assertIn('-yixian-spirit-selftest',command)
            self.assertIn('-yixian-selftest-quit',command)
            self.assertNotIn('-yixian-fates-selftest',command)
            self.assertEqual(wait.call_args.kwargs['expected_scope'],'sword-spirit-conversion')
