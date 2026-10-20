#!/usr/bin/env python3
"""Run explicit in-game Mono suites using isolated memory runs, never user saves."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
STEAM = Path.home() / '.local/share/Steam'
MOD = STEAM / 'steamapps/common/轮回小队 Demo/Mods/yixian.sect'


def game_running():
    for path in Path('/proc').glob('[0-9]*/cmdline'):
        try:
            first = path.read_bytes().split(b'\0')[0].decode(errors='replace').replace('\\', '/')
        except (OSError, ProcessLookupError):
            continue
        if first.rsplit('/', 1)[-1].lower() == 'reincarnationsquad.exe':
            return True
    return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--include-fates', action='store_true', help='run the independent fate suite in the same launch')
    group.add_argument('--schools-only', action='store_true', help='rerun only new-school command loops and isolation checks')
    args = parser.parse_args()
    if game_running():
        print('游戏仍在运行。请先从主菜单正常退出；不会强行关闭当前游戏。', file=sys.stderr)
        return 2
    if not (MOD / 'plugins/YixianSect.dll').is_file():
        print('未找到已安装的 YixianSect.dll。', file=sys.stderr)
        return 2
    results = MOD / 'TestResults'
    suites = ['runtime', 'fates'] if args.include_fates else ['runtime']
    prior = {suite: set(results.glob(suite + '-*.json')) for suite in suites}
    out = ROOT / 'logs/yixian-runtime-tests' / ('run-' + time.strftime('%Y%m%d-%H%M%S'))
    out.mkdir(parents=True, exist_ok=False)
    flags = ['-yixian-schools-selftest' if args.schools_only else '-yixian-selftest', '-modverbose'] + (['-yixian-fates-selftest'] if args.include_fates else [])
    with (out / 'launch.log').open('w') as log:
        subprocess.run([str(STEAM / 'ubuntu12_32/steam'), '-applaunch', '5241190', *flags], env={**os.environ, 'DISPLAY': os.environ.get('DISPLAY', ':0')}, stdout=log, stderr=subprocess.STDOUT, check=True)
    reports = {}
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        for suite in suites:
            if suite in reports:
                continue
            files = sorted(set(results.glob(suite + '-*.json')) - prior[suite])
            if not files:
                continue
            try:
                report = json.loads(files[-1].read_text())
            except (OSError, json.JSONDecodeError):
                continue
            if report.get('status') not in ('passed', 'failed'):
                continue
            reports[suite] = report
            name = 'results.json' if suite == 'runtime' else 'fate-results.json'
            (out / name).write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
            visual = results / files[-1].stem
            if visual.is_dir():
                shutil.copytree(visual, out / ('visual' if suite == 'runtime' else 'fate-visual'), dirs_exist_ok=True)
            print(suite, report.get('runtime'), report.get('modVersion'), f'{report.get("passed")} passed / {report.get("failed")} failed')
            for case in report.get('cases', []):
                print(case['status'].upper(), case['name'])
                if case.get('status') == 'failed':
                    print(case.get('error', case.get('warnings_and_errors', '')))
        if len(reports) == len(suites):
            persistent = STEAM / 'steamapps/compatdata/5241190/pfx/drive_c/users/steamuser/AppData/LocalLow/yuyuyi/轮回小队'
            for source, name in [(persistent / 'Player.log', 'Player.log'), (persistent / 'ModLogs/latest.log', 'ModLogs.log')]:
                if source.is_file(): shutil.copy2(source, out / name)
            print('Evidence:', out.relative_to(ROOT))
            print('测试局未接入存档；游戏保持运行。请通过游戏菜单正常退出。')
            return 0 if all(report['status'] == 'passed' for report in reports.values()) else 1
        time.sleep(.5)
    print('120秒内未取得全部报告；游戏未被强行关闭。检查日志或在游戏内确认代码许可。', file=sys.stderr)
    print('Evidence:', out.relative_to(ROOT))
    return 3


if __name__ == '__main__':
    raise SystemExit(main())
