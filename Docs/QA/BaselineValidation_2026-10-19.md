# M0 基线验证记录（2026-10-19）

里程碑：M0 冻结现有闭环。本次没有修改任何玩法代码、场景或配置。

## 环境

- Unity 6000.6.3f1，URP 17.6；编辑器已打开 `Assets/Game/Scenes/VerticalSlice.unity`。
- 测试经 MCP for Unity（`http://127.0.0.1:8080/mcp`）在已打开的编辑器内运行；编辑器占用工程，无法另起 batchmode。
- Git 基线提交：`905d8cb Initial Unity project`（`main`）。
- 工作区存在用户/策划未提交改动：`CLAUDE.md`、`Docs/AI/UnityProjectContext.md`、`Docs/Design/GameDesignBrief.md`、`Docs/Design/VerticalSlicePlan.md`、`Game/Packages/manifest.json`、`Game/Packages/packages-lock.json`，以及未跟踪的 `Docs/Design/向火而生_首个可玩版本计划_V0.1.md`、`Game/ProjectSettings/Packages/` 等。M0 未触碰这些文件。

## 自动化测试

| 套件 | 结果 | 用时 |
| --- | --- | --- |
| EditMode | 38/38 通过 | 0.36 s |
| PlayMode | 4/4 通过 | 10.0 s |

PlayMode 用例（`ExtractionLoopTests`）：

- `LootThenExtract_AddsToStash_AndPersistsIntoNextRun`
- `LeavingZone_InterruptsExtraction`
- `Death_LosesInventory_StashUnchanged`
- `SearchCancelledWhenDamaged`

`ClaudeTask-002` 中记录的“PlayMode 2/4 通过”已不复现，当前全部通过。

## Console

- 测试前：仅 1 条警告 `Account API did not become accessible within 30 seconds`（Unity 账号服务网络问题，与工程无关）。
- 进入 Play Mode 运行 8 秒：0 条 Error / Warning / Exception / Assert。

## 手动走通

本轮由代理执行，无法代替人手操作键鼠；以下闭环由 PlayMode 测试间接覆盖：搜刮、撤离、撤离中断、死亡清包、受伤取消搜索、仓库跨局保留。

仍需用户手动确认一次：移动与翻滚、手枪射击与换弹、敌人近战、死亡后重开。

## 已知基线问题

- 无新增失败。
- `WildernessSliceSetup.cs` 与 `CoreLogicTests.cs` 仍以 `#if false` 禁用（重复实现，按施工单保持不动）。
- 场景仍有方向光，不符合永久黑暗目标（属于 M1 范围，M0 不处理）。
