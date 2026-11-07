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

    /// <summary>单例实例（防止场景里挂多个 SaveSystem 并发读写）</summary>
    public static SaveSystem Instance { get; private set; }

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

    // 防重入标志：避免 Load() 执行中被快捷键再次触发
    private bool isLoading = false;

    void Awake()
    {
        // 单例保护：场景里不小心挂多个 SaveSystem 时，后创建的自动销毁
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // 订阅 GameStats 变化事件（图鉴发现新鱼、新掉宝时立即写盘）
        GameStats.OnCodexChanged -= OnCodexChanged;
        GameStats.OnCodexChanged += OnCodexChanged;

        Debug.Log($"[保存系统] savePath = {savePath}");
        StartCoroutine(LoadAfterDelay());
    }

    IEnumerator LoadAfterDelay()
    {
        // 先播种所有 DropItemCounterUI 的初始库存值
        DropItem.SeedAllInitialInLoadedScene();

        // 场景所有脚本的 Awake + Start 都是同步串行执行的，进第一帧前就全部完成了。
        // 之前硬等 1.5 秒完全没必要，改成等一帧即可——让首帧先渲染出来，
        // 存档加载从第二帧开始跑，用户几乎感知不到延迟。
        yield return null;

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
        try
        {
            SaveData data = new SaveData();

            // 1. 保存掉落物品数量（DropItem._counts 静态字典）
            var countsField = typeof(DropItem).GetField("_counts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (countsField == null)
            {
                Debug.LogError("[保存系统] 反射失败：找不到 DropItem._counts 字段（字段名可能被改了）");
            }
            else
            {
                var dict = countsField.GetValue(null) as Dictionary<string, int>;
                if (dict == null)
                {
                    Debug.LogError("[保存系统] 反射失败：DropItem._counts 返回 null");
                }
                else
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

            // 4. 同步 Steam 云存档（Steam 不可用时自动跳过，异常也不影响本地）
            try { SteamCloudStorage.UploadFromLocal(savePath); }
            catch (System.Exception e) { Debug.LogWarning($"[保存系统] Steam云存档上传异常：{e.Message}"); }

            Debug.Log($"[保存系统] ✅ 已保存到 {savePath}，物品类型 {data.itemCounts.Count}，" +
                      $"窗口=({data.windowState.posX:F0},{data.windowState.posY:F0}) 缩放={data.windowState.windowScale:F2}，" +
                      $"已见鱼 {data.seenFish.Count} 种，已掉宝 {data.droppedFish.Count} 种");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[保存系统] ❌ 保存失败：{e.GetType().Name} - {e.Message}\n{e.StackTrace}");
        }
    }

    // ==================== 加载 ====================
    public void Load()
    {
        // 防重入：Load() 执行中被快捷键再次触发则跳过
        if (isLoading)
        {
            Debug.LogWarning("[保存系统] Load() 正在执行中，跳过本次调用");
            return;
        }
        isLoading = true;

        try
        {
            // Step 0: Steam 云同步——异常绝不允许阻断本地存档
            try
            {
                SteamCloudStorage.SyncToLocalIfNewer(savePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[保存系统] Steam云同步异常，保留本地存档：{e.Message}");
            }

            // Step 1: 文件存在性检查
            if (!File.Exists(savePath))
            {
                Debug.LogWarning("[保存系统] ⚠️ 找不到存档文件（新游戏/首次运行）");
                OnLoadComplete?.Invoke(0);
                return;
            }

            FileInfo fi = new FileInfo(savePath);
            Debug.Log($"[保存系统] 📂 发现存档文件，大小 {fi.Length} 字节，修改时间 {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");

            // Step 2: 读取 JSON
            string json;
            try
            {
                json = File.ReadAllText(savePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[保存系统] ❌ 读取存档文件失败：{e.Message}");
                return;
            }

            // Step 3: 解析 JSON
            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[保存系统] ❌ 存档 JSON 解析失败：{e.Message}");
                return;
            }

            if (data == null)
            {
                Debug.LogError("[保存系统] ❌ 存档 JSON 解析返回 null（文件可能损坏或为空）");
                return;
            }

            Debug.Log($"[保存系统] 📋 JSON 解析成功：itemCounts={data.itemCounts?.Count ?? 0}, " +
                      $"seenFish={data.seenFish?.Count ?? 0}, droppedFish={data.droppedFish?.Count ?? 0}");

            // Step 4: 恢复掉落物品数量
            if (data.itemCounts != null && data.itemCounts.Count > 0)
            {
                int restored = 0;
                foreach (ItemCountData item in data.itemCounts)
                {
                    if (string.IsNullOrEmpty(item.type)) continue;
                    DropItem.SetCount(item.type, item.count);
                    restored++;
                }
                Debug.Log($"[保存系统] 🔢 已恢复 {restored} 种物品库存");
            }
            else
            {
                Debug.Log("[保存系统] 🔢 存档中没有 itemCounts（物品数量未保存）");
            }

            // Step 5: 恢复窗口状态
            DesktopWindowController dwc = FindObjectOfType<DesktopWindowController>();
            if (dwc != null)
            {
                if (data.windowState.posX != 0 || data.windowState.posY != 0)
                {
                    dwc.SetWindowPosition(data.windowState.posX, data.windowState.posY);
                    Debug.Log($"[保存系统] 🪟 窗口位置恢复为 ({data.windowState.posX:F0}, {data.windowState.posY:F0})");
                }
                else
                {
                    Debug.Log("[保存系统] 🪟 跳过恢复窗口位置（存档值为 0,0 或未设置）");
                }
                // 不恢复旧的 windowScale——用当前 DesktopWindowController 的默认值
                // 旧存档里的 0.73/0.785 等会让 orthoSize 变大、场景看起来缩小
                Debug.Log($"[保存系统] 🪟 跳过恢复 windowScale（存档值={data.windowState.windowScale:F2}），使用默认值 1.0");
            }
            else
            {
                Debug.Log("[保存系统] 🪟 场景中没找到 DesktopWindowController，跳过窗口恢复");
            }

            // Step 6: 恢复 GameStats 进度
            GameStats.Restore(data.seenFish, data.droppedFish, data.totalCoralsPlaced, data.totalCoralsSold);
            Debug.Log($"[保存系统] 🐟 图鉴进度恢复：已见 {data.seenFish?.Count ?? 0} 种，" +
                      $"已掉宝 {data.droppedFish?.Count ?? 0} 种，" +
                      $"放置珊瑚 {data.totalCoralsPlaced}，卖出 {data.totalCoralsSold}");

            int itemTypeCount = data.itemCounts?.Count ?? 0;

            Debug.Log($"[保存系统] ✅ 加载完成！物品类型 {itemTypeCount}，" +
                      $"已见鱼 {data.seenFish?.Count ?? 0} 种，已掉宝 {data.droppedFish?.Count ?? 0} 种");

            // 通知所有订阅者存档已加载完成
            OnLoadComplete?.Invoke(itemTypeCount);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[保存系统] ❌ 加载过程中出现未预期异常：{e.GetType().Name} - {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            isLoading = false;
        }
    }
}
