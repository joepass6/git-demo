using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 命令行打包脚本：
///   Unity.exe -batchmode -quit -projectPath <项目> -executeMethod GameBuilder.Build -buildTarget Win64 ^
///             -outputPath "<输出目录>\珊瑚牧歌.exe" -logFile "<日志路径>"
/// 不传 -outputPath 时默认输出到 项目根/Builds/珊瑚牧歌_<日期>/珊瑚牧歌.exe
/// </summary>
public static class GameBuilder
{
    public static void Build()
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            string outputPath = GetArg("-outputPath");
            if (string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.Combine(projectRoot, "Builds", "珊瑚牧歌_" + DateTime.Now.ToString("MMdd_HHmm"));
                outputPath = Path.Combine(dir, "珊瑚牧歌.exe");
            }

            string outDir = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // 场景：优先用 EditorBuildSettings 中启用的场景；没有则回退 Assets/场景.unity
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var enabled = scenes.FindAll(s => s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(Path.Combine(projectRoot, s.path)));
            string[] scenePaths;
            if (enabled.Count > 0)
            {
                scenePaths = System.Array.ConvertAll(enabled.ToArray(), s => s.path);
            }
            else
            {
                string fallback = Path.Combine(projectRoot, "Assets", "场景.unity");
                if (!File.Exists(fallback)) throw new BuildFailedException("找不到任何可构建场景（EditorBuildSettings 为空且 Assets/场景.unity 不存在）");
                scenePaths = new[] { "Assets/场景.unity" };
            }

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
                throw new BuildFailedException("构建失败: result=" + summary.result + " errors=" + summary.totalErrors);
            }

            Debug.Log("[打包] 成功: " + outputPath);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[打包] 异常: " + e);
            EditorApplication.Exit(1);
        }
    }

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
