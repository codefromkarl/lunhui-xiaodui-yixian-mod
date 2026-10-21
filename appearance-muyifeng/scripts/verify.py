#!/usr/bin/env python3
"""Verify an already authorized test copy using Linux/Proton or macOS/Wine."""
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


def game_running(game):
    """Match the test executable, including Wine's Z: mapping; leave other games alone."""
    expected = str((game / 'ReincarnationSquad.exe').resolve()).casefold()
    if sys.platform == 'linux':
        for path in Path('/proc').glob('[0-9]*/cmdline'):
            try:
                value = path.read_bytes().split(b'\0')[0].decode(errors='replace').replace('\\', '/')
            except OSError:
                continue
            if value.casefold().startswith('z:/'):
                value = value[2:]
            if value.casefold() == expected:
                return True
        return False
    processes = subprocess.check_output(['ps', '-axo', 'comm='], text=True).replace('\\', '/')
    return expected in processes.casefold()


def launch_settings(args, out, platform=None):
    platform = platform or sys.platform
    flags = ['-muyifeng-verify', '-muyifeng-quit', '-screen-fullscreen', '0',
             '-screen-width', '1280', '-screen-height', '720', '-modverbose',
             '-logfile', 'Z:' + str(out / 'Player.log').replace('/', '\\')]
    if getattr(args, 'static', False):
        flags.append('-muyifeng-static')
    if getattr(args, 'keyposes', False):
        flags.append('-muyifeng-keyposes')
    env = {**os.environ, 'SteamAppId': '5241190', 'SteamGameId': '5241190'}
    if platform == 'linux':
        proton = args.proton.resolve()
        if not proton.is_file():
            raise ValueError('Proton launcher missing: ' + str(proton))
        compat = args.compat_data.resolve()
        main_prefix = (STEAM / 'steamapps/compatdata/5241190').resolve()
        if compat == main_prefix:
            raise ValueError('Use a dedicated test compat-data directory, not the main game prefix.')
        env.update(STEAM_COMPAT_CLIENT_INSTALL_PATH=str(STEAM), STEAM_COMPAT_DATA_PATH=str(compat),
                   DISPLAY=os.environ.get('DISPLAY', ':0'))
        command = [str(proton), 'run', str(args.game / 'ReincarnationSquad.exe'), *flags]
    elif platform == 'darwin':
        if not args.wine or not args.wine.is_file() or not args.wine_prefix:
            raise ValueError('macOS requires --wine and --wine-prefix (or YIXIAN_WINE/WINEPREFIX).')
        wine = args.wine.resolve()
        base = wine.parents[2]
        frameworks = base.parent / 'Frameworks'
        env.update(WINEPREFIX=str(args.wine_prefix), WINEDEBUG='-all',
                   WINEDLLPATH_DXMT=str(base / 'renderer/dxmt/wine'),
                   WINEDLLPATH_PREPEND=str(base / 'renderer/dxmt/wine'),
                   WINEDLLOVERRIDES='d3d11,dxgi,winemetal=b',
                   DYLD_FALLBACK_LIBRARY_PATH=':'.join([str(frameworks), str(base / 'renderer/dxmt/wine/x86_64-unix'), str(base / 'wine/lib/wine/x86_64-unix'), '/usr/local/lib', '/usr/lib']))
        command = [str(wine), str(args.game / 'ReincarnationSquad.exe'), *flags]
    else:
        raise ValueError('Supported platforms: Linux/Proton and macOS/Wine.')
    return command, env


def preflight(args):
    args.game = args.game.resolve()
    if args.timeout <= 0:
        raise ValueError('Timeout must be positive.')
    if not (args.game / 'ReincarnationSquad.exe').is_file():
        raise ValueError('Test game executable missing: ' + str(args.game))
    if args.game == (STEAM / 'steamapps/common/轮回小队 Demo').resolve():
        raise ValueError('Use an independent game copy; this verifier updates its test package.')
    if game_running(args.game):
        raise ValueError('Target test copy is running; close it normally first.')
    source = getattr(args, 'package', None) or ROOT / 'mod/appearance.muyifeng'
    source = Path(source).resolve()
    if getattr(args, 'package', None) is not None and json.loads((source / 'mod.json').read_text()).get('id') != 'appearance.muyifeng':
        raise ValueError('The isolated candidate must be appearance.muyifeng.')
    if not (source / 'plugins/MuyifengAppearance.dll').is_file():
        raise ValueError('Build the appearance DLL first.')
    enabled_path = args.game / 'Mods/enabled.json'
    consent_path = args.game / 'mod-code-consent.json'
    enabled = json.loads(enabled_path.read_text()) if enabled_path.is_file() else {}
    consent = json.loads(consent_path.read_text()) if consent_path.is_file() else {}
    author = json.loads((source / 'mod.json').read_text())['author']
    if 'appearance.muyifeng' not in enabled.get('packages', []):
        raise ValueError('Enable appearance.muyifeng in the test copy with authorization first.')
    if consent.get('version') != 2 or consent.get('packages', {}).get('appearance.muyifeng', {}).get('author') != author:
        raise ValueError('The test copy needs explicit code consent for appearance.muyifeng; verifier does not grant it.')
    command, env = launch_settings(args, ROOT / 'output/preflight')
    return source, command, env


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, default=Path.home() / 'Games/ReincarnationSquadAppearance')
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--package', type=Path, help='Use an isolated appearance.muyifeng candidate instead of the shared build directory')
    parser.add_argument('--keyposes', action='store_true', help='Capture idle and raised-sword poses before the full motion suite')
    parser.add_argument('--static', action='store_true', help='Capture only the native idle pose for visual inspection before the full motion suite')
    parser.add_argument('--proton', type=Path, default=STEAM / 'steamapps/common/Proton - Experimental/proton')
    parser.add_argument('--compat-data', type=Path, default=ROOT / 'output/proton-compat-data')
    parser.add_argument('--wine', type=Path, default=os.environ.get('YIXIAN_WINE'))
    parser.add_argument('--wine-prefix', type=Path, default=os.environ.get('WINEPREFIX'))
    parser.add_argument('--preflight', action='store_true', help='Read-only target and authorization checks; no install or game launch')
    args = parser.parse_args()
    try:
        source, _, _ = preflight(args)
    except (ValueError, OSError) as error:
        parser.error(str(error))
    if args.preflight:
        print('PASS test-copy preflight:', args.game)
        return 0
    out = ROOT / 'output' / ('run-' + time.strftime('%Y%m%d-%H%M%S'))
    out.mkdir(parents=True, exist_ok=False)
    package = args.game / 'Mods/appearance.muyifeng'
    if package.exists():
        shutil.copytree(package, out / 'previous-test-package', ignore=shutil.ignore_patterns('TestResults'))
    prior = set((package / 'TestResults').glob('*/results.json'))
    shutil.copytree(source, package, dirs_exist_ok=True)
    command, env = launch_settings(args, out)
    if sys.platform == 'linux':
        # Unity's first startup may fail to create this Unicode leaf in a fresh prefix.
        (args.compat_data / 'pfx/drive_c/users/steamuser/AppData/LocalLow/yuyuyi/轮回小队').mkdir(parents=True, exist_ok=True)
    (out / 'launch.json').write_text(json.dumps({'game': str(args.game), 'graphics': True, 'headless': False, 'command': command}, ensure_ascii=False, indent=2))
    print('Evidence:', out, flush=True)
    with (out / 'launch.log').open('w') as log:
        process = subprocess.Popen(command, cwd=args.game, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            result = process.wait(timeout=args.timeout)
        except subprocess.TimeoutExpired:
            (out / 'watchdog.json').write_text(json.dumps({'status': 'timeout', 'game_left_running': True}))
            print('Verification timeout; target left running for diagnosis.', file=sys.stderr)
            return 3
    fresh = sorted(set((package / 'TestResults').glob('*/results.json')) - prior)
    if len(fresh) == 1:
        shutil.copytree(fresh[0].parent, out / 'results')
        report = json.loads(fresh[0].read_text())
    else:
        report = {'status': 'missing_fresh_report', 'reports': len(fresh)}
    receipt = out / 'results/quit.json'
    quit_report = json.loads(receipt.read_text()) if receipt.is_file() else {}
    clean_quit = quit_report.get('normal_quit') is True and quit_report.get('startup_completed') is True
    success = result == 0 and report.get('status') == 'passed' and clean_quit and not game_running(args.game)
    (out / 'watchdog.json').write_text(json.dumps({'status': 'passed_and_quit' if success else 'failed', 'exit_code': result, 'fresh_report': len(fresh) == 1, 'normal_quit_receipt': clean_quit}))
    print('Exit:', result, flush=True)
    print(json.dumps({key: report.get(key) for key in ('status', 'passed', 'failed', 'frames', 'fps')}, indent=2), flush=True)
    return 0 if success else 1


if __name__ == '__main__':
    raise SystemExit(main())
