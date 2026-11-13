# 导入说明

- 来源：见 SOURCE_URL.txt；许可：CC0（LICENSE.txt 为包内原文）；生成式 AI：页面标注无。
- 提取脚本：Docs/Map01/tools/extract_kaykit.ps1，原始压缩包不进仓库。
- 导入设置由 Assets/Map01/Editor/KayKitImport.cs 统一：只导网格，不导材质/动画/相机/灯光，1 单位 = 1 米，无碰撞。
- 原始文件保持只读；改色、比例、碰撞全部在 Assets/Map01 下处理。
- 只导入 fbx(unity) 中 Color2（深绿，正常林相）与 Color5（枯黄，污染边缘）两套的树、枯树、灌木、岩石、草。
- 材质不随模型导入；项目材质 M_KK_Forest 使用降饱和副本 Assets/Map01/Art/Textures/T_KK_Forest_Graded.png（饱和度 x0.42，明度 x0.86）。
- 树按目标高度 6.5–9m 适配；灌木、草、岩石统一 x1.8。

