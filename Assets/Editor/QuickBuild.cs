using UnityEditor;
using UnityEditor.Build.Reporting;
using System.IO;

public static class QuickBuild
{
    [MenuItem("Build/Quick Windows x64")]
    public static void BuildWindows()
    {
        string outDir = "D:/BuildOut";
        // 先清空旧文件，确保 BuildPipeline 能干净地覆盖
        if (Directory.Exists(outDir))
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
        Directory.CreateDirectory(outDir);
        string exePath = Path.Combine(outDir, "珊瑚牧歌.exe");

        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/场景.unity" },
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        // 不覆盖 PlayerSettings，保持 ProjectSettings.asset 里的原值

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            UnityEngine.Debug.Log($"✅ Build 成功: {exePath}  ({summary.totalSize / 1024 / 1024} MB, {summary.totalTime.Seconds}秒)");
        }
        else
        {
            UnityEngine.Debug.LogError($"❌ Build 失败: {summary.result}");
        }
    }
}
