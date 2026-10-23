using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// <summary>命令行打包入口。
/// Unity.exe -batchmode -quit -projectPath ... -executeMethod BuildGame.BuildStandalone -logFile -</summary>
public static class BuildGame
{
    public static void BuildStandalone()
    {
        string scenePath = "Assets/场景.unity";
        string outDir = @"D:\测试\Win64";
        Directory.CreateDirectory(outDir);
        string exe = Path.Combine(outDir, "珊瑚牧歌.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Debug.Log($"[Build] result={report.summary.result} errors={report.summary.totalErrors} outDir={outDir}");
        EditorApplication.Exit(report.summary.totalErrors > 0 ? 1 : 0);
    }
}
