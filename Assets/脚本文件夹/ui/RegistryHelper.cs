using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

/// <summary>
/// Windows 注册表开机自启动辅助工具
/// 仅在 Windows 打包版生效，编辑器和其他平台自动返回 false
/// </summary>
public static class RegistryHelper
{
    // Run 注册表路径（当前用户，不需要管理员权限）
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// 获取用于注册 Run 键的应用名（用产品名，避免冲突）
    /// </summary>
    static string AppKeyName
    {
        get
        {
            // 用 Unity 的产品名，取不到就用进程名兜底
            string name = string.IsNullOrEmpty(UnityEngine.Application.productName)
                ? Process.GetCurrentProcess().ProcessName
                : UnityEngine.Application.productName;
            return "海洞_" + name;
        }
    }

    /// <summary>
    /// 当前是否已注册为开机自启动
    /// </summary>
    public static bool IsAutoStartEnabled()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null) return false;
                object value = key.GetValue(AppKeyName);
                return value != null;
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[注册表] 读取开机自启动失败: {e.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// 注册或取消开机自启动
    /// </summary>
    public static void SetAutoStart(bool enable)
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    UnityEngine.Debug.LogError("[注册表] 无法打开 Run 键");
                    return;
                }

                if (enable)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        UnityEngine.Debug.LogError("[注册表] 找不到当前 exe 路径");
                        return;
                    }
                    // 加引号防路径里有空格
                    string quoted = $"\"{exePath}\"";
                    key.SetValue(AppKeyName, quoted, RegistryValueKind.String);
                    UnityEngine.Debug.Log($"[注册表] 已注册开机自启动: {quoted}");
                }
                else
                {
                    if (key.GetValue(AppKeyName) != null)
                    {
                        key.DeleteValue(AppKeyName, false);
                        UnityEngine.Debug.Log("[注册表] 已取消开机自启动");
                    }
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[注册表] 设置开机自启动失败: {e.Message}");
        }
#else
        UnityEngine.Debug.Log("[注册表] 非 Windows 打包版，跳过开机自启动设置");
#endif
    }
}
