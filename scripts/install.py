#!/usr/bin/env python3
"""Install complete packages into clean directories, retaining a full rollback copy."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime
from runtime_package import PLUGINS, runtime_files

SCRIPT = Path(__file__).resolve().parent
BUNDLED = (SCRIPT / 'BUNDLE.json').is_file()
ROOT = SCRIPT if BUNDLED else SCRIPT.parent
GAME = Path(os.environ['GAME_MANAGED']).parent.parent if os.environ.get('GAME_MANAGED') else (
    Path.home() / 'Library/Application Support/Steam/steamapps/common/轮回小队 Demo' if sys.platform == 'darwin' else
    Path(os.environ.get('PROGRAMFILES(X86)', 'C:/Program Files (x86)')) / 'Steam/steamapps/common/轮回小队 Demo' if sys.platform == 'win32' else
    Path.home() / '.local/share/Steam/steamapps/common/轮回小队 Demo')


def game_running(game):
    if sys.platform == 'win32':
        return 'reincarnationsquad.exe' in subprocess.check_output(['tasklist', '/FI', 'IMAGENAME eq ReincarnationSquad.exe', '/NH'], text=True).casefold()
    expected = (Path(game) / 'ReincarnationSquad.exe').resolve()
    def matches(value):
        value = value.strip().replace('\\', '/')
        if value.rsplit('/', 1)[-1].casefold() != 'reincarnationsquad.exe': return False
        if len(value)>2 and value[1:3]==':/':
            drive, relative = value[0].lower(), value[3:]
            if drive == 'z': value = '/' + relative
            elif os.environ.get('WINEPREFIX'):
                prefix = Path(os.environ['WINEPREFIX']); device = prefix / 'dosdevices' / (drive + ':')
                mapped = device if device.is_dir() else prefix / 'drive_c' if drive == 'c' else None
                if mapped is None: return True
                value = str(mapped / relative)
            else: return True
        candidate = Path(value)
        return not candidate.is_absolute() or str(candidate.resolve()).casefold() == str(expected).casefold()
    if sys.platform == 'darwin':
        return any(matches(line) for line in subprocess.check_output(['ps', '-axo', 'comm='], text=True).splitlines())
    for path in Path('/proc').glob('[0-9]*/cmdline'):
        try:
            if matches(path.read_bytes().split(b'\0')[0].decode(errors='replace')): return True
        except OSError:
            continue
    return False


def candidate(slug):
    if BUNDLED: return ROOT / 'Mods' / slug
    return ROOT / ('appearance-muyifeng/mod' if slug == 'appearance.muyifeng' else 'mod') / slug


def install_packages(game, sources, backup_root):
    game = Path(game).resolve()
    if not (game / 'ReincarnationSquad.exe').is_file():
        raise ValueError('请选择含 ReincarnationSquad.exe 的游戏目录。')
    selected = []
    for slug, source in sources.items():
        source = Path(source).resolve(); target = game / 'Mods' / slug
        if source == target.resolve() or target.is_symlink():
            raise ValueError('安装源不能是目标目录，目标也不能是符号链接。')
        manifest, files = runtime_files(source, slug)
        selected.append((slug, source, target, manifest, files))
    if game_running(game): raise ValueError('游戏仍在运行；请正常退出游戏后重试。')
    backup = Path(backup_root) / datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup.mkdir(parents=True, exist_ok=False)
    (game / 'Mods').mkdir(exist_ok=True)
    stage_root = Path(tempfile.mkdtemp(prefix='.yixian-install-', dir=game))
    switched = []
    receipt = {'installed': False, 'game': str(game), 'backup': str(backup), 'packages': []}
    try:
        for slug, source, target, manifest, files in selected:
            stage = stage_root / slug; stage.mkdir()
            hashes = {}
            for p, name in files:
                relative = Path(name).relative_to('Mods/' + slug)
                dest = stage / relative; dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(p, dest)
                hashes[relative.as_posix()] = hashlib.sha256(dest.read_bytes()).hexdigest()
            if target.exists(): shutil.copytree(target, backup / slug)
            old_files = {p.relative_to(target).as_posix() for p in target.rglob('*') if p.is_file()} if target.exists() else set()
            receipt['packages'].append(dict(id=slug, version=manifest['version'], target=str(target),
                removed_files=sorted(old_files-set(hashes)), files_sha256=hashes))
        if game_running(game): raise ValueError('游戏在准备期间启动；已取消安装，备份保留。')
        for slug, source, target, manifest, files in selected:
            displaced = stage_root / (slug + '.previous')
            if target.exists(): os.replace(target, displaced)
            switched.append((target, displaced))
            os.replace(stage_root / slug, target)
        for entry in receipt['packages']:
            for name, digest in entry['files_sha256'].items():
                if hashlib.sha256((Path(entry['target']) / name).read_bytes()).hexdigest() != digest:
                    raise ValueError('安装后校验失败：' + name)
        receipt.update(installed=True, requires_restart=True)
        (backup / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    except Exception:
        for target, displaced in reversed(switched):
            if target.exists(): shutil.rmtree(target)
            if displaced.exists(): os.replace(displaced, target)
        raise
    finally:
        shutil.rmtree(stage_root)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, default=GAME)
    parser.add_argument('--source', type=Path, help='完整候选包；仅用于单包安装')
    parser.add_argument('--packages', nargs='+', choices=list(PLUGINS))
    parser.add_argument('--all', action='store_true', help='一次安装玩法与外观')
    parser.add_argument('--apply', action='store_true', help='执行安装；默认只读检查')
    args = parser.parse_args()
    slugs = list(PLUGINS) if args.all or BUNDLED and not args.packages else args.packages or ['yixian.sect']
    if args.source and len(slugs) != 1: parser.error('--source 只支持单包安装')
    sources = {slug: args.source or candidate(slug) for slug in slugs}
    try:
        for slug, source in sources.items(): runtime_files(source, slug)
        if not args.apply:
            print(json.dumps(dict(sources={k:str(v) for k,v in sources.items()}, game=str(args.game), game_running=game_running(args.game), installed=False), ensure_ascii=False, indent=2)); return 0
        # Source validation runs against the exact chosen gameplay candidate.
        if not BUNDLED and 'yixian.sect' in sources:
            subprocess.run([sys.executable, str(ROOT / 'scripts/check_contracts.py'), '--mod-root', str(sources['yixian.sect'])], cwd=ROOT, check=True)
        if BUNDLED:
            info = json.loads((ROOT / 'BUNDLE.json').read_text(encoding='utf-8'))
            for row in info['inventory']:
                p = ROOT / row['path']
                if hashlib.sha256(p.read_bytes()).hexdigest() != row['sha256']:
                    raise ValueError('安装包损坏，请重新解压：' + row['path'])
        result = install_packages(args.game, sources, ROOT / 'logs/installation')
        print(json.dumps(result, ensure_ascii=False, indent=2)); return 0
    except (ValueError, OSError) as error:
        print(str(error), file=sys.stderr); return 2


if __name__ == '__main__': raise SystemExit(main())
