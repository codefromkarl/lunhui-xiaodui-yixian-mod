# 第三方资源与公开范围

本仓库公开 Mod 源码、规则数据、来源索引和开发工具，不包含《轮回小队》的游戏 DLL／可执行文件，也不分发《弈仙牌》的卡图、门派徽章和仙命原图。

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

代码许可证尚未由作者指定。原游戏及图鉴资源不受本项目任何后续代码许可证覆盖。公开源码仓库不等于已经验收的可安装版本；仓库未自动发布附带第三方美术的 Release 包。

## 独立角色外观包

`appearance-muyifeng`发布18位角色的生成像素拆件、代码和元数据，完整安装包在`appearance-v0.7.1` Release提供。其素材不是下载的原角色立绘；逐角色参考网页、原图链接及生成提示词保存在各自的`provenance.json`和`prompt-v1.txt`。这些参考链接不表示取得原角色设计或原图的再分发授权，相关权利仍归原权利人。该Release不含《轮回小队》DLL／可执行文件，也不含本仓库门派玩法包使用的原卡图、徽章或仙命原图。
