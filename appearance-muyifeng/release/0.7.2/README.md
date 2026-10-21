# 0.7.2：动作与形象协调验收

2026-10-21，基于外观发布基线`936bab9`，在独立分支`feat/appearance-coherence-v0.7.2`完成。主游戏未安装、main未合并、新Release未发布；本目录是源码随附的验收记录，编译DLL不进入Git。

## 先看实际变化

- [关键形象与闭眼状态：0.7.1／0.7.2对照](before-after.jpg)
- [单剑、双剑、提灯施法、重型持环：六时刻动作对照](four-profile-beats.jpg)
- [18位标准尺寸待机](standard-idle.jpg) · [标准尺寸攻击](standard-attack.jpg)
- [18位放大待机](detail-idle.jpg) · [攻击](detail-attack.jpg) · [受击](detail-damaged.jpg) · [闭眼倒地](detail-death.jpg)
- [移动](detail-move.jpg) · [镜像](detail-mirror.jpg) · [恢复](detail-restore.jpg)

这些是实际游戏渲染截图的固定区域裁切拼版，没有缩放或修改生产素材，裁切记录见[SOURCES.json](SOURCES.json)。动作六时刻图使用独立采样的原生待机骨架，方便看清新持物轨迹；全角色攻击图同时叠加原生近战动画，是另一组姿态压力检查，不把两组图当成相同的动画时刻。

## 检查结果

- **548项Unity／Mono图形检查通过，0失败，正常退出**：[完整报告](runtime-results.json)、[退出回执](watchdog.json)。本机Linux／Proton、Unity 2022.3.62f2c1、llvmpipe。
- 18位各25点新动作采样，验证手掌／持物的相对方向与关节连接，确认原生骨骼和实际步枪装备不变；每位保存6个动作时刻，共108张原截图。
- 每位实际调用原生`PlayAttackMotion`、读取原生攻击时钟、受击和倒地状态；实际经过`CombatFeedbackPresenter.PlayAtSource → IVfxPlayer → PooledVfxPlayer.Play`，检查该角色持物起点。该项用无资源加载的测试接收器观察请求，不等同完整战役的特效观感验收。
- 自身治疗目标位置、特效类型／目标／时长等字段不变；嵌套来源恢复、后续原生效果不串用角色、未配置presentation的旧外观维持旧绑定通过。
- 继续覆盖18身份与创建菜单、对象池与保存／原生网络字段往返、原生装备恢复、17位七阶段×8点接合检查、18位HUD与三画幅；牧逸风336帧回归保留。
- **39项Python测试通过**：[记录](unit.log)。**Release构建0警告、0错误**：[记录](build.log)。
- 在新的临时目录从冻结布局完整重建角色元数据，再生成动作元数据；全部有效JSON与受测候选一致：[结果](generator.json)、[命令输出](generator.log)。

已实际检查18人各阶段拼版、标准尺寸图、四类动作分拍图及修复前后对照。首轮看图发现的灯体斜转遮脸、单剑峰值反向已修正；状态头图通过连通区域去除悬浮碎点。握点与发起点落在不透明源像素内。既有牧逸风脸／服装原像素检查保持通过。

## 制作与保留范围

杜伶鸳红灯笼、花沁蕊面帘补图、18位受击／闭眼状态头图均由内置image_gen生成；全部20张旧源PNG保留，共新增4张源PNG。新图与完整提示词对应关系见[素材来源](../../mod/appearance.muyifeng/art/motion-v2/provenance.json)。

吴策原图已经有披毛，本轮恢复的是可见轮廓，不另画衣服。小尺寸协调集中在统一非剑施法持物体量、举持位置和状态表现；牧逸风与其他角色原服装的细节密度差异仍保留，没有声称整套服装已重画为完全一致的画风。

未进行主游戏日常战役或真实Steam双端测试；本轮不安装主游戏。联机仍需各端相同版本。角色身份、装备、结算及存档格式不变。

## 本地受测包

`appearance-muyifeng/output/appearance-yixian-three-sects-0.7.2-motion-and-expression-tested.zip`

106个运行文件，约32.56 MiB；候选、独立受测副本和ZIP逐文件字节回读一致，见[清单](package.json)。ZIP在本地交付，不随Git推送，也未创建新Release。正常退出游戏并备份旧目录后才能替换安装；本轮未执行该操作。
