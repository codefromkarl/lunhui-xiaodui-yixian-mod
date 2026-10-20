# 剑宗仙命回归

唯一C#源：`src/Sects/Sword/FateRules.cs`及`src/Tests/FateRuntimeTests.cs`。内容源在`data/sword/fates.json`和`fate-statuses.json`，统一生成器不会改写源码。

项目根目录执行`bash scripts/check.sh`包含离线仙命回归。游戏正常退出后，`python3 scripts/run_yixian_runtime_tests.py --include-fates`在同一进程顺序完成主套件和独立仙命套件，不绑定真实存档。

历史入口`python3 scripts/yixian-fates/run_runtime.py`仍只运行仙命专项。唯一验收清单见`docs/implementation-plan.md`。
