using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using SkillSystem;

/// <summary>
/// 拖拽放置系统（不依赖UI事件接口，直接用Input检测）
/// 从UI拖拽生成物品到场景中，放置成功后扣除指定文本的数量
/// 放置条件：珊瑚文本数值 >= 消耗文本数值
/// </summary>
public class DragAndPlaceSystem : MonoBehaviour
{
    /// <summary>珊瑚放置成功时触发（参数=刚实例化的真实珊瑚物体，教程用）</summary>
    public static event System.Action<GameObject> OnCoralPlaced;

    /// <summary>
    /// 供外部系统（如搬家 MoveSystem）触发放置成功事件的代理方法。
    /// C# event 的 Invoke 只能在声明类内部调用，外部类用这个。
    /// </summary>
    public static void 触发放置事件(GameObject obj)
    {
        OnCoralPlaced?.Invoke(obj);
    }

    [Header("设置")]
    [Tooltip("要摆放的物品预制体")]
    public GameObject prefabToSpawn;

    [Tooltip("可放置的图层（射线检测用）")]
    public LayerMask placementLayer;

    [Header("放置检测（和搬家功能一样，用物体体积）")]
    [Tooltip("物体的体积碰到带此Tag的Collider2D才能放置（例如Obstacle地面）")]
    public string placementTag = "Obstacle";

    [Header("文本设置")]
    [Tooltip("珊瑚数量文本（放置后数量会减少）")]
    public TextMeshProUGUI coralCountText;

    [Tooltip("消耗数量文本（珊瑚数量必须 >= 此数值才能放置）")]
    public TextMeshProUGUI costText;

    [Header("提示")]
    [Tooltip("数量不足时变色的Image组件")]
    public Image targetImage;

    [Tooltip("数量不足时变色的TextMeshProUGUI组件（可选）")]
    public TextMeshProUGUI targetText;

    [Tooltip("数量不足时Image变成的颜色")]
    public Color insufficientColor = Color.red;

    [Tooltip("数量不足时Text变成的颜色")]
    public Color insufficientTextColor = Color.red;

    [Header("图层设置")]
    [Tooltip("拖入物体的基础排序值（数值越大越靠前）")]
    public int baseSortingOrder = 100;

    [Tooltip("落位吸附的额外 Y 偏移（世界单位）：正值往上抬，用于抵消 sprite 透明边距")]
    public float baseSnapOffsetY = 0f;

    // 私有字段
    private Color originalImageColor;
    private Color originalTextColor;
    private GameObject currentDragObject;
    private Camera mainCam;
    private bool canPlace = true;
    private bool isDragging = false;
    /// <summary>是否有珊瑚正在被拖拽/携带（携带期间点击优先用于放置，不拾取掉落物）</summary>
    public static bool IsCarryingObject { get; private set; }
    private GameObject currentHoverRock; // 当前预览体正悬停的石头（用于切换 SetVisible/SetOutline）

    // ===== 摇晃动画相关 =====
    private Vector3 shakeOriginalLocalPos; // 摇晃前的原始本地坐标
    private bool shakeInited = false;      // 是否已缓存原始位置
    private Coroutine rejectShakeCo;       // 被拒绝时的一次性摇晃协程
    [Tooltip("条件不足、被玩家尝试拖动时的摇晃振幅（UI 像素）")]
    public float shakeAmplitude = 8f;
    [Tooltip("条件不足、被玩家尝试拖动时的摇晃时长（秒）")]
    public float shakeDuration = 0.35f;
    [Tooltip("条件不足时变暗程度（0=不变，1=全黑，建议0.55左右）")]
    [Range(0f, 1f)] public float darkenAmount = 0.55f;

    // ===== 桌面窗口拖动排除（DesktopWindowController 调用）=====
    // 图标可能没开 Raycast Target，EventSystem 射线扫不到，所以这里直接登记实例供窗口脚本查询
    static readonly System.Collections.Generic.List<DragAndPlaceSystem> all = new System.Collections.Generic.List<DragAndPlaceSystem>();
    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable()
    {
        all.Remove(this);
        // 拖动中被面板关闭/禁用时 Update 不再执行，预览体会永久残留，这里兜底销毁
        if (currentDragObject != null)
        {
            Destroy(currentDragObject);
            currentDragObject = null;
        }
        isDragging = false;
        IsCarryingObject = false;
    }

    /// <summary>鼠标是否按在任意珊瑚拖拽图标上（按图标矩形检测，不依赖射线）</summary>
    public static bool IsPointerOverAnyIcon(Vector2 screenPos)
    {
        for (int i = 0; i < all.Count; i++)
        {
            var d = all[i];
            if (d == null || !d.enabled || !d.gameObject.activeInHierarchy) continue;
            var rt = d.GetComponent<RectTransform>();
            if (rt == null) continue;
            var canvas = d.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam)) return true;
        }
        return false;
    }

    void Start()
    {
        Debug.Log("[DragAndPlace] Start 启动!");

        // 自动给所有带 placementTag 的物体补 RockCapacity 组件
        RockCapacity.EnsureAll(placementTag);
        RockCapacity.RefreshAll(placementTag);

        // 初始化目标Image和原始颜色
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (targetImage != null) originalImageColor = targetImage.color;

        // 初始化目标Text和原始颜色
        if (targetText != null) originalTextColor = targetText.color;

        // 缓存摇晃前的原始本地坐标
        shakeOriginalLocalPos = transform.localPosition;
        shakeInited = true;

        mainCam = Camera.main;
        Debug.Log($"[DragAndPlace] targetImage={targetImage}, targetText={targetText}, mainCam={mainCam}, prefab={prefabToSpawn}");
    }

    void Update()
    {
        // ===== 检查可放置状态 =====
        CheckCanPlace();

        // ===== 每帧应用颜色（变暗/恢复）=====
        ApplyDisabledVisual();

        // ===== 拖拽逻辑 =====
        if (isDragging && currentDragObject != null)
        {
            // 拖拽中：预览物体跟随鼠标
            Vector3 worldPos = DesktopWindowController.ScreenToWorldPointCorrect(mainCam, Input.mousePosition);
            currentDragObject.transform.position = new Vector3(worldPos.x, worldPos.y, 0);

            // ===== 每帧检测悬停的石头，通知 RockCapacity 显示 UI + 描边 =====
            UpdateHoverRock();

            // 鼠标松开 → 尝试放置
            if (Input.GetMouseButtonUp(0))
            {
                EndDrag();
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            // 鼠标点中了这个UI物体
            if (!IsPointerOverThisUI()) return;

            if (!canPlace)
            {
                // 数量不够 → 拒绝 + 短暂摇晃反馈
                if (shakeInited)
                {
                    if (rejectShakeCo != null) StopCoroutine(rejectShakeCo);
                    rejectShakeCo = StartCoroutine(RejectShakeRoutine());
                }
                // 同时播拒绝音效
                if (AudioManager.Instance != null)
                    AudioManager.Instance.播放拒绝音效();
                return;
            }

            // 数量够 → 正常开始拖拽
            BeginDrag();
        }
    }

    /// <summary>
    /// 数量不足时图标变暗；足够时恢复正常颜色（持续生效）
    /// </summary>
    void ApplyDisabledVisual()
    {
        if (targetImage != null)
        {
            targetImage.color = canPlace ? originalImageColor
                : Color.Lerp(originalImageColor, Color.black, darkenAmount);
        }
        if (targetText != null)
        {
            targetText.color = canPlace ? originalTextColor
                : Color.Lerp(originalTextColor, Color.black, darkenAmount);
        }
    }

    /// <summary>
    /// 玩家硬拖条件不足的图标 → 短促摇晃拒绝反馈
    /// 用一个相位来回摆 shakeDuration 秒，结束后归位
    /// </summary>
    System.Collections.IEnumerator RejectShakeRoutine()
    {
        Vector3 origin = shakeOriginalLocalPos;
        float elapsed = 0f;
        // 用两个叠加 Sin（相位相反）制造"猛地一摆回来"的拒绝感
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shakeDuration; // 0→1
            float k = Mathf.Sin(t * Mathf.PI * 6f); // 快频率来回
            float dx = k * shakeAmplitude;
            transform.localPosition = origin + new Vector3(dx, 0, 0);
            yield return null;
        }
        transform.localPosition = origin;
        rejectShakeCo = null;
    }

    /// <summary>
    /// 预览体每帧收集所有“底座贴合”的石头 → 选 Y 最靠下的"主石头"显示 UI + 描边。
    /// 🔑 和 EndDrag 用完全相同的 FindSupportRocks（珊瑚底座探针 + 石头顶面判定），
    ///    保证拖拽过程中"亮的那块"就是松手后"落下去的那块"。
    /// </summary>
    void UpdateHoverRock()
    {
        List<GameObject> allHit = FindSupportRocks(currentDragObject, placementTag, SkillCaster.RockTouchTolerance);

        // 统一选最靠下那块当主石头（和 EndDrag/RefreshAll 完全一致）
        GameObject newRock = FindPrimaryRock(allHit);

        // 预览体吸附到石头顶面：所见即所得，松手后的落点就是现在看到的位置
        if (newRock != null && currentDragObject != null)
        {
            Vector3 p = currentDragObject.transform.position;
            p.y += SnapDeltaY(currentDragObject, newRock);
            currentDragObject.transform.position = p;
        }

        // 切换了石头
        if (newRock != currentHoverRock)
        {
            if (currentHoverRock != null)
            {
                RockCapacity.SetVisible(currentHoverRock, false);
                RockCapacity.SetOutline(currentHoverRock, false);
            }
            if (newRock != null)
            {
                RockCapacity.SetVisible(newRock, true);
                RockCapacity.SetOutline(newRock, true);
            }
            currentHoverRock = newRock;
        }
    }

    // ==================== 放置状态检查 ====================

    void CheckCanPlace()
    {
        // 未配置文本字段时默认允许放置
        if (coralCountText == null || costText == null)
        {
            canPlace = true;
            return;
        }

        float coral = GetCoralCount();
        float cost = GetCost();
        bool newCanPlace = coral >= cost;

        if (newCanPlace != canPlace)
        {
            Debug.Log($"[DragAndPlace] 珊瑚={coral}, 消耗={cost}, 可放置={newCanPlace}");
            canPlace = newCanPlace;
        }
    }

    float GetCoralCount() { return ParseNumber(coralCountText); }
    float GetCost() { return ParseNumber(costText); }

    float ParseNumber(TextMeshProUGUI text)
    {
        if (text == null) return 0f;
        float value;
        if (float.TryParse(text.text, out value)) return value;
        return 0f;
    }

    /// <summary>
    /// 设置颜色（拖动中数量不足时变成指定颜色）
    /// </summary>
    void UpdateColor()
    {
        if (targetImage != null)
        {
            targetImage.color = canPlace ? originalImageColor : insufficientColor;
        }
        if (targetText != null)
        {
            targetText.color = canPlace ? originalTextColor : insufficientTextColor;
        }
    }

    /// <summary>
    /// 恢复原本颜色
    /// </summary>
    void RestoreColor()
    {
        if (targetImage != null)
        {
            targetImage.color = originalImageColor;
        }
        if (targetText != null)
        {
            targetText.color = originalTextColor;
        }
    }

    // ==================== 检测点击 ====================

    /// <summary>
    /// 检测鼠标是否点中了这个UI物体
    /// </summary>
    bool IsPointerOverThisUI()
    {
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null)
        {
            Debug.LogError("[DragAndPlace] 没有 RectTransform 组件!");
            return false;
        }

        // ===== 方法1：直接矩形检测 =====
        // 注意：非 Overlay 画布（ScreenSpaceCamera/WorldSpace）必须传画布的 worldCamera，
        // 传 null 会把屏幕像素当 Overlay 坐标换算，结果差几个数量级（日志里出现过 175136 这种数字）
        Vector2 localPoint;
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        Camera uiCam = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? parentCanvas.worldCamera
            : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt, Input.mousePosition, uiCam, out localPoint);
        bool directHit = rt.rect.Contains(localPoint);

        Debug.Log($"[DragAndPlace] 点击检测: 物体名={gameObject.name}, 鼠标本地坐标={localPoint}, 矩形范围={rt.rect}, 命中={directHit}");

        if (directHit) return true;

        // ===== 方法2：EventSystem 检测（备用） =====
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            var raycastResults = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            var eventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            eventData.position = Input.mousePosition;
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(eventData, raycastResults);

            Debug.Log($"[DragAndPlace] EventSystem命中 {raycastResults.Count} 个物体:");
            foreach (var result in raycastResults)
            {
                Debug.Log($"  - {result.gameObject.name} (tag={result.gameObject.tag})");
                if (result.gameObject == this.gameObject)
                {
                    return true;
                }
            }
        }
        else
        {
            Debug.LogWarning("[DragAndPlace] EventSystem 不存在! 请在场景创建: 右键→UI→Event System");
        }

        return false;
    }

    // ==================== 拖拽事件 ====================

    void BeginDrag()
    {
        if (prefabToSpawn == null)
        {
            Debug.LogError("[DragAndPlace] prefabToSpawn 未配置!");
            return;
        }

        Debug.Log("[DragAndPlace] BeginDrag 开始拖拽!");
        isDragging = true;
        IsCarryingObject = true;

        // 生成预览物体（仅用于显示，禁用所有生成/运动逻辑）
        currentDragObject = Instantiate(prefabToSpawn);
        Debug.Log($"[DragAndPlace] 生成预览: {currentDragObject.name}");

        // 禁用物理
        Rigidbody2D rb = currentDragObject.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.simulated = false;
        }

        // 禁用可能生成鱼/掉落的组件，避免拖拽过程中产生副作用
        DisableSpawnComponents(currentDragObject);

        // 放到鼠标位置
        Vector3 worldPos = DesktopWindowController.ScreenToWorldPointCorrect(mainCam, Input.mousePosition);
        currentDragObject.transform.position = new Vector3(worldPos.x, worldPos.y, 0);
    }

    /// <summary>
    /// 禁用预览物体上所有可能生成鱼或产生副作用的脚本
    /// （只保留显示用的组件如SpriteRenderer/Image）
    /// </summary>
    void DisableSpawnComponents(GameObject obj)
    {
        // 禁用 ObjectSpawner（避免Start时生成鱼）
        ObjectSpawner spawner = obj.GetComponent<ObjectSpawner>();
        if (spawner != null) spawner.enabled = false;

        // 禁用 鱼运动（避免移动、掉落、自动销毁）
        鱼运动 fishMove = obj.GetComponent<鱼运动>();
        if (fishMove != null) fishMove.enabled = false;

        // 禁用所有粒子特效（避免拖拽预览时播放，只在放置时触发）
        ParticleSystem[] allPs = obj.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in allPs)
        {
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    void EndDrag()
    {
        Debug.Log("[DragAndPlace] EndDrag 结束拖拽!");
        isDragging = false;
        IsCarryingObject = false;

        // ===== 隐藏 RockCapacity 的容量提示和描边 =====
        if (currentHoverRock != null)
        {
            RockCapacity.SetVisible(currentHoverRock, false);
            RockCapacity.SetOutline(currentHoverRock, false);
            currentHoverRock = null;
        }

        // 松开后恢复原本颜色
        RestoreColor();

        if (currentDragObject == null) return;

        // 用物体的体积检测：收集所有接触的 placementTag 石头（一个珊瑚可能同时压多块）
        Vector3 placePos = currentDragObject.transform.position;

        Debug.Log($"[DragAndPlace] 体积检测: 位置={placePos}");

        // 🔑 底座检测：珊瑚底边要真实压在石头表面附近才算命中。
        //    珊瑚预制体没有 Collider2D，旧逻辑走 SpriteRenderer.bounds 的 AABB 兜底，
        //    只判断“包围盒与任意 Obstacle 相交”，所以悬在水中擦到窗框也会通过。
        var allHitRocks = FindSupportRocks(currentDragObject, placementTag, SkillCaster.RockTouchTolerance);

        Debug.Log($"[DragAndPlace] 共命中 {allHitRocks.Count} 块石头: {string.Join(", ", allHitRocks.ConvertAll(r => r.name))}");

        // ====== 成功判定 ======
        // 1. 命中至少一块石头
        // 2. 玩家珊瑚道具数量够
        // 3. **主石头（Y 最靠下那块）** 还有空位 —— 和 UpdateHoverRock 显示的是同一块！
        GameObject primaryRock = FindPrimaryRock(allHitRocks);
        bool primaryHasRoom = primaryRock != null && RockCapacity.IsAvailable(primaryRock);
        bool allPass = allHitRocks.Count > 0 && canPlace && primaryHasRoom;

        Debug.Log($"[DragAndPlace] 放置判定: 命中石头数={allHitRocks.Count}, canPlace={canPlace}, primaryRock={primaryRock?.name}, primaryHasRoom={primaryHasRoom}, pass={allPass}");

        if (allPass)
        {
            Debug.Log($"[DragAndPlace] 放置成功! 主石头: {primaryRock?.name}");

            // 底边吸附到主石头顶面：落点 = 预览看到的位置，而不是鼠标中心
            float snapY = SnapDeltaY(currentDragObject, primaryRock);
            Vector3 finalPos = placePos + new Vector3(0f, snapY, 0f);

            Destroy(currentDragObject);
            currentDragObject = Instantiate(prefabToSpawn, finalPos, Quaternion.identity);
            Debug.Log($"[DragAndPlace] 生成真实物体: {currentDragObject.name}");

            // 🔑 登记到 SaveSystem：让它在任何时机（包括退出阶段）都能保存这个玩家珊瑚
            var spawner = currentDragObject.GetComponent<ObjectSpawner>();
            if (spawner != null)
            {
                SaveSystem.RegisterPlacedSpawner(spawner);
                Debug.Log($"[DragAndPlace] ✅ 已登记到 SaveSystem: {currentDragObject.name}");
            }
            else
            {
                Debug.LogWarning($"[DragAndPlace] ⚠️ {currentDragObject.name} 没有 ObjectSpawner，SaveSystem 无法追踪保存");
            }

            触发放置事件(currentDragObject);
            SetToTopLayer(currentDragObject);
            PlayPlaceEffect(currentDragObject);
            TriggerCoralSkill(currentDragObject, primaryRock);
            SpawnBubbleRing(finalPos, currentDragObject);

            // 6. 扣除珊瑚数量并更新文本显示
            float newCount = GetCoralCount() - GetCost();
            if (coralCountText != null)
            {
                coralCountText.text = newCount.ToString();
            }

            // 7. 刷新所有石头的容量统计（第一次放置也必须扣减）
            RockCapacity.RefreshAll(placementTag);
        }
        else
        {
            // 区分失败原因，方便 Console 排查
            if (allHitRocks.Count == 0)
                Debug.LogWarning("[DragAndPlace] 放置失败: 没碰到石头");
            else if (!canPlace)
                Debug.LogWarning("[DragAndPlace] 放置失败: 珊瑚道具数量不足");
            else if (!primaryHasRoom)
                Debug.LogWarning("[DragAndPlace] 放置失败: 主石头已满! 主石头=" + primaryRock?.name + ", 所有命中=" + string.Join(", ", allHitRocks.ConvertAll(r => r.name)));
            Destroy(currentDragObject);
        }

        currentDragObject = null;
    }

    /// <summary>
    /// 设置物体及其所有子物体的 SpriteRenderer.sortingOrder 为场景最靠前
    /// 扫描场景中最高的排序值，让新放置的物体整体压到所有物体之上（保留预制体内部相对层级）
    /// </summary>
    void SetToTopLayer(GameObject obj)
    {
        // 收集放置物体自己的所有渲染器
        HashSet<SpriteRenderer> mine = new HashSet<SpriteRenderer>(obj.GetComponentsInChildren<SpriteRenderer>(true));

        // 扫描场景中最靠前的排序值（排除自己）
        int maxOrder = baseSortingOrder - 1;
        SpriteRenderer[] allSrs = FindObjectsOfType<SpriteRenderer>();
        foreach (var sr in allSrs)
        {
            if (sr == null || mine.Contains(sr)) continue;
            if (sr.sortingOrder > maxOrder) maxOrder = sr.sortingOrder;
        }

        // 整体平移到最前面
        int minOrder = int.MaxValue;
        foreach (var sr in mine)
        {
            if (sr != null && sr.sortingOrder < minOrder) minOrder = sr.sortingOrder;
        }
        if (minOrder == int.MaxValue) return;

        int delta = (maxOrder + 1) - minOrder;
        if (delta <= 0) return; // 本来就在最前面就不用动

        foreach (var sr in mine)
        {
            if (sr != null) sr.sortingOrder += delta;
        }

        Debug.Log($"[DragAndPlace] 设置图层: {obj.name}, 场景最高排序值={maxOrder}, 新排序值={maxOrder + 1}, SpriteRenderer数量={mine.Count}");
    }

    /// <summary>
    /// 放置成功播放特效：遍历所有粒子系统，先 Clear+Stop 清干净 playOnAwake 自动播的残留，
    /// 再 Play() 一次（loop=false 的粒子自然播完就停；loop=true 的也只触发一次 burst）。
    /// 顺序在 TriggerCoralSkill 之前：放置特效和技能特效可能是同一粒子，先播放置、技能再管持续。
    /// </summary>
    void PlayPlaceEffect(GameObject obj)
    {
        var allPs = obj.GetComponentsInChildren<ParticleSystem>(true);
        if (allPs.Length == 0) return;
        foreach (var ps in allPs)
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
        }
    }

    // ======================== 放置气泡特效 ========================

    /// <summary>缓存运行时生成的白色圆形气泡 sprite（第一次用的时候才生成，避免占内存）</summary>
    static Sprite _bubbleSprite;

    /// <summary>运行时画一个 64x64 白色圆形，带软边缘（alpha 渐变到 0），用作气泡 sprite</summary>
    static Sprite GetBubbleSprite()
    {
        if (_bubbleSprite != null) return _bubbleSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float cx = size * 0.5f, cy = size * 0.5f, r = size * 0.45f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d) / r;
                // 边缘 10% 用二次方让过渡更柔和
                a = a > 0.8f ? 1f : a * a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        _bubbleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _bubbleSprite;
    }

    /// <summary>
    /// 放置成功时在落点位置生成一圈气泡提示——告诉玩家"放下了"。
    /// 动态建一个临时 FX GameObject，挂 N 个小气泡沿圆周分布、向上浮起同时淡出，0.7s 后整体销毁。
    /// sortingOrder 跟随珊瑚当前值 +1，保证在珊瑚 sprite 之上可见。
    /// </summary>
    void SpawnBubbleRing(Vector3 worldPos, GameObject coral)
    {
        // 取珊瑚的 sorting layer / order（珊瑚是 SpriteRenderer 物体）
        var coralSr = coral.GetComponentInChildren<SpriteRenderer>();
        int layer = coralSr != null ? coralSr.sortingLayerID : 0;
        int order = coralSr != null ? coralSr.sortingOrder : 10;

        var fx = new GameObject("FX_PlaceBubbles");
        fx.transform.position = worldPos + new Vector3(0, 0.05f, 0); // 稍微抬一点，避免穿石头
        var sr = fx.AddComponent<SpriteRenderer>();
        sr.sortingLayerID = layer;
        sr.sortingOrder = order + 1;
        sr.enabled = false; // 主物体不用渲染，下面的子气泡负责

        StartCoroutine(BubbleRoutine(fx.transform, layer, order + 1));
    }

    System.Collections.IEnumerator BubbleRoutine(Transform root, int layer, int order)
    {
        Sprite bubbleSprite = GetBubbleSprite();
        int count = 16;                  // 一圈 16 个小气泡
        float ringRadius = 0.7f;         // 圈半径（世界单位）
        float duration = 0.7f;           // 总时长

        var spawned = new System.Collections.Generic.List<Transform>(count);
        for (int i = 0; i < count; i++)
        {
            float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.05f, 0.05f);
            float rr = ringRadius + Random.Range(-0.08f, 0.08f);
            Vector3 pos = root.position + new Vector3(Mathf.Cos(angle) * rr, Mathf.Sin(angle) * rr * 0.5f, 0);
            var go = new GameObject("bub_" + i);
            go.transform.SetParent(root, true);   // 跟随 FX 根一起销毁，避免 16 个气泡永久残留
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = bubbleSprite;
            sr.color = new Color(0.85f, 0.95f, 1f, 0.9f); // 淡蓝半透明白
            sr.sortingLayerID = layer;
            sr.sortingOrder = order + i;                 // 每个气泡稍微错开 order 避免 z-fighting
            float baseScale = Random.Range(0.08f, 0.16f);
            go.transform.localScale = Vector3.one * baseScale;
            spawned.Add(go.transform);
        }

        // 主协程：0→duration，每个气泡按自己的 lifetime 向上浮 + 淡出 + 缩放
        float t = 0f;
        float[] lifetimes = new float[count];
        Vector3[] targets = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            lifetimes[i] = Random.Range(0.55f, duration);
            float tx = Random.Range(-0.15f, 0.15f);
            float ty = Random.Range(0.5f, 0.9f);
            targets[i] = spawned[i].position + new Vector3(tx, ty, 0);
        }

        while (t < duration && root != null)
        {
            t += Time.deltaTime;
            for (int i = 0; i < count; i++)
            {
                var tr = spawned[i];
                if (tr == null) continue;
                float lt = Mathf.Clamp01(t / lifetimes[i]);
                // 位置：起点 → target，前快后慢（EaseOutQuad）
                float e = 1f - (1f - lt) * (1f - lt);
                tr.position = Vector3.Lerp(tr.position, targets[i], Time.deltaTime * 8f);
                // 淡出
                var bsr = tr.GetComponent<SpriteRenderer>();
                if (bsr != null)
                {
                    Color c = bsr.color;
                    c.a = 1f - lt;
                    bsr.color = c;
                }
                // 微微放大（气泡上浮会略微变大）
                float baseScale = Random.Range(0.08f, 0.16f); // 每帧略抖一下大小
                tr.localScale = Vector3.one * baseScale * (1f + lt * 0.4f);
            }
            yield return null;
        }

        // 最后销毁整个 FX 物体
        if (root != null) Destroy(root.gameObject);
    }

    /// <summary>
    /// 触发技能：查找新放置物体上的 SkillCaster，施放它引用的技能定义
    /// </summary>
    void TriggerCoralSkill(GameObject placedCoral, GameObject rockGo)
    {
        if (placedCoral == null || rockGo == null) return;

        var caster = placedCoral.GetComponent<SkillCaster>();
        if (caster == null)
        {
            Debug.Log($"[DragAndPlace] {placedCoral.name} 没有挂载 SkillCaster，跳过技能触发");
            return;
        }

        Debug.Log($"[DragAndPlace] 触发技能: {placedCoral.name} → 石头 {rockGo.name}");

        try
        {
            caster.Cast(rockGo);
            Debug.Log($"[DragAndPlace]   技能施放完毕");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DragAndPlace]   技能施放出错: {e.Message}");
        }
    }

    /// <summary>
    /// 收集“珊瑚底座真正贴在其表面”的石头。
    /// 珊瑚预制体没有 Collider2D，只能用渲染包围盒求底座：
    ///   1. 在珊瑚底边中段取一条窄探针，要求它和石头碰撞体实际重叠（不是只看 AABB 相交）；
    ///   2. 石头的顶面必须就在珊瑚底边附近——高出太多=悬空，低太多=珊瑚嵌在石头腰部，
    ///      也顺便排除塔身上那些细高的「窗口」碰撞体（珊瑚悬在窗口中段时底边远低于窗顶）。
    /// </summary>
    internal static List<GameObject> FindSupportRocks(GameObject coral, string placementTag, float touchTolerance)
    {
        var result = new List<GameObject>();
        if (coral == null || string.IsNullOrEmpty(placementTag)) return result;

        if (!TryGetCoralBaseProbe(coral, out Vector2 probeCenter, out Vector2 probeSize, out Bounds coralBounds))
            return result;

        var seen = new HashSet<Collider2D>();
        foreach (var other in Physics2D.OverlapBoxAll(probeCenter, probeSize, 0f))
        {
            if (other == null || seen.Contains(other)) continue;
            seen.Add(other);
            if (other.transform.IsChildOf(coral.transform) || other.gameObject == coral) continue;

            var go = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
            if (!go.CompareTag(placementTag) || result.Contains(go)) continue;

            Bounds rockBounds = other.bounds;
            float baseY = coralBounds.min.y;
            // 塔身上的「窗口」是细高的长条碰撞体（高/宽≈4~5），珊瑚不可能“踩”在它上面；
            // 石头的高宽比都在 2 以内，这里用 3 倍作为分界。
            if (rockBounds.size.y > rockBounds.size.x * 3f) continue;
            // 允许的最大“陷入深度”：小石头按 45% 高度、最少 0.3，避免把正常压边误判成悬空/嵌入
            float sinkLimit = Mathf.Max(0.3f, rockBounds.size.y * 0.45f);
            if (baseY > rockBounds.max.y + touchTolerance) continue; // 悬空
            if (baseY < rockBounds.max.y - sinkLimit) continue;      // 嵌在石头腰部/窗框中段

            result.Add(go);
        }
        return result;
    }

    /// <summary>珊瑚底边贴到石头顶面所需的 Y 位移（正值=往上抬）</summary>
    float SnapDeltaY(GameObject coral, GameObject rock)
    {
        if (coral == null || rock == null) return 0f;
        if (!TryGetCoralBaseProbe(coral, out _, out _, out Bounds coralBounds)) return 0f;

        float topY = float.MinValue;
        foreach (var c in rock.GetComponentsInChildren<Collider2D>())
            if (c != null && c.enabled && c.bounds.max.y > topY) topY = c.bounds.max.y;
        if (topY == float.MinValue) return 0f;

        return (topY - coralBounds.min.y) + baseSnapOffsetY;
    }

    /// <summary>取珊瑚所有 SpriteRenderer 的世界包围盒，在底边生成一条横向探针</summary>
    static bool TryGetCoralBaseProbe(GameObject coral, out Vector2 center, out Vector2 size, out Bounds bounds)
    {
        center = Vector2.zero;
        size = Vector2.zero;
        bounds = default;

        bool has = false;
        foreach (var sr in coral.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null) continue;
            if (!has) { bounds = sr.bounds; has = true; }
            else bounds.Encapsulate(sr.bounds);
        }
        if (!has)
        {
            foreach (var col in coral.GetComponentsInChildren<Collider2D>(true))
            {
                if (col == null) continue;
                if (!has) { bounds = col.bounds; has = true; }
                else bounds.Encapsulate(col.bounds);
            }
        }
        if (!has || bounds.size.x <= 0.0001f) return false;

        // 探针跨过底边上下各 0.1，允许最多约 0.1 的视觉缝隙（透明边距）
        size = new Vector2(Mathf.Max(0.08f, bounds.size.x * 0.8f), 0.2f);
        center = new Vector2(bounds.center.x, bounds.min.y);
        return true;
    }

    /// <summary>
    /// 从 OverlapBox/Area 的碰撞体数组里，过滤出 placementTag 的石头 GameObject，去重加入 hit 列表
    /// </summary>
    static void CollectHitRocks(Collider2D[] overlaps, GameObject self, string placementTag,
        HashSet<Collider2D> checkedOthers, List<GameObject> hitRocks)
    {
        if (overlaps == null) return;
        foreach (var other in overlaps)
        {
            if (other == null) continue;
            if (checkedOthers.Contains(other)) continue;
            checkedOthers.Add(other);
            if (other.transform.IsChildOf(self.transform) || other.gameObject == self) continue;

            // 先取 attachedRigidbody 的 GameObject（Collider2D 可能在子物体上，Tag/Rigidbody 在父物体上）
            var go = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
            if (!go.CompareTag(placementTag)) continue;
            if (!hitRocks.Contains(go)) hitRocks.Add(go);
        }
    }

    /// <summary>
    /// 从命中的多块石头里挑"主石头"——Y 坐标最靠下的那块（通常是珊瑚真正站在上面的底座）
    /// internal 供 MoveSystem 复用，保证两套系统选石头的规则完全一致
    /// </summary>
    internal static GameObject FindPrimaryRock(List<GameObject> hitRocks)
    {
        if (hitRocks == null || hitRocks.Count == 0) return null;
        GameObject best = hitRocks[0];
        float bestY = best.transform.position.y;
        for (int i = 1; i < hitRocks.Count; i++)
        {
            float y = hitRocks[i].transform.position.y;
            if (y < bestY) { bestY = y; best = hitRocks[i]; }
        }
        return best;
    }
}