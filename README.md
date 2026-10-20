# 轮回小队 · 弈仙门派 Mod

一个发布包 `yixian.sect`，三个内部门派模块。开发源已从反编译分析目录迁入本项目；本仓库是唯一开发入口，生成、构建和测试不读取旧目录。

公开仓库：`codefromkarl/lunhui-xiaodui-yixian-mod`。当前 v0.7.0 已完成 Linux／Proton 下的离线与真实 Mono 验证；本仓库发布源码，不自动分发带第三方原图的安装包。覆盖范围、崩溃排查和未覆盖项见[测试状态](docs/testing-status.md)。

## 首次克隆后构建

需要 Python 3、.NET 8 SDK，以及你本地合法安装的《轮回小队 Demo》。仓库不包含游戏程序集与第三方原图。

```bash
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements-dev.txt
# 先阅读 THIRD_PARTY_ASSETS.md；以下命令显式准备本地美术
python3 scripts/prepare_assets.py --download
export GAME_MANAGED="/你的Steam目录/steamapps/common/轮回小队 Demo/ReincarnationSquad_Data/Managed"
bash scripts/check.sh
```

现有自动化脚本以 Linux/Proton 为主要开发环境。Windows 可通过 `dotnet build src/YixianSect.csproj -c Release -p:GameManaged="<游戏目录>\ReincarnationSquad_Data\Managed"` 指定程序集；Windows 的自动启动与界面测试尚未验收。

第三方资源与代码许可证状态见[资源说明](THIRD_PARTY_ASSETS.md)。代码生成候选在 `mod/yixian.sect/`，编译后的 DLL 和本地图片不进入 Git。

## 三个门派，统一体量

| 门派 | 内容 | 状态 |
|---|---|---|
| 云灵剑宗 | 五境界，48张牌、12个四张包、4个独立仙命 | 保留既有牌与近期修复 |
| 五行道盟 | 五境界，48张牌、12个四张包、4个独立仙命 | 灵印激活、锋锐击伤、水势与五灵阵 |
| 七星阁 | 五境界，48张牌、12个四张包、4个独立仙命 | 常驻星力、逐次卦象、八格星位与后招 |

每个角色最多一个弈仙门派，允许搭配原生血统。每派境界卡包数为2／2／2／3／3，每包4张；入宗赠两个炼气包，筑基二选一，之后购买。五行、七星依据归档原作1级牌表与卡面费用适配；手动出牌、属性成长和卡包分组采用本Mod规则。

四项剑宗仙命使用各自的《弈仙牌》原作仙命图标；剑阵守护状态共用其仙命图标。对应与来源见[图标来源](mod/yixian.sect/icons/fates/SOURCES.md)。

出牌不收基础斗气：无灵气费用的牌消耗0斗气，1／2灵气分别消耗4／8斗气；减费可降至0。主动摸牌仍耗1行动力、不耗斗气。

剑宗构筑入口：战斗中打开**卡包管理 → 推荐构筑**。套用只改变待选，点击原生保存按钮后生效；说明和各境界配置见[剑宗三路线](docs/sword-builds.md)。

全部卡牌与公式：[卡包规则](docs/card-packs.md)。开发约定：[架构与兼容](docs/architecture.md)。当前验收与实机依赖：[实施清单](docs/implementation-plan.md)。

内容身份自v0.6.0统一到`yixian.sect.*`；不提供旧ID映射或旧存档迁移，需要开新局；契约及候选位置见[新版身份](docs/identity-migration.md)。

## 修改后生成并检查

```bash
bash scripts/check.sh
```

运行环境：Python 3、.NET 8 SDK、游戏本地Managed程序集。`GAME_MANAGED`可覆盖构建／测试引用路径。美术在本地准备，不随Git分发。状态图标与预览生成需要Pillow和Noto字体（可用`YIXIAN_FONT`指定）；脚本不会重绘有来源的原卡图：

```bash
python3 scripts/build_prototype_art.py
```

内容源在 `data/`，C#源在 `src/`；`mod/yixian.sect/`是生成候选。只改候选JSON会被下一次生成覆盖。

## 正常退出游戏后再安装

```bash
python3 scripts/install.py          # 只读预检
python3 scripts/install.py --apply  # 保存旧包后安装；游戏运行中拒绝写入
python3 scripts/run_yixian_runtime_tests.py --include-fates
```

安装位置仍为 `轮回小队 Demo/Mods/yixian.sect`，不要另装一份旧剑宗包并同时启用。备份与安装回执保存在本项目 `logs/installation/`，不放在游戏Mods目录中。

安装不会修改启用名单、代码许可或存档。重启后如提示代码许可，请在游戏内确认。实机测试只创建内存局，结束后保留游戏运行；通过游戏菜单正常退出，不强退。脚本区分断言失败、报告超时和进程提前退出，记录最后用例与`watchdog.json`；启动异常也会留存日志。

如需回退，先正常退出游戏，再用安装回执中的 `previous-package` 恢复到原位置。保留完整备份，不能靠清空存档回退。旧包内可能残留历史`src/`；唯一开发源为本项目`src/`。

## 门派设计与验收

五行的具体规则见[五行道盟](docs/five-elements.md)，七星的具体规则见[七星阁](docs/seven-stars.md)。新增境界牌、资源状态与仙命均有可执行结算和专项测试；实际Mono、存档与界面的最新结果以[实施清单](docs/implementation-plan.md)为准。
