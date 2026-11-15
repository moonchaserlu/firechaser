# 开局教学关《最后一段路》（实现说明）

> 首版 2026-11-03；2026-11-12 按试玩反馈修改（长暗段中间灯、结尾炸墙进安全屋）。依据：Claude Doc《向火而生 · 开局教学关卡设计 V0.1》

## 玩法

新存档在主菜单点「开始游戏」后先进教学关。玩家沿一条蛇形废弃车道走 9 段，每段只教一个操作；做完一段，前面的引路灯被点亮（安全光 + 移开阻隔 + 新的复活点）。

结尾：走进尽头那间屋子，身后落石把来路彻底堵死；在屋角的炸药箱里搜到炸药，到西边有裂缝的外墙旁按 F 安放，退开等引信烧完把墙炸开；墙后有一段往下的台阶；走到墙洞口时由过场接管（上下黑边，镜头转向台阶，角色自己走下台阶、没入黑暗，黑幕淡入），然后黑屏字幕「墙后是一座还算完好的地下安全屋……」后进入地下安全屋（`Shelter`）。存档从此记为安全屋：玩家在安全屋里整理仓库、合成、练枪，自己决定什么时候从楼梯出发去 Map01。

| 段 | 名称 | 完成条件（事实） | 完成时 |
| --- | --- | --- | --- |
| 1 | 余烬 | `moved`（累计走 3 米）+ `sprinted` | 第 1 盏灯亮，第一道阻隔移开 |
| 2 | 灯与暗 | `reached_lamp_2`（穿过约 7.5 米黑暗走到第 2 盏灯） | 第 2 盏灯亮 |
| 3 | 翻货箱 | `crate_cloth` + `crate_chemicals`（两只箱子搜空）+ `backpack_opened` | 第 3 盏灯亮，阻隔移开 |
| 4 | 拼一份燃料 | `has_fuel`（背包合成页做出燃料包） | 阻隔移开 |
| 5 | 长暗段 | `campfire_built` + `reached_far_lamp` | 火堆点起来的同时引燃中间灯和对岸灯，整段路连续有光 |
| 6 | 卡住的失火者 | `dodged`（失火者 4 米内翻滚）+ `pinned_killed` | 拐角下排的灯亮，阻隔移开，出现「寻火者的遗物」 |
| 7 | 射击场 | `targets_cleared`（打碎 3 个悬挂油罐）+ `reloaded` | 射击场、半路、尽头屋子三盏灯亮，阻隔移开 |
| 8 | 最后的屋子 | `at_exit`（走进尽头的屋子） | 身后落石封路 |
| 9 | 炸开外墙 | `wall_blown` + `entered_shelter`（走到墙洞口 `stairs_reached` 触发下台阶过场，播完记下） | 结尾字幕，进入地下安全屋 |

按键提示统一用 Map01 HUD 的写法（交互写 F，E 同样可用）。手枪在 1 号槽，短刀在 2 号槽。

## 教学里的规则偏差（只在教学关生效）

- 开局背包只有短刀，没有燃料和手枪；教学里拿到的东西不带进安全屋（安全屋有自己的初始仓库）。
- 黑暗伤害不会让生命低于 30（`darknessHealthFloor`）。
- 被怪物或爆炸打到生命低于 15（`knockdownHealth`）时倒下：在最近一盏被点亮的灯旁满血醒来，不进结算。最后一段倒下 2 次后撤走一只巡逻失火者。落石之后只在屋里醒来，不会被送回石头另一边；石头有碰撞并挖掉导航面，巡逻失火者也过不来。
- 卡住的失火者：不移动、抬爪 0.8 秒、伤害 10、生命 56。巡逻失火者：移速 3、伤害 12。
- 炸药：引信 3 秒，3.5 米内受 25 点伤害并发出噪声。
- 只解锁一条配方：燃料包（布料 ×2 + 化学品 ×1），开局可做。
- 兜底：合成段箱子搜空仍缺材料时补进背包；建火段燃料用光、火已灭、还没到对面时 3 秒后再给 1 份燃料；炸药丢了而墙还没炸开时再给 1 份。

设计稿里写的「教学火堆 45 秒」没有做：火堆时长写在火堆预制体自己的配置里，教学沿用正式的 90 秒。

## 2026-11-15 加长到约 6 分钟 + 事件选择

从 9 段扩到 12 段（`TutorialConfig.layoutVersion = 5`；重建教学关时发现资产是旧版本会重写段落、字幕和节奏，其余数值保留）：

| 段 | 名称 | 完成条件 | 内容 |
| --- | --- | --- | --- |
| 7 | 止血（新） | `bleeding_stopped` | 卡住的失火者倒下前一爪让玩家流血；遗物包里有 3 卷脏绷带（自动进快捷栏 3），守火段补给箱里再放 1 卷（背包只有 2×4，放多了会挤满）；包扎前伤口一直渗血，用绷带/急救喷雾止住才算 |
| 8 | 射击场 | `targets_cleared` | 原第 7 段 |
| 9 | 借光潜行（新） | `sneaked_past` | 射击场往西一小圈灯（`Lamp_8_Sneak`）正落在巡逻失火者 A 的横穿路线上；等它转身从背后溜过去，走到 `Zone_SneakEnd` |
| 10 | 守住火堆（新） | `choice_made` + `campfire_built_2` + `patrol_b_killed` | 灯下有受伤的寻火者（事件选择「昏暗 / 长明」）和补给箱（材料再拼一份燃料包）；往西是一段黑路，`Gate_10_Hold` 封住去屋子的路；生第二堆火时发出噪声（`fireLureRadius` 30 米）把失火者 B 引到火边，打退它（倒下 2 次后被撤走也算） |
| 11 | 最后的屋子 | `at_exit` | 原第 8 段 |
| 12 | 炸开外墙 | 不变 | 原第 9 段 |

**事件选择「昏暗 / 长明」**（`TutorialChoicePoint`，文案在 `TutorialConfig.choice`）：守火段灯下靠着一个受伤的寻火者，走过去按 F，他把火种交给你，面板二选一（点击或按 1 / 2，选择时不能操作）。

- **视野**＝跟随镜头的高度（`FixedObliqueCamera.HeightScale`，沿同一角度拉近/拉远）。新加的 `PlayerVision`（由 `PlayerCharacter` 自动挂上）让镜头高度**默认只有原来的 75%**（看到的地图更少），教学、Map01、安全屋都一样。
- 昏暗：视野保持 75%；存档记 `vision_dim`。以后的事件读这个标记多发物资（`SaveSlots.HasStoryFlag(PlayerVision.DimFlag)`），**目前还没有后续事件，只留了接口**。结尾字幕追加「你学会了在昏暗里看路。」
- 长明：镜头高度立刻恢复到 100% 并一直保持（存档记 `vision_everbright`，之后每个场景的 `PlayerVision` 读到它就用 100%）。代价是怪物更容易发现你：失火者发现和追踪你的距离 ×1.3（警戒 9 → 11.7 米，追击 12 → 15.6 米，`PlayerVision.EverbrightDetectionScale`）。结尾字幕追加「你身边的火，比别人亮一些。」

剧情标记写在存档摘要的 `storyFlags`（`SaveSlots.AddStoryFlag / HasStoryFlag`）。

**节奏**：开场字幕 4→7 秒、结尾字幕 3→6 秒、下台阶过场走得更慢（2.2→1.5 米/秒，镜头转 2 秒、黑幕淡入 1.6 秒）。

**时长估算**（按布局和各环节耗时推算，未实测）：熟练玩家约 5.5–6 分钟，第一次玩约 7–8 分钟。新增部分：止血约 40 秒、潜行约 45 秒、事件选择 + 守火约 80 秒、节奏约 15 秒。

截图：`Docs/Tutorial/Extended_20261115/`（`zh_*` / `en_*`）。

## 2026-11-15 精修

试玩截图里发现教学提示条和底部快捷栏重叠、几乎看不清，这次一起修了观感：

- 提示条移到快捷栏上方，深色底板 + 左侧火光色竖条；提示里的按键（F、Tab、Shift、Q、R、1、空格、左键……）自动标成火光色的 `[键]`；12 秒没进展时描边闪烁，引信倒计时用红色描边并持续跳动。
- 开场 1.6 秒从黑里亮起来，开场/结尾字幕加半透明底衬。
- 右上角进度改成「教学 3/9 + 段名 + 9 格进度条」，每段完成时屏幕上方弹出「✓ 段名」约 2.4 秒。
- 引路灯点亮时火光由暗到亮、抖两下再稳住（`TutorialBeacon.Ignite`，约 0.9 秒；开局就亮的灯不播）。玩法上的“已点亮”仍是立即生效。
- 炸墙时镜头震动 0.5 秒（`TutorialBreachWall.Shake`，借用相机的 `Kick`）。
- 长暗段第一句提示还写着已删掉的“坏灯”，改为「前面这段路又长又黑，一口气跑不过去……」（`TutorialConfig.asset` 与 `TutorialBuilder` 默认值同步）。
- 所有教学文案支持中英文切换，见 `Docs/Localization.md`。

对比截图：`Docs/Tutorial/Polish_20261115/`（`before_*` 修改前，`after_zh_*` / `after_en_*` 修改后中英文），由 `TutorialPolishShots`（PlayMode，Explicit，需有窗口的编辑器）生成。

## 2026-11-12 修复：长暗段一直掉血

原来长暗段中间立的是一盏故意做成“点不亮”的坏灯，建完火堆后火光与对岸灯之间还剩约 8 米黑路，玩家一路受黑暗伤害。现在去掉坏灯，改为一盏真正的引路灯（`Lamp_4_Middle`），火堆点起来时与对岸灯（`Lamp_5_FarSide`）一起被引燃。测试逐 0.5 米检查从第 3 盏灯到对岸全程都在有效光里。

## 结构

| 部分 | 文件 | 作用 |
| --- | --- | --- |
| 纯规则 | `Tutorial/Runtime/TutorialStepModel.cs` | 段落顺序、事实去重、提前获得的事实轮到时直接认可、跳过 |
| 配置 | `Tutorial/Runtime/TutorialConfig.cs`，资产 `Tutorial/Data/TutorialConfig.asset` | 各段标题、完成事实、分阶段提示文案、字幕、规则偏差数值、结束后去的场景。只在缺失时生成，之后在 Inspector 改 |
| 导演 | `Tutorial/Runtime/TutorialDirector.cs` | 把现有事件翻译成事实；点灯（按段完成或按事实）；生命底线与倒下复活；炸墙交互；兜底；结束后改存档并切场景 |
| 场景组件 | `TutorialBeacon` / `TutorialZone` / `TutorialCrate` / `TutorialEnemy` / `TutorialTarget` / `TutorialRockfall` / `TutorialBreachWall` | 引路灯、地面区域、固定内容箱子、教学怪物标记、可打碎油罐、落石、可炸开的外墙 |
| HUD | `Tutorial/Runtime/TutorialHud.cs` | 开场/结尾字幕、下方一句提示（12 秒没进展闪烁一次）、右上角段落进度、引信倒计时、暂停时「跳过教学 [K]」 |
| 场景生成 | `Tutorial/Editor/TutorialBuilder.cs`，菜单 `Wilderness/Tutorial/Build Tutorial` | 生成 `Tutorial/Scenes/Tutorial.unity`，把教学关和安全屋登记到 Build Settings |
| 截图 | `Tutorial/Editor/TutorialCapture.cs`，菜单 `Wilderness/Tutorial/Capture Screenshots` | 输出到 `Docs/Tutorial` |

教学场景复用 Map01 的权威层、背包与合成面板、HUD、暂停流程和玩家预制体，只给权威层一张空地图（只有一个永远不会激活的撤离候选点），不刷随机物资和怪物。

对现有文件的改动：

- `Game/Runtime/Session/RaidTransfer.cs`：`GameScenes.Tutorial`。
- `Game/Runtime/Menu/SaveSlots.cs`：`SetActiveScene`，教学完成或跳过后把当前存档改记为安全屋。
- `Game/Runtime/Menu/MainMenuController.cs`：新增 `newGameScenePath`（默认教学关），新存档先进教学；继续游戏读存档里记的场景。
- `Map01/Runtime/Loop/Map01Hud.cs`：`tutorialLayout` 开关，教学里左上角只显示背包和光照。
- `Game/Tests/PlayMode/MainMenuTests.cs`：开始游戏现在进教学关。

## 验证

`TutorialFlowTests`（PlayMode）覆盖：开局只有出生灯亮、只有短刀、阻隔关闭、关键点位在导航面上；9 段逐段推进（真实搜箱、背包合成、建火、炸墙，翻滚/换弹用事实代替按键）；建火后长暗段全程有光；进屋身后落石；没炸药不能安放、安放扣炸药、退开不受伤、墙体消失、台阶灯亮；走下台阶后存档改记为安全屋且不走结算；站在墙边被炸倒后在屋里醒来；黑暗生命底线；倒下后在最近的灯旁醒来；暂停里跳过直接进安全屋。

## 未做 / 需要手动确认

- 主菜单设置里「再玩一次教学」入口没做；目前想重玩只能新建存档。
- 自动测试没有覆盖真实按键操作和怪物真实攻击，需要进 Play 手动走一遍：`Wilderness/Tutorial/Open Tutorial` 后 Play，或从主菜单新建存档进入。
- 炸墙只有闪光和碎块，没有音效和镜头震动。
- 下台阶过场是代码驱动的简易版（角色按路径移动、镜头插值、黑边与淡入），台阶只是地面上的暗板；有正式台阶模型和动画后可替换。过场镜头位置在 `TutorialDirector` 的「下台阶过场」字段里调。
- 教学文案是初稿，在 `TutorialConfig.asset` 里改。

截图：`Docs/Tutorial/Tutorial_StairsCutscene.png`（下台阶过场镜头）、`Docs/Tutorial/Tutorial_TopDown.png`（全关俯视，编辑模式下所有灯、落石和外墙都显示）、`Docs/Tutorial/Tutorial_Start.png`（出生点游戏视角）。
