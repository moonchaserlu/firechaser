# Map01 实现审计（阶段 0）

> 日期：2026-10-23  
> 依据：《地图01_3D素材与Claude实现指南.md》第 0、5、9、10 节  
> 本阶段只做检查，没有修改任何工程文件。

## 1. 结论摘要

- 指南所在目录 `C:\Users\moonchaser\Documents\作品集` 里确实没有引擎工程，只有指南、地图 PNG 和策划案 PDF。
- 本机另有一个 Unity 工程 `E:\作品集\Game`，就是之前线程搭建的《向火而生》。另一个 `E:\git\we-ll-be-right-back` 与本项目无关。
- 《向火而生》与 Map01 策划案方向不同：前者是**单人**、永久黑暗、光路探索、2.5D 无跳跃；Map01 策划案是**多人语音合作**、跳跃、双人搬运、发电机/电池、服务器权威。
- 工程里**没有任何联网框架**，目标平台也未在任何文档中写明。按指南第 0 节第 8 条，确认引擎工程、联网方案和目标平台之前，不能进入阶段 1 引入框架或改工程。
- 三个基础素材包都已在本地，校验值与指南记录一致，均为 CC0。Furniture Bits 实际已是 1.0 SOURCE 完整版，不需要再下载免费版。

## 2. 引擎与工程

| 项 | 检测结果 | 证据 |
|---|---|---|
| 引擎 | Unity 6000.6.3f1 | `E:\作品集\Game\ProjectSettings\ProjectVersion.txt` |
| 渲染管线 | URP 17.6.0 | `Game/Packages/manifest.json` |
| 输入 | Input System 1.20.0 | 同上 |
| 导航 | AI Navigation 2.0.14（已装，可烘焙 NavMesh） | 同上 |
| ProBuilder | **未安装** | 同上 |
| 联网框架 | **无**（无 NGO、Mirror、Fish-Net、Photon） | 同上 |
| 测试 | Unity Test Framework 1.8.0；上次记录 EditMode 81/81、PlayMode 29/29 | 前一线程报告 |
| MCP | MCP for Unity（`com.coplaydev.unity-mcp`） | 同上 |
| 目标平台 | **未定义** | `Docs/`、策划案均未写 |
| 现有场景 | `Assets/Game/Scenes/VerticalSlice.unity`（Build 第一位）、`Assets/Scenes/SampleScene.unity` | 文件列表 |
| 运行时代码 | `Assets/Game/Runtime/`，命名空间 `WildernessExtraction`，单人逻辑 | `Docs/AI/UnityProjectContext.md` |

工程约定（`E:\作品集\CLAUDE.md`）要点：不手改 `.unity/.prefab/.asset` YAML，通过 UnityMCP 或 Editor Builder 生成；运行时代码不引用 `UnityEditor`；不提交 `Library/Temp/Logs/UserSettings`。

## 3. 工作区状态（不覆盖用户文件）

`E:\作品集` 是 git 仓库（远端 `github.com/moonchaserlu/firechaser`），当前分支 `claude/project-thread-myeypg`，最新提交 `8159d72 Hostile AI bots`，未推送。

存在**未提交的用户改动**，后续任何工作都不得覆盖：

```text
 M CLAUDE.md
 M Docs/AI/UnityProjectContext.md
 M Docs/Design/GameDesignBrief.md
 M Docs/Design/VerticalSlicePlan.md
 M Game/Game.slnx
 M Game/Packages/manifest.json
 M Game/Packages/packages-lock.json
 M Game/ProjectSettings/EditorBuildSettings.asset
 M Game/ProjectSettings/ProjectSettings.asset
?? Docs/Design/向火而生_首个可玩版本计划_V0.1.md
?? Game/ProjectSettings/Packages/
?? 作品集策划文档总结.md
?? 敌人与AIBot索敌逻辑策划案.md
?? 美术资源包/
```

注意：`美术资源包/` 约 151 MB 原始压缩包，目前未被 `.gitignore` 排除。指南第 5 节要求原始素材不进公开仓库；若 firechaser 是公开仓库，需要先把它加入忽略。

## 4. 素材清单

位置：`E:\作品集\美术资源包\`（`C:\Users\moonchaser\Downloads\` 中有同样文件）。均**未导入**工程。

| 包 | 版本 | 大小（字节） | SHA-256 | 许可 | 格式 | 方案状态 |
|---|---|---:|---|---|---|---|
| KayKit Forest Nature Pack | 1.0 SOURCE | 98,805,295 | `618525339D974E04CB7A36F0C5D434FFDA32493422F82CDC3F21C451D53F2EFF` | CC0（包内 License.txt） | FBX 3176（含 `fbx(unity)`、8 套 Color）、GLTF、OBJ、Blend | P0 保留，与指南一致 |
| KayKit Dungeon Pack | 1.1 SOURCE | 41,575,408 | `BB24DE53BB71274B3C064132523EE959D06F1FAE45BF1B37BFDEBE1F35D82086` | CC0 | FBX 566（含 `fbx(unity)`）、GLTF、OBJ、Blend | P0 保留，与指南一致 |
| KayKit Furniture Bits | **1.0 SOURCE** | 8,707,417 | `23E065BD28D8A2258E889AA8D94857707272A56C76311852705B10CF5BF9B45D` | CC0 | FBX 148（含 `fbx (unity)`）、GLTF、OBJ、Blend | P0 保留；比指南预期的免费版更完整 |
| Low Poly Camping and Survival Props Pack | 未标注 | 780,204 | `2AACDA3CC6AEEEBCCAC9E4FCEED4C008FB76372D1AC2FC1BC43E8DCAA3724A97` | README 中许可待读全文 | FBX 31 | **不在指南方案中**，未通过并排测试前不导入 |
| Low-poly, Cozy, 3D Survival Asset Pack（Medical Supplies） | 未标注 | 1,580,625 | `6422EF095FB53D9228EAC76B230AB46091DE458914479737B68F5E37DF7CE62F` | **包内无许可文件** | FBX 1 | **不在指南方案中**，许可不明，不导入 |

导入时 Unity 只选用各包的 `fbx(unity)` 目录，不导入 GLTF/OBJ/Blend，避免重复资产。

未使用、也未发现的包：Space Base Bits、Charming Kitchen（按你的要求不使用）。

阶段 2 及以后需要但本地没有的素材，见 `ASSET_MISSING_REPORT.md`。阶段 1 白盒**不依赖任何缺失素材**。

## 5. 风险

1. **方向冲突**：在《向火而生》工程里直接做 Map01，会把单人永久黑暗玩法和多人合作玩法混在同一套代码里。现有玩家控制器无跳跃、为单人设计，Map01 需要的多人和服务器权威改造会触及“不重做已有系统”的约定。
2. **联网框架缺失**：阶段 4、5 必须有联网方案。NGO 需要从项目第一天就按服务器权威组织玩家、拾取、门和发电机，越晚加越贵。
3. **未提交改动**：工作区已有用户改动，切分支或重新生成场景前必须先确认处理方式。
4. **原始素材进仓库**：`美术资源包/` 未忽略。
5. **白盒工具**：ProBuilder 未安装。可以用 Unity 基础几何体加 Editor Builder 脚本白盒，不必新增插件。

## 6. 建议执行分支（需你确认）

| 选项 | 内容 | 优点 | 代价 |
|---|---|---|---|
| A（推荐） | 在现有 `E:\作品集\Game` 中新建独立目录 `Assets/Map01/`、独立程序集、独立场景 `Map01.unity`，新开 git 分支；不改 `WildernessExtraction` 现有代码 | 立即可开工；复用 URP、Input System、AI Navigation、MCP 与测试设置；随时可整体拆出 | 两个方向共用一个工程，阶段 4 引入联网框架时会影响整个工程 |
| B | 新建一个独立 Unity 6000.6.3f1 URP 工程专门做 Map01 多人游戏 | 方向干净，联网从第一天开始设计 | 需要你确认创建；重新配置包和测试 |

联网方案建议：Unity **Netcode for GameObjects**（指南 9.2 已按它写，官方维护，Host/专用服务器都支持）。  
目标平台建议：**Windows PC**。

## 7. 确认后将修改的文件（选项 A）

```text
E:\作品集\.gitignore                                # 增加 美术资源包/ 忽略（需你同意）
E:\作品集\Game\Assets\Map01\Runtime\              # 点位、屋顶显隐、建筑分层组件，独立 asmdef
E:\作品集\Game\Assets\Map01\Editor\Map01Builder.cs # 菜单生成白盒场景、碰撞与 NavMesh，可重复运行
E:\作品集\Game\Assets\Map01\Data\                  # MapDefinition / SpawnPointDefinition ScriptableObject
E:\作品集\Game\Assets\Map01\Scenes\Map01.unity     # 由 Builder 生成，不手改 YAML
E:\作品集\Game\Assets\Map01\Tests\                 # 屋顶显隐计数、GUID 稳定性、桥梁可达等测试
```

不会修改：`Assets/Game/**`、`VerticalSlice.unity`、你未提交的文档与设置。

## 8. 阶段 1 计划预览

- 480m × 320m，原点居中，图片顶部为 +Z；蓝图 PNG 只作为编辑器参考平面，构建时禁用。
- 地形、河流（不可走）、桥（唯一过河，NavMesh 连续表面）、道路、山体、仓库、房区、厂房、树林、主安全屋、两个撤离候选点的体块，坐标取指南第 2 节表格。
- 一栋厂房、一栋住宅：`ExteriorLower / InteriorVisuals / RoofRevealGroup / Physics / InteriorVolumes` 分层，进入计数控制本地屋顶显隐，只切 Renderer，不动碰撞与导航。
- 稳定 GUID 点位组件；烘焙 NavMesh；PlayMode 测试从安全屋到每个 POI 的路径可达。
