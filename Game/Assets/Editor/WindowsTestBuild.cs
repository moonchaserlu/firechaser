using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 一键打 Windows 测试包。菜单：Tools/Build/Windows 测试包；
/// 命令行：Unity.exe -batchmode -quit -projectPath Game -executeMethod WindowsTestBuild.BuildFromCommandLine
/// 输出到 Game/Builds/Windows/向火而生.exe（Builds/ 已被 gitignore 忽略）。
/// </summary>
public static class WindowsTestBuild
{
    private const string OutputDirectory = "Builds/Windows";
    private const string ExecutableName = "向火而生.exe";

    [MenuItem("Tools/Build/Windows 测试包")]
    public static void BuildFromMenu()
    {
        var report = Build(BuildOptions.Development);
        if (report.summary.result == BuildResult.Succeeded)
        {
            EditorUtility.RevealInFinder(Path.Combine(OutputDirectory, ExecutableName));
        }
    }

    public static void BuildFromCommandLine()
    {
        var report = Build(BuildOptions.Development);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static BuildReport Build(BuildOptions options)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("Build Settings 中没有启用的场景。");
        }

        Directory.CreateDirectory(OutputDirectory);
        var buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(OutputDirectory, ExecutableName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = options,
        };

        Debug.Log($"[WindowsTestBuild] 场景: {string.Join(", ", scenes)}");
        var report = BuildPipeline.BuildPlayer(buildOptions);
        var summary = report.summary;
        Debug.Log($"[WindowsTestBuild] 结果: {summary.result}, 错误 {summary.totalErrors}, 警告 {summary.totalWarnings}, " +
                  $"大小 {summary.totalSize / (1024 * 1024)} MB, 用时 {summary.totalTime}, 输出 {summary.outputPath}");
        return report;
    }
}
