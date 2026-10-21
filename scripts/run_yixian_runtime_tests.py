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


def game_running(game=None, wine_prefix=None):
    def matches(first):
        value = first.strip().replace('\\', '/')
        if value.rsplit('/', 1)[-1].lower() != 'reincarnationsquad.exe':
            return False
        if game is None:
            return True
        if len(value) > 2 and value[1:3] == ':/':
            drive, relative = value[0].lower(), value[3:]
            prefix = wine_prefix or os.environ.get('WINEPREFIX')
            if drive == 'z':
                value = '/' + relative
            elif prefix:
                device = Path(prefix) / 'dosdevices' / (drive + ':')
                root = device if device.is_dir() else Path(prefix) / 'drive_c' if drive == 'c' else None
                if root is None:
                    return True  # Cannot identify the installation: fail closed.
                value = str(root / relative)
            else:
                return True
        candidate = Path(value)
        if not candidate.is_absolute():
            return True
        return str(candidate.resolve()).casefold() == str((Path(game) / 'ReincarnationSquad.exe').resolve()).casefold()

    if sys.platform == 'darwin':
        processes = subprocess.run(['ps', '-axo', 'comm='], capture_output=True, text=True, check=True)
        return any(matches(line) for line in processes.stdout.splitlines())
    for path in Path('/proc').glob('[0-9]*/cmdline'):
        try:
            first = path.read_bytes().split(b'\0')[0].decode(errors='replace').replace('\\', '/')
        except (OSError, ProcessLookupError):
            continue
        if matches(first):
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


def wait_for_reports(results, suites, prior, out, timeout=120, launch_process=None, quit_after_tests=False, game=None, wine_prefix=None, is_running=None):
    """A disappearing observed game is an early exit, not proof of a native crash."""
    reports = {}; report_files = {}; progress = {}; observed_game = False
    running_probe = is_running or (lambda: game_running(game, wine_prefix))
    started = time.monotonic()
    outcome = 'timeout'
    while time.monotonic() - started < timeout:
        running = running_probe()
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
            report_files[suite] = files[-1].name
            name = 'results.json' if suite == 'runtime' else 'fate-results.json'
            (out / name).write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
            visual = results / files[-1].stem
            if visual.is_dir():
                shutil.copytree(visual, out / ('visual' if suite == 'runtime' else 'fate-visual'), dirs_exist_ok=True)
            balance = results / ('balance-' + files[-1].name)
            if balance.is_file():
                shutil.copy2(balance, out / 'balance-results.json')
            print(suite, report.get('runtime'), report.get('modVersion'), f'{report.get("passed")} passed / {report.get("failed")} failed')
            for case in report.get('cases', []):
                print(case['status'].upper(), case['name'])
                if case.get('status') == 'failed':
                    print(case.get('error', case.get('warnings_and_errors', '')))
        if len(reports) == len(suites):
            if quit_after_tests:
                # A receipt must name these exact fresh reports; an old marker or
                # a crash after writing reports must not count as a clean exit.
                receipt = None
                for marker in results.glob('quit-*.json'):
                    try:
                        candidate = json.loads(marker.read_text())
                    except (OSError, json.JSONDecodeError):
                        continue
                    if candidate.get('status') == 'quit_requested' and candidate.get('reports') == report_files:
                        receipt = candidate
                        break
                if receipt is not None and not running:
                    (out / 'quit-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
                    outcome = 'passed_and_quit' if all(r['status'] == 'passed' for r in reports.values()) else 'assertion_failed_and_quit'
                    code = 0 if outcome == 'passed_and_quit' else 1
                    break
                if running:
                    time.sleep(.5)
                    continue
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
    # A native exit may leave a valid matrix checkpoint but no final suite report.
    # Retain it with an explicit progress label; never upgrade partial rows to a pass.
    for suite in progress:
        files = sorted(set(results.glob(suite + '-*.json')) - prior[suite])
        if not files:
            continue
        try:
            checkpoint = json.loads((results / ('balance-' + files[-1].name)).read_text())
        except (OSError, json.JSONDecodeError):
            continue
        (out / 'balance-progress.json').write_text(json.dumps(checkpoint, ensure_ascii=False, indent=2) + '\n')
    cursor = results / 'balance-active.json'
    if progress.get('runtime', {}).get('scope') == 'balance' and cursor.is_file():
        try:
            active = json.loads(cursor.read_text())
        except (OSError, json.JSONDecodeError):
            active = {}
        fresh = {p.name for p in set(results.glob('runtime-*.json')) - prior.get('runtime', set())}
        if active.get('report') in fresh:
            shutil.copy2(cursor, out / 'balance-active.json')
    watchdog = dict(outcome=outcome, elapsed_seconds=round(time.monotonic() - started, 2),
                    game_observed=observed_game, game_running_at_finish=running_probe(),
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
    parser.add_argument('--balance', action='store_true', help='run bounded three-school resource probes instead of the regular suite')
    parser.add_argument('--balance-config', type=Path, default=ROOT / 'data/balance-refined-scenarios.json')
    parser.add_argument('--direct-game', action='store_true', help='macOS: launch a prepared independent copy for headless balance or cost probes')
    parser.add_argument('--headless', action='store_true', help='balance or cost probes: Unity batch mode without rendering')
    parser.add_argument('--quit-after-tests', action='store_true', help='request normal Unity shutdown after all suite reports; verify exit receipt and process exit')
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--include-fates', action='store_true', help='run independent fate suite in the same launch')
    group.add_argument('--schools-only', action='store_true', help='run only new-school loops and isolation checks')
    group.add_argument('--costs-only', action='store_true', help='verify all 96 five/star original-face costs, HUD models, payments and atomic rejections')
    args = parser.parse_args()
    if args.balance and (args.include_fates or args.schools_only or args.costs_only):
        parser.error('--balance cannot be combined with --include-fates, --schools-only or --costs-only')
    if (args.direct_game or args.headless) and not (args.direct_game and args.headless and (args.balance or args.costs_only) and sys.platform == 'darwin'):
        parser.error('direct/headless mode requires macOS, --direct-game --headless and either --balance or --costs-only')
    if args.timeout <= 0:
        parser.error('--timeout must be positive')
    if sys.platform == 'darwin':
        if not args.wine or not args.wine.is_file() or not args.wine_prefix or not args.persistent_data:
            parser.error('macOS requires --wine, --wine-prefix and --persistent-data (or their environment variables)')
        if not (args.wine_prefix / 'system.reg').is_file():
            parser.error('Wine prefix does not contain system.reg')
        steam_exe = args.game.parent.parent.parent / 'steam.exe'
        if not args.direct_game and not steam_exe.is_file():
            parser.error('Windows Steam executable missing beside steamapps: ' + str(steam_exe))
        if args.direct_game:
            appid = args.game / 'steam_appid.txt'
            if not (args.game / 'ReincarnationSquad.exe').is_file() or not appid.is_file() or appid.read_text().strip() != '5241190':
                parser.error('Direct probe requires an independent game copy with steam_appid.txt containing 5241190; Steam must already be signed in')
    elif sys.platform != 'linux':
        parser.error('This launcher supports Linux/Proton and macOS/Wine only')
    if game_running(args.game, args.wine_prefix):
        print('游戏仍在运行。请先从主菜单正常退出；不会强行关闭当前游戏。', file=sys.stderr)
        return 2
    mod = args.game / 'Mods/yixian.sect'
    if not (mod / 'plugins/YixianSect.dll').is_file():
        print('未找到已安装的 YixianSect.dll。', file=sys.stderr)
        return 2
    results = mod / 'TestResults'
    if args.costs_only:
        results.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / 'data/sources/original-correction/original-costs.json', results / 'original-costs-fixture.json')
    if args.balance:
        results.mkdir(parents=True, exist_ok=True)
        shutil.copy2(args.balance_config, results / 'balance-config.json')
    suites = ['runtime', 'fates'] if args.include_fates else ['runtime']
    prior = {suite: set(results.glob(suite + '-*.json')) for suite in suites}
    out = ROOT / 'logs/yixian-runtime-tests' / ('run-' + time.strftime('%Y%m%d-%H%M%S'))
    out.mkdir(parents=True, exist_ok=False)
    flags = ['-yixian-schools-selftest' if args.schools_only else '-yixian-selftest', '-modverbose'] + (['-yixian-fates-selftest'] if args.include_fates else [])
    if args.quit_after_tests:
        flags.append('-yixian-selftest-quit')
    if args.balance:
        flags.append('-yixian-balance-selftest')
    if args.costs_only:
        flags.append('-yixian-costs-selftest')
    if args.headless:
        flags += ['-batchmode', '-nographics']
    try:
        launch_process = None
        with (out / 'launch.log').open('w') as log:
            if sys.platform == 'darwin':
                # Existing Windows Steam handles the request; do not wait for its lifetime.
                command = [str(args.wine), str(args.game / 'ReincarnationSquad.exe'), *flags] if args.direct_game else [str(args.wine), str(steam_exe), '-applaunch', '5241190', *flags]
                launch_process = subprocess.Popen(command,
                                                  env=wine_environment(args.wine, args.wine_prefix),
                                                  cwd=args.game,
                                                  stdout=log, stderr=subprocess.STDOUT)
            else:
                subprocess.run([str(STEAM / 'ubuntu12_32/steam'), '-applaunch', '5241190', *flags],
                               env={**os.environ, 'DISPLAY': os.environ.get('DISPLAY', ':0')},
                               stdout=log, stderr=subprocess.STDOUT, check=True)
        return wait_for_reports(results, suites, prior, out, timeout=args.timeout, launch_process=launch_process, quit_after_tests=args.quit_after_tests, game=args.game, wine_prefix=args.wine_prefix)
    finally:
        # Headless Unity writes its own log to launch.log. Shared profile logs may
        # belong to a separate normal game and must not be attributed to this probe.
        if not args.headless:
            collect_diagnostics(out, args.persistent_data)
        print('Evidence:', out.relative_to(ROOT))
        print('测试局未接入存档；' + ('已请求测试完成后正常退出，未使用强制终止。' if args.quit_after_tests else '默认保留游戏运行，可用 --quit-after-tests 在下次自测完成后正常退出。'))


if __name__ == '__main__':
    raise SystemExit(main())
