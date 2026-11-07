using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

/// <summary>
/// Windows 注册表开机自启动辅助工具
///
/// 使用反射动态加载 Microsoft.Win32.Registry，无需编译时引用该程序集，
/// 保证在 netstandard2.1 (IL2CPP / Mono) 目标下也能正常编译和构建。
///
/// 仅在 Windows 平台的 Player 构建中真正生效；
/// Editor 模式下反射找不到 Registry（因为 dll 不在 AppDomain），
/// 所有方法自动返回 false / 打印提示，不会抛异常。
/// </summary>
public static class RegistryHelper
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // ===== 反射缓存 =====
    static bool _initialized = false;
    static bool _registryAvailable = false;
    static FieldInfo _currentUserField;
    static Type _registryKeyType;
    static Type _registryValueKindType;
    static object _valueKindString; // RegistryValueKind.String 枚举值

    /// <summary>
    /// 尝试反射加载 Microsoft.Win32.Registry 程序集
    /// 成功时 _registryAvailable = true，所有方法可用
    /// 失败时优雅降级
    /// </summary>
    static void EnsureRegistryLoaded()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // Microsoft.Win32.Registry 在 Windows 运行时（.NET Framework / .NET Core / netstandard）
            // 都在 GAC 或随 mono runtime 一起提供，Load 它需要公钥标识
            var asm = Assembly.Load(
                "Microsoft.Win32.Registry, Version=4.1.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");

            var registryType = asm.GetType("Microsoft.Win32.Registry");
            if (registryType == null) return;

            _registryKeyType = asm.GetType("Microsoft.Win32.RegistryKey");
            _registryValueKindType = asm.GetType("Microsoft.Win32.RegistryValueKind");
            _currentUserField = registryType.GetField(
                "CurrentUser", BindingFlags.Public | BindingFlags.Static);

            // 缓存 RegistryValueKind.String 枚举值
            if (_registryValueKindType != null)
            {
                _valueKindString = Enum.Parse(_registryValueKindType, "String");
            }

            _registryAvailable = (_currentUserField != null && _registryKeyType != null);
        }
        catch (Exception e)
        {
            // 反射失败（非 Windows 平台、Editor 模式、dll 不可用等）
            UnityEngine.Debug.Log($"[注册表] 反射加载 Registry 失败: {e.Message}，功能已禁用");
            _registryAvailable = false;
        }
    }

    // ===== 工具方法 =====

    /// <summary>用反射调 RegistryKey.OpenSubKey(</summary>
    static object OpenSubKey(object key, string subPath, bool writable)
    {
        if (key == null) return null;
        var mi = _registryKeyType.GetMethod("OpenSubKey", new[] { typeof(string), typeof(bool) });
        return mi?.Invoke(key, new object[] { subPath, writable });
    }

    /// <summary>用反射调 RegistryKey.CreateSubKey(</summary>
    static object CreateSubKey(object key, string subPath, bool writable)
    {
        if (key == null) return null;
        var mi = _registryKeyType.GetMethod("CreateSubKey", new[] { typeof(string), typeof(bool) });
        return mi?.Invoke(key, new object[] { subPath, writable });
    }

    /// <summary>用反射调 RegistryKey.GetValue(</summary>
    static object GetValue(object key, string name)
    {
        if (key == null) return null;
        var mi = _registryKeyType.GetMethod("GetValue", new[] { typeof(string) });
        return mi?.Invoke(key, new object[] { name });
    }

    /// <summary>用反射调 RegistryKey.SetValue(</summary>
    static void SetValue(object key, string name, object value)
    {
        if (key == null || _valueKindString == null) return;
        var mi = _registryKeyType.GetMethod("SetValue",
            new[] { typeof(string), typeof(object), _registryValueKindType });
        mi?.Invoke(key, new object[] { name, value, _valueKindString });
    }

    /// <summary>用反射调 RegistryKey.DeleteValue(</summary>
    static void DeleteValue(object key, string name, bool throwOnMissing)
    {
        if (key == null) return;
        var mi = _registryKeyType.GetMethod("DeleteValue", new[] { typeof(string), typeof(bool) });
        mi?.Invoke(key, new object[] { name, throwOnMissing });
    }

    /// <summary>用反射调 RegistryKey.Dispose()</summary>
    static void Dispose(object key)
    {
        if (key == null) return;
        var mi = _registryKeyType.GetMethod("Dispose", Type.EmptyTypes);
        mi?.Invoke(key, null);
    }

    // ===== 公共 API =====

    /// <summary>
    /// 获取用于注册 Run 键的应用名（用产品名，避免冲突）
    /// </summary>
    static string AppKeyName
    {
        get
        {
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
        EnsureRegistryLoaded();
        if (!_registryAvailable) return false;

        try
        {
            var currentUser = _currentUserField.GetValue(null);
            var keyObj = OpenSubKey(currentUser, RunKeyPath, false);
            try
            {
                if (keyObj == null) return false;
                object value = GetValue(keyObj, AppKeyName);
                return value != null;
            }
            finally { Dispose(keyObj); }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[注册表] 读取开机自启动失败: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 注册或取消开机自启动
    /// </summary>
    public static void SetAutoStart(bool enable)
    {
        EnsureRegistryLoaded();
        if (!_registryAvailable)
        {
            UnityEngine.Debug.Log("[注册表] 反射不可用，跳过开机自启动设置");
            return;
        }

        try
        {
            var currentUser = _currentUserField.GetValue(null);
            var keyObj = CreateSubKey(currentUser, RunKeyPath, true);
            try
            {
                if (keyObj == null)
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
                    string quoted = $"\"{exePath}\"";
                    SetValue(keyObj, AppKeyName, quoted);
                    UnityEngine.Debug.Log($"[注册表] 已注册开机自启动: {quoted}");
                }
                else
                {
                    object existing = GetValue(keyObj, AppKeyName);
                    if (existing != null)
                    {
                        DeleteValue(keyObj, AppKeyName, false);
                        UnityEngine.Debug.Log("[注册表] 已取消开机自启动");
                    }
                }
            }
            finally { Dispose(keyObj); }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[注册表] 设置开机自启动失败: {e.Message}");
        }
    }
}
