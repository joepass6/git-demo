using UnityEngine;
using System.IO;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 保存系统：保存/加载游戏数据、窗口状态
/// 快捷键：Ctrl+S 保存，Ctrl+L 加载，F5 保存，F9 加载
/// 启动时自动加载存档；保存后自动同步 Steam 云存档；图鉴变化时立即写盘
/// </summary>
public class SaveSystem : MonoBehaviour
{
    /// <summary>存档加载完成后触发（含启动时自动加载和手动 Ctrl+L/F9 加载）。参数为成功加载到的物品类型数。</summary>
    public static event System.Action<int> OnLoadComplete;

    [Header("自动保存")]
    [Tooltip("每隔多少秒自动保存一次（0=关闭）")]
    public float autoSaveInterval = 60f;

    [Tooltip("退出游戏时自动保存")]
    public bool saveOnQuit = true;

    private string savePath => Path.Combine(Application.persistentDataPath, "save.json");
    private float autoSaveTimer = 0f;

    // 防抖立即写盘：图鉴变化等实时变化的事件触发时，延迟几百毫秒写盘，避免高频 I/O
    private float pendingSaveAt = -1f;
    private const float PendingSaveDelay = 0.3f;

    void Start()
    {
        // 订阅 GameStats 变化事件（图鉴发现新鱼、新掉宝时立即写盘）
        GameStats.OnCodexChanged -= OnCodexChanged;
        GameStats.OnCodexChanged += OnCodexChanged;

        StartCoroutine(LoadAfterDelay());
    }

    IEnumerator LoadAfterDelay()
    {
        // 先播种所有 DropItemCounterUI 的初始库存值
        DropItem.SeedAllInitialInLoadedScene();

        // 等待场景其他脚本初始化（DropItemCounterUI.Awake、鱼运动初始化等）
        yield return new WaitForSeconds(1.5f);

        Load();
    }

    void OnDestroy()
    {
        GameStats.OnCodexChanged -= OnCodexChanged;
    }

    void OnCodexChanged()
    {
        pendingSaveAt = Time.time + PendingSaveDelay;
    }

    void Update()
    {
        // 防抖立即写盘
        if (pendingSaveAt > 0f && Time.time >= pendingSaveAt)
        {
            pendingSaveAt = -1f;
            Save();
        }

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
        // GameStats 进度数据
        public List<string> seenFish = new List<string>();
        public List<string> droppedFish = new List<string>();
        public int totalCoralsPlaced;
        public int totalCoralsSold;
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

        // 3. 保存 GameStats 进度（图鉴、放置/卖出计数）
        data.seenFish = GameStats.GetSeenFishSnapshot();
        data.droppedFish = GameStats.GetDroppedFishSnapshot();
        data.totalCoralsPlaced = GameStats.TotalCoralsPlaced;
        data.totalCoralsSold = GameStats.TotalCoralsSold;

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(savePath, json);

        // 4. 同步 Steam 云存档（Steam 不可用时自动跳过）
        SteamCloudStorage.UploadFromLocal(savePath);

        Debug.Log($"[保存系统] 已保存到 {savePath}，物品类型 {data.itemCounts.Count}，" +
                  $"窗口=({data.windowState.posX:F0},{data.windowState.posY:F0}) 缩放={data.windowState.windowScale:F2}，" +
                  $"已见鱼 {data.seenFish.Count} 种，已掉宝 {data.droppedFish.Count} 种");
    }

    // ==================== 加载 ====================
    public void Load()
    {
        // 先尝试 Steam 云同步（云端更新则下载覆盖本地）
        SteamCloudStorage.SyncToLocalIfNewer(savePath);

        if (!File.Exists(savePath))
        {
            Debug.LogWarning("[保存系统] 找不到存档文件");
            return;
        }

        string json = File.ReadAllText(savePath);
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        if (data == null)
        {
            Debug.LogWarning("[保存系统] 存档文件解析失败");
            return;
        }

        // 1. 恢复掉落物品数量
        if (data.itemCounts != null)
        {
            foreach (ItemCountData item in data.itemCounts)
            {
                DropItem.SetCount(item.type, item.count);
            }
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

        // 3. 恢复 GameStats 进度
        GameStats.Restore(data.seenFish, data.droppedFish, data.totalCoralsPlaced, data.totalCoralsSold);

        int itemTypeCount = data.itemCounts?.Count ?? 0;

        Debug.Log($"[保存系统] 已加载 {savePath}，物品类型 {itemTypeCount}，" +
                  $"窗口=({data.windowState.posX:F0},{data.windowState.posY:F0}) 缩放={data.windowState.windowScale:F2}，" +
                  $"已见鱼 {data.seenFish?.Count ?? 0} 种，已掉宝 {data.droppedFish?.Count ?? 0} 种");

        // 通知所有订阅者存档已加载完成
        OnLoadComplete?.Invoke(itemTypeCount);
    }
}
