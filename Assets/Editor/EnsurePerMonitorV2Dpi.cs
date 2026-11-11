using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 检查/设置 Windows 打包的 DPI 感知级别 = Per Monitor V2。
///
/// 桌宠窗口走 UpdateLayeredWindow 物理像素 1:1 上屏，进程最好是 PerMonitorV2，
/// 否则在 125%/150% 缩放的玩家机器上坐标体系混用，会出现窗口尺寸、点击坐标与画面错位。
///
/// 版本差异（重点）：
///   - 新版本 Unity 的 PlayerSettings 有 DPI Awareness 开关，直接写入即可；
///   - Unity 2022.3 及更早版本没有这个开关（ProjectSettings.asset 里也没有该键），
///     但 Windows 播放器自带的清单 WindowsPlayer.manifest 已经声明了
///     dpiAware=True/PM + dpiAwareness=PerMonitorV2，打包结果本身就是 PerMonitorV2。
///   所以这里改成：有开关就写开关，没开关就核对播放器清单，不再提示用户去一个
///   2022.3 里根本不存在的 Player Settings 选项里手动设置。
/// </summary>
[InitializeOnLoad]
static class EnsurePerMonitorV2Dpi
{
    const string Target = "PerMonitorV2";
    const string SessionKeyOkay = "CoralSong.DpiCheck.Ok";
    const string SessionKeyWarn = "CoralSong.DpiCheck.Warn";

    static EnsurePerMonitorV2Dpi()
    {
        EditorApplication.delayCall += Apply;
    }

    static void Apply()
    {
        try
        {
            // 路线 1：SerializedObject 直读 ProjectSettings.asset（2022.2+ 有序列化键）
            if (TrySetViaSerializedObject()) return;

            // 路线 2：反射 PlayerSettings 上的 dpi 静态属性
            if (TrySetViaReflection()) return;

            // 路线 3：当前 Unity 版本没有开关，改看播放器清单（打包结果的实际依据）
            CheckPlayerManifest();
        }
        catch (Exception e)
        {
            WarnOnce("[DPI设置] 检查失败: " + e.Message);
        }
    }

    static bool TrySetViaSerializedObject()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets == null || assets.Length == 0) return false;

        var so = new SerializedObject(assets[0]);
        var prop = so.FindProperty("dpiAwareness");
        if (prop == null || prop.propertyType != SerializedPropertyType.Enum) return false;

        string[] names = prop.enumNames;
        int idx = FindTargetIndex(names);
        if (idx < 0)
        {
            WarnOnce("[DPI设置] 找到了 dpiAwareness 但枚举里没有 PerMonitorV2，可选值: " + string.Join("/", names));
            return true; // 设置项存在但版本太老，不再走反射
        }

        if (prop.enumValueIndex != idx)
        {
            prop.enumValueIndex = idx;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[DPI设置] dpiAwareness 已设为 {names[idx]}（重新打包后对玩家机器生效）");
        }
        return true;
    }

    static bool TrySetViaReflection()
    {
        Type t = typeof(PlayerSettings);
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (p.Name.IndexOf("dpi", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!p.CanRead || !p.CanWrite || !p.PropertyType.IsEnum) continue;

            Array values = Enum.GetValues(p.PropertyType);
            string[] names = Enum.GetNames(p.PropertyType);
            int idx = FindTargetIndex(names);
            if (idx < 0) continue;

            object targetValue = values.GetValue(idx);
            object cur = p.GetValue(null, null);
            if (!Equals(cur, targetValue))
            {
                p.SetValue(null, targetValue, null);
                AssetDatabase.SaveAssets();
                Debug.Log($"[DPI设置] PlayerSettings.{p.Name} 已设为 {names[idx]}（重新打包后生效）");
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// 没有 PlayerSettings 开关时，核对 Windows 播放器的默认清单。
    /// 清单内置 PerMonitorV2 就说明打包结果天然正确，只需提示一次，无需任何操作。
    /// </summary>
    static void CheckPlayerManifest()
    {
        string manifest = PlayerManifestPath();
        if (!File.Exists(manifest))
        {
            LogOnce("[DPI设置] 当前 Unity 版本没有 DPI Awareness 开关，也未找到播放器清单；" +
                    "运行时的 SetProcessDpiAwarenessContext 会兜底，无需处理。");
            return;
        }

        string text = File.ReadAllText(manifest);
        if (text.IndexOf(Target, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            LogOnce("[DPI设置] 当前 Unity 版本无 DPI Awareness 开关；Windows 播放器清单已声明 PerMonitorV2，" +
                    "打包结果正确，无需处理。");
        }
        else
        {
            WarnOnce("[DPI设置] 播放器清单里没有 PerMonitorV2，且当前 Unity 版本没有 PlayerSettings 开关。" +
                     "打包版将依赖运行时 SetProcessDpiAwarenessContext 兜底。清单路径: " + manifest);
        }
    }

    static string PlayerManifestPath()
    {
        // EditorApplication.applicationContentsPath = <UnityEditor>/Data
        return Path.Combine(EditorApplication.applicationContentsPath,
            "PlaybackEngines", "windowsstandalonesupport",
            "Source", "WindowsPlayer", "WindowsPlayer", "WindowsPlayer.manifest");
    }

    /// <summary>整个编辑器会话只记一次，避免每次脚本重编译都刷屏</summary>
    static void LogOnce(string msg)
    {
        if (SessionState.GetBool(SessionKeyOkay, false)) return;
        SessionState.SetBool(SessionKeyOkay, true);
        Debug.Log(msg);
    }

    static void WarnOnce(string msg)
    {
        if (SessionState.GetBool(SessionKeyWarn, false)) return;
        SessionState.SetBool(SessionKeyWarn, true);
        Debug.LogWarning(msg);
    }

    /// <summary>在枚举名里找 PerMonitorV2（兼容 PerMonitorAwareV2 / PerMonitorV2DpiAware 等命名）</summary>
    static int FindTargetIndex(string[] names)
    {
        if (names == null) return -1;
        // 精确语义：名字同时含 permonitor 和 v2
        for (int i = 0; i < names.Length; i++)
        {
            string n = names[i].ToLowerInvariant();
            if (n.Contains("permonitor") && n.Contains("v2")) return i;
        }
        return -1;
    }
}
