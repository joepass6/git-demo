using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 打包版运行时，把所有日志（含 [桌面窗口]/[对齐诊断]）实时镜像到桌面上的文件，
/// 玩家不用翻 LocalLow 目录，直接把桌面文件发回即可。
/// 文件：桌面\珊瑚牧歌_日志.txt（每次启动覆盖；编辑器里不生成）
/// </summary>
public static class DesktopLogMirror
{
    static StreamWriter writer;
    static readonly object writeLock = new object();
    static string logPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
#if !UNITY_EDITOR
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            logPath = Path.Combine(desktop, "珊瑚牧歌_日志.txt");

            writer = new StreamWriter(logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
            Application.logMessageReceived += OnLog;
            Application.quitting += Close;

            WriteLine($"===== 会话开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            WriteLine($"OS: {SystemInfo.operatingSystem}, GPU: {SystemInfo.graphicsDeviceType}, 屏幕: {Screen.width}x{Screen.height}, DPI: {Screen.dpi:F0}");
            Debug.Log($"[桌面日志] 日志正在同时写入: {logPath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[桌面日志] 创建失败: " + e.Message);
        }
#endif
    }

    // 回调可能来自非主线程，全程加锁写文件
    static void OnLog(string condition, string stackTrace, LogType type)
    {
        WriteLine($"[{type}] {condition}");
        if (type == LogType.Exception || type == LogType.Error)
            WriteLine(stackTrace);
    }

    static void WriteLine(string line)
    {
        if (writer == null) return;
        lock (writeLock)
        {
            try { writer.WriteLine(line); } catch { /* 写盘失败不影响游戏 */ }
        }
    }

    static void Close()
    {
        Application.logMessageReceived -= OnLog;
        Application.quitting -= Close;
        lock (writeLock)
        {
            try { writer?.WriteLine($"===== 会话结束 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====="); } catch { }
            try { writer?.Flush(); writer?.Close(); } catch { }
            writer = null;
        }
    }
}
