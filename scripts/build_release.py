#!/usr/bin/env python3
"""Build an artwork-free update archive for an existing complete yixian.sect install."""
import argparse
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
INSTRUCTIONS = '''# 弈仙门派 {version} 部署更新包

这是覆盖更新包，不是首次安装包。包含本项目的 DLL、PDB 和配置；不含游戏程序集或第三方原卡图、徽章、仙命图。

## 更新现有安装

1. 需要已有完整的 `Mods/yixian.sect`（v0.7.0 的136牌补位版或更新的兼容版本），包含形意剑、反身剑、流云乱剑及五行、七星原图。
2. 正常退出游戏，将整个旧 `yixian.sect` 备份到游戏 `Mods` 之外。
3. 把本压缩包内 `yixian.sect/` 的文件覆盖到原目录，**保留原 `icons/`**，不要删除旧包再解压本更新包。
4. 启动游戏，启用「弈仙门派」并确认代码许可。内容身份较旧或牌池不同的存档建议使用旧版，当前版本建议新局。

`release-manifest.json` 列出必须由本地原包提供的全部图片。缺失时不要启动不完整包；从仓库源码按 `docs/development.md` 准备本地合法资源。首次安装也采用该流程。

完整来源核对要求与账本一致的308×508剑宗原图；更新包不覆盖原图。需要重建／修复美术时，阅读 THIRD_PARTY_ASSETS.md 后执行 `python3 scripts/prepare_assets.py --download`，再运行 `bash scripts/check.sh`，使用生成的完整包安装。

本版：普通剑招单段筋肉、多段反应，灵剑路线能量；20点「灵剑转化」持续扣除筋肉／反应／体质各60%转为能量，后续持久加点继续转化。原有能量保留，不重复收费／扣除，不转换战斗临时属性。
'''


def build(source, output, source_commit):
    source = Path(source)
    manifest = json.loads((source / 'mod.json').read_text())
    if manifest['id'] != 'yixian.sect':
        raise ValueError('Only yixian.sect updates are supported')
    files = [Path('mod.json'), Path('README.md'), Path('plugins/YixianSect.dll'), Path('plugins/YixianSect.pdb')]
    files += [Path(p) for p in manifest['content']]
    files += [Path(manifest['tables']['cardtable'])]
    files = sorted(set(files))
    if any(p.is_absolute() or '..' in p.parts for p in files):
        raise ValueError('Package file path must stay inside source')
    for p in files:
        if not (source / p).is_file():
            raise FileNotFoundError(p)
    assets = sorted(set(a['file'] for a in manifest['assets'].values()))
    if any(not (source / p).is_file() for p in assets):
        raise ValueError('Build from a complete, verified local package')
    info = dict(packageId=manifest['id'], version=manifest['version'], kind='existing-install-update',
                sourceCommit=source_commit, includesThirdPartyArtwork=False, includesGameAssemblies=False,
                files=['yixian.sect/' + p.as_posix() for p in files], requiredLocalAssets=assets)
    output = Path(output); output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for p in files:
            archive.write(source / p, 'yixian.sect/' + p.as_posix())
        archive.writestr('UPDATE.md', INSTRUCTIONS.format(version=manifest['version']))
        archive.writestr('release-manifest.json', json.dumps(info, ensure_ascii=False, indent=2) + '\n')
    return info


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True, help='complete frozen tested package')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source-commit', required=True)
    args = parser.parse_args()
    info = build(args.source, args.output, args.source_commit)
    print(json.dumps(dict(archive=str(args.output), **info), ensure_ascii=False, indent=2))

if __name__ == '__main__':
    main()
