# 第三方资源与公开范围

源码仓库公开 Mod 代码、规则数据、来源索引和开发工具，不包含《轮回小队》的游戏 DLL／可执行文件，也不纳入《弈仙牌》的独立卡图、门派徽章和仙命原图。`docs/images/` 仅保留 README 使用的三张游戏画面，来源见[截图记录](docs/images/SOURCES.md)。

## 在本地准备美术

图像来源和落地文件一一记录在 `data/third-party-assets.json`。图鉴来源为：

- [弈仙牌资料站](https://sharpobject.github.io/yxp_wiki/zh/index.html)
- [云灵剑宗仙命图鉴](https://sharpobject.github.io/yxp_wiki/zh/fates/cloud-spirit-sword-sect.html)
- [牧逸风专属仙命](https://sharpobject.github.io/yxp_wiki/zh/characters/1000001.html)

原图权利归原权利人；本仓库未提供其公开再分发授权。确认用途允许后，可以自行放入资源，或执行以下显式下载命令：

```bash
python3 -m pip install -r requirements-dev.txt
python3 scripts/prepare_assets.py --download
```

下载只补本地缺失文件；WebP 转 PNG 保留透明通道，不重绘原卡图。脚本随后生成本项目代码绘制的状态／适配仙命图标。原图及生成图片都在 Git 忽略范围内，不随推送上传。

状态图标生成需要中文字体，Linux 默认读取 Noto Sans CJK Medium。其他安装路径可设置 `YIXIAN_FONT=/path/to/font.ttf`。

## 代码与发布包

当前[完整整合包](https://github.com/codefromkarl/lunhui-xiaodui-yixian-mod/releases/tag/appearance-v0.7.1)包含运行所需的 Mod DLL、图标与角色素材，来源与清单随包保留；不含游戏本体或宿主程序集。GitHub 自动生成的 Source code 压缩包不含这些运行文件，不能代替整合包。

门派部署更新包只包含本项目的 DLL、PDB 与配置，不包含第三方美术或游戏程序集；用于覆盖已有完整安装，不能作为首次安装包。使用该更新包前须已有包含合法资源的完整安装。

代码许可证尚未由作者指定。原游戏及图鉴资源不受本项目任何后续代码许可证覆盖；来源链接不表示获得原图或角色设计的再分发授权。
