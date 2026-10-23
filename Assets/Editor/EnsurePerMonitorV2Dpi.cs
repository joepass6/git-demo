using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 强制 Windows 打包的 DPI 感知级别 = Per Monitor V2。
///
/// 桌宠窗口走 UpdateLayeredWindow 物理像素 1:1 上屏：进程必须是 PerMonitorV2，
/// 否则在 125%/150% 缩放的玩家机器上坐标体系混用，会出现窗口尺寸、点击坐标与画面错位。
/// 运行时再调用 SetProcessDpiAwarenessContext 可能已晚，根因必须写进 PlayerSettings（exe manifest）。
///
/// 不同 Unity 版本该设置的公开 API 名称不同，这里用 SerializedObject/反射自适应发现，
/// 不硬编码任何可能在 2022.3 编译不过的 API。
/// </summary>
[InitializeOnLoad]
static class EnsurePerMonitorV2Dpi
{
    const string Target = "PerMonitorV2";

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

            Debug.LogWarning("[DPI设置] 当前 Unity 版本没有内置 DPI Awareness 设置，" +
                             "请手动确认：Player Settings → Windows → DPI Awareness = Per Monitor V2；" +
                             "或忽略此项（运行时已有 SetProcessDpiAwarenessContext 兜底）");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DPI设置] 自动设置失败: " + e.Message);
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
            Debug.LogWarning("[DPI设置] 找到了 dpiAwareness 但枚举里没有 PerMonitorV2，可选值: " + string.Join("/", names));
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
