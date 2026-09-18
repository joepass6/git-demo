using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 保存系统：保存/加载游戏数据、窗口状态
/// 快捷键：Ctrl+S 保存，Ctrl+L 加载，F5 保存，F9 加载
/// </summary>
public class SaveSystem : MonoBehaviour
{
    [Header("自动保存")]
    [Tooltip("每隔多少秒自动保存一次（0=关闭）")]
    public float autoSaveInterval = 60f;

    [Tooltip("退出游戏时自动保存")]
    public bool saveOnQuit = true;

    private string savePath => Path.Combine(Application.persistentDataPath, "save.json");
    private float autoSaveTimer = 0f;

    void Update()
    {
        // Ctrl+S 保存
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.S))
        {
            Save();
        }
        // Ctrl+L 加载
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L))
        {
            Load();
        }
        // F5 快速保存
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Save();
        }
        // F9 快速加载
        if (Input.GetKeyDown(KeyCode.F9))
        {
            Load();
        }

        // 自动保存
        if (autoSaveInterval > 0f)
        {
            autoSaveTimer += Time.deltaTime;
            if (autoSaveTimer >= autoSaveInterval)
            {
                autoSaveTimer = 0f;
                Save();
            }
        }
    }

    void OnApplicationQuit()
    {
        if (saveOnQuit) Save();
    }

    // ==================== 保存数据结构 ====================
    [System.Serializable]
    public class SaveData
    {
        public List<ItemCountData> itemCounts = new List<ItemCountData>();
        public WindowStateData windowState = new WindowStateData();
    }

    [System.Serializable]
    public class ItemCountData
    {
        public string type;
        public int count;
    }

    [System.Serializable]
    public class WindowStateData
    {
        public float posX, posY;
        public float windowScale = 1f;
    }

    // ==================== 保存 ====================
    public void Save()
    {
        SaveData data = new SaveData();

        // 1. 保存掉落物品数量（DropItem._counts 静态字典）
        var countsField = typeof(DropItem).GetField("_counts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (countsField != null)
        {
            var dict = countsField.GetValue(null) as Dictionary<string, int>;
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    data.itemCounts.Add(new ItemCountData { type = kvp.Key, count = kvp.Value });
                }
            }
        }

        // 2. 保存窗口状态
        DesktopWindowController dwc = FindObjectOfType<DesktopWindowController>();
        if (dwc != null)
        {
            data.windowState.posX = dwc.GetWindowPosition().x;
            data.windowState.posY = dwc.GetWindowPosition().y;
            data.windowState.windowScale = dwc.GetWindowScale();
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(savePath, json);
        Debug.Log($"[保存系统] 已保存到 {savePath}，物品类型 {data.itemCounts.Count}，窗口=({data.windowState.posX:F0},{data.windowState.posY:F0}) 缩放={data.windowState.windowScale:F2}");
    }

    // ==================== 加载 ====================
    public void Load()
    {
        if (!File.Exists(savePath))
        {
            Debug.LogWarning("[保存系统] 找不到存档文件");
            return;
        }

        string json = File.ReadAllText(savePath);
        SaveData data = JsonUtility.FromJson<SaveData>(json);

        // 1. 恢复掉落物品数量
        foreach (ItemCountData item in data.itemCounts)
        {
            DropItem.SetCount(item.type, item.count);
        }

        // 2. 恢复窗口状态
        DesktopWindowController dwc = FindObjectOfType<DesktopWindowController>();
        if (dwc != null)
        {
            if (data.windowState.posX != 0 || data.windowState.posY != 0)
            {
                dwc.SetWindowPosition(data.windowState.posX, data.windowState.posY);
            }
            dwc.SetWindowScale(data.windowState.windowScale);
        }

        Debug.Log($"[保存系统] 已加载 {savePath}，物品类型 {data.itemCounts.Count}，窗口=({data.windowState.posX:F0},{data.windowState.posY:F0}) 缩放={data.windowState.windowScale:F2}");
    }
}
