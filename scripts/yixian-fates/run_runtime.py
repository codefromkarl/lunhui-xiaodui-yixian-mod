#!/usr/bin/env python3
"""Launch the opt-in in-game Mono suite and collect its machine-readable result.
Never closes another game process, changes startup settings, or edits a user save.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
STEAM = Path.home()/'.local/share/Steam'
MOD = STEAM/'steamapps/common/轮回小队 Demo/Mods/yixian.sect'


def game_running():
    for p in Path('/proc').glob('[0-9]*/cmdline'):
        try:
            first = p.read_bytes().split(b'\0')[0].decode(errors='replace').replace('\\','/')
        except (OSError, ProcessLookupError):
            continue
        if first.rsplit('/',1)[-1].lower() == 'reincarnationsquad.exe':
            return True
    return False


def main():
    if game_running():
        print('游戏仍在运行。请先从主菜单正常退出，再运行本命令；不会强行关闭当前游戏。',file=sys.stderr)
        return 2
    if not (MOD/'plugins/YixianSect.dll').exists():
        print('未找到已安装的 YixianSect.dll。',file=sys.stderr)
        return 2
    results=MOD/'TestResults'
    prior=set(results.glob('fates-*.json')) if results.exists() else set()
    out=ROOT/'logs/yixian-fates'/('run-'+time.strftime('%Y%m%d-%H%M%S'))
    out.mkdir(parents=True,exist_ok=False)
    with (out/'launch.log').open('w') as log:
        subprocess.run([str(STEAM/'ubuntu12_32/steam'),'-applaunch','5241190','-yixian-fates-selftest','-modverbose'],env={**os.environ,'DISPLAY':os.environ.get('DISPLAY',':0')},stdout=log,stderr=subprocess.STDOUT,check=True)
    deadline=time.monotonic()+120
    while time.monotonic()<deadline:
        files=sorted(set(results.glob('fates-*.json'))-prior) if results.exists() else []
        if files:
            try:
                report=json.loads(files[-1].read_text())
            except (OSError,json.JSONDecodeError):
                time.sleep(.5)
                continue
            if report.get('status') in ('passed','failed'):
                (out/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
                persistent=STEAM/'steamapps/compatdata/5241190/pfx/drive_c/users/steamuser/AppData/LocalLow/yuyuyi/轮回小队'
                for src,dest in [(persistent/'Player.log','Player.log'),(persistent/'ModLogs/latest.log','ModLogs.log')]:
                    if src.exists():(out/dest).write_bytes(src.read_bytes())
                visual=results/files[-1].stem
                if visual.exists():
                    import shutil
                    shutil.copytree(visual,out/'visual',dirs_exist_ok=True)
                print('Runtime:',report.get('runtime'),'Mod:',report.get('modVersion'))
                print(f"{report.get('passed')} passed / {report.get('failed')} failed")
                for case in report.get('cases',[]):
                    print(case['status'].upper(),case['name'])
                    if case.get('status')=='failed':print(case.get('error',case.get('warnings_and_errors','')))
                print('Evidence:',out.relative_to(ROOT))
                print('测试局未接入存档；游戏保持运行。请进入主菜单后再关闭，避免触发游戏原生的启动保护。')
                return 0 if report['status']=='passed' else 1
        time.sleep(.5)
    print('120 秒内未取得完整报告；游戏未被强行关闭。检查 Player.log、ModLogs 或代码许可。',file=sys.stderr)
    return 3

if __name__=='__main__':
    raise SystemExit(main())
