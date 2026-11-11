#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 清除玩法进度的 Editor 菜单入口（保留窗口位置/缩放、置顶、时钟微调、语言、音量）。
/// 运行时（Play 模式）直接调 ClearSaveSystem.ClearAll()；
/// 非运行时重写 save.json 的玩法字段 + 只删 FishFound_* 图鉴标记。
/// </summary>
public static class ClearSaveMenu
{
    const string MenuPath = "Tools/清除玩法进度";

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

    /// <summary>
    /// 非 Play 模式下的手动清除：保留 save.json 的 windowState，只清玩法字段；
    /// PlayerPrefs 只删 FishFound_* 图鉴标记，不动窗口/时钟/语言/音量。
    /// </summary>
    static void ClearManually()
    {
        string pdp = Application.persistentDataPath;
        string savePath = Path.Combine(pdp, "save.json");

        Debug.Log("========== [Editor清除存档] 手动执行（保留显示状态）==========");
        Debug.Log($"[Editor清除存档] persistentDataPath = {pdp}");

        // 1. 先读旧 windowState，再写回空玩法数据
        SaveSystem.WindowStateData keptWindow = null;
        if (File.Exists(savePath))
        {
            try
            {
                var old = JsonUtility.FromJson<SaveSystem.SaveData>(File.ReadAllText(savePath));
                keptWindow = old != null ? old.windowState : null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Editor清除存档] 读取旧存档失败，窗口状态将用默认值: {e.Message}");
            }
        }

        var empty = new SaveSystem.SaveData();
        if (keptWindow != null) empty.windowState = keptWindow;
        File.WriteAllText(savePath, JsonUtility.ToJson(empty, true));
        Debug.Log($"[Editor清除存档] ✅ 已重写 save.json（玩法清空，窗口状态{(keptWindow != null ? "保留" : "无旧值")}）");

        // 2. 只清图鉴标记，保留其他 PlayerPrefs
        FishJournalPage.ClearAllFound();
        Debug.Log("[Editor清除存档] ✅ 已清 FishFound_* 图鉴标记（其他 PlayerPrefs 保留）");

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
