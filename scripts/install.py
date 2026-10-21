#!/usr/bin/env python3
"""Install the checked candidate after normal game exit, with backup and rollback."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
from run_yixian_runtime_tests import game_running

ROOT = Path(__file__).resolve().parents[1]
# Removed generated features must not survive the overlay from an older package.
# Everything outside these owned paths (including TestResults) stays untouched.
RETIRED_PACKAGE_PATHS = (
    'content/professions.json', 'content/alchemy.json', 'content/cardlevels.json',
    'icons/professions', 'icons/alchemy',
)
if os.environ.get('GAME_MANAGED'):
    GAME = Path(os.environ['GAME_MANAGED']).parent.parent
elif sys.platform == 'darwin':
    GAME = Path.home() / 'Library/Application Support/Steam/steamapps/common/轮回小队 Demo'
else:
    GAME = Path.home() / '.local/share/Steam/steamapps/common/轮回小队 Demo'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, default=GAME)
    parser.add_argument('--source', type=Path, default=ROOT / 'mod/yixian.sect', help='checked package to install, including a frozen accepted candidate')
    parser.add_argument('--apply', action='store_true', help='install; default is read-only preflight')
    args = parser.parse_args()
    source = args.source.resolve(); target = args.game / 'Mods/yixian.sect'
    if source == target.resolve():
        parser.error('--source must not be the installed target')
    manifest = json.loads((source / 'mod.json').read_text())
    if manifest['id'] != 'yixian.sect' or not (source / 'plugins/YixianSect.dll').is_file():
        raise RuntimeError('候选身份或 DLL 不完整。先运行 scripts/check.sh。')
    for asset in manifest['assets'].values():
        if not (source / asset['file']).is_file(): raise RuntimeError('缺少资源：' + asset['file'])
    info = dict(version=manifest['version'], source=str(source), target=str(target), game_running=game_running(args.game), installed=False)
    if not args.apply:
        print(json.dumps(info, ensure_ascii=False, indent=2)); return 0
    if info['game_running']:
        print('游戏仍在运行；未安装。请正常退出游戏后重试。', file=sys.stderr); return 2
    # Recheck the actual candidate instead of trusting an old report for a different build.
    subprocess.run([sys.executable, str(ROOT / 'scripts/check_contracts.py'), '--mod-root', str(source)], cwd=ROOT, check=True)
    stamp = time.strftime('%Y%m%d-%H%M%S')
    backup = ROOT / 'logs/installation' / stamp
    backup.mkdir(parents=True, exist_ok=False)
    target.parent.mkdir(parents=True, exist_ok=True)
    stage = target.parent / ('.yixian-stage-' + stamp)
    if stage.exists(): raise RuntimeError('暂存目录已存在；未覆盖。')
    # Preserve existing unknown files, TestResults, enabled-list and permission storage.
    if target.exists():
        shutil.copytree(target, backup / 'previous-package')
        shutil.copytree(target, stage)
    else:
        stage.mkdir()
    shutil.copytree(source, stage, dirs_exist_ok=True)
    removed_files = []
    for relative in RETIRED_PACKAGE_PATHS:
        stale = stage / relative
        if (source / relative).exists() or not stale.exists():
            continue
        if stale.is_dir() and not stale.is_symlink():
            removed_files.extend(str(path.relative_to(stage)) for path in stale.rglob('*') if path.is_file())
            shutil.rmtree(stale)
        else:
            removed_files.append(relative)
            stale.unlink()
    if game_running(args.game):
        shutil.rmtree(stage)
        print('游戏在准备期间启动；已取消安装，保留备份。', file=sys.stderr); return 2
    displaced = target.parent / ('.yixian-previous-' + stamp)
    try:
        if target.exists(): os.replace(target, displaced)
        os.replace(stage, target)
    except Exception:
        if displaced.exists() and not target.exists(): os.replace(displaced, target)
        raise
    if displaced.exists(): shutil.rmtree(displaced)  # this exact package is already backed up above
    info.update(installed=True, backup=str(backup / 'previous-package'), requires_restart=True, runtime_verified=False,
                removed_retired_files=sorted(removed_files))
    (backup / 'receipt.json').write_text(json.dumps(info, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(info, ensure_ascii=False, indent=2))
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
