# Unity 项目上下文

## 技术基线

- Unity：6000.6.3f1
- 渲染管线：Universal Render Pipeline 17.6.0
- 输入：Input System 1.20.0
- 导航：AI Navigation 2.0.14
- 测试：Unity Test Framework 1.8.0
- MCP：MCP for Unity 10.3.0
- Unity 工程目录：`Game/`
- 默认场景：`Game/Assets/Scenes/SampleScene.unity`

## 产品方向

游戏是一款 2.5D 俯视角 PvE 搜打撤生存游戏。场景、角色、掩体、碰撞和光照使用 3D；UI、图标及部分特效可以使用 2D 素材。

核心循环：

`营地准备 -> 进入区域 -> 搜刮/战斗/生存 -> 判断风险 -> 撤离 -> 制作与建设 -> 挑战更危险区域`

核心设计支柱：

- 搜刮物资同时服务战斗、生存和营地建设。
- 饥饿、精神、昼夜和天气改变玩家的路线与撤离判断。
- 死亡损失局内物资，但营地、知识和关键解锁长期保留。
- 风格可以轻松荒诞，决策压力必须真实。

## 当前工程状态

- URP 空项目已创建并完成首次导入。
- `SampleScene` 包含 Main Camera、Directional Light 和 Global Volume。
- Unity MCP HTTP 服务地址：`http://127.0.0.1:8080/mcp`。
- Codex 与 Claude Code 均已配置 `UnityMCP`。
- `Game/Assets/Editor/UnityMcpBootstrap.cs` 会启用本地 HTTP 模式并在打开编辑器时自动连接。
- MCP 只读探针已成功读取场景层级，Unity Console 无错误。

## 第一阶段边界

先验证单人核心循环，不实现多人联机、大型随机世界、完整剧情、季节系统或复杂营地 NPC 模拟。

首个可玩切片应包含：移动、鼠标瞄准、翻滚、基础射击、搜索容器、背包、一个敌人、固定撤离点和局外仓库。

