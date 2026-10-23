using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 物体生成器
/// 在指定区域按概率生成多种物体，物体消失后自动按概率重新生成
/// 点击挂载物自身会弹出指定的UI面板，面板自动显示已生成物体的信息
/// </summary>
public class ObjectSpawner : MonoBehaviour
{
    // ===== 全局实例集合（供 RockCapacity 等遍历所有珊瑚生成器）=====
    static readonly HashSet<ObjectSpawner> s_all = new HashSet<ObjectSpawner>();
    public static System.Collections.Generic.IEnumerable<ObjectSpawner> AllSpawners => s_all;
    void Awake() { s_all.Add(this); }
    void OnDestroy() { s_all.Remove(this); }

    /// <summary>
    /// 生成物体的条目配置（预制件+概率权重）
    /// </summary>
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("种类编号（可自定义，代码里用这个编号调用）")]
        public int kindId;

        [Tooltip("要生成的物体预制件")]
        public GameObject prefab;

        [Tooltip("生成概率权重（数值越大，生成概率越高）")]
        public float weight = 1f;
    }

    [Header("生成配置")]
    [Tooltip("物体生成的位置（不设置则使用挂载物体位置）")]
    public Transform spawnPoint;

    [Tooltip("生成区域的BoxCollider2D（场景中固定位置的物体），物体只会在这个框内生成。不设置时会自动查找 Tag=SpawnArea 的物体")]
    public BoxCollider2D spawnArea;

    [Tooltip("自动查找的活动框 Tag 名称（用于场景中固定的生成区域）")]
    public string spawnAreaTag = "SpawnArea";

    [Tooltip("不同物体的预制件和概率配置")]
    public List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    [Tooltip("要生成的物体数量")]
    public int spawnCount = 4;

    [Header("珊瑚技能的永久修正（运行时由技能系统累加，不要手动填）")]
    [Tooltip("鱼存在时间的永久加成（秒）。每次在此珊瑚上放置有'存在时间±'技能的珊瑚时累加；之后新生成/复用的鱼都用 预制体基础值+此修正")]
    public float bonusDestroyTime = 0f;

    [Tooltip("每次掉落数量的永久加成。之后新生成/复用的鱼都用 预制体基础值+此修正（最小为0）")]
    public int bonusDropCount = 0;

    [Tooltip("物体消失后重新生成的检查间隔（秒）")]
    public float respawnInterval = 0.5f;

    [Tooltip("生成位置的随机偏移范围（未设置spawnArea时使用）")]
    public float spawnOffset = 0.5f;

    [Header("点击交互")]
    [Tooltip("点击挂载物时，通过Tag查找要打开的物体（在Project Settings → Tags里添加自定义Tag，比如 'DetailPanel'）")]
    public string uiPanelTag = "";

    [Tooltip("是否关闭其他挂载物打开的面板（只显示当前点击的那个）")]
    public bool closeOtherPanels = true;

    [Tooltip("页面弹出位置相对挂载物的偏移（屏幕像素），如(0,200)表示在挂载物上方200像素")]
    public Vector2 popupOffset = new Vector2(0f, 200f);

    [Header("点击选中效果")]
    [Tooltip("点击时珊瑚变暗的程度（0=不变暗，1=全黑，建议0.4左右）")]
    [Range(0f, 1f)] public float clickDarkenAmount = 0.45f;

    [Tooltip("变暗后恢复原色的时长（秒）")]
    public float clickFlashDuration = 0.25f;

    // 点击闪烁协程（防止重复启动）
    private Coroutine flashCoroutine;

    // 已找到的面板（缓存，避免每次都搜索）
    private GameObject cachedPanel;

    // 所有已打开的面板（用于关闭其他面板）
    private static List<GameObject> openedPanels = new List<GameObject>();

    // 当前打开页面的挂载物（供 FishInfoPanelButtons 读取）
    public static ObjectSpawner CurrentSpawner { get; private set; }

    // 页面是否正在跟随挂载物
    private static bool isFollowing = false;

    // 上一次激活的面板（用于关闭）
    private GameObject lastOpenedPanel;

    // 当前已生成的物体列表
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private float respawnTimer;

    // ==================== 生命周期 ====================

    private void Start()
    {
        Debug.Log("[ObjectSpawner] Start: " + gameObject.name + ", enabled=" + enabled + ", Collider=" + (GetComponent<Collider2D>() != null || GetComponentInChildren<Collider2D>() != null));

        // 预制体实例化后自动查找丢失的场景引用
        AutoFindMissingReferences();
        SpawnInitialObjects();
    }

    /// <summary>
    /// 通过Tag查找要打开的面板物体
    /// </summary>
    void AutoFindMissingReferences()
    {
        // 已经有缓存的有效引用
        if (cachedPanel != null && cachedPanel.activeInHierarchy)
        {
            return;
        }

        // 通过Tag查找
        if (!string.IsNullOrEmpty(uiPanelTag))
        {
            GameObject found = GameObject.FindGameObjectWithTag(uiPanelTag);
            if (found != null)
            {
                cachedPanel = found;
                Debug.Log("[ObjectSpawner] " + gameObject.name + ": 通过Tag '" + uiPanelTag + "' 找到面板: " + cachedPanel.name);
            }
            else
            {
                // 尝试查找inactive的物体
                GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj != null && obj.CompareTag(uiPanelTag) && obj.scene.IsValid())
                    {
                        cachedPanel = obj;
                        Debug.Log("[ObjectSpawner] " + gameObject.name + ": 通过Tag '" + uiPanelTag + "' 找到inactive面板: " + cachedPanel.name);
                        break;
                    }
                }
            }

            if (cachedPanel == null)
            {
                Debug.LogWarning("[ObjectSpawner] " + gameObject.name + ": 没找到Tag为 '" + uiPanelTag + "' 的物体");
            }
        }
        else
        {
            Debug.LogError("[ObjectSpawner] " + gameObject.name + ": 没有指定Tag！请在Inspector的 'UI Panel Tag' 字段填Tag名");
        }

        // 自动查找生成区域
        if (spawnArea == null)
        {
            GameObject areaObj = GameObject.FindGameObjectWithTag(spawnAreaTag);
            if (areaObj != null)
            {
                spawnArea = areaObj.GetComponent<BoxCollider2D>();
                if (spawnArea == null)
                {
                    Debug.LogWarning($"[ObjectSpawner] 找到 Tag={spawnAreaTag} 的物体 '{areaObj.name}'，但没有 BoxCollider2D 组件!");
                }
                else
                {
                    Debug.Log($"[ObjectSpawner] 自动绑定生成区域: {areaObj.name}");
                }
            }
        }
    }

    private void Update()
    {
        CheckAndRespawnObjects();
        CheckClick();
    }

    private void OnEnable()
    {
        Debug.Log("[ObjectSpawner] 已启用: " + gameObject.name + ", Collider=" + (GetComponent<Collider2D>() != null || GetComponentInChildren<Collider2D>() != null));
    }

    // ==================== 点击检测 ====================

    /// <summary>
    /// 检测鼠标点击是否在挂载物自身上
    /// 点击挂载物会直接显示 uiPanelToShow 指定的物体
    /// </summary>
    void CheckClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        if (UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            // 鼠标在UI上，跳过
            return;
        }

        if (cachedPanel == null)
        {
            AutoFindMissingReferences();
        }

        // 查找 Collider2D（支持子物体）
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) col = GetComponentInChildren<Collider2D>();
        if (col == null)
        {
            Debug.LogError("[ObjectSpawner] CheckClick: 没找到Collider2D（本体和子物体都没有）! " + gameObject.name);
            return;
        }

        if (Camera.main == null)
        {
            Debug.LogError("[ObjectSpawner] CheckClick: Camera.main 为 null!");
            return;
        }

        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 mousePos2D = new Vector2(worldPos.x, worldPos.y);

        if (col.OverlapPoint(mousePos2D))
        {
            Debug.Log("[ObjectSpawner] 点击命中: " + gameObject.name);

            // 点击选中效果：变暗一下再恢复
            ShowClickFlash();

            if (cachedPanel == null)
            {
                Debug.LogError("[ObjectSpawner] 点击命中了但cachedPanel为null！请检查: 1) uiPanelTag字段是否填了 2) 场景面板是否有对应Tag");
                return;
            }

            OpenPanel();
        }
    }

    /// <summary>
    /// 打开指定的面板物体
    /// </summary>
    public void OpenPanel()
    {
        if (cachedPanel == null)
        {
            Debug.LogError("[ObjectSpawner] OpenPanel: cachedPanel为null!");
            return;
        }

        Debug.Log($"[ObjectSpawner] OpenPanel: 打开面板 {cachedPanel.name}, spawner={gameObject.name}");

        // 记录当前挂载物（供 FishInfoPanelButtons 等读取）
        CurrentSpawner = this;

        // 关闭其他面板
        if (closeOtherPanels)
        {
            CloseAllPanelsExcept(cachedPanel);
        }

        // 激活面板
        cachedPanel.SetActive(true);

        // 如果有 CanvasGroup，恢复显示状态
        CanvasGroup cg = cachedPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        // ====== 强制确保 FishInfoPanelButtons 存在并正确初始化 ======
        FishInfoPanelButtons buttons = EnsureFishInfoPanelButtons();
        if (buttons != null)
        {
            buttons.OnPanelShown(this);
            Debug.Log($"[ObjectSpawner] FishInfoPanelButtons 已绑定, followSpawner={buttons.followSpawner}");
        }

        // ====== 立即设置面板位置 ======
        SetPanelPositionToSpawner();

        // 通知 FishTagScanner 重新扫描更新
        FishTagScanner scanner = FindObjectOfType<FishTagScanner>();
        if (scanner != null)
        {
            scanner.ManualScan();
        }

        // 记录已打开的面板
        if (!openedPanels.Contains(cachedPanel))
        {
            openedPanels.Add(cachedPanel);
        }
        lastOpenedPanel = cachedPanel;

        Debug.Log("[ObjectSpawner] 面板打开完成: " + cachedPanel.name);
    }

    /// <summary>
    /// 确保面板上有 FishInfoPanelButtons 组件，没有则自动添加
    /// </summary>
    FishInfoPanelButtons EnsureFishInfoPanelButtons()
    {
        FishInfoPanelButtons buttons = cachedPanel.GetComponent<FishInfoPanelButtons>();
        if (buttons == null) buttons = cachedPanel.GetComponentInChildren<FishInfoPanelButtons>(true);
        if (buttons == null) buttons = cachedPanel.GetComponentInParent<FishInfoPanelButtons>();

        if (buttons == null)
        {
            Debug.LogWarning("[ObjectSpawner] 面板上没找到 FishInfoPanelButtons，自动添加");
            buttons = cachedPanel.AddComponent<FishInfoPanelButtons>();
        }

        Debug.Log($"[ObjectSpawner] FishInfoPanelButtons 已就绪: followSpawner={buttons.followSpawner}");
        return buttons;
    }

    /// <summary>
    /// 将面板定位到挂载物位置
    /// </summary>
    void SetPanelPositionToSpawner()
    {
        RectTransform rt = cachedPanel.GetComponent<RectTransform>();
        if (rt == null) return;

        // 找到根RectTransform（Canvas的直接子物体）
        RectTransform rootRect = rt;
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.transform != rt.parent)
        {
            Transform t = rt;
            while (t.parent != null && t.parent != canvas.transform) t = t.parent;
            rootRect = t as RectTransform;
        }

        // 屏幕坐标 → Canvas局部坐标 → anchoredPosition
        // 用渲染画布的相机做世界→屏幕投影，和画布保持一致，缩放时位置才不会偏
        Camera projCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            ? canvas.worldCamera : Camera.main;
        Vector2 screenPos = projCam.WorldToScreenPoint(transform.position);
        Vector2 targetScreenPos = screenPos + popupOffset;

        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

        Vector2 localPos;
        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas != null ? canvas.transform as RectTransform : rootRect,
            targetScreenPos,
            cam,
            out localPos);

        if (ok && rootRect != null)
        {
            rootRect.anchoredPosition = localPos;
            Debug.Log($"[ObjectSpawner] 面板定位成功: anchoredPosition={localPos}");
        }
        else
        {
            Debug.LogWarning($"[ObjectSpawner] ScreenPointToLocalPoint失败");
        }
    }

    /// <summary>
    /// 点击选中效果：把珊瑚所有图片变暗一下，clickFlashDuration 秒后恢复
    /// </summary>
    void ShowClickFlash()
    {
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(ClickFlashRoutine());
    }

    System.Collections.IEnumerator ClickFlashRoutine()
    {
        // 收集本珊瑚的所有 SpriteRenderer（含子物体和自身）
        List<SpriteRenderer> srs = new List<SpriteRenderer>(GetComponentsInChildren<SpriteRenderer>(true));
        SpriteRenderer self = GetComponent<SpriteRenderer>();
        if (self != null && !srs.Contains(self)) srs.Insert(0, self);

        // 兜底：珊瑚如果是UI物体则收集 Image
        List<Image> imgs = new List<Image>(GetComponentsInChildren<Image>(true));

        // 保存原色并变暗（向黑色插值 clickDarkenAmount 比例）
        List<Color> origSrs = new List<Color>(srs.Count);
        for (int i = 0; i < srs.Count; i++)
        {
            if (srs[i] == null) { origSrs.Add(Color.white); continue; }
            origSrs.Add(srs[i].color);
            srs[i].color = Color.Lerp(origSrs[i], Color.black, clickDarkenAmount);
        }
        List<Color> origImgs = new List<Color>(imgs.Count);
        for (int i = 0; i < imgs.Count; i++)
        {
            if (imgs[i] == null) { origImgs.Add(Color.white); continue; }
            origImgs.Add(imgs[i].color);
            imgs[i].color = Color.Lerp(origImgs[i], Color.black, clickDarkenAmount);
        }

        yield return new WaitForSeconds(clickFlashDuration);

        // 恢复原色
        for (int i = 0; i < srs.Count; i++)
            if (srs[i] != null) srs[i].color = origSrs[i];
        for (int i = 0; i < imgs.Count; i++)
            if (imgs[i] != null) imgs[i].color = origImgs[i];

        flashCoroutine = null;
    }

    /// <summary>
    /// 关闭指定的面板物体
    /// </summary>
    public void ClosePanel()
    {
        if (cachedPanel == null) return;

        cachedPanel.SetActive(false);

        CanvasGroup cg = cachedPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        openedPanels.Remove(cachedPanel);
        Debug.Log("[ObjectSpawner] 关闭面板: " + cachedPanel.name);
    }

    /// <summary>
    /// 关闭所有面板（静态方法，可从外部调用）
    /// </summary>
    public static void CloseAllPanels()
    {
        foreach (var panel in openedPanels)
        {
            if (panel != null && panel.activeSelf)
            {
                panel.SetActive(false);
                CanvasGroup cg = panel.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
            }
        }
        openedPanels.Clear();
    }

    /// <summary>
    /// 关闭除了指定面板之外的所有面板
    /// </summary>
    static void CloseAllPanelsExcept(GameObject keepPanel)
    {
        List<GameObject> toRemove = new List<GameObject>();
        foreach (var panel in openedPanels)
        {
            if (panel != null && panel != keepPanel && panel.activeSelf)
            {
                panel.SetActive(false);
                CanvasGroup cg = panel.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
                toRemove.Add(panel);
            }
        }
        foreach (var p in toRemove) openedPanels.Remove(p);
    }

    /// <summary>
    /// 获取当前挂载物生成的所有活着的鱼
    /// </summary>
    public List<鱼运动> GetSpawnedFish()
    {
        List<鱼运动> fishList = new List<鱼运动>();
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            if (spawnedObjects[i] == null || !spawnedObjects[i].activeInHierarchy)
            {
                spawnedObjects.RemoveAt(i);
                continue;
            }
            鱼运动 fish = spawnedObjects[i].GetComponent<鱼运动>();
            if (fish != null) fishList.Add(fish);
        }
        return fishList;
    }

    // ==================== 生成逻辑 ====================

    /// <summary>
    /// 初始生成物体
    /// </summary>
    void SpawnInitialObjects()
    {
        if (spawnEntries == null || spawnEntries.Count == 0) return;

        for (int i = 0; i < spawnCount; i++)
        {
            SpawnOneObject();
        }
    }

    /// <summary>
    /// 生成一个物体
    /// </summary>
    /// <param name="forceKindIndex">>=0 时强制生成第 N 种；-1（默认）时按概率随机生成</param>
    void SpawnOneObject(int forceKindIndex = -1)
    {
        if (spawnEntries == null || spawnEntries.Count == 0) return;

        // 根据 forceKindIndex 决定是指定种类还是随机
        GameObject prefab;
        int kindIndex;
        if (forceKindIndex >= 0 && forceKindIndex < spawnEntries.Count && spawnEntries[forceKindIndex].prefab != null)
        {
            prefab = spawnEntries[forceKindIndex].prefab;
            kindIndex = forceKindIndex;
        }
        else
        {
            if (!GetRandomPrefab(out prefab, out kindIndex)) return;
        }

        // 全局鱼数量上限检查：要生成的是鱼、且场上鱼已满时不再生成
        // 否则多个珊瑚互相抢名额：上限杀最老的鱼 → 生成器立刻补 → 再杀再补 = 快速瞎刷鱼
        if (prefab.GetComponent<鱼运动>() != null && 鱼运动.AliveFishCount >= 鱼运动.maxFishCount) return;

        Vector3 spawnPosition;

        if (spawnArea != null)
        {
            // 在BoxCollider2D框内随机位置生成
            Vector2 center = spawnArea.bounds.center;
            Vector2 size = spawnArea.bounds.size;
            spawnPosition = new Vector3(
                Random.Range(center.x - size.x / 2f, center.x + size.x / 2f),
                Random.Range(center.y - size.y / 2f, center.y + size.y / 2f),
                0f
            );
        }
        else
        {
            // 使用基础位置+随机偏移
            Vector3 basePos = spawnPoint != null ? spawnPoint.position : transform.position;
            spawnPosition = basePos + new Vector3(
                Random.Range(-spawnOffset, spawnOffset),
                Random.Range(-spawnOffset, spawnOffset),
                0f
            );
        }

        // 鱼：走对象池（复用隐藏的鱼，省掉反复Instantiate/Destroy的开销）；其他物体照旧直接实例化
        // 鱼的预制体基础值（每个 prefab 共享），技能修正按 "基础值 + bonus" 重新算，
        // 这样池里复用过的旧鱼也不会带着上一次的旧修正
        鱼运动 fishComp = prefab.GetComponent<鱼运动>();
        float baseDestroyTime = fishComp != null ? fishComp.destroyTime : 0f;
        int baseDropCount = fishComp != null ? fishComp.dropCount : 0;

        GameObject obj = fishComp != null
            ? 鱼运动.SpawnFromPool(prefab, spawnPosition, Quaternion.identity, fish =>
            {
                // destroyTime=0 在鱼运动里表示"永不销毁"，这种鱼不吃寿命修正；
                // 有寿命的鱼扣到负数时压到 1 秒下限（快速消失），不能给 0 否则会变永生
                fish.destroyTime = baseDestroyTime <= 0f ? 0f : Mathf.Max(1f, baseDestroyTime + bonusDestroyTime);
                fish.dropCount = Mathf.Max(0, baseDropCount + bonusDropCount);
            }).gameObject
            : Instantiate(prefab, spawnPosition, Quaternion.identity);
        obj.transform.SetParent(null);  // 确保鱼成为独立物体，不跟随生成器移动

        // 把编号写到名字里，方便在 Hierarchy 里辨认
        string cleanName = prefab.name.Replace("(Clone)", "").Trim();
        obj.name = $"{cleanName} [{spawnEntries[kindIndex].kindId}]";

        spawnedObjects.Add(obj);
    }

    /// <summary>
    /// 根据权重随机选择一个预制件，同时返回该条目在 spawnEntries 中的索引（即种类编号）
    /// </summary>
    /// <returns>是否成功选中（有可用 prefab）</returns>
    bool GetRandomPrefab(out GameObject prefab, out int kindIndex)
    {
        float totalWeight = 0f;
        for (int i = 0; i < spawnEntries.Count; i++)
        {
            if (spawnEntries[i].prefab != null) totalWeight += spawnEntries[i].weight;
        }

        prefab = null;
        kindIndex = -1;

        if (totalWeight <= 0f) return false;

        float randomValue = Random.value * totalWeight;
        float currentWeight = 0f;

        for (int i = 0; i < spawnEntries.Count; i++)
        {
            if (spawnEntries[i].prefab != null)
            {
                currentWeight += spawnEntries[i].weight;
                if (randomValue <= currentWeight)
                {
                    prefab = spawnEntries[i].prefab;
                    kindIndex = i;
                    return true;
                }
            }
        }

        // 兜底返回第一个可用预制件
        for (int i = 0; i < spawnEntries.Count; i++)
        {
            if (spawnEntries[i].prefab != null)
            {
                prefab = spawnEntries[i].prefab;
                kindIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查物体是否消失，消失则按概率重新生成
    /// </summary>
    void CheckAndRespawnObjects()
    {
        respawnTimer += Time.deltaTime;
        if (respawnTimer < respawnInterval) return;
        respawnTimer = 0f;

        // 清理已销毁/已回池（隐藏）的物体引用
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            if (!spawnedObjects[i] || !spawnedObjects[i].activeInHierarchy) spawnedObjects.RemoveAt(i);
        }

        // 数量不足则补充生成
        int missingCount = spawnCount - spawnedObjects.Count;
        for (int i = 0; i < missingCount; i++)
        {
            SpawnOneObject();
        }
    }

    // ==================== 公共API ====================

    /// <summary>获取配置的所有 SpawnEntry 列表</summary>
    public List<SpawnEntry> GetSpawnEntries() => spawnEntries;

    // ---- 按列表索引（第几种）----

    /// <summary>按列表索引获取条目（0 = 第一种），超出范围返回 null</summary>
    public SpawnEntry GetSpawnEntryByIndex(int index)
    {
        if (spawnEntries == null || index < 0 || index >= spawnEntries.Count) return null;
        return spawnEntries[index];
    }

    /// <summary>按列表索引获取该种类所有活着的生成物体</summary>
    public List<GameObject> GetSpawnedByIndex(int index)
    {
        List<GameObject> result = new List<GameObject>();
        var entry = GetSpawnEntryByIndex(index);
        if (entry == null || entry.prefab == null) return result;
        string needle = $"[{entry.kindId}]";
        foreach (var obj in spawnedObjects)
            if (obj != null && obj.activeInHierarchy && obj.name.Contains(needle))
                result.Add(obj);
        return result;
    }

    // ---- 按自定义 kindId（你在 Inspector 里填的编号）----

    /// <summary>根据自定义 kindId 查找条目（返回第一个匹配的；找不到返回 null）</summary>
    public SpawnEntry GetSpawnEntryById(int kindId)
    {
        if (spawnEntries == null) return null;
        for (int i = 0; i < spawnEntries.Count; i++)
            if (spawnEntries[i].kindId == kindId) return spawnEntries[i];
        return null;
    }

    /// <summary>按自定义 kindId 获取该种类所有活着的生成物体</summary>
    public List<GameObject> GetSpawnedById(int kindId)
    {
        List<GameObject> result = new List<GameObject>();
        var entry = GetSpawnEntryById(kindId);
        if (entry == null || entry.prefab == null) return result;
        string needle = $"[{kindId}]";
        foreach (var obj in spawnedObjects)
            if (obj != null && obj.activeInHierarchy && obj.name.Contains(needle))
                result.Add(obj);
        return result;
    }

    // ==================== 编辑器可视化 ====================

    private void OnDrawGizmos()
    {
        if (spawnArea != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(spawnArea.bounds.center, spawnArea.bounds.size);
        }
    }
}