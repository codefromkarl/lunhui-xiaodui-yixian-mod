# 轮回小队 · 弈仙门派 Mod

在《轮回小队 Demo》中体验《弈仙牌》的三门派构筑，搭配 18 位角色造型。

**[下载完整 Mod v0.7.1](https://github.com/codefromkarl/lunhui-xiaodui-yixian-mod/releases/download/sect-v0.7.1/lunhui-yixian-full-mod-0.7.1.zip)** · [安装位置](#安装位置) · [如何使用](#如何使用)

![三门派18位角色的游戏内待机造型汇总](docs/images/characters.jpg)

## 门派 Mod

云灵剑宗、五行道盟、七星阁，共 **136 张卡牌、12 项仙命、五个境界**。每名角色可加入一个弈仙门派，并搭配原生血统。

从炼气到化神，解锁卡牌与仙命，在战斗中手动出牌、调整构筑。首回合发 3 张，之后每回合自动补 2 张；需要额外补牌时，可消耗 1 行动力主动摸牌。

**v0.7.1 剑宗更新**：普通单段剑招取筋肉、多段取反应，灵剑路线取能量。新增20点持续被动「灵剑转化」，实际扣除筋肉／反应／体质各60%转为能量，后续加点同样生效。[完整说明](docs/sword-scaling.md)。

各派的路线与机制：[云灵剑宗](docs/sword-builds.md) · [五行道盟](docs/five-elements.md) · [七星阁](docs/seven-stars.md)

![游戏内卡包管理：查看卡牌效果、选择卡包并保存构筑](docs/images/card-packs.png)

## 外观 Mod

三门派共 **18 位可选造型**，带角色自有持物与头肩头像，只改变外观，不改装备和数值。玩法与外观是两个独立包，可以一起使用，也可只启用其中一个。

选择造型不会自动加入对应门派，角色外观和玩法路线可以自由搭配。

![角色创建界面的造型选择器：按门派选择角色并预览](docs/images/appearance-picker.png)

## 安装位置

在 Steam 库中右键《轮回小队 Demo》，选择 **属性 → 已安装文件 → 浏览**，打开游戏目录。Mod 放在这里的 `Mods/` 文件夹中，与 `ReincarnationSquad.exe` 同级：

```text
轮回小队 Demo/
├── ReincarnationSquad.exe
└── Mods/
    ├── yixian.sect/          # 门派玩法
    └── appearance.muyifeng/  # 角色外观
```

1. 正常退出游戏，下载并解压上方完整包。包内已包含门派与外观，无需分别下载。
2. 将包内的 `Mods/` 合并到游戏目录。更新前把对应旧包移到 `Mods/` 外备份，不要删除其他 Mod，也不要套成 `Mods/Mods/`。
3. 启动游戏，在 Mod 管理中启用「弈仙门派」与「弈仙牌 · 三门派角色外观」，按提示确认代码许可；若提示重启，正常重启。

## 如何使用

**门派玩法**：新开一局，在主神空间的角色商店选择门派入宗，使用奖励点突破境界、取得卡牌与仙命。进入战斗后，在「卡牌」页出牌，通过 **卡包管理** 调整构筑；推荐方案需要确认保存才生效。

**角色外观**：新建角色时，打开 **信息 → 造型**，选择门派，再选角色；选择「原生自定义」可恢复原生造型。目前不支持给已创建的原生角色中途换肤。

> [!IMPORTANT]
> 从旧版门派 Mod 更新时，建议新开局；旧版牌库的存档请搭配对应旧版 Mod 使用。联机玩家需安装并启用相同版本。

## 已有完整安装的覆盖更新

也可下载[无图覆盖更新包](https://github.com/codefromkarl/lunhui-xiaodui-yixian-mod/releases/download/sect-v0.7.1/yixian-sect-0.7.1-update-no-art.zip)，只更新门派DLL和配置，**保留旧 icons/**。它不能用于首次安装。完整包包含已发布受测的外观v0.7.1，未混入外观开发中改动。

开发、来源与当前验收：[开发指南](docs/development.md) · [架构](docs/architecture.md) · [测试状态](docs/testing-status.md) · [第三方资源](THIRD_PARTY_ASSETS.md)。本版完整门派实机40／40、仙命6／6通过；正式游戏已安装并逐文件回读。血统成长表未在本轮重排。
