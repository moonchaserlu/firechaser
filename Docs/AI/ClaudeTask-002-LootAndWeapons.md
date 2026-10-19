# Claude 施工单 002：搜刽物品与武器深化

## 目标

依据 `Docs/Design/LootAndWeaponsDesign.md`，在当前首个可玩切片上实现物品、容器、枪械、近战和声音引怪机制。不得加入策划明确排除的系统。

## 开工前必读

- `CLAUDE.md`
- `Docs/AI/UnityProjectContext.md`
- `Docs/Design/VerticalSlicePlan.md`
- `Docs/Design/LootAndWeaponsDesign.md`

## 开工前清理

- 当前权威场景为 `Assets/Game/Scenes/VerticalSlice.unity`。
- 当前权威搭建器为 `Assets/Game/Editor/VerticalSliceBuilder.cs`。
- `WildernessSliceSetup.cs` 与 `CoreLogicTests.cs` 已因并发重复实现而使用 `#if false` 禁用；本任务不得重新启用或基于其继续开发。
- 先运行现有 EditMode 和 PlayMode 测试，记录基线。已知上一轮 PlayMode 测试为 2/4 通过；先修复已有两项失败，再扩展功能。

## 里程碑

### A. 修复基线

- 修复 `ExtractionLoopTests` 中“搜刮后撤离”和“死亡清包”失败。
- 保持 EditMode 34 项全部通过。
- 进入 Play Mode 5 秒无 Error、Exception 或 Assert。

### B. 物品与背包模型

- 扩展物品类别、稀有度、尺寸、堆叠、重量、价值和标签。
- 实现堆叠、两格物品判定、丢弃。
- 为纯逻辑添加 EditMode 测试，覆盖容量、堆叠、跨格、丢弃与结算。

### C. 容器与搜索

- 实现六种容器身份和独立掉落池。
- 实现打开后保持掉落、选择拿取、背包不足反馈。
- 普通容器 1.5 秒，保险箱 4 秒并发布声音事件。

### D. 武器框架与枪械

- 将现有射击重构为数据驱动武器框架。
- 实现三种口径、背包弹药、换弹结算、两个武器槽和切换。
- 实现手枪、霰弹枪、半自动步枪的散布、衰减和声音半径。
- 保留当前旧手枪手感作为回归基线。

### E. 近战与声音调查

- 实现短刀和消防斧。
- 使用明确攻击阶段、体力消耗、一次攻击一次命中。
- 敌人接收声音事件并前往最近声源调查；到达后重新感知玩家。

### F. UI、场景与验证

- 更新 HUD、搜索面板、物品/武器比较和反馈。
- 用 `VerticalSliceBuilder` 幂等生成新增配置、掉落表和场景引用。
- 保留原灰盒主路线，在深区放置高价值保险箱与高级武器机会。
- 运行完整 EditMode、PlayMode 和至少一次手动闭环冒烟。

## 工程约束

- 不手工编辑 `.unity`、`.prefab` 或 `.asset` YAML。
- 运行时代码不得引用 `UnityEditor`。
- 配置由 ScriptableObject 持有，状态逻辑尽量与 MonoBehaviour 解耦。
- 不引入第三方依赖，不建立联网抽象。
- 不同时维护两套物品、武器或场景搭建框架；先迁移再删除旧路径。
- 每完成一个里程碑，先确认 Unity 编译与 Console 状态。

## 完成标准

1. 旧切片闭环不退化。
2. 三把枪、两件近战武器均可获得、装备和使用。
3. 弹药口径、弹匣、换弹和背包库存一致，无复制或吞弹。
4. 容器身份、掉落池和选择拿取生效。
5. 近战阶段、体力与单次命中正确。
6. 枪声能引发敌人调查，近战明显更安静。
7. EditMode 与 PlayMode 全部通过，Play Mode Console 无新增错误。
8. 最终报告列明测试数量、场景入口、操作方式、修改文件和未完成项。
