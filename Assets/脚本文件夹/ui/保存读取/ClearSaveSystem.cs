using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 清除所有存档（最终版）
///
/// 使用方式：
///   1. 把你场景里已有的按钮拖到 Inspector 的 "targetButton" 字段
///   2. 或者在 Button 的 OnClick() 里手动加 ClearSaveSystem → ClearAll() 引用
///   3. 快捷键 Delete / Ctrl+Delete 也可以触发
///   4. 顶部菜单 Tools → 清除存档（全清）
/// </summary>
public class ClearSaveSystem : MonoBehaviour
{
    [Header("绑定你的按钮")]
    [Tooltip("把你场景里想用来清除存档的 Button 拖进来（可选，拖了会自动绑 OnClick）")]
    public Button targetButton;

    [Header("快捷键")]
    public bool enableKeyboardShortcut = true;
    public KeyCode shortcutKey = KeyCode.Delete;
    public bool alsoNeedCtrl = true;

    void Awake()
    {
        // 自动绑定 targetButton.OnClick → ClearAll()
        if (targetButton != null)
        {
            targetButton.onClick.AddListener(ClearAll);
            Debug.Log("[清除存档] ✅ 已自动绑定 targetButton.onClick → ClearAll()");
        }
        else
        {
            Debug.Log("[清除存档] ⚠️ targetButton 为空 —— 请在 Inspector 把你的按钮拖进来，或手动在 Button.OnClick 里绑 ClearAll()");
        }
    }

    void Update()
    {
        if (!enableKeyboardShortcut) return;

        bool ctrlOK = !alsoNeedCtrl ||
                      Input.GetKey(KeyCode.LeftControl) ||
                      Input.GetKey(KeyCode.RightControl);

        if (ctrlOK && Input.GetKeyDown(shortcutKey))
        {
            Debug.Log($"[清除存档] 快捷键 {shortcutKey}{(alsoNeedCtrl ? "+Ctrl" : "")} 触发");
            ClearAll();
        }
    }

    [ContextMenu("清除所有存档")]
    public void ClearAll()
    {
        Debug.Log("============================================");
        Debug.Log("======== [清除存档] 开始执行 ============");
        Debug.Log("============================================");

        SaveSystem saveSys = SaveSystem.Instance;
        float oldAutoSave = 0f;
        bool oldSaveOnQuit = false;

        // -------- 0. 前置诊断 --------
        string pdp = Application.persistentDataPath;
        string savePath = Path.Combine(pdp, "save.json");
        Debug.Log($"[清除存档] ① persistentDataPath = \"{pdp}\"");
        Debug.Log($"[清除存档] ② savePath = \"{savePath}\"");
        Debug.Log($"[清除存档] ③ File.Exists(savePath) = {File.Exists(savePath)}");
        Debug.Log($"[清除存档] ④ SaveSystem.Instance = {(saveSys != null ? "存在" : "NULL！")}");

        // -------- Step 1: 暂停所有反写系统 --------
        if (saveSys != null)
        {
            oldAutoSave = saveSys.autoSaveInterval;
            oldSaveOnQuit = saveSys.saveOnQuit;
            saveSys.autoSaveInterval = 0f;
            saveSys.saveOnQuit = false;
            Debug.Log($"[清除存档] 已暂停 SaveSystem（原 {oldAutoSave}s，OnQuit={oldSaveOnQuit}）");
        }

        var pausedSpawners = new List<ObjectSpawner>();
        foreach (var s in ObjectSpawner.AllSpawners)
        {
            if (s != null && s.enabled) { s.enabled = false; pausedSpawners.Add(s); }
        }
        Debug.Log($"[清除存档] 已暂停 {pausedSpawners.Count} 个 ObjectSpawner");

        // -------- Step 2: 删 Steam 云端 --------
        bool cloudDeleted = SteamCloudStorage.DeleteCloudSave();
        Debug.Log($"[清除存档] 云端删除: {(cloudDeleted ? "✅" : "❌（空存档会兜底）")}");

        // -------- Step 3: 清 DropItem --------
        DropItem.ClearAllCounts();
        ClearDropItemSeededTypes();
        Debug.Log("[清除存档] 已清 DropItem 库存 + _seededTypes");

        // -------- Step 4: 清 PlayerPrefs --------
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[清除存档] 已清 PlayerPrefs（全清）");

        // -------- Step 5: 清 GameStats --------
        Debug.Log($"[清除存档] GameStats 清前: seen={GameStats.SeenFishCount} placed={GameStats.TotalCoralsPlaced} sold={GameStats.TotalCoralsSold}");
        GameStats.Reset();
        Debug.Log($"[清除存档] GameStats 清后: seen={GameStats.SeenFishCount} placed={GameStats.TotalCoralsPlaced} sold={GameStats.TotalCoralsSold}");

        // -------- Step 6: 删本地存档文件 --------
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log("[清除存档] 已删 save.json");
        }
        else Debug.Log("[清除存档] save.json 不存在，跳过");

        // -------- Step 7: 销毁场景中所有 DropItem 实例 --------
        var drops = FindObjectsOfType<DropItem>();
        foreach (var d in drops) if (d != null) Destroy(d.gameObject);
        Debug.Log($"[清除存档] 已销毁 {drops.Length} 个 DropItem 实例");

        // -------- Step 8: 刷新图鉴 UI --------
        var allPages = FindObjectsOfType<FishJournalPage>(true);
        foreach (var page in allPages) page.Refresh();
        Debug.Log($"[清除存档] 已刷新 {allPages.Length} 个图鉴页面");

        // -------- Step 9: 重置珊瑚数量文本 --------
        var placers = FindObjectsOfType<DragAndPlaceSystem>();
        int resetCount = 0;
        foreach (var placer in placers)
        {
            if (placer != null && placer.coralCountText != null)
            {
                placer.coralCountText.text = "0";
                resetCount++;
            }
        }
        Debug.Log($"[清除存档] 已重置 {resetCount} 个 coralCountText");

        // -------- Step 10: 写空存档覆盖本地 + 云端 --------
        if (saveSys != null)
        {
            saveSys.Save();
            Debug.Log("[清除存档] ✅ 已保存空存档");
        }
        else
        {
            try { SaveEmptyJson(savePath); Debug.Log("[清除存档] ✅ 手动写入空存档"); }
            catch (System.Exception e) { Debug.LogError($"[清除存档] ❌ 写空存档失败: {e.Message}"); }
        }

        // -------- Step 11: 恢复 --------
        foreach (var s in pausedSpawners) if (s != null) s.enabled = true;
        if (saveSys != null)
        {
            saveSys.autoSaveInterval = oldAutoSave;
            saveSys.saveOnQuit = oldSaveOnQuit;
        }

        Debug.Log("============================================");
        Debug.Log("============ [清除存档] 全部完成 ============");
        Debug.Log("============================================");
    }

    // ==================== 辅助方法 ====================

    static void ClearDropItemSeededTypes()
    {
        try
        {
            var field = typeof(DropItem).GetField("_seededTypes", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) { Debug.LogWarning("[清除存档] 反射找不到 DropItem._seededTypes"); return; }
            var set = field.GetValue(null) as HashSet<string>;
            if (set != null) set.Clear();
        }
        catch (System.Exception e) { Debug.LogWarning($"[清除存档] 清 _seededTypes 反射异常: {e.Message}"); }
    }

    static void SaveEmptyJson(string path)
    {
        string emptyJson = "{\"itemCounts\":[],\"windowState\":{\"posX\":0,\"posY\":0,\"windowScale\":1},\"seenFish\":[],\"droppedFish\":[],\"totalCoralsPlaced\":0,\"totalCoralsSold\":0,\"placedObjects\":[]}";
        File.WriteAllText(path, emptyJson);
        try { SteamCloudStorage.UploadFromLocal(path); } catch { }
    }
}
