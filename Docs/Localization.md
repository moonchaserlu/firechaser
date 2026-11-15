# 中英文切换（2026-11-15）

## 怎么用

- 主菜单 → 设置 → 画面 → 「语言 / Language」，左右切换，**立即生效**并写入设置文件（`firechaser_settings.json` 的 `language`：-1 跟随系统，0 中文，1 英文）。
- 没有设置文件时跟随系统语言：中文系统显示中文，其他系统显示英文（itch.io 上的海外玩家默认英文）。
- Esc 菜单的设置窗口也能切换（由 UI 线程接入）。

## 原理

- `Game/Runtime/Core/Loc.cs`：**中文原文就是键**。代码和资产照旧写中文，显示前调用：
  - `Loc.T("背包")` → 英文时返回 `Backpack`；
  - `Loc.F("生命 {0}/{1}", hp, max)` → 先翻译格式串再填参数（不要再用 `$"中文{x}"` 拼显示文字）。
  - 查不到英文时原样显示中文，编辑器里打一条 `[Loc] 缺少英文` 警告。
- 英文对照表：`Game/Assets/Game/Resources/Localization_en.txt`，每条两行 `zh=` / `en=`，`\n` 表示换行。
- 资产里的文案（物品名、配方说明、任务与剧情记录、教学提示、箱子/设施名）在显示处过 `Loc.T`；`ItemDefinition.DisplayName`、`WeaponConfig.DisplayName`、`LootContainer.DisplayName`、`ShelterStation.DisplayName`、`ShopEntry.DisplayName` 已经是翻译后的名字，原始中文仍在 `displayName` 字段。
- 语言切换时触发 `Loc.Changed`；IMGUI 界面每帧重画，自动换语言。已经显示出来的一次性提示（例如顶部播报）保留生成时的语言。

## 改文案 / 加新文案

1. 改中文：同时改对照表里对应的 `zh=` 行（键必须完全一致，包括全角标点和空格）。
2. 新增显示文字：用 `Loc.T` / `Loc.F` 包起来，再在对照表里加一条。
3. 跑 EditMode 测试 `LocalizationCoverageTests`：缺英文的会列出来；设置环境变量 `LOC_DUMP_MISSING=<文件>` 会把缺的条目按对照表格式写出来，填好英文贴进对照表即可。

## 未覆盖

- 旧原型场景 VerticalSlice 用的 `SliceHud` / `RunHud` / `HudText` 和 AI 调试标签不在发布版里，没有翻译。
- 字体用的是 IMGUI 默认字体，英文显示正常；以后换正式字体时注意同时包含中英文字形。
