using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 唯一的打包入口（编辑器菜单与命令行共用同一套逻辑）。
///
/// 菜单：Build → 打包 Windows x64
/// 命令行：
///   Unity.exe -batchmode -quit -projectPath "D:\桌面游戏海洞" ^
///     -executeMethod GameBuilder.Build ^
///     -outputPath "D:\BuildOut\珊瑚牧歌.exe" -logFile "%TEMP%\珊瑚牧歌Build.log"
///
/// 不传 -outputPath 时输出到 DefaultOutputDir\珊瑚牧歌.exe。
/// 场景取 Build Settings 中启用的场景；为空时回退 Assets/场景.unity。
/// QuickBuild / BuildGame 只是兼容旧调用的转发壳，实际逻辑都在这里。
/// </summary>
public static class GameBuilder
{
    /// <summary>统一输出目录（唯一出口，不要再在别处写死构建路径）</summary>
    public const string DefaultOutputDir = @"D:\BuildOut";

    /// <summary>可执行文件名</summary>
    public const string ExeName = "珊瑚牧歌.exe";

    /// <summary>默认输出路径</summary>
    public static string DefaultOutputPath => Path.Combine(DefaultOutputDir, ExeName);

    /// <summary>编辑器菜单入口：构建到统一输出目录，不退出编辑器</summary>
    [MenuItem("Build/打包 Windows x64", false, 0)]
    public static void BuildInteractive()
    {
        BuildTo(DefaultOutputPath);
    }

    /// <summary>命令行入口：构建完成后退出 Unity 进程，退出码 0=成功、1=失败</summary>
    public static void Build()
    {
        string outputPath = GetArg("-outputPath");
        if (string.IsNullOrEmpty(outputPath)) outputPath = DefaultOutputPath;

        bool ok = BuildTo(outputPath);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// 执行一次 Windows x64 构建。菜单与命令行都走这里，保证行为一致。
    /// </summary>
    /// <returns>是否构建成功</returns>
    public static bool BuildTo(string outputPath)
    {
        try
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("outputPath 不能为空", nameof(outputPath));

            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            string[] scenePaths = ResolveScenes();

            Debug.Log("[打包] 输出: " + outputPath);
            Debug.Log("[打包] 场景: " + string.Join("; ", scenePaths));

            var options = new BuildPlayerOptions
            {
                scenes = scenePaths,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[打包] 结果: {summary.result}，总错误 {summary.totalErrors}，总警告 {summary.totalWarnings}，输出大小 {summary.totalSize / (1024 * 1024)} MB");

            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
            {
                Debug.LogError($"[打包] 失败: result={summary.result} errors={summary.totalErrors}，输出: {outputPath}");
                return false;
            }

            Debug.Log("[打包] 成功: " + outputPath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[打包] 异常: " + e);
            return false;
        }
    }

    /// <summary>
    /// 收集要构建的场景：优先 Build Settings 中启用且文件存在的场景，否则回退主场景。
    /// </summary>
    static string[] ResolveScenes()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        var enabled = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            .FindAll(s => s.enabled && !string.IsNullOrEmpty(s.path)
                          && File.Exists(Path.Combine(projectRoot, s.path)));

        if (enabled.Count > 0)
            return enabled.ConvertAll(s => s.path).ToArray();

        string fallback = Path.Combine(projectRoot, "Assets", "场景.unity");
        if (!File.Exists(fallback))
            throw new BuildFailedException("找不到任何可构建场景（Build Settings 为空且 Assets/场景.unity 不存在）");

        return new[] { "Assets/场景.unity" };
    }

    /// <summary>读取命令行参数值（形如 -name value）</summary>
    static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }
}
