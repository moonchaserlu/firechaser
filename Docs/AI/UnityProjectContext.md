# Unity 项目上下文

<!-- BEGIN GENERATED: UNITY PROJECT CONTEXT -->

最后核对：2026-10-19
代码基线：`905d8cb8269e06372c271f633b85b3d5bde6233f`

## 技术基线

- Unity：6000.6.3f1
- 渲染管线：Universal Render Pipeline 17.6.0
- 输入：Input System 1.20.0
- 导航：AI Navigation 2.0.14
- 测试：Unity Test Framework 1.8.0
- MCP：MCP for Unity 10.3.0，HTTP 地址 `http://127.0.0.1:8080/mcp`
- Unity 工程目录：`Game/`
- 首个构建场景：`Game/Assets/Game/Scenes/VerticalSlice.unity`
- 备用示例场景：`Game/Assets/Scenes/SampleScene.unity`

## 当前产品方向

项目暂名《向火而生》，是一款 2.5D 斜俯视单人 PvE 搜打撤生存游戏。世界永久黑暗；玩家从固定安全屋出发，沿有限光源探索，用可带回的燃料建立临时火堆，在安全、收益与返程能力之间做取舍。

首个可玩版本只验证：玩家是否会观察光源、规划路线，并为了继续深入或确保返程而消耗本可带回的资源。

当前权威设计文档：

- `Docs/Design/向火而生_首个可玩版本计划_V0.1.md`
- 外部设计来源：`向火而生_游戏策划总结_V0.3.md`

旧文档中的常规昼夜、饥饿和精神压力不属于当前首个可玩版本；永久黑暗方向优先。

## 已有工程能力

- `VerticalSlice.unity` 已进入 Build Settings 第一位，另有对应 NavMesh 数据。
- 玩家：XZ 移动、鼠标瞄准、翻滚、体力、生命、受伤与死亡。
- 战斗：两武器槽、手枪等武器配置、射线射击、换弹、散布、射程衰减、近战入口与枪声事件。
- 敌人：一种 NavMesh 近战敌人，具备待机、调查、警戒、追逐、攻击和死亡状态。
- 搜刮：搜索容器、掉落表、堆叠背包、交互距离及受伤取消搜索。
- 局内闭环：开始、暂停、固定撤离区、死亡/成功结算、重开与会话级仓库。
- HUD：生命、体力、弹药、武器槽、背包、交互、撤离进度、仓库和结算提示。
- 编辑器工具：`Wilderness/Build Vertical Slice` 可重建配置、预制体、灰盒场景与 NavMesh。
- 测试：EditMode 覆盖核心纯逻辑；PlayMode 覆盖搜刮撤离、撤离中断、死亡结算和受伤取消搜索。

## 当前缺口

- 尚无区分“装饰光”与“有效安全光”的玩法光源契约。
- 尚无离光预警、黑暗暴露和持续伤害。
- 尚无火堆资源、放置、燃烧和安全范围。
- 尚无流血状态、地面血迹和敌人循迹行为。
- 尚无安全屋返程语义和三档光源密度地图。
- 尚无随机事件框架及两种原型事件。
- 现有场景仍有方向光，不符合永久黑暗目标。

## 架构与修改边界

- 运行时代码位于 `Game/Assets/Game/Runtime/`，命名空间为 `WildernessExtraction`。
- 配置优先使用 ScriptableObject，纯规则优先落在可 EditMode 测试的 C# 类中。
- 场景、预制体和 `.asset` 由 Unity Editor、UnityMCP 或现有 Builder 生成，不直接手改序列化 YAML。
- 修改 `VerticalSliceBuilder` 时必须保证重复运行不会覆盖策划已调过的配置资产数值。
- 扩展现有组件，不重做移动、射击、背包、搜刮、敌人和结算系统。
- 每个里程碑都要检查 Unity 编译、Console，并补足与风险相称的 EditMode/PlayMode 验证。

## 已知风险

- 美术亮度必须和玩法安全半径一致，否则玩家会因误判而把伤害归咎于系统。
- 若燃料完全随机，可能出现无有效选择的软锁；近区必须提供最低保底供给。
- 血迹只能放大风险，不能形成无限刷怪或不可恢复的死亡螺旋。
- `SessionStash` 当前只保证运行会话内持久，不等于磁盘存档。

<!-- END GENERATED: UNITY PROJECT CONTEXT -->
