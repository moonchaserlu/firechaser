# Unity 项目开发约定

## 项目定位

这是一个使用 Unity 6 + URP 制作的 2.5D 俯视角 PvE 搜打撤生存游戏。玩法方向参考“轻松卡通表现下的高风险撤离”，并加入饥饿、精神、昼夜、营地建设和长期生存压力。

Unity 工程位于 `Game/`，共享策划和技术上下文位于 `Docs/`。

## 分工

- 用户是主创和最终决策者。
- Codex 负责策划、系统规则、数值框架、文案、开发任务和验收标准。
- Claude Code 负责 Unity 工程实现、测试、修复和技术文档。

实现前先读取 `Docs/AI/UnityProjectContext.md` 及相关策划文档。策划未确认时，不擅自扩大玩法范围。

## Unity 工作规则

- Unity 版本固定为 `6000.6.3f1`，渲染管线为 URP 17.6。
- 通过 `UnityMCP` 读取场景、Console 和测试状态；完成脚本改动后必须检查编译错误。
- 角色在 XZ 平面移动，3D 场景配合斜俯视相机呈现 2.5D 视觉。
- 运行时代码不得引用 `UnityEditor`；编辑器代码必须放在 `Editor/` 目录或 Editor 程序集。
- 优先使用 Input System、ScriptableObject 配置和组合式组件。
- 不手工编辑 `.unity`、`.prefab` 或 `.asset` YAML，除非已经验证对象 ID 和序列化结构。
- 不修改或提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`。
- 不同时让两个代理修改同一个文件；每项任务应有明确的文件所有者。

## 完成标准

每个任务至少需要：

1. 功能实现与必要配置。
2. Unity 编译无新增错误。
3. 适当的 EditMode 或 PlayMode 验证。
4. 说明修改文件、验证结果和仍需手动设置的内容。

