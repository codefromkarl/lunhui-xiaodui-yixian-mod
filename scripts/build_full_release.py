#!/usr/bin/env python3
"""Bundle a frozen sect package with an already published, tested appearance package."""
import argparse
import json
from pathlib import Path, PurePosixPath
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PLUGINS = {'yixian.sect': 'plugins/YixianSect.dll', 'appearance.muyifeng': 'plugins/MuyifengAppearance.dll'}
GUIDE = '''轮回小队 · 弈仙门派完整整合包 {version}

包含 Mods/yixian.sect（三派玩法 {version}）和 Mods/appearance.muyifeng（已发布受测外观 {appearance_version}）。插件、配置和运行图片齐全；不是 GitHub 自动生成的源码压缩包，不需要再下载外观单包。

安装：
1. 正常退出游戏，把旧的上述两个Mod目录备份到Mods之外；其他Mod、启用名单、许可和存档不动。
2. 将本包Mods目录合并到游戏根目录（与ReincarnationSquad.exe同级），不要套成Mods/Mods。
3. 启动游戏，在Mod管理中启用所需包并确认代码许可。旧身份／旧牌池存档建议使用对应旧版，新版建议新局。
4. 门派在主神空间入宗；造型在新建角色的信息→造型选择，两者独立。

本版剑宗：普通单段筋肉、多段反应；灵剑路线能量。20点灵剑转化自选购买、默认关闭，主神空间可免费开启／关闭；开启时扣除筋肉／反应／体质各60%转能量，后续持久加点继续生效，原能量保留；不转战斗临时属性，不重复收费或扣减。
三派五阶价格15／55／130／240／380；卡包与仙命保留降价。三派成长表不在本版重排。

回退：正常退出游戏，移出新包，恢复上述备份和对应旧存档。联机各端必须使用相同版本；本版未验收真实Steam跨机联机、全平台和完整战役平衡。

不包含游戏本体、宿主程序集、账号信息、存档、启用／许可配置、旧测试结果或外观开发WIP。资源权利说明见THIRD_PARTY_ASSETS.md与sources/，组件、源码提交与文件清单见BUNDLE.json。
'''
RIGHTS = '''# 资源来源与权利说明

本完整包按用户要求包含两个Mod的运行资源。玩法原卡图、门派徽章和仙命图标来自弈仙牌社区资料站，权利归原权利人；来源见sources/。本包不宣称取得原图或角色设计的再分发许可，也不对其另行授予许可证。

角色像素拆件为已发布外观包的ImageGen生成素材；参考链接和提示词保留在各art目录的provenance.json及prompt文件，不是下载的原角色立绘。

代码许可证尚未由作者指定。DLL仅为两个自制Mod插件，不含《轮回小队》游戏程序集、可执行文件或Unity运行库。源码仓库与当前发行说明：https://github.com/codefromkarl/lunhui-xiaodui-yixian-mod
'''


def component(source, slug):
    source = Path(source); manifest = json.loads((source / 'mod.json').read_text())
    if manifest['id'] != slug or manifest['code']['assembly'] != PLUGINS[slug]:
        raise ValueError('Unexpected component or plugin: ' + slug)
    refs = [*manifest.get('content', []), *manifest.get('tables', {}).values(), PLUGINS[slug],
            *[a['file'] for a in manifest.get('assets', {}).values()]]
    for name in refs:
        p = PurePosixPath(name)
        if p.is_absolute() or '..' in p.parts or not (source / name).is_file():
            raise ValueError('Missing or unsafe runtime resource: ' + name)
    files = []
    for p in sorted(source.rglob('*')):
        if not p.is_file(): continue
        rel = p.relative_to(source)
        if any(x in {'TestResults', 'src', 'bin', 'obj', '__pycache__'} for x in rel.parts) or p.suffix == '.pdb': continue
        if p.suffix.lower() == '.exe' or p.suffix.lower() == '.dll' and rel.as_posix() != PLUGINS[slug]:
            raise ValueError('Host or unknown executable cannot be bundled: ' + str(rel))
        if p.name in {'enabled.json', 'mod-code-consent.json'} or p.name.startswith('.env'):
            raise ValueError('User configuration cannot be bundled: ' + str(rel))
        files.append((p, 'Mods/' + slug + '/' + rel.as_posix()))
    return manifest, files


def build(sect, appearance, output, source_commit, appearance_commit):
    sm, sf = component(sect, 'yixian.sect'); am, af = component(appearance, 'appearance.muyifeng')
    files = sf + af
    info = dict(bundle='lunhui-yixian-full-mod-' + sm['version'],
                components=[dict(id='yixian.sect', version=sm['version'], sourceCommit=source_commit, files=len(sf)),
                            dict(id='appearance.muyifeng', version=am['version'], sourceCommit=appearance_commit, files=len(af))],
                runtimeResourcesComplete=True, includesHostAssemblies=False, includesUserConfigurationOrSaves=False,
                appearanceSource='previously published tested package, unchanged',
                inventory=[dict(path=name, size=p.stat().st_size) for p, name in files])
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
        z.writestr('BUNDLE.json', json.dumps(info, ensure_ascii=False, indent=2) + '\n')
    return info


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--sect-source', type=Path, required=True); p.add_argument('--appearance-source', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True); p.add_argument('--source-commit', required=True)
    p.add_argument('--appearance-commit', required=True)
    a = p.parse_args(); info = build(a.sect_source, a.appearance_source, a.output, a.source_commit, a.appearance_commit)
    print(json.dumps(dict(archive=str(a.output), **info), ensure_ascii=False, indent=2))

if __name__ == '__main__': main()
