using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 清除玩法进度（保留窗口等显示状态）
///
/// 清掉：库存、图鉴、玩家放置的珊瑚、累计统计、场景掉落物。
/// 保留：窗口位置/缩放（save.json 的 windowState）、置顶、时钟微调、
///       语言、音量等 PlayerPrefs 中的显示与偏好设置。
///
/// 使用方式：
///   1. 把你场景里已有的按钮拖到 Inspector 的 "targetButton" 字段
///   2. 或者在 Button 的 OnClick() 里手动加 ClearSaveSystem → ClearAll() 引用
///   3. 快捷键 Delete / Ctrl+Delete 也可以触发
///   4. 顶部菜单 Tools → 清除玩法进度
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

        // -------- Step 1.5: 先抓取窗口显示状态（清档只清玩法进度，窗口位置/缩放要保留）--------
        SaveSystem.WindowStateData keptWindow = CaptureKeptWindowState(savePath);
        if (keptWindow != null)
        {
            Debug.Log($"[清除存档] 将保留窗口状态: pos=({keptWindow.posX:F0},{keptWindow.posY:F0}) scale={keptWindow.windowScale:F2}");
        }

        // -------- Step 2: 删 Steam 云端 --------
        bool cloudDeleted = SteamCloudStorage.DeleteCloudSave();
        Debug.Log($"[清除存档] 云端删除: {(cloudDeleted ? "✅" : "❌（空存档会兜底）")}");

        // -------- Step 3: 清 DropItem --------
        DropItem.ClearAllCounts();
        ClearDropItemSeededTypes();
        Debug.Log("[清除存档] 已清 DropItem 库存 + _seededTypes");

        // -------- Step 4: 只清图鉴标记，保留显示与偏好设置 --------
        // 不能 DeleteAll()：那会把 WindowTopMost、ClockNudgeOffsetX/Y、语言、音量一起清掉。
        FishJournalPage.ClearAllFound();
        Debug.Log("[清除存档] 已清 FishFound_* 图鉴标记（PlayerPrefs 其余键保留）");

        // -------- Step 5: 清 GameStats --------
        Debug.Log($"[清除存档] GameStats 清前: seen={GameStats.SeenFishCount} placed={GameStats.TotalCoralsPlaced} sold={GameStats.TotalCoralsSold}");
        GameStats.Reset();
        Debug.Log($"[清除存档] GameStats 清后: seen={GameStats.SeenFishCount} placed={GameStats.TotalCoralsPlaced} sold={GameStats.TotalCoralsSold}");

        // -------- Step 6: 不删除 save.json，Step 11 用空玩法数据重写（windowState 保留）--------
        Debug.Log($"[清除存档] 保留 save.json（存在={File.Exists(savePath)}），稍后用空玩法数据覆盖");

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

        // -------- Step 10: 🔥 清除场景中所有玩家放置的珊瑚（ObjectSpawner）--------
        // ⚠️ 这是之前漏掉的关键一步！
        // 存档文件清了，但场景里玩家放的珊瑚还活着，SaveSystem.Save() 会把它们重新写回 placedObjects！
        ClearPlayerPlacedCorals();

        // -------- Step 11: 写空玩法存档覆盖本地 + 云端（显式带回保留的窗口状态）--------
        if (saveSys != null)
        {
            saveSys.Save(keptWindow);
            Debug.Log("[清除存档] ✅ 已保存空存档（窗口状态已保留）");
        }
        else
        {
            try { SaveEmptyJson(savePath, keptWindow); Debug.Log("[清除存档] ✅ 手动写入空存档（窗口状态已保留）"); }
            catch (System.Exception e) { Debug.LogError($"[清除存档] ❌ 写空存档失败: {e.Message}"); }
        }

        // -------- Step 12: 恢复 --------
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

    /// <summary>
    /// 🔥 删掉场景里所有玩家放置的珊瑚（ObjectSpawner）。
    /// 用和 SaveSystem.ClearAndRestorePlacedObjects 完全相同的判断标准：
    ///   - 跳过 _initialSceneSpawners（策划预设的初始场景珊瑚）
    ///   - 只删带 coral tag 的（递归检查子物体）
    ///   - 同时清掉 _playerPlacedSpawners 注册表
    ///   - 最后触发 RockCapacity 重算
    /// </summary>
    static void ClearPlayerPlacedCorals()
    {
        const string CoralTag = "coral";

        // 1. 确保 SaveSystem 已经记录了初始场景 spawners（兜底）
        var recordMethod = typeof(SaveSystem).GetMethod("RecordInitialSpawners",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (recordMethod != null && SaveSystem.Instance != null)
        {
            recordMethod.Invoke(SaveSystem.Instance, null);
        }

        // 2. 用反射拿 _initialSceneSpawners HashSet（和 SaveSystem 共享同一个）
        var initialField = typeof(SaveSystem).GetField("_initialSceneSpawners",
            BindingFlags.NonPublic | BindingFlags.Static);
        var initialSet = initialField?.GetValue(null) as HashSet<ObjectSpawner>;

        // 3. 用反射拿 ClearPlacedSpawnersRegistry（清注册表）
        var clearRegistryMethod = typeof(SaveSystem).GetMethod("ClearPlacedSpawnersRegistry",
            BindingFlags.Public | BindingFlags.Static);

        // 4. 递归检查 coral tag（和 SaveSystem.HasTagInHierarchy 一致）
        bool HasTag(GameObject go)
        {
            if (go == null) return false;
            if (go.CompareTag(CoralTag)) return true;
            for (int i = 0; i < go.transform.childCount; i++)
                if (HasTag(go.transform.GetChild(i).gameObject)) return true;
            return false;
        }

        // 5. 收集要删的 ObjectSpawner
        var toDestroy = new List<ObjectSpawner>();
        foreach (var sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            if (initialSet != null && initialSet.Contains(sp)) continue; // 初始场景的留着
            if (!HasTag(sp.gameObject)) continue;                        // 不是珊瑚的留着
            toDestroy.Add(sp);
        }

        // 6. 销毁
        foreach (var sp in toDestroy)
        {
            if (sp != null) UnityEngine.Object.Destroy(sp.gameObject);
        }

        // 7. 清注册表（防止 SaveSystem 下次 Save() 还扫到这些）
        clearRegistryMethod?.Invoke(null, null);

        // 8. 重算 RockCapacity（如果存在）
        try
        {
            var refreshAllMethod = System.Type.GetType("RockCapacity, Assembly-CSharp")
                ?.GetMethod("RefreshAll", BindingFlags.Public | BindingFlags.Static);
            if (refreshAllMethod != null)
            {
                refreshAllMethod.Invoke(null, new object[] { CoralTag, CoralTag });
            }
        }
        catch { /* 没有 RockCapacity 就算了 */ }

        Debug.Log($"[清除存档] 🪸 已清除 {toDestroy.Count} 个玩家放置的珊瑚 + 注册表");
    }

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

    /// <summary>
    /// 读取要保留的窗口状态：优先用运行时 DesktopWindowController 的实时值（打包环境最准）；
    /// 编辑器里 GetWindowPosition() 固定返回 0,0，则回退到 save.json 里已有的 windowState。
    /// </summary>
    static SaveSystem.WindowStateData CaptureKeptWindowState(string savePath)
    {
        SaveSystem.WindowStateData disk = null;
        try
        {
            if (File.Exists(savePath))
            {
                var data = JsonUtility.FromJson<SaveSystem.SaveData>(File.ReadAllText(savePath));
                disk = data != null ? data.windowState : null;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[清除存档] 读取旧 windowState 失败: {e.Message}"); }

        DesktopWindowController dwc = FindObjectOfType<DesktopWindowController>();
        if (dwc == null) return disk ?? new SaveSystem.WindowStateData();

        Vector2 pos = dwc.GetWindowPosition();
        var live = new SaveSystem.WindowStateData
        {
            posX = pos.x,
            posY = pos.y,
            windowScale = dwc.GetWindowScale()
        };

        // 编辑器里窗口位置恒为 0,0，用磁盘值兜底，避免清档把位置重置到左上角
        if (live.posX == 0f && live.posY == 0f && disk != null)
        {
            live.posX = disk.posX;
            live.posY = disk.posY;
        }
        if (live.windowScale <= 0.01f && disk != null)
            live.windowScale = disk.windowScale;

        return live;
    }

    static void SaveEmptyJson(string path, SaveSystem.WindowStateData windowState)
    {
        var empty = new SaveSystem.SaveData();
        if (windowState != null) empty.windowState = windowState;
        File.WriteAllText(path, JsonUtility.ToJson(empty, true));
        try { SteamCloudStorage.UploadFromLocal(path); } catch { }
    }
}
