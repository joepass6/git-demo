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

    // ===== 放置物保存/恢复辅助 =====

    /// <summary>启动时场景里已有的 ObjectSpawner（策划预先摆好的，不用保存/恢复）</summary>
    static HashSet<ObjectSpawner> _initialSceneSpawners = new HashSet<ObjectSpawner>();
    static bool _initialSpawnersRecorded = false;

    /// <summary>
    /// 玩家运行时放置的 ObjectSpawner（DragAndPlaceSystem 成功放置时主动登记）。
    /// 🔑 这个集合不依赖 Unity 的 FindObjectsOfType 或 ObjectSpawner.AllSpawners——
    ///    退出阶段 Unity 会先 Destroy 所有物体，AllSpawners 在 OnDestroy 时被清空；
    ///    这个集合是"主动登记"的，任何时机都能拿到完整列表。
    /// </summary>
    static readonly HashSet<ObjectSpawner> _playerPlacedSpawners = new HashSet<ObjectSpawner>();

    /// <summary>预制体名称 → 预制体引用。启动时从所有 DragAndPlaceSystem 收集</summary>
    static readonly Dictionary<string, GameObject> _prefabRegistry = new Dictionary<string, GameObject>();
    static bool _prefabRegistryBuilt = false;

    /// <summary>Tag=coral 的名称常量（与 DragAndPlaceSystem/ObjectSpawner 上的 coral tag 一致）</summary>
    const string CoralTag = "coral";

    void Awake()
    {
        // 单例保护
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // 🔑 RecordInitialSpawners / BuildPrefabRegistry 都不在 Awake/Start 里调——
        // 那时候 ObjectSpawner.Awake 还没跑完，AllSpawners HashSet 是空的
    }

    void Start()
    {
        GameStats.OnCodexChanged -= OnCodexChanged;
        GameStats.OnCodexChanged += OnCodexChanged;
        Debug.Log($"[保存系统] savePath = {savePath}");
        StartCoroutine(LoadAfterDelay());
    }

    IEnumerator LoadAfterDelay()
    {
        DropItem.SeedAllInitialInLoadedScene();

        // 🔑 关键：等一帧，让场景里所有物体的 Awake + Start 全部执行完毕
        // 这样 ObjectSpawner.AllSpawners 和 FindObjectsOfType 才能拿到完整数据
        yield return null;

        // 所有脚本 Awake+Start 都跑完了，现在扫描才准确
        RecordInitialSpawners();
        BuildPrefabRegistry();

        Load();
    }

    /// <summary>
    /// 记录启动时场景里已有的 ObjectSpawner。
    /// 这些是策划预先摆好的，每次 Play 都会自动 Start 生成鱼，
    /// 不属于玩家运行时放置的东西，所以不用保存/恢复。
    /// </summary>
    void RecordInitialSpawners()
    {
        if (_initialSpawnersRecorded) return;
        _initialSpawnersRecorded = true;
        _initialSceneSpawners.Clear();
        // 优先用 ObjectSpawner.AllSpawners（静态 HashSet，最可靠）
        foreach (var sp in ObjectSpawner.AllSpawners)
        {
            if (sp != null) _initialSceneSpawners.Add(sp);
        }
        // 兜底：AllSpawners 理论上和 FindObjectsOfType 数量一致
        Debug.Log($"[保存系统] 📍 记录了 {_initialSceneSpawners.Count} 个初始场景 ObjectSpawner（策划预设，不保存）");
    }

    // ==================== 玩家珊瑚登记 API ====================

    /// <summary>
    /// DragAndPlaceSystem 成功放置珊瑚后调用，登记到玩家珊瑚集合。
    /// 这个集合不依赖 Unity 生命周期，退出阶段也能拿到完整列表。
    /// </summary>
    public static void RegisterPlacedSpawner(ObjectSpawner spawner)
    {
        if (spawner == null) return;
        _playerPlacedSpawners.Add(spawner);
    }

    /// <summary>
    /// 玩家放置的珊瑚被销毁时调用（OnDestroy 里反向登记），保持集合干净。
    /// 不在 ObjectSpawner 里自动调，避免引入新依赖；由 SaveSystem 扫描时顺便清理 null 引用。
    /// </summary>
    public static void UnregisterPlacedSpawner(ObjectSpawner spawner)
    {
        if (spawner == null) return;
        _playerPlacedSpawners.Remove(spawner);
    }

    /// <summary>清档/清场时重置玩家珊瑚集合（恢复初始状态）</summary>
    public static void ClearPlacedSpawnersRegistry()
    {
        _playerPlacedSpawners.Clear();
    }

    /// <summary>
    /// 获取所有当前活跃的玩家放置 ObjectSpawner（自动清理已销毁的 null 引用）。
    /// 优先用主动登记的集合（最可靠），再合并 AllSpawners 兜底（处理移动系统等其他放置路径）。
    /// </summary>
    static IEnumerable<ObjectSpawner> GetAllPlayerSpawners()
    {
        // 清理已被销毁的 null 引用
        _playerPlacedSpawners.RemoveWhere(s => s == null);

        // 用一个临时 HashSet 去重
        var result = new HashSet<ObjectSpawner>(_playerPlacedSpawners);

        // 兜底：合并 AllSpawners 里符合 coral tag 且不是初始场景的
        foreach (var sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            if (_initialSceneSpawners.Contains(sp)) continue;
            if (!HasTagInHierarchy(sp.gameObject, CoralTag)) continue;
            result.Add(sp);
        }
        return result;
    }

    /// <summary>
    /// 构建预制体名称 → 预制体引用的注册表。
    /// 🔥 终极方案：用 Resources.FindObjectsOfTypeAll 扫 Project 里所有 GameObject 资产，
    ///    不依赖任何脚本的 Awake/Start 执行时机，任何时机都能扫到完整数据！
    /// 过滤条件：带 coral tag + 有 ObjectSpawner 组件 + 不是场景实例（scene 无效）
    /// </summary>
    void BuildPrefabRegistry()
    {
        if (_prefabRegistryBuilt) return;
        _prefabRegistryBuilt = true;
        _prefabRegistry.Clear();

        // 🔥 暴力扫 Project 所有 GameObject 资产——绕开所有生命周期问题
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null) continue;
            if (go.scene.IsValid()) continue; // 跳过场景实例，只保留 Project 资产（预制体）
            if (!HasTagInHierarchy(go, CoralTag)) continue; // 没有 coral tag 的跳过
            if (go.GetComponent<ObjectSpawner>() == null) continue; // 没有 ObjectSpawner 组件的跳过

            string key = go.name.Replace("(Clone)", "").Trim();
            if (!_prefabRegistry.ContainsKey(key))
            {
                _prefabRegistry[key] = go;
            }
        }

        Debug.Log($"[保存系统] 📦 预制体注册表已构建（扫Project资产），共 {_prefabRegistry.Count} 种");
        foreach (var kv in _prefabRegistry)
        {
            Debug.Log($"[保存系统] 📦   key=\"{kv.Key}\"  →  {kv.Value.name}");
        }
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
        // 玩家运行时放置的珊瑚（ObjectSpawner）
        public List<PlacedObjectData> placedObjects = new List<PlacedObjectData>();
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

    /// <summary>
    /// 玩家运行时放置的一个珊瑚（ObjectSpawner）的序列化数据。
    /// 只存关键字段：预制体名（加载时从注册表找引用）、世界坐标（唯一恢复位置的信息）、
    /// bonus 修正（技能永久叠加值需要原样恢复）。
    /// </summary>
    [System.Serializable]
    public class PlacedObjectData
    {
        public string prefabName;
        public float posX, posY, posZ;
        public float bonusDestroyTime;
        public int bonusDropCount;
    }

    // ==================== 保存 ====================
    public void Save() { Save(null); }

    /// <summary>
    /// 保存。
    /// windowStateOverride 非空时用它作为窗口状态（清档时保留窗口位置/缩放），
    /// 为空时照旧从 DesktopWindowController 读取当前窗口状态。
    /// </summary>
    public void Save(WindowStateData windowStateOverride)
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

            // 2. 保存窗口状态（清档时由调用方传入保留值，避免把显示状态一起清掉）
            if (windowStateOverride != null)
            {
                data.windowState = windowStateOverride;
            }
            else
            {
                DesktopWindowController dwc = FindObjectOfType<DesktopWindowController>();
                if (dwc != null)
                {
                    data.windowState.posX = dwc.GetWindowPosition().x;
                    data.windowState.posY = dwc.GetWindowPosition().y;
                    data.windowState.windowScale = dwc.GetWindowScale();
                }
            }

            // 3. 保存 GameStats 进度（图鉴、放置/卖出计数）
            data.seenFish = GameStats.GetSeenFishSnapshot();
            data.droppedFish = GameStats.GetDroppedFishSnapshot();
            data.totalCoralsPlaced = GameStats.TotalCoralsPlaced;
            data.totalCoralsSold = GameStats.TotalCoralsSold;

            // 4. 保存玩家运行时放置的珊瑚（ObjectSpawner）
            //    🔑 GetAllPlayerSpawners 优先用玩家主动登记集合（_playerPlacedSpawners），
            //       兜底合并 AllSpawners。这样退出阶段也能拿到完整列表。
            RecordInitialSpawners(); // 兜底
            int scanPass = 0, scanFromRegistry = 0, scanFromAll = 0;
            var seen = new HashSet<ObjectSpawner>();
            // 先扫主动登记的（最可靠，退出阶段也有）
            foreach (var sp in _playerPlacedSpawners)
            {
                if (sp == null || !sp.enabled) continue;
                seen.Add(sp);
                scanFromRegistry++;
                var entry = new PlacedObjectData
                {
                    prefabName = sp.name.Replace("(Clone)", "").Trim(), // ← Trim 掉尾部空格
                    posX = sp.transform.position.x,
                    posY = sp.transform.position.y,
                    posZ = sp.transform.position.z,
                    bonusDestroyTime = sp.bonusDestroyTime,
                    bonusDropCount = sp.bonusDropCount,
                };
                data.placedObjects.Add(entry);
                scanPass++;
            }
            // 再扫 AllSpawners 兜底（处理没走主动登记路径的情况）
            foreach (var sp in ObjectSpawner.AllSpawners)
            {
                if (sp == null || !sp.enabled) continue;
                if (seen.Contains(sp)) continue;
                if (_initialSceneSpawners.Contains(sp)) continue;
                if (!HasTagInHierarchy(sp.gameObject, CoralTag)) continue;
                var entry = new PlacedObjectData
                {
                    prefabName = sp.name.Replace("(Clone)", "").Trim(),
                    posX = sp.transform.position.x,
                    posY = sp.transform.position.y,
                    posZ = sp.transform.position.z,
                    bonusDestroyTime = sp.bonusDestroyTime,
                    bonusDropCount = sp.bonusDropCount,
                };
                data.placedObjects.Add(entry);
                scanFromAll++;
                scanPass++;
            }
            Debug.Log($"[保存系统] 🪸 扫描完成: 主动登记 {scanFromRegistry} + AllSpawners兜底 {scanFromAll} = 玩家珊瑚 {scanPass} 个");

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(savePath, json);

            // 5. 同步 Steam 云存档（Steam 不可用时自动跳过，异常也不影响本地）
            try { SteamCloudStorage.UploadFromLocal(savePath); }
            catch (System.Exception e) { Debug.LogWarning($"[保存系统] Steam云存档上传异常：{e.Message}"); }

            Debug.Log($"[保存系统] ✅ 已保存到 {savePath}，物品类型 {data.itemCounts.Count}，" +
                      $"玩家珊瑚 {data.placedObjects.Count}，" +
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
                      $"placedObjects={data.placedObjects?.Count ?? 0}, " +
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

            // Step 5: 清场 + 恢复玩家放置的珊瑚
            ClearAndRestorePlacedObjects(data);

            // Step 6: 恢复窗口状态（位置 + 缩放）
            DesktopWindowController dwc = FindObjectOfType<DesktopWindowController>();
            if (dwc != null)
            {
                if (data.windowState != null)
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

                    float savedScale = data.windowState.windowScale;
                    if (savedScale > 0.01f)
                    {
                        dwc.SetWindowScale(savedScale);
                        Debug.Log($"[保存系统] 🪟 窗口缩放恢复为 {savedScale:F2}");
                    }
                    else
                    {
                        Debug.Log("[保存系统] 🪟 跳过恢复 windowScale（存档值无效），使用默认值 1.0");
                    }
                }
            }
            else
            {
                Debug.Log("[保存系统] 🪟 场景中没找到 DesktopWindowController，跳过窗口恢复");
            }

            // Step 7: 恢复 GameStats 进度
            GameStats.Restore(data.seenFish, data.droppedFish, data.totalCoralsPlaced, data.totalCoralsSold);
            Debug.Log($"[保存系统] 🐟 图鉴进度恢复：已见 {data.seenFish?.Count ?? 0} 种，" +
                      $"已掉宝 {data.droppedFish?.Count ?? 0} 种，" +
                      $"放置珊瑚 {data.totalCoralsPlaced}，卖出 {data.totalCoralsSold}");

            int itemTypeCount = data.itemCounts?.Count ?? 0;

            Debug.Log($"[保存系统] ✅ 加载完成！物品类型 {itemTypeCount}，" +
                      $"玩家珊瑚 {data.placedObjects?.Count ?? 0}，" +
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

    // ==================== 工具方法 ====================

    /// <summary>
    /// 递归检查物体自身及所有子物体是否带指定 Tag。
    /// 很多珊瑚预制体的根节点 Tag 是 Untagged，coral tag 在子物体上——必须递归查。
    /// </summary>
    static bool HasTagInHierarchy(GameObject go, string tag)
    {
        if (go == null) return false;
        if (go.CompareTag(tag)) return true;
        for (int i = 0; i < go.transform.childCount; i++)
        {
            if (HasTagInHierarchy(go.transform.GetChild(i).gameObject, tag)) return true;
        }
        return false;
    }

    // ==================== 放置物恢复 ====================

    /// <summary>
    /// 清场当前玩家放置的珊瑚 → 按存档数据重建 → 重算 RockCapacity。
    /// 
    /// 时序细节：Instantiate 后 Unity 只会同步执行 Awake，Start 要等到下一帧。
    /// 所以 bonusDestroyTime/bonusDropCount 在 Instantiate 之后、Start 之前写入，
    /// 等到 Start 执行 SpawnInitialObjects 时，鱼就已经吃到正确的永久修正了。
    /// </summary>
    void ClearAndRestorePlacedObjects(SaveData data)
    {
        // --- 清场：删掉当前场景里所有玩家放置的 ObjectSpawner（保留初始场景自带的）---
        RecordInitialSpawners();
        var toDestroy = new List<ObjectSpawner>();
        foreach (var sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            if (_initialSceneSpawners.Contains(sp)) continue;
            if (!HasTagInHierarchy(sp.gameObject, CoralTag)) continue;
            toDestroy.Add(sp);
        }
        foreach (var sp in toDestroy)
        {
            if (sp != null) Destroy(sp.gameObject);
        }
        if (toDestroy.Count > 0)
        {
            Debug.Log($"[保存系统] 🗑️ 清掉了 {toDestroy.Count} 个旧玩家珊瑚");
        }

        // --- 恢复：按存档逐个 Instantiate ---
        if (data.placedObjects == null || data.placedObjects.Count == 0)
        {
            Debug.Log("[保存系统] 🪸 存档中没有玩家珊瑚（跳过恢复）");
        }
        else
        {
            int restored = 0;
            int missing = 0;
            foreach (var entry in data.placedObjects)
            {
                string prefabName = entry.prefabName?.Trim() ?? "";
                if (string.IsNullOrEmpty(prefabName)) { missing++; continue; }

                GameObject prefab;
                if (!_prefabRegistry.TryGetValue(prefabName, out prefab) || prefab == null)
                {
                    Debug.LogWarning($"[保存系统] 🪸 找不到预制体 '{prefabName}'（注册表没收录？），跳过恢复");
                    missing++;
                    continue;
                }

                // Instantiate → 只触发 Awake，Start 要等下一帧
                Vector3 pos = new Vector3(entry.posX, entry.posY, entry.posZ);
                var obj = Instantiate(prefab, pos, Quaternion.identity);

                // Instantiate 出来的实例可能会自动带上 "(Clone)"，但不影响 ObjectSpawner 自身
                obj.name = entry.prefabName; // 还原成预制体原名（方便 Hierarchy 查看）

                // 关键：在 Start 执行之前把 bonus 写入 + 立即登记到玩家集合
                var spawner = obj.GetComponent<ObjectSpawner>();
                if (spawner != null)
                {
                    spawner.bonusDestroyTime = entry.bonusDestroyTime;
                    spawner.bonusDropCount = entry.bonusDropCount;
                    RegisterPlacedSpawner(spawner); // ← 恢复出来的珊瑚立刻登记，让玩家再存档能存到
                }

                restored++;
            }
            Debug.Log($"[保存系统] 🪸 恢复了 {restored} 个玩家珊瑚（预制体缺失 {missing} 个）");
        }

        // --- RockCapacity 重算 ---
        // 读档时所有石头还是场景初始状态（没被 RestoreAllMax 重置过），
        // 但我们新恢复的珊瑚已经 Instantiate 好了，Start 执行后会生成鱼——
        // 等鱼生成完再 RefreshAll 就能拿到正确的"已放置数量"。
        // 注意：这里调的是静态方法，会遍历所有 RockCapacity 组件。
        string placementTag = FindPlacementTag();
        if (!string.IsNullOrEmpty(placementTag))
        {
            RockCapacity.RefreshAll(placementTag, CoralTag);
        }
    }

    /// <summary>
    /// 从所有 DragAndPlaceSystem 里取 placementTag。
    /// 理论上所有 DragAndPlaceSystem 应该用同一个 placementTag，取第一个非空的就行。
    /// </summary>
    static string FindPlacementTag()
    {
        foreach (var dps in FindObjectsOfType<DragAndPlaceSystem>())
        {
            if (dps != null && !string.IsNullOrEmpty(dps.placementTag))
            {
                return dps.placementTag;
            }
        }
        return "Obstacle"; // 兜底
    }

    // ==================== 对外工具方法 ====================

    /// <summary>
    /// 清档时调用：删掉场景中所有玩家运行时放置的 ObjectSpawner（保留策划初始摆好的）。
    /// 供 ClearSaveSystem 等外部系统使用。
    /// </summary>
    public static void ClearPlacedCorals()
    {
        if (!_initialSpawnersRecorded) return;
        var toDelete = new List<ObjectSpawner>();
        foreach (var sp in _playerPlacedSpawners)
        {
            if (sp == null) continue;
            toDelete.Add(sp);
        }
        foreach (var sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            if (_initialSceneSpawners.Contains(sp)) continue;
            if (!HasTagInHierarchy(sp.gameObject, CoralTag)) continue;
            if (_playerPlacedSpawners.Contains(sp)) continue; // 已经在上面处理过
            toDelete.Add(sp);
        }
        foreach (var sp in toDelete)
        {
            if (sp != null) Destroy(sp.gameObject);
        }
        // 清空玩家登记集合（物体都销毁了，集合也该重置）
        ClearPlacedSpawnersRegistry();
        if (toDelete.Count > 0)
        {
            Debug.Log($"[保存系统] 🗑️ ClearPlacedCorals: 删掉了 {toDelete.Count} 个玩家放置的珊瑚 + 重置登记集合");
        }
    }
}
