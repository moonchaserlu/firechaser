# Map01 工作台合成（实现说明）

> 日期：2026-10-29　分支：`claude/map01-whitebox`（未提交）　参考：逃离鸭科夫工作台配方（只参考设计）

## 玩法

- 庇护所里、炉火光圈内有一张工作台（Layout 道具 `Workbench`，局部 (-1.4, -2.4)），开局 Q02 之前也能安全使用。
- 靠近按 E 打开合成面板，世界暂停（同阅读记录）。W/S 或 ↑/↓ 选择，E / Enter 制作，Esc 离开，也可以用鼠标点。
- 面板左侧按分组列配方，标“可制作 / 缺材料 / 未解锁”；右侧是说明、材料“现有/需要”和制作按钮，未解锁时写明“完成 Qxx「任务名」后解锁”。
- 合成请求走 `Map01Authority`（`InteractionAction.Craft`），距离、设施、解锁、材料、背包空间都在权威层校验；背包放不下时整笔回滚。

## 配方与任务节奏

任务模式下每条配方在对应任务完成后解锁，保证 Q01 燃料、Q02 零件与电池、Q03 药品不能靠合成跳过；原救援循环（非任务模式）全部可用。

| 分组 | 成品 | 材料 | 解锁 |
|---|---|---|---|
| 火 | 燃料包 | 布料 ×2 + 化学品 ×1 | Q01 火不能灭 |
| 光 | 空电池 | 旧电池 ×1 + 废金属 ×1 | Q02 让这里亮起来 |
| 修理 | 机械零件 | 废金属 ×3 | Q02 |
| 药 | 脏绷带 | 布料 ×2 | Q03 备一点药 |
| 药 | 急救喷雾 | 脏绷带 ×1 + 化学品 ×2 | Q03 |
| 弹药 | 9mm 手枪弹 ×6 | 废金属 ×2 + 化学品 ×1 | Q06 到桥那边去 |

配方、说明文案、分组和解锁任务都在 `Assets/Game/Data/Crafting/CraftingRecipes_Basic.asset`，Inspector 里直接改；`Map01CraftingSetup` 只在资源缺失时生成，重建场景不会覆盖。

## 文件

- 运行时：`Game/Runtime/Config/CraftingRecipeBook.cs`、`Game/Runtime/Core/Crafting.cs`、`Map01/Runtime/Loop/Map01CraftingPanel.cs`；`Map01Authority`、`Map01Interactor`、`Map01RunFlow`、`Map01Hud`、`Map01Interactable`、`Map01QuestDirector`（`IsCompleted`/`TitleOf`）、`Map01Layout` 有小改动。
- 编辑器：`Map01CraftingSetup.cs`（配方资源）、`Map01CraftingCapture.cs`（截图，需有界面的编辑器运行，见类注释）、`Map01GameplaySetup.cs` 接线。
- 测试：EditMode `CraftingTests`（5），PlayMode `Map01CraftingTests`（4）。全工程 EditMode 109/109、PlayMode 68/68。
- 截图：`Map01_Crafting_Ready.png`、`Map01_Crafting_Locked.png`。图标暂为物品颜色块 + 首字占位。
