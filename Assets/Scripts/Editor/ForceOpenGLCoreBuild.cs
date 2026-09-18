#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 构建前强制切换到 OpenGLCore 并打印状态
/// 用法：菜单 Build → Force OpenGLCore and Build
/// </summary>
public static class ForceOpenGLCoreBuild
{
    [MenuItem("Build/Force OpenGLCore and Build (桌面透明专用)")]
    public static void Build()
    {
        // 强制 Windows 只用 OpenGLCore
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new UnityEngine.Rendering.GraphicsDeviceType[]
        {
            UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore
        });
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);

        // 打印确认
        var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
        Debug.Log("========== 构建前检查 ==========");
        Debug.Log("Windows Graphics APIs: " + string.Join(", ", apis));
        Debug.Log("Color Space: " + PlayerSettings.colorSpace);
        Debug.Log("useFlipModelSwapchain: " + PlayerSettings.useFlipModelSwapchain);

        // 自动获取场景列表
        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.enabled) scenes.Add(s.path);
        }
        if (scenes.Count == 0)
        {
            Debug.LogError("Build Settings 里没有启用的场景！先去 File → Build Settings 把场景加进去");
            return;
        }
        Debug.Log("构建场景: " + string.Join(", ", scenes));
        Debug.Log("================================");

        // 选输出路径
        string path = EditorUtility.SaveFolderPanel("选择构建输出文件夹", "D:\\", "海洞游戏_GL");
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("用户取消了构建");
            return;
        }

        // 确保路径里有 exe 文件名
        if (!path.EndsWith(".exe"))
        {
            path = Path.Combine(path, PlayerSettings.productName + ".exe");
        }

        Debug.Log("开始构建到: " + path);
        Debug.Log("使用 OpenGLCore 强制构建...");

        // 用 BuildPlayerOptions 构建
        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("构建完成！结果: " + report.summary.result);
        Debug.Log("输出路径: " + report.summary.outputPath);
    }
}
#endif
