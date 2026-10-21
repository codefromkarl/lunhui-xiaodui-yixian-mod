#!/usr/bin/env python3
"""Install the independent skin into its test copy and run Unity graphics verification."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game', type=Path, default=Path.home() / 'Games/ReincarnationSquadAppearance')
parser.add_argument('--timeout', type=int, default=180)
args = parser.parse_args()
wine = Path(os.environ['YIXIAN_WINE'])
prefix = Path(os.environ['WINEPREFIX'])
processes = subprocess.check_output(['ps', '-axo', 'comm='], text=True)
assert str(args.game / 'ReincarnationSquad.exe') not in processes, 'Target test copy is running; close it normally first.'
package = args.game / 'Mods/appearance.muyifeng'
if package.exists():
    backup = ROOT / 'output' / ('previous-test-package-' + time.strftime('%Y%m%d-%H%M%S'))
    shutil.copytree(package, backup, ignore=shutil.ignore_patterns('TestResults'))
prior = set((package / 'TestResults').glob('*/results.json'))
shutil.copytree(ROOT / 'mod/appearance.muyifeng', package, dirs_exist_ok=True)
out = ROOT / 'output' / ('run-' + time.strftime('%Y%m%d-%H%M%S'))
out.mkdir(parents=True)
base = wine.parents[2]
frameworks = base.parent / 'Frameworks'
env = {**os.environ, 'WINEPREFIX': str(prefix), 'WINEDEBUG': '-all',
       'WINEDLLPATH_DXMT': str(base / 'renderer/dxmt/wine'),
       'WINEDLLPATH_PREPEND': str(base / 'renderer/dxmt/wine'),
       'WINEDLLOVERRIDES': 'd3d11,dxgi,winemetal=b',
       'SteamAppId': '5241190', 'SteamGameId': '5241190',
       'DYLD_FALLBACK_LIBRARY_PATH': ':'.join([str(frameworks), str(base / 'renderer/dxmt/wine/x86_64-unix'), str(base / 'wine/lib/wine/x86_64-unix'), '/usr/local/lib', '/usr/lib'])}
command = [str(wine), str(args.game / 'ReincarnationSquad.exe'), '-muyifeng-verify', '-muyifeng-quit',
           '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-modverbose',
           '-logfile', 'Z:' + str(out / 'Player.log').replace('/', '\\')]
(out / 'launch.json').write_text(json.dumps({'game': str(args.game), 'graphics': True, 'headless': False, 'command': command}, ensure_ascii=False, indent=2))
print('Evidence:', out, flush=True)
with (out / 'launch.log').open('w') as log:
    process = subprocess.Popen(command, cwd=args.game, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    try:
        result = process.wait(timeout=args.timeout)
    except subprocess.TimeoutExpired:
        (out / 'watchdog.json').write_text(json.dumps({'status': 'timeout', 'game_left_running': True}))
        raise SystemExit('Verification timeout; target left running for diagnosis.')
results = package / 'TestResults'
fresh = sorted(set(results.glob('*/results.json')) - prior)
if len(fresh) == 1:
    shutil.copytree(fresh[0].parent, out / 'results')
    report = json.loads(fresh[0].read_text())
else:
    report = {'status': 'missing_fresh_report', 'reports': len(fresh)}
receipt = out / 'results/quit.json'
success = result == 0 and report.get('status') == 'passed' and receipt.is_file()
(out / 'watchdog.json').write_text(json.dumps({'status': 'passed_and_quit' if success else 'failed', 'exit_code': result, 'fresh_report': len(fresh) == 1, 'normal_quit_receipt': receipt.is_file()}))
print('Exit:', result, flush=True)
print(json.dumps({key: report.get(key) for key in ('status', 'passed', 'failed', 'frames', 'fps')}, indent=2), flush=True)
raise SystemExit(0 if success else 1)
