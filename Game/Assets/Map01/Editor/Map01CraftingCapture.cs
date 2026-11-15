using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 打一个只含 Map01 的截图包（定义 MAP01_PANEL_SHOTS，见 Map01PanelShots）。IMGUI 在批处理编辑器里不会画，
    /// 所以打成独立播放器来拍；可以用批处理打包，不需要有界面的编辑器：
    /// <c>Unity.exe -batchmode -projectPath Game -executeMethod WildernessExtraction.Map01.EditorTools.Map01CraftingCapture.BuildShotsPlayer -shotsPlayer &lt;exe 路径&gt; -quit</c>
    /// 然后运行 <c>&lt;exe&gt; -craftShots &lt;输出目录&gt; -screen-width 1600 -screen-height 900 -screen-fullscreen 0</c>，拍完自动退出。
    /// </summary>
    public static class Map01CraftingCapture
    {
        public static void BuildShotsPlayer()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-shotsPlayer");
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("缺少 -shotsPlayer <exe 路径>");
            Directory.CreateDirectory(Path.GetDirectoryName(args[at + 1]));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Map01Builder.ScenePath },
                locationPathName = args[at + 1],
                target = BuildTarget.StandaloneWindows64,
                extraScriptingDefines = new[] { "MAP01_PANEL_SHOTS" }
            });
            Debug.Log($"[CraftingCapture] build {report.summary.result}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
