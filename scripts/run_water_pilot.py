#!/usr/bin/env python3
"""Run one reproducible Mac/Wine water-pilot arm in an independent game copy."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
from run_yixian_runtime_tests import game_running
ROOT=Path(__file__).resolve().parents[1]


def run_arm(arm,low,game,timeout):
    if game_running(game):raise RuntimeError('请先正常退出目标测试副本。')
    suffix='low' if low else 'main'
    name='carryover-main' if arm=='carryover' else ('before-' if arm=='legacy' else 'after-')+suffix
    if low and arm=='carryover':raise ValueError('carryover currently uses the main attribute cohort')
    config=game/'Mods/yixian.sect/content/five-water-pilot.json'
    original=config.read_bytes();value=json.loads(original);value['waterMasteryMultiplier']=1 if arm=='plain' else 2
    try:
        config.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n')
        return subprocess.run([sys.executable,str(ROOT/'scripts/run_yixian_runtime_tests.py'),'--balance','--balance-config',str(ROOT/'data'/('water-pilot-'+name+'.json')),
            '--game',str(game),'--direct-game','--headless','--quit-after-tests','--timeout',str(timeout)],cwd=ROOT).returncode
    finally:
        config.write_bytes(original)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--arm',choices=['legacy','plain','pilot','carryover'],required=True)
    p.add_argument('--low',action='store_true');p.add_argument('--game',type=Path,default=Path.home()/'Games/ReincarnationSquadBalanceSteam')
    p.add_argument('--timeout',type=int,default=600);args=p.parse_args()
    if args.timeout<=0:p.error('timeout must be positive')
    return run_arm(args.arm,args.low,args.game,args.timeout)
if __name__=='__main__':raise SystemExit(main())
