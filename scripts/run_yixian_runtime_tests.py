#!/usr/bin/env python3
"""Run isolated Mono tests; preserve progress and logs on failure, timeout or process exit."""
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
    if sys.platform == 'darwin':
        processes = subprocess.run(['ps', '-axo', 'comm='], capture_output=True, text=True, check=True)
        return any(line.strip().replace('\\', '/').rsplit('/', 1)[-1].lower() == 'reincarnationsquad.exe'
                   for line in processes.stdout.splitlines())
    for path in Path('/proc').glob('[0-9]*/cmdline'):
        try:
            first = path.read_bytes().split(b'\0')[0].decode(errors='replace').replace('\\', '/')
        except (OSError, ProcessLookupError):
            continue
        if first.rsplit('/', 1)[-1].lower() == 'reincarnationsquad.exe':
            return True
    return False


def collect_diagnostics(out, persistent=None):
    persistent = persistent or STEAM / 'steamapps/compatdata/5241190/pfx/drive_c/users/steamuser/AppData/LocalLow/yuyuyi/轮回小队'
    for source, name in [(persistent / 'Player.log', 'Player.log'),
                         (persistent / 'ModLogs/latest.log', 'ModLogs.log')]:
        if source.is_file():
            shutil.copy2(source, out / name)


def wine_environment(wine, prefix):
    env = {**os.environ, 'WINEPREFIX': str(prefix)}
    # Sikarugir ships Wine's dylibs in the enclosing app's Frameworks directory.
    for parent in wine.resolve().parents:
        frameworks = parent / 'Frameworks'
        if parent.name == 'Contents' and frameworks.is_dir():
            previous = env.get('DYLD_FALLBACK_LIBRARY_PATH', '/usr/local/lib:/usr/lib')
            env['DYLD_FALLBACK_LIBRARY_PATH'] = str(frameworks) + ':' + previous
            break
    return env


def wait_for_reports(results, suites, prior, out, timeout=120, launch_process=None):
    """A disappearing observed game is an early exit, not proof of a native crash."""
    reports = {}; progress = {}; observed_game = False
    started = time.monotonic()
    outcome = 'timeout'
    while time.monotonic() - started < timeout:
        running = game_running()
        observed_game |= running
        launch_code = launch_process.poll() if launch_process is not None else None
        if not observed_game and launch_code is not None and launch_code != 0:
            outcome = 'launch_failed'; code = 2
            print(f'启动器退出（{launch_code}），游戏未启动；检查 launch.log。', file=sys.stderr)
            break
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
            progress[suite] = report
            (out / (suite + '-progress.json')).write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
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
            # Full reports do not make an already exited process healthy.
            if not running:
                outcome = 'process_exited'; code = 4
            else:
                outcome = 'passed' if all(r['status'] == 'passed' for r in reports.values()) else 'assertion_failed'
                code = 0 if outcome == 'passed' else 1
            break
        if observed_game and not running:
            outcome = 'process_exited'; code = 4
            print('游戏进程在取得全部报告前退出；保留最后用例和日志，需据日志判断原因。', file=sys.stderr)
            break
        time.sleep(.5)
    else:
        code = 3
        print(f'{timeout}秒内未取得全部报告；检查最后用例、日志或游戏内代码许可。', file=sys.stderr)
    watchdog = dict(outcome=outcome, elapsed_seconds=round(time.monotonic() - started, 2),
                    game_observed=observed_game, game_running_at_finish=game_running(),
                    completed_suites=list(reports),
                    active_cases={suite: report.get('activeCase') for suite, report in progress.items()})
    (out / 'watchdog.json').write_text(json.dumps(watchdog, ensure_ascii=False, indent=2) + '\n')
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    managed = os.environ.get('GAME_MANAGED')
    default_game = Path(managed).parent.parent if managed else MOD.parent.parent
    parser.add_argument('--game', type=Path, default=default_game, help='game installation containing ReincarnationSquad.exe')
    parser.add_argument('--wine', type=Path, default=os.environ.get('YIXIAN_WINE'), help='macOS Wine executable from the working Steam wrapper')
    parser.add_argument('--wine-prefix', type=Path, default=os.environ.get('WINEPREFIX'), help='macOS Wine prefix containing Windows Steam')
    parser.add_argument('--persistent-data', type=Path, default=os.environ.get('YIXIAN_PERSISTENT_DATA'), help='game persistent-data directory containing Player.log')
    parser.add_argument('--timeout', type=int, default=120, help='seconds to wait for fresh reports (default: 120)')
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--include-fates', action='store_true', help='run independent fate suite in the same launch')
    group.add_argument('--schools-only', action='store_true', help='run only new-school loops and isolation checks')
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error('--timeout must be positive')
    if sys.platform == 'darwin':
        if not args.wine or not args.wine.is_file() or not args.wine_prefix or not args.persistent_data:
            parser.error('macOS requires --wine, --wine-prefix and --persistent-data (or their environment variables)')
        if not (args.wine_prefix / 'system.reg').is_file():
            parser.error('Wine prefix does not contain system.reg')
        steam_exe = args.game.parent.parent.parent / 'steam.exe'
        if not steam_exe.is_file():
            parser.error('Windows Steam executable missing beside steamapps: ' + str(steam_exe))
    elif sys.platform != 'linux':
        parser.error('This launcher supports Linux/Proton and macOS/Wine only')
    if game_running():
        print('游戏仍在运行。请先从主菜单正常退出；不会强行关闭当前游戏。', file=sys.stderr)
        return 2
    mod = args.game / 'Mods/yixian.sect'
    if not (mod / 'plugins/YixianSect.dll').is_file():
        print('未找到已安装的 YixianSect.dll。', file=sys.stderr)
        return 2
    results = mod / 'TestResults'
    suites = ['runtime', 'fates'] if args.include_fates else ['runtime']
    prior = {suite: set(results.glob(suite + '-*.json')) for suite in suites}
    out = ROOT / 'logs/yixian-runtime-tests' / ('run-' + time.strftime('%Y%m%d-%H%M%S'))
    out.mkdir(parents=True, exist_ok=False)
    flags = ['-yixian-schools-selftest' if args.schools_only else '-yixian-selftest', '-modverbose'] + (['-yixian-fates-selftest'] if args.include_fates else [])
    try:
        launch_process = None
        with (out / 'launch.log').open('w') as log:
            if sys.platform == 'darwin':
                # Existing Windows Steam handles the request; do not wait for its lifetime.
                launch_process = subprocess.Popen([str(args.wine), str(steam_exe), '-applaunch', '5241190', *flags],
                                                  env=wine_environment(args.wine, args.wine_prefix),
                                                  stdout=log, stderr=subprocess.STDOUT)
            else:
                subprocess.run([str(STEAM / 'ubuntu12_32/steam'), '-applaunch', '5241190', *flags],
                               env={**os.environ, 'DISPLAY': os.environ.get('DISPLAY', ':0')},
                               stdout=log, stderr=subprocess.STDOUT, check=True)
        return wait_for_reports(results, suites, prior, out, timeout=args.timeout, launch_process=launch_process)
    finally:
        collect_diagnostics(out, args.persistent_data)
        print('Evidence:', out.relative_to(ROOT))
        print('测试局未接入存档；不会强退游戏。如仍在运行，请通过游戏菜单正常退出。')


if __name__ == '__main__':
    raise SystemExit(main())
