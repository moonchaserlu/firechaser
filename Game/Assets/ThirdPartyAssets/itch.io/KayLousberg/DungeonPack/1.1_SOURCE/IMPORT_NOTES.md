# 导入说明

- 来源：见 SOURCE_URL.txt；许可：CC0（LICENSE.txt 为包内原文）；生成式 AI：页面标注无。
- 提取脚本：Docs/Map01/tools/extract_kaykit.ps1，原始压缩包不进仓库。
- 导入设置由 Assets/Map01/Editor/KayKitImport.cs 统一：只导网格，不导材质/动画/相机/灯光，1 单位 = 1 米，无碰撞。
- 原始文件保持只读；改色、比例、碰撞全部在 Assets/Map01 下处理。
- 只导入中性结构与道具（箱桶、货架、脚手架、路障、地板、桌椅床、碎石等）。
- 排除 banner、candle、torch、金币与金色物件、武器、尖刺、带装饰/桌布变体，它们不得进入 Map01。
- 项目材质 M_KK_Dungeon 使用 T_KK_Dungeon_Graded.png（饱和度 x0.7）。
- 包级比例 x0.8（原始约为真实尺寸 1.5 倍）。

