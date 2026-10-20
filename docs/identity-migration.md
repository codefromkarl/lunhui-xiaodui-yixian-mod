# v0.6.0：只使用新身份，不兼容旧ID存档

按用户最新决定，取消旧ID精确映射与旧存档迁移。血统、卡牌、卡包和仙命统一使用`yixian.sect.*`；原生BL08身份不改，原生命名警告及重名拒绝保持有效。

| 内容 | 身份 |
|---|---|
| 血统 | `yixian.sect.lineage.sword` / `five` / `star` |
| 卡牌 | `yixian.sect.card.<card-key>` |
| 卡包 | `yixian.sect.pack.<pack-key>` |
| 仙命 | `yixian.sect.fate.<school>.<fate-key>` |
| 状态 | `yixian.sect.status.*` |

`icon.<内容ID>`与牌桌引用同步使用新身份，图像文件保持。`SectIds`和`identity_ids.py`只构造新ID，不读取映射表。生成内容不含`legacyIds`、迁移版本字段或旧身份副本；没有本Mod的本局／档案读档恢复补丁。

## 从新局开始

**此版本不兼容使用旧ID的存档，请开新局。** 旧局若要保留，继续使用对应旧版Mod。不要用新版读取后覆盖旧局；原生缺失内容修复可能移除无法识别的血统或卡包，本Mod不会把旧引用转换为新引用，也不拦截原生修复。

移除兼容层不会删除用户的存档、改动代码许可或自动安装新包。新ID局仍使用原生保存／恢复路径，现有新ID往返测试保留。历史剑宗夹具仅用于规则和素材回归，不作为旧存档迁移入口。

## 候选和验收

身份候选仍单独放在`logs/namespace-migration/candidate-project/`，包含可复现源码和`mod/yixian.sect/`完整包；本轮也同步移除了其中的兼容层。该快照保留原命名迁移基线玩法，不包含另一条正在进行的五行原作卡表替换。

当前离线证据：`logs/namespace-migration/no-legacy-check.log`。构建0警告／0错误，主检查5026项、仙命181项通过；原生加载器消费218条内容声明无诊断，旧前缀负例仍警告、重复ID负例仍拒绝。测试确认目录引用使用新ID、原生对象保留、生成数据无旧ID表，且程序集没有读档迁移补丁。

隔离候选时，共享开发目录的完整检查曾被同期五行替换的`five_content.describe_card`缺失阻断，见`logs/namespace-migration/shared-no-legacy-check.log`；这不是独立候选的失败，也不由本轮改动该门派机制解决。共享DLL构建还发现五行`Activate`重复定义，见`shared-no-legacy-build.log`。共享候选中先前含兼容层的DLL／PDB已移到`logs/namespace-migration/retired-compatibility-build/`，不留可被误装的旧构建；五行负责人修复并构建后会重新生成该DLL。此前仅交付候选；2026-10-20 17:47经用户授权并确认退出游戏后，已安装这份无兼容层的独立v0.6.0候选。未启动游戏或改动真实存档。

安装回执及旧包备份：`logs/namespace-migration/candidate-project/logs/installation/20261020-174723/`。已逐文件核对安装包与已验证候选一致，确认生成内容没有旧ID映射。证据：`logs/namespace-migration/install-verification.json`。完整Mono正式命令及实际UI验收尚未运行；本次授权仅执行安装。启动后请开新局。
