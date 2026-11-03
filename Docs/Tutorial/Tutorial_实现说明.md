# 开局教学关《最后一段路》（实现说明）

> 日期：2026-11-03　分支：`claude/map01-whitebox`（未提交）　依据：Claude Doc《向火而生 · 开局教学关卡设计 V0.1》

## 玩法

新存档在主菜单点「开始游戏」后先进教学关。玩家沿一条蛇形废弃车道走 8 段，每段只教一个操作；做完一段，前面的引路灯被点亮（安全光 + 移开阻隔 + 新的复活点）。走到庇护所门口时身后落石，来路被彻底堵死；在门前站满 3 秒，黑屏字幕「来路被落石封死了……」后进入 Map01，Q01 照常开始。

| 段 | 名称 | 完成条件（事实） | 完成时 |
| --- | --- | --- | --- |
| 1 | 余烬 | `moved`（累计走 3 米）+ `sprinted` | 第 1 盏灯亮，第一道阻隔移开 |
| 2 | 灯与暗 | `reached_lamp_2`（穿过约 7.5 米黑暗走到第 2 盏灯） | 第 2 盏灯亮 |
| 3 | 翻货箱 | `crate_cloth` + `crate_chemicals`（两只箱子搜空）+ `backpack_opened` | 第 3 盏灯亮，阻隔移开 |
| 4 | 拼一份燃料 | `has_fuel`（背包合成页做出燃料包） | 阻隔移开（第 4 盏灯是坏的） |
| 5 | 长暗段 | `campfire_built` + `reached_far_lamp`（约 15 米无光，建火后走到对面） | 拐角大灯亮 |
| 6 | 卡住的失火者 | `dodged`（失火者 4 米内翻滚）+ `pinned_killed` | 拐角下排的灯亮，阻隔移开，出现「寻火者的遗物」 |
| 7 | 射击场 | `targets_cleared`（打碎 3 个悬挂油罐）+ `reloaded` | 射击场、半路、庇护所门三盏灯亮，阻隔移开 |
| 8 | 庇护所门前 | `exit_held`（门前连续停留 3 秒，受伤或离开中断） | 到门口时身后落石封路；站满后结尾字幕，进入 Map01 |

按键提示统一用 Map01 HUD 的写法（交互写 F，E 同样可用）。手枪在 1 号槽，短刀在 2 号槽。

## 教学里的规则偏差（只在教学关生效）

- 开局背包只有短刀，没有燃料和手枪。
- 黑暗伤害不会让生命低于 30（`darknessHealthFloor`）。
- 落石之后倒下只在门前醒来，不会被送回石头另一边；石头有碰撞并挖掉导航面，巡逻失火者也过不来。
- 被怪物打到生命低于 15（`knockdownHealth`）时倒下：在最近一盏已点亮的引路灯旁满血醒来，不进结算。最后一段倒下 2 次后撤走一只巡逻失火者。
- 卡住的失火者：不移动、抬爪 0.8 秒、伤害 10、生命 56。巡逻失火者：移速 3、伤害 12。
- 只解锁一条配方：燃料包（布料 ×2 + 化学品 ×1），开局可做。
- 兜底：合成段箱子搜空仍缺材料时补进背包；建火段燃料用光、火已灭、还没到对面时 3 秒后再给 1 份燃料。

设计稿里写的「教学火堆 45 秒」没有做：火堆时长写在火堆预制体自己的配置里，教学沿用正式的 90 秒，对这段路足够。

## 结构

| 部分 | 文件 | 作用 |
| --- | --- | --- |
| 纯规则 | `Tutorial/Runtime/TutorialStepModel.cs` | 段落顺序、事实去重、提前获得的事实轮到时直接认可、跳过 |
| 配置 | `Tutorial/Runtime/TutorialConfig.cs`，资产 `Tutorial/Data/TutorialConfig.asset` | 8 段的标题、完成事实、分阶段提示文案、字幕、规则偏差数值。只在缺失时生成，之后在 Inspector 改 |
| 导演 | `Tutorial/Runtime/TutorialDirector.cs` | 把现有事件翻译成事实；点灯；生命底线与倒下复活；门前停留；兜底；结束后改存档并切场景 |
| 场景组件 | `TutorialBeacon` / `TutorialZone` / `TutorialCrate` / `TutorialEnemy` / `TutorialTarget` / `TutorialRockfall` | 引路灯、地面区域、固定内容箱子、教学怪物标记、可打碎油罐、门前落石 |
| HUD | `Tutorial/Runtime/TutorialHud.cs` | 开场/结尾字幕、下方一句提示（12 秒没进展闪烁一次）、右上角段落进度、门前停留进度、暂停时「跳过教学 [K]」 |
| 场景生成 | `Tutorial/Editor/TutorialBuilder.cs`，菜单 `Wilderness/Tutorial/Build Tutorial` | 生成 `Tutorial/Scenes/Tutorial.unity` 并登记到 Build Settings |
| 截图 | `Tutorial/Editor/TutorialCapture.cs`，菜单 `Wilderness/Tutorial/Capture Screenshots` | 输出到 `Docs/Tutorial` |

教学场景复用 Map01 的权威层、背包与合成面板、HUD、暂停流程和玩家预制体，只给权威层一张空地图（只有一个永远不会激活的撤离候选点），不刷随机物资和怪物。

对现有文件的改动：

- `Game/Runtime/Session/RaidTransfer.cs`：`GameScenes.Tutorial`。
- `Game/Runtime/Menu/SaveSlots.cs`：`SetActiveScene`，教学完成或跳过后把当前存档改记为 Map01。
- `Game/Runtime/Menu/MainMenuController.cs`：新增 `newGameScenePath`（默认教学关），新存档先进教学；继续游戏仍读存档里记的场景。
- `Map01/Runtime/Loop/Map01Hud.cs`：`tutorialLayout` 开关，教学里左上角只显示背包和光照。
- `Game/Tests/PlayMode/MainMenuTests.cs`：开始游戏现在进教学关。

## 验证

| 测试集 | 结果 |
| --- | --- |
| EditMode 全工程 | 132/132（新增 `TutorialStepModelTests` 4 个） |
| PlayMode 全工程 | 107/107（落石前跑的全量）；加落石后重跑教学 + 主菜单 9/9（新增 `TutorialFlowTests` 4 个） |

`TutorialFlowTests` 覆盖：开局只有出生灯亮、只有短刀、阻隔关闭、关键点位在导航面上；8 段逐段推进（真实搜箱、背包合成、建火，翻滚/换弹用事实代替按键），每段点亮对应的灯、移开阻隔；走到门口身后落石、石头挡路；门前离开会清零、站满 3 秒结束；结束后存档改记为 Map01 且不走结算；黑暗生命底线；倒下后在最近的灯旁满血醒来；暂停里跳过直接进 Map01。

## 未做 / 需要手动确认

- 主菜单设置里「再玩一次教学」入口没做；目前想重玩只能新建存档。
- 自动测试没有覆盖真实按键操作和怪物真实攻击，需要进 Play 手动走一遍：`Wilderness/Tutorial/Open Tutorial` 后 Play，或从主菜单新建存档进入。
- 教学文案是初稿，在 `TutorialConfig.asset` 里改。

截图：`Docs/Tutorial/Tutorial_TopDown.png`（全关俯视，编辑模式下灯和落石都显示）、`Docs/Tutorial/Tutorial_Start.png`（出生点游戏视角）。
