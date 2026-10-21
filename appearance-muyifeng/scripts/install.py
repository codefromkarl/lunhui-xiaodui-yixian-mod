#!/usr/bin/env python3
"""Install the standalone appearance package, preserving other packages and receipts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game', required=True, type=Path)
parser.add_argument('--apply', action='store_true')
parser.add_argument('--stage-running-update', action='store_true', help='Prepare the next-start version by backing up and atomically switching whole directories; never overwrite loaded DLL files in place')
args = parser.parse_args()
source = ROOT / 'mod/appearance.muyifeng'
game = args.game.resolve()
assert (game / 'ReincarnationSquad.exe').is_file(), 'Game executable missing'
assert (source / 'plugins/MuyifengAppearance.dll').is_file(), 'Build the independent DLL first'
target = game / 'Mods/appearance.muyifeng'
processes = subprocess.check_output(['ps', '-axo', 'comm='], text=True).replace('\\', '/')
running = str(game / 'ReincarnationSquad.exe') in processes
assert not (running and target.exists() and not args.stage_running_update), 'Existing package is in use: close normally, or explicitly stage an atomic next-start directory update'
enabled_path = game / 'Mods/enabled.json'
consent_path = game / 'mod-code-consent.json'
original_enabled = enabled_path.read_bytes() if enabled_path.exists() else None
original_consent = consent_path.read_bytes() if consent_path.exists() else None
enabled = json.loads(original_enabled) if original_enabled else {'packages': []}
consent = json.loads(original_consent) if original_consent else {'version': 2, 'packages': {}}
assert consent.get('version') == 2, 'Unexpected consent format; use native Mod management instead'
print(json.dumps({'target': str(target), 'game_running': running, 'first_install': not target.exists(), 'other_packages': enabled.get('packages'), 'apply': args.apply}, ensure_ascii=False, indent=2))
if not args.apply:
    raise SystemExit(0)
backup = ROOT / 'output' / ('installation-' + time.strftime('%Y%m%d-%H%M%S'))
backup.mkdir(parents=True)
if original_enabled is not None:
    (backup / 'enabled.json').write_bytes(original_enabled)
if original_consent is not None:
    (backup / 'mod-code-consent.json').write_bytes(original_consent)
if target.exists():
    shutil.copytree(target, backup / 'previous-package')
staging = game / 'Mods' / ('.appearance-muyifeng-install-' + str(os.getpid()))
shutil.copytree(source, staging, ignore=shutil.ignore_patterns('TestResults', '*.pdb'))
assert (enabled_path.read_bytes() if enabled_path.exists() else None) == original_enabled, 'Enabled list changed during installation'
assert (consent_path.read_bytes() if consent_path.exists() else None) == original_consent, 'Consent changed during installation'
if target.exists():
    target.rename(backup / 'replaced-package')
staging.rename(target)
if 'appearance.muyifeng' not in enabled['packages']:
    enabled['packages'].append('appearance.muyifeng')
consent['packages']['appearance.muyifeng'] = {'author': '你'}
def atomic_json(path, value):
    temporary = path.with_name(path.name + '.appearance-tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')
    temporary.replace(path)
atomic_json(enabled_path, enabled)
atomic_json(consent_path, consent)
hashes = {str(p.relative_to(target)): hashlib.sha256(p.read_bytes()).hexdigest() for p in target.rglob('*') if p.is_file()}
version = json.loads((source / 'mod.json').read_text())['version']
receipt = {'package': 'appearance.muyifeng', 'version': version, 'target': str(target), 'game_running_at_install': running, 'atomic_directory_switch': True, 'loaded_files_overwritten_in_place': False, 'activation': 'next normal restart', 'enabled_packages': enabled['packages'], 'files_sha256': hashes}
(backup / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n')
print('Installed; restart normally to load. Receipt:', backup / 'receipt.json')
