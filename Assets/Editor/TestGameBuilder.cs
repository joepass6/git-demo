#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 测试版打包脚本（宝物数值 99999999）。
///
/// 两种用法：
/// 1) 编辑器里点菜单：Build / 打包测试版(宝物99999999)
/// 2) 命令行：
///    Unity.exe -batchmode -quit -projectPath "D:\桌面游戏海洞" ^
///      -executeMethod TestGameBuilder.BuildTest -buildOutput "D:\珊瑚牧歌_测试版" -logFile 打包日志.txt
///
/// 做的事情：
/// - 临时加上 TEST_BUILD 宏（激活测试版宝物覆盖脚本、禁止测试版写存档）
/// - 临时把产品名改成“珊瑚牧歌_测试版”（exe 名与存档目录都与正式版隔离）
/// - 强制 D3D11（与正式发布包一致）
/// - 构建结束（无论成败）自动还原产品名和宏定义，不污染工程配置
/// </summary>
public static class TestGameBuilder
{
    const string TestDefine = "TEST_BUILD";
    const string TestProductName = "珊瑚牧歌_测试版";
    const string DefaultOutputDir = @"D:\珊瑚牧歌_测试版";

    [MenuItem("Build/打包测试版(宝物99999999)")]
    public static void BuildTestMenu()
    {
        string dir = EditorUtility.SaveFolderPanel("选择测试版输出文件夹", "D:\\", "珊瑚牧歌_测试版");
        if (string.IsNullOrEmpty(dir))
        {
            Debug.LogWarning("[测试版打包] 已取消");
            return;
        }
        DoBuild(Path.Combine(dir, TestProductName + ".exe"), true);
    }

    /// <summary>命令行入口</summary>
    public static void BuildTest()
    {
        string outDir = GetArg("-buildOutput");
        if (string.IsNullOrEmpty(outDir)) outDir = DefaultOutputDir;

        bool ok = DoBuild(Path.Combine(outDir, TestProductName + ".exe"), false);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool DoBuild(string outputPath, bool interactive)
    {
        var target = NamedBuildTarget.Standalone;
        string oldDefines = PlayerSettings.GetScriptingDefineSymbols(target);
        string oldProductName = PlayerSettings.productName;

        try
        {
            string outDir = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // 1. 临时修改：产品名（隔离存档目录）+ TEST_BUILD 宏
            PlayerSettings.productName = TestProductName;

            var defines = new List<string>(oldDefines.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries));
            if (!defines.Contains(TestDefine)) defines.Add(TestDefine);
            string newDefines = string.Join(";", defines.ToArray());
            PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
            AssetDatabase.SaveAssets();

            // 2. D3D11（与正式发布包相同，双显卡/新 Win 兼容性最好）
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);

            // 3. 收集启用的场景
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.enabled && !string.IsNullOrEmpty(s.path)) scenes.Add(s.path);
            }
            if (scenes.Count == 0)
            {
                string fallback = "Assets/场景.unity";
                if (File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, fallback)))
                    scenes.Add(fallback);
                else
                    throw new BuildFailedException("Build Settings 中没有启用的场景，且 Assets/场景.unity 不存在");
            }

            Debug.Log("[测试版打包] 场景: " + string.Join("; ", scenes));
            Debug.Log("[测试版打包] 宏定义: " + newDefines);
            Debug.Log("[测试版打包] 输出: " + outputPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[测试版打包] 结果: {summary.result}，错误 {summary.totalErrors}，警告 {summary.totalWarnings}，大小 {summary.totalSize / (1024 * 1024)} MB");

            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
            {
                Debug.LogError("[测试版打包] 构建失败");
                return false;
            }

            Debug.Log("[测试版打包] 成功: " + outputPath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[测试版打包] 异常: " + e);
            return false;
        }
        finally
        {
            // 4. 无论成败都还原工程配置
            PlayerSettings.SetScriptingDefineSymbols(target, oldDefines);
            PlayerSettings.productName = oldProductName;
            AssetDatabase.SaveAssets();
            Debug.Log("[测试版打包] 已还原产品名与宏定义");
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
#endif
