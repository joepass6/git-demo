#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 清除存档的 Editor 菜单入口。
/// 运行时（Play 模式）直接调 ClearSaveSystem.ClearAll()；
/// 非运行时手动删 save.json + PlayerPrefs 注册表。
/// </summary>
public static class ClearSaveMenu
{
    const string MenuPath = "Tools/清除存档（全清）";

    [MenuItem(MenuPath)]
    public static void ClearSave()
    {
        if (Application.isPlaying)
        {
            // Play 模式：直接调场景里的 ClearSaveSystem
            var sys = Object.FindObjectOfType<ClearSaveSystem>();
            if (sys != null)
            {
                sys.ClearAll();
            }
            else
            {
                Debug.LogWarning("[清除存档] 场景里没找到 ClearSaveSystem 组件，改用手动清除");
                ClearManually();
            }
        }
        else
        {
            // 非 Play 模式：手动清
            ClearManually();
        }
    }

    /// <summary>最兜底的手动清除——不依赖任何游戏代码，直接删文件+注册表</summary>
    static void ClearManually()
    {
        string pdp = Application.persistentDataPath;
        string savePath = Path.Combine(pdp, "save.json");

        Debug.Log("========== [Editor清除存档] 手动执行 ==========");
        Debug.Log($"[Editor清除存档] persistentDataPath = {pdp}");

        // 1. 删 save.json
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log($"[Editor清除存档] ✅ 已删除 save.json");
        }
        else
        {
            Debug.Log($"[Editor清除存档] save.json 不存在，跳过");
        }

        // 2. 清 PlayerPrefs
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[Editor清除存档] ✅ 已清 PlayerPrefs");

        // 3. 尝试删 Steam 云端（游戏没运行的话 Steam 可能没初始化，会自动跳过）
        try
        {
            var type = System.Type.GetType("SteamCloudStorage, Assembly-CSharp");
            if (type != null)
            {
                var method = type.GetMethod("DeleteCloudSave", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null) method.Invoke(null, null);
                Debug.Log("[Editor清除存档] ✅ 已尝试删 Steam 云端存档");
            }
        }
        catch { /* Steam 相关代码在非 Play 时可能有问题，忽略 */ }

        Debug.Log("========== [Editor清除存档] 完成 ==========");

        if (Application.isPlaying)
        {
            // Play 模式下再触发 Unity 事件
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
}
#endif
