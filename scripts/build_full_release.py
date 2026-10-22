#!/usr/bin/env python3
"""Build one complete install/update bundle from checked local runtime packages."""
import argparse
import subprocess
import json
from pathlib import Path
import hashlib
from runtime_package import PLUGINS, runtime_files
import zipfile

ROOT = Path(__file__).resolve().parents[1]
GUIDE = '''轮回小队 · 弈仙门派完整整合包 {version}

包含 Mods/yixian.sect（三派玩法 {version}）和 Mods/appearance.muyifeng（角色外观 {appearance_version}）。插件、配置和运行图片齐全；不是 GitHub 自动生成的源码压缩包，不需要再下载外观单包。

安装：
1. 正常退出游戏，解压完整包。首次安装和更新使用同一个完整包。
2. Windows双击“安装.bat”，Mac双击“安装.command”，选择或输入游戏目录；自动备份并替换两个包。
   也可手动把旧的上述两个目录移到Mods之外，再把包内Mods放到游戏根目录，与ReincarnationSquad.exe同级。
3. 启动游戏，在Mod管理中启用所需包并确认代码许可。旧身份／旧牌池存档建议使用对应旧版，新版建议新局。
4. 门派在主神空间入宗；造型在新建角色的信息→造型选择，两者独立。

本版剑宗：普通单段筋肉、多段反应；灵剑路线能量。20点灵剑转化自选购买、默认关闭，主神空间的角色档案→能力与血统→已掌握能力中可免费开启／关闭；开启时扣除筋肉／反应／体质各60%转能量，后续持久加点继续生效，原能量保留；不转战斗临时属性，不重复收费或扣减。
三派五阶价格15／55／130／240／380；卡包与仙命保留降价。三派成长已与原生血统预算对齐。

回退：正常退出游戏，移出新包，恢复上述备份和对应旧存档。联机各端必须使用相同版本；本版未验收真实Steam跨机联机、全平台和完整战役平衡。

不包含游戏本体、宿主程序集、账号信息、存档、启用／许可配置、旧测试结果或外观开发WIP。资源权利说明见THIRD_PARTY_ASSETS.md与sources/，组件、源码提交与文件清单见BUNDLE.json。
'''
RIGHTS = '''# 资源来源与权利说明

本完整包按用户要求包含两个Mod的运行资源。玩法原卡图、门派徽章和仙命图标来自弈仙牌社区资料站，权利归原权利人；来源见sources/。本包不宣称取得原图或角色设计的再分发许可，也不对其另行授予许可证。

角色像素拆件使用ImageGen生成素材；参考链接和提示词保留在sources/appearance/，运行图集位于Mods/appearance.muyifeng/art/，不包含下载的原角色立绘。

代码许可证尚未由作者指定。DLL仅为两个自制Mod插件，不含《轮回小队》游戏程序集、可执行文件或Unity运行库。源码仓库与当前发行说明：https://github.com/codefromkarl/lunhui-xiaodui-yixian-mod
'''


def component(source, slug):
    return runtime_files(source, slug)


def build(sect, appearance, output, source_commit, appearance_commit):
    sm, sf = component(sect, 'yixian.sect'); am, af = component(appearance, 'appearance.muyifeng')
    files = sf + af
    info = dict(bundle='lunhui-yixian-full-mod-' + sm['version'],
                components=[dict(id='yixian.sect', version=sm['version'], sourceCommit=source_commit, files=len(sf)),
                            dict(id='appearance.muyifeng', version=am['version'], sourceCommit=appearance_commit, files=len(af))],
                runtimeResourcesComplete=True, includesHostAssemblies=False, includesUserConfigurationOrSaves=False,
                appearanceSource='runtime dependencies of the selected appearance candidate',
                inventory=[dict(path=name, size=p.stat().st_size, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p, name in files])
    output = Path(output); output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as z:
        for p, name in files: z.write(p, name)
        z.writestr('安装说明.txt', GUIDE.format(version=sm['version'], appearance_version=am['version']).encode('utf-8-sig'))
        z.writestr('THIRD_PARTY_ASSETS.md', RIGHTS)
        for p, name in [(ROOT/'data/third-party-assets.json', 'third-party-assets.json'),
                        (ROOT/'data/sources/original-correction/SOURCES.json', 'original-card-sources.json'),
                        (ROOT/'data/sources/original-correction/original-costs.json', 'original-card-costs.json'),
                        (Path(sect)/'icons/fates/SOURCES.md', 'fate-sources.md')]:
            if p.is_file(): z.write(p, 'sources/' + name)
        appearance_art = Path(appearance) / 'art'
        for p in sorted(appearance_art.rglob('*')):
            if p.is_file() and (p.name == 'provenance.json' or 'prompt' in p.name and p.suffix == '.txt'):
                z.write(p, 'sources/appearance/' + p.relative_to(appearance_art).as_posix())
        for name in ['install.py', 'runtime_package.py', '安装.command', '安装.bat', 'install.ps1']:
            z.write(ROOT / 'scripts' / name, name)
        z.writestr('BUNDLE.json', json.dumps(info, ensure_ascii=False, indent=2) + '\n')
    return info


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--sect-source', type=Path, default=ROOT/'mod/yixian.sect'); p.add_argument('--appearance-source', type=Path, default=ROOT/'appearance-muyifeng/mod/appearance.muyifeng')
    p.add_argument('--output', type=Path, default=ROOT/'dist/lunhui-yixian-full-mod-0.7.3.zip'); p.add_argument('--source-commit')
    p.add_argument('--appearance-commit')
    a = p.parse_args()
    commit = subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    if subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip(): commit += '-dirty'
    a.source_commit = a.source_commit or commit; a.appearance_commit = a.appearance_commit or commit
    info = build(a.sect_source, a.appearance_source, a.output, a.source_commit, a.appearance_commit)
    print(json.dumps(dict(archive=str(a.output), **info), ensure_ascii=False, indent=2))

if __name__ == '__main__': main()
