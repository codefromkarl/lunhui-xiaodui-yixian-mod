# 轮回小队 · 弈仙门派 Mod

一个发布包 `yixian.sect`，三个内部门派模块。开发源已从反编译分析目录迁入本项目；本仓库是唯一开发入口，生成、构建和测试不读取旧目录。

公开仓库：`codefromkarl/lunhui-xiaodui-yixian-mod`。当前 v0.7.0 已完成 Linux／Proton 和 Mac／Sikarugir Wine 下的离线与真实 Mono 验证；本仓库发布源码，不自动分发带第三方原图的安装包。覆盖范围、崩溃排查和未覆盖项见[测试状态](docs/testing-status.md)。

## 首次克隆后构建

需要 Python 3.10+、.NET 8 SDK，以及你本地合法安装的《轮回小队 Demo》。仓库不包含游戏程序集与第三方原图。

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

## Mac 开发环境与不依赖游戏的检查

macOS 自带的 Python 3.9 不满足当前 Pillow 依赖要求；可使用 Homebrew 的 Python 3.12：

```bash
brew install python@3.12  # 已安装则跳过
python3.12 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements-dev.txt
```

若尚无 .NET 8，可用微软官方脚本安装至用户目录，与其他版本并存：

```bash
mkdir -p logs/setup
curl -fL https://dot.net/v1/dotnet-install.sh -o logs/setup/dotnet-install.sh
bash logs/setup/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet" --no-path
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --list-sdks
dotnet --list-runtimes
```

安装脚本参考：[微软官方说明](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)。上述环境变量只影响当前终端，不修改全局 shell 配置。

没有游戏程序集时，仍可准备美术并执行完整的内容检查：

```bash
# 先阅读 THIRD_PARTY_ASSETS.md；下载用于本地开发的缺失图片
python3 scripts/prepare_assets.py --download
bash scripts/check.sh --content-only
```

`check.sh` 自动优先使用项目 `.venv` 的 Python；Mac 图标生成默认使用系统黑体，也可通过 `YIXIAN_FONT` 覆盖。内容模式执行图片转换、内容生成、原作来源和契约检查、Python 测试，日志保存在 `logs/`。成功仅代表这些检查通过，不代表 C# 构建或游戏实机验证通过。检查过程不安装 Mod，也不启动游戏。

取得本地游戏的完整 `ReincarnationSquad_Data/Managed` 目录后，运行：

```bash
export GAME_MANAGED="/实际游戏目录/ReincarnationSquad_Data/Managed"
bash scripts/check.sh
```

完整模式额外执行 Release 构建和两套 C# 离线测试；默认依次使用 `DOTNET_ROOT`、`$HOME/.dotnet/dotnet`、PATH 中的 dotnet。Mac 默认游戏路径为 `~/Library/Application Support/Steam/steamapps/common/轮回小队 Demo/`，CrossOver 等其他安装位置需要显式设置 `GAME_MANAGED`。缺少游戏程序集时，完整检查返回非零，不会把 C# 检查标为通过。

《轮回小队 Demo》目前提供 Windows 版本。Mac 可在 Windows Steam 包装器（如 CrossOver／Sikarugir）中安装；Mac 原生 Steam 的游戏目录通常不含这个 Windows Demo。先正常退出游戏并安装 Mod，再启动游戏，在 Mod 管理界面启用包并确认代码许可；运行自测前再次正常退出游戏。

Mac 安装时可显式指定游戏目录，也可从 `GAME_MANAGED` 推导；进程检测支持 Wine 的 Windows 进程名。实机启动器通过包装器内的 Windows Steam 启动，另需以下本地路径：

```bash
export YIXIAN_WINE="/Steam包装器.app/Contents/SharedSupport/wine/bin/wine"
export WINEPREFIX="/Steam包装器.app/Contents/SharedSupport/prefix"
export YIXIAN_PERSISTENT_DATA="$WINEPREFIX/drive_c/users/实际用户名/AppData/LocalLow/yuyuyi/轮回小队"
python3 scripts/install.py --game "${GAME_MANAGED%/ReincarnationSquad_Data/Managed}" --apply
python3 scripts/run_yixian_runtime_tests.py --include-fates
```

启动器也接受 `--game`、`--wine`、`--wine-prefix`、`--persistent-data` 和 `--timeout`。Sikarugir 的 `Contents/Frameworks` 动态库路径自动加入子进程环境；不修改系统动态库或全局 shell 配置。日志、逐项进度和最终报告仍写入 `logs/yixian-runtime-tests/`。离线检查通过与游戏内 Mono 验证通过分别记录；单独安装开发环境不代表实机验证通过。

2026-10-20 已在 Apple M1 Pro／macOS 26.5.2／Sikarugir Wine 10.0 上完成构建、5858＋185 项 C# 离线检查、17 项 Python 检查，以及 28＋6 组真实 Mono 自测；全部通过。具体覆盖和本地证据见[测试状态](docs/testing-status.md)。本机使用 Git 忽略的 `.env.local` 保存环境变量，载入后可直接执行上述检查命令。

第三方资源与代码许可证状态见[资源说明](THIRD_PARTY_ASSETS.md)。代码生成候选在 `mod/yixian.sect/`，编译后的 DLL 和本地图片不进入 Git。

## 三个门派，统一体量

| 门派 | 内容 | 状态 |
|---|---|---|
| 云灵剑宗 | 五境界，48张牌、12个四张包、4个独立仙命 | 保留既有牌与近期修复 |
| 五行道盟 | 五境界，48张牌、12个四张包、4个独立仙命 | 灵印激活、锋锐击伤、水势与五灵阵 |
| 七星阁 | 五境界，48张牌、12个四张包、4个独立仙命 | 常驻星力、逐次卦象、八格星位与后招 |

每个角色最多一个弈仙门派，允许搭配原生血统。每派境界卡包数为2／2／2／3／3，每包4张；入宗赠两个炼气包，筑基二选一，之后购买。五行、七星依据归档原作1级牌表与卡面费用适配；手动出牌、属性成长和卡包分组采用本Mod规则。

四项剑宗仙命使用各自的《弈仙牌》原作仙命图标；剑阵守护状态共用其仙命图标。对应与来源见[图标来源](mod/yixian.sect/icons/fates/SOURCES.md)。

出牌不收基础斗气：无灵气费用的牌消耗0斗气，1／2／3灵气分别消耗4／8／12斗气；减费可降至0。剑宗48张牌逐张按原卡左上角标核费，飞牙剑为1灵气／4斗气，巨鹏2灵气／8斗气，巨鲲3灵气／12斗气；三峰剑无灵气费用。主动摸牌仍耗1行动力、不耗斗气。

五行道盟、七星阁各48张牌也已逐张重核原卡费用角标：分别13／11张付费牌，其余35／37张零费，均与现有数据一致。真实游戏Mono逐张验证费用预览、HUD费用模型、实际扣费及不足费用时的原子拒绝；水灵·波澜在水灵条件下免灵气。逐牌证据见[测试状态](docs/testing-status.md)。

剑宗构筑入口：战斗中打开**卡包管理 → 推荐构筑**。套用只改变待选，点击原生保存按钮后生效；说明和各境界配置见[剑宗三路线](docs/sword-builds.md)。

全部卡牌与公式：[卡包规则](docs/card-packs.md)。开发约定：[架构与兼容](docs/architecture.md)。当前验收与实机依赖：[实施清单](docs/implementation-plan.md)。

内容身份自v0.6.0统一到`yixian.sect.*`；不提供旧ID映射或旧存档迁移，需要开新局；契约及候选位置见[新版身份](docs/identity-migration.md)。

## 修改后生成并检查

```bash
bash scripts/check.sh
```

运行环境：Python 3.10+、.NET 8 SDK、游戏本地Managed程序集。`GAME_MANAGED`可覆盖构建／测试引用路径。美术在本地准备，不随Git分发。状态图标与预览生成需要Pillow和中文字体（Linux默认Noto，Mac默认系统黑体，可用`YIXIAN_FONT`指定）；脚本不会重绘有来源的原卡图：

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

自动化复测可用 `python3 scripts/run_yixian_runtime_tests.py --include-fates --quit-after-tests`：全部请求的套件写完报告后，工具等待游戏自行完成主菜单启动保护，再调用 `Application.Quit(0)` 正常退出。若停在本地登录场景且没有用户局，只通过原生场景导航打开主菜单，不激活档案或绑定局。启动器核对本轮报告对应的退出回执并确认目标进程消失；无回执的提前退出仍判失败。默认启动方式继续保留游戏运行。

不加退出选项时，自测完成也会在上述条件下完成原生主菜单启动，然后保留游戏运行。HUD专项现在渲染三派首回合卡牌页及能力页截图，并核对“卡牌”页签可见、手牌3张、能力页不混入手牌。

单独重跑五行／七星96张费用专项：`python3 scripts/run_yixian_runtime_tests.py --costs-only --quit-after-tests`。启动器复制独立原卡费用账本作为测试预期。Mac已准备独立游戏副本时，可另加 `--game "/副本目录" --direct-game --headless` 验证真实Mono命令与HUD模型；副本需要已启用Mod、允许代码，且含内容为`5241190`的`steam_appid.txt`，Windows Steam需已登录。无图形模式不验证屏幕渲染或鼠标交互。

如需回退，先正常退出游戏，再用安装回执中的 `previous-package` 恢复到原位置。保留完整备份，不能靠清空存档回退。旧包内可能残留历史`src/`；唯一开发源为本项目`src/`。

## 门派设计与验收

五行的具体规则见[五行道盟](docs/five-elements.md)，七星的具体规则见[七星阁](docs/seven-stars.md)。新增境界牌、资源状态与仙命均有可执行结算和专项测试；实际Mono、存档与界面的最新结果以[实施清单](docs/implementation-plan.md)为准。
