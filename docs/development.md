# 开发、构建与实机验证

本指南面向从源码构建门派 Mod 的开发者。安装与游玩入口见[主 README](../README.md)，各门派玩法见[剑宗](sword-builds.md)、[五行](five-elements.md)和[七星](seven-stars.md)。外观 Mod 使用独立的[构建与测试流程](../appearance-muyifeng/README.md)。

以下命令均在仓库根目录执行。内容源为 `data/`，C# 源码为 `src/`，`mod/yixian.sect/` 是生成候选；本项目不再从旧反编译分析目录读取开发源。

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

第三方资源与代码许可证状态见[资源说明](../THIRD_PARTY_ASSETS.md)。代码生成候选在 `mod/yixian.sect/`，编译后的 DLL 和本地图片不进入 Git。

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

安装位置仍为 `轮回小队 Demo/Mods/yixian.sect`，不要另装一份旧剑宗包并同时启用。备份与安装回执保存在本项目 `logs/installation/`，不放在游戏Mods目录中。 安装指定受测包可用 `python3 scripts/install.py --source "/受测包/yixian.sect" --apply`；内容契约针对该包检查，不会改装共享目录后来重建的候选。

安装不会修改启用名单、代码许可或存档。安装器备份旧包后，清除新包已不包含的副职／炼丹／升阶内容表及对应图标目录，保留其他未知文件和`TestResults`。重启后如提示代码许可，请在游戏内确认。实机测试只创建内存局，结束后保留游戏运行；通过游戏菜单正常退出，不强退。脚本区分断言失败、报告超时和进程提前退出，记录最后用例与`watchdog.json`；启动异常也会留存日志。

自动化复测可用 `python3 scripts/run_yixian_runtime_tests.py --include-fates --quit-after-tests`：全部请求的套件写完报告后，工具等待游戏自行完成主菜单启动保护，再调用 `Application.Quit(0)` 正常退出。若停在本地登录场景且没有用户局，只通过原生场景导航打开主菜单，不激活档案或绑定局。启动器核对本轮报告对应的退出回执并确认目标进程消失；无回执的提前退出仍判失败。默认启动方式继续保留游戏运行。

不加退出选项时，自测完成也会在上述条件下完成原生主菜单启动，然后保留游戏运行。HUD专项现在渲染三派首回合卡牌页及能力页截图，并核对“卡牌”页签可见、手牌3张、能力页不混入手牌。

单独重跑五行／七星88张费用专项：`python3 scripts/run_yixian_runtime_tests.py --costs-only --quit-after-tests`。启动器复制独立原卡费用账本作为测试预期。Mac已准备独立游戏副本时，可另加 `--game "/副本目录" --direct-game --headless` 验证真实Mono命令与HUD模型；副本需要已启用Mod、允许代码，且含内容为`5241190`的`steam_appid.txt`，Windows Steam需已登录。无图形模式不验证屏幕渲染或鼠标交互。

五行修行专项：`python3 scripts/run_yixian_runtime_tests.py --cultivation-only --quit-after-tests`。验证自动主属性、成长预算转移、保存恢复、战斗锁定、原生八张组牌及只读状态界面；启动器核对`five-cultivation`报告范围，需要先安装包含该专项的候选。

气疗术／气吞山河指定治疗专项：`python3 scripts/run_yixian_runtime_tests.py --star-healing-only --quit-after-tests`。只运行两张牌的真实玩家／网络指令场景和诊断、用户隔离检查，不带全套或仙命；需要先安装包含新专项的候选。启动器会核对报告的`star-selected-healing`范围，旧DLL意外跑出全套报告也不能计为专项通过。该专项不覆盖物理鼠标选人或治疗动画；本轮候选位置与实机待验项见[测试状态](testing-status.md)。

三派决策与多段演出专项：安装到独立测试副本后运行 `python3 scripts/run_yixian_runtime_tests.py --depth-effects-only --quit-after-tests`。报告范围须为 `sect-depth-effects`，旧 DLL 的全套结果不能代替本专项；候选、演出检查与安装状态见[测试状态](testing-status.md)。

如需回退，先正常退出游戏，再用安装回执中的 `previous-package` 恢复到原位置。保留完整备份，不能靠清空存档回退。旧包内可能残留历史`src/`；唯一开发源为本项目`src/`。

## 门派覆盖更新包

完整门派候选通过检查和实机后，用`python3 scripts/build_release.py --source <受测完整包> --output <更新包.zip> --source-commit <提交号>`生成无第三方原图、无游戏程序集的部署更新包。压缩包中的`UPDATE.md`说明覆盖步骤，`release-manifest.json`列出需要旧完整安装提供的图片；不把该更新包称为首次安装包。

剑宗持续转化专项：`python3 scripts/run_yixian_runtime_tests.py --sword-scaling-only --quit-after-tests`，覆盖先购买能力、再正式加点、升级及保存恢复。
