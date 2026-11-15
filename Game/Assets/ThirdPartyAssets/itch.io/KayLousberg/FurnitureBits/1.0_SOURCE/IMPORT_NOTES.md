# 导入说明

- 来源：见 SOURCE_URL.txt；许可：CC0（LICENSE.txt 为包内原文）；生成式 AI：页面标注无。
- 提取脚本：Docs/Map01/tools/extract_kaykit.ps1，原始压缩包不进仓库。
- 导入设置由 Assets/Map01/Editor/KayKitImport.cs 统一：只导网格，不导材质/动画/相机/灯光，1 单位 = 1 米，无碰撞。
- 原始文件保持只读；改色、比例、碰撞全部在 Assets/Map01 下处理。
- 导入全部家具，排除电脑外设、游戏机、仙人掌、办公椅、台灯。
- 项目材质 M_KK_Furniture 使用 alt_A 色板（旧木褐、灰绿、奶油白）的降饱和副本 T_KK_Furniture_Graded.png（饱和度 x0.75）。
- 包级比例 x0.8。

