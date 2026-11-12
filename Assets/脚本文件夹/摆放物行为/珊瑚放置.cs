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

    [Header("沿支撑面倾斜")]
    [Tooltip("最大倾斜角（度）：超过按此值截断，避免陡壁把珊瑚转飞。<=0 时按 30 度兜底（老场景没这个字段）")]
    public float maxSurfaceTilt = 30f;

    [Tooltip("坡度采样点数（沿珊瑚底座宽度均匀取点）。<3 时按 5 兜底（老场景没这个字段）")]
    [Range(3, 9)] public int surfaceTiltSamples = 5;

    [Header("允许和石头重叠")]
    [Tooltip("开启（默认）：珊瑚底座只要和石头有实际重叠就能放 —— 可以嵌进石头、压在石头身上。\n" +
             "底边仍在顶面附近时，照旧吸附到顶面并沿坡度倾斜；嵌进石头时保持玩家拖到的位置（预览=落位）。\n" +
             "关闭：退回旧规则 —— 底边必须压在石头顶面附近，侧贴/嵌进腰部一律拒绝。")]
    public bool allowRockOverlap = true;

    /// <summary>全局重叠开关：放置、搬家、容量统计必须用同一套判定，所以做成静态。
    /// 由各 DragAndPlaceSystem 在 OnEnable / Inspector 改动时写入。</summary>
    internal static bool AllowRockOverlap = true;

    /// <summary>主石头切换迟滞（世界单位）：新石头必须比当前这块明显更靠下才切换，防止两帧来回跳。
    /// 所有入口（放置 / 搬家 / 容量统计）共用同一份，所以做成静态常量。</summary>
    internal const float PrimarySwitchMargin = 0.12f;

    [Header("放置成功气泡（一大团）")]
    [Tooltip("气泡贴图。留空 = 自动用 Resources/Effects/气泡2（素材/UI/气泡 2.png 的副本）；再失败则用代码生成的柔边圆")]
    public Sprite bubbleSprite;

    [Tooltip("一次爆发的气泡数量（峰值对象数 = 本值 + 1 个 FX 根）")]
    [Range(8, 120)] public int bubbleCount = 48;

    [Tooltip("单个气泡直径范围（世界单位）。参考：珊瑚本体高约 0.3~0.5，底座宽约 0.31")]
    public Vector2 bubbleSizeRange = new Vector2(0.08f, 0.22f);

    [Tooltip("上浮初速度范围（世界单位/秒）：冲出去后受阻尼减速")]
    public Vector2 bubbleRiseSpeedRange = new Vector2(1.0f, 2.0f);

    [Tooltip("横向扩散半径（世界单位）：出生点在底座这个半径内随机散开")]
    public float bubbleSpreadRadius = 0.4f;

    [Tooltip("相对正上方的最大扩散张角（度）：越大越像一整团炸开")]
    [Range(10f, 89f)] public float bubbleSpreadAngle = 65f;

    [Tooltip("基准生命周期（秒）：每个气泡再乘 0.8~1.15 随机")]
    public float bubbleLifetime = 1.2f;

    [Tooltip("气泡颜色（A = 初始不透明度）")]
    public Color bubbleColor = new Color(0.85f, 0.95f, 1f, 0.9f);

    [Tooltip("整团气泡的尺寸/速度/扩散全局倍率：只调这一个就能整体放大缩小")]
    public float bubbleEffectScale = 1f;

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

    /// <summary>拖拽开始时量出的底座半宽（未旋转时的值，供贴面倾斜采样用，避免旋转→AABB变宽→采样范围抖动的反馈）</summary>
    private float dragBaseHalfWidth;

    /// <summary>倾斜上限：<=0 时按 30 度兜底（场景里没有这两个新字段时会反序列化成 0）</summary>
    float EffectiveMaxTilt => maxSurfaceTilt > 0.001f ? maxSurfaceTilt : 30f;
    /// <summary>采样点数：<3 时按 5 兜底</summary>
    int EffectiveTiltSamples => surfaceTiltSamples < 3 ? 5 : surfaceTiltSamples;

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

    [Header("调试")]
    [Tooltip("勾选后：拖拽时每当主石头发生变化就打一条日志（贴面角度 / 接触距离），用于排查吸附抖动")]
    public bool debugBaseProbe = false;

    // ===== 桌面窗口拖动排除（DesktopWindowController 调用）=====
    // 图标可能没开 Raycast Target，EventSystem 射线扫不到，所以这里直接登记实例供窗口脚本查询
    static readonly System.Collections.Generic.List<DragAndPlaceSystem> all = new System.Collections.Generic.List<DragAndPlaceSystem>();
    void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        AllowRockOverlap = allowRockOverlap;
    }
    void OnValidate() { AllowRockOverlap = allowRockOverlap; }
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
    /// 预览体每帧收集"底座贴合"的石头 → 选主石头（Y 最靠下那块，带迟滞防抖）→ 吸附到顶面。
    /// 🔑 和 EndDrag / MoveSystem / RockCapacity 用完全相同的
    ///    FindSupportRocks + FindPrimaryRock + SnapDeltaY + MeasureSurfaceTilt，
    ///    保证拖拽中亮着的那块、预览看到的位置和倾角，就是松手后的结果。
    /// </summary>
    void UpdateHoverRock()
    {
        if (currentDragObject == null) return;

        // 先用「水平姿态」做支撑面检测：倾斜会撑大 AABB，让底座探针变宽/变高，
        // 可能凭空多碰到旁边的石头 → 主石头来回跳。测完再摆倾斜，互不干扰。
        if (currentDragObject != null)
            currentDragObject.transform.rotation = Quaternion.identity;

        List<GameObject> allHit = FindSupportRocks(currentDragObject, placementTag, SkillCaster.RockTouchTolerance);

        // 选 Y 最靠下的那块当主石头（和 EndDrag/RefreshAll 同一套，带迟滞防抖）
        GameObject newRock = FindPrimaryRock(allHit, currentHoverRock, PrimarySwitchMargin);

        // 预览体吸附到石头顶面：所见即所得，松手后的落点就是现在看到的位置
        if (newRock != null && currentDragObject != null)
        {
            // 先按石头顶面坡度摆正朝向，再吸附底边——两件事必须同帧同序，
            // 否则旋转后 AABB 最低点变了，吸附量会差一截（表现为一碰石头就上下跳）
            float tilt = MeasureSurfaceTilt(currentDragObject, newRock, currentDragObject.transform.position.x,
                                            dragBaseHalfWidth, EffectiveTiltSamples, EffectiveMaxTilt);
            currentDragObject.transform.rotation = Quaternion.Euler(0f, 0f, tilt);

            Vector3 p = currentDragObject.transform.position;
            p.y += SnapDeltaY(currentDragObject, newRock, baseSnapOffsetY);
            currentDragObject.transform.position = p;
        }

        if (debugBaseProbe && newRock != currentHoverRock)
        {
            bool ok = TryGetCoralBaseProbe(currentDragObject, out _, out _, out Bounds cb);
            Debug.Log($"[探针] 悬停切换 → {(newRock != null ? newRock.name : "无")} | 预览Y={currentDragObject.transform.position.y:F4}"
                + $" 探针Y={(ok ? cb.min.y : 0f):F4} 吸附Δ={(newRock != null ? SnapDeltaY(currentDragObject, newRock, baseSnapOffsetY) : 0f):F4} autoSync={Physics2D.autoSyncTransforms}");
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

        // 底座半宽只在这里量一次（此时还是未旋转的水平姿态），后面贴面倾斜都用它
        dragBaseHalfWidth = GetBaseHalfWidth(currentDragObject);
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

        // 记下预览时高亮的那块：容量判定和落位都要和"看到的"一致
        GameObject preferredRock = currentHoverRock;

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

        // 用物体的底座探针检测：收集所有接触的 placementTag 石头（一个珊瑚可能同时压多块）
        Vector3 placePos = currentDragObject.transform.position;

        // 和 UpdateHoverRock 一致：检测前先把预览体摆回水平，避免倾斜后的 AABB 影响底座探针
        currentDragObject.transform.rotation = Quaternion.identity;

        Debug.Log($"[DragAndPlace] 底座检测: 位置={placePos}");

        // 🔑 底座检测：珊瑚底边要真实压在石头顶面附近才算命中。
        var allHitRocks = FindSupportRocks(currentDragObject, placementTag, SkillCaster.RockTouchTolerance);

        Debug.Log($"[DragAndPlace] 共命中 {allHitRocks.Count} 块石头: {string.Join(", ", allHitRocks.ConvertAll(r => r.name))}");

        // ====== 成功判定 ======
        // 1. 命中至少一块石头
        // 2. 玩家珊瑚道具数量够
        // 3. **主石头（Y 最靠下那块，带迟滞）** 还有空位 —— 和 UpdateHoverRock 显示的是同一块！
        GameObject primaryRock = FindPrimaryRock(allHitRocks, preferredRock, PrimarySwitchMargin);
        bool primaryHasRoom = primaryRock != null && RockCapacity.IsAvailable(primaryRock);
        bool allPass = allHitRocks.Count > 0 && canPlace && primaryHasRoom;

        Debug.Log($"[DragAndPlace] 放置判定: 命中石头数={allHitRocks.Count}, canPlace={canPlace}, primaryRock={primaryRock?.name}, primaryHasRoom={primaryHasRoom}, pass={allPass}");

        if (allPass)
        {
            Debug.Log($"[DragAndPlace] 放置成功! 主石头: {primaryRock?.name}");

            // 底边吸附到主石头顶面：落点 = 预览看到的位置，而不是鼠标中心
            float snapY = SnapDeltaY(currentDragObject, primaryRock, baseSnapOffsetY);
            Vector3 finalPos = placePos + new Vector3(0f, snapY, 0f);

            // 沿支撑面倾斜：用拖拽开始时量好的底座半宽去采样主石头顶面坡度
            float tiltZ = MeasureSurfaceTilt(currentDragObject, primaryRock, finalPos.x, dragBaseHalfWidth,
                                             EffectiveTiltSamples, EffectiveMaxTilt);

            Destroy(currentDragObject);
            currentDragObject = Instantiate(prefabToSpawn, finalPos, Quaternion.Euler(0f, 0f, tiltZ));

            // 旋转后底边 AABB 的最低点变了，再吸附一次：保证倾斜后的最低点也压在石头顶面上
            float snapAfterRotate = SnapDeltaY(currentDragObject, primaryRock, baseSnapOffsetY);
            if (Mathf.Abs(snapAfterRotate) > 0.0001f)
                currentDragObject.transform.position += new Vector3(0f, snapAfterRotate, 0f);

            Debug.Log($"[DragAndPlace] 生成真实物体: {currentDragObject.name}，贴面倾斜={tiltZ:F1}°，旋转后二次吸附ΔY={snapAfterRotate:F4}");

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
            BringToFront(currentDragObject, baseSortingOrder);
            PlayPlaceEffect(currentDragObject);
            TriggerCoralSkill(currentDragObject, primaryRock);
            SpawnBubbleBurst(currentDragObject);

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
    /// 把物体及其所有子物体整体抬到场景 SpriteRenderer 最前面（保留预制体内部相对层级）。
    ///
    /// 🔑 静态方法，三条路都走它，保证"新放的 / 读档恢复的 / 搬完家的"珊瑚排序完全一致：
    ///    1) 放置成功（本类 EndDrag）；
    ///    2) 读档恢复（SaveSystem.ClearAndRestorePlacedObjects）；
    ///    3) 搬家结束（MoveSystem.DisableMoveMode）。
    /// 之前只有第 1 条路会抬，读档/搬家结束会把珊瑚还原成预制体的 sortingOrder=0，
    /// 而场景里 `窗口_1/2/3` 是 Obstacle 且就在 coral 层 order=2 —— 存档珊瑚就被窗口压住了。
    ///
    /// 只改 sortingOrder、不改 SortingLayer：保持项目现有的远近关系
    /// （珊瑚层 < 鱼层，鱼从珊瑚前方游过仍然正常）。
    /// </summary>
    internal static void BringToFront(GameObject obj, int baseOrder = 100)
    {
        if (obj == null) return;

        // 收集放置物体自己的所有渲染器
        HashSet<SpriteRenderer> mine = new HashSet<SpriteRenderer>(obj.GetComponentsInChildren<SpriteRenderer>(true));
        if (mine.Count == 0) return;

        // 扫描场景中最靠前的排序值（排除自己）
        int maxOrder = baseOrder - 1;
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
    /// 放置成功：从珊瑚底座往上炸开一大团气泡。
    /// 全部气泡都是 FX 根的子物体 → 根销毁时一起走；根自身还挂了一个定时 Destroy 兜底，
    /// 即使协程被打断（脚本/面板被关闭、搬家取消、切场景前）也不会留下游离气泡。
    /// </summary>
    void SpawnBubbleBurst(GameObject coral)
    {
        if (coral == null) return;

        var coralSr = coral.GetComponentInChildren<SpriteRenderer>();
        int layer = coralSr != null ? coralSr.sortingLayerID : 0;
        int order = coralSr != null ? coralSr.sortingOrder : 10;

        // 起点 = 珊瑚底座（渲染包围盒底边中点），跟着本次放置的珊瑚走，不是世界固定点
        Vector3 basePos = coral.transform.position;
        if (coralSr != null)
        {
            var b = coralSr.bounds;
            basePos = new Vector3(b.center.x, b.min.y, 0f);
        }
        else
        {
            var col = coral.GetComponentInChildren<Collider2D>();
            if (col != null) basePos = new Vector3(col.bounds.center.x, col.bounds.min.y, 0f);
        }

        var fx = new GameObject("FX_PlaceBubbles");
        fx.transform.SetPositionAndRotation(basePos, Quaternion.identity);
        var fxSr = fx.AddComponent<SpriteRenderer>();
        fxSr.enabled = false;                 // 根只当容器
        fxSr.sortingLayerID = layer;
        fxSr.sortingOrder = order + 1;

        // 兜底销毁：到时候无条件销毁整个 FX 根（气泡都是它的子物体）
        float guardLife = Mathf.Max(0.2f, bubbleLifetime) * 1.15f + 0.35f;
        Destroy(fx, guardLife);

        StartCoroutine(BubbleBurstRoutine(fx.transform, layer, order + 1));
    }

    /// <summary>贴图：Inspector 指定 → Resources/Effects/气泡2 → 代码柔边圆</summary>
    static Sprite _artBubbleSprite;
    static bool _artBubbleTried;

    static Sprite ResolveBubbleSprite(Sprite overrideSprite)
    {
        if (overrideSprite != null) return overrideSprite;
        if (!_artBubbleTried)
        {
            _artBubbleTried = true;
            _artBubbleSprite = Resources.Load<Sprite>("Effects/气泡2");
        }
        return _artBubbleSprite != null ? _artBubbleSprite : GetBubbleSprite();
    }

    /// <summary>单个气泡的运行时状态（只在爆发期间存在，不跨帧复用）</summary>
    class BurstBubble
    {
        public Transform tr;
        public SpriteRenderer sr;
        public Vector2 pos;      // FX 根局部坐标（根无父节点，等同世界偏移）
        public Vector2 vel;
        public float size;       // 已换算成 localScale 的直径
        public float delay;
        public float life;
        public float swayAmp;
        public float swayFreq;
        public float phase;
    }

    System.Collections.IEnumerator BubbleBurstRoutine(Transform root, int layer, int order)
    {
        Sprite sprite = ResolveBubbleSprite(bubbleSprite);

        int count = Mathf.Clamp(bubbleCount, 1, 120);
        float sizeMin = Mathf.Min(bubbleSizeRange.x, bubbleSizeRange.y);
        float sizeMax = Mathf.Max(bubbleSizeRange.x, bubbleSizeRange.y);
        if (sizeMax <= 0.0001f) { sizeMin = 0.08f; sizeMax = 0.22f; }
        float speedMin = Mathf.Min(bubbleRiseSpeedRange.x, bubbleRiseSpeedRange.y);
        float speedMax = Mathf.Max(bubbleRiseSpeedRange.x, bubbleRiseSpeedRange.y);
        if (speedMax <= 0.0001f) { speedMin = 1f; speedMax = 2f; }
        float global = Mathf.Max(0.05f, bubbleEffectScale);
        float spreadR = Mathf.Max(0f, bubbleSpreadRadius) * global;
        float halfAngle = Mathf.Clamp(bubbleSpreadAngle, 5f, 89f) * Mathf.Deg2Rad;
        float baseLife = Mathf.Max(0.15f, bubbleLifetime);

        // 美术图/生成图的真实世界尺寸：换算成“按直径控制”的 localScale
        float spriteWorld = 1f;
        if (sprite != null) spriteWorld = Mathf.Max(0.0001f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));

        var bubbles = new BurstBubble[count];
        float maxDuration = 0f;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("bub_" + i);
            go.transform.SetParent(root, false);      // 关键：挂在 FX 根下，根销毁 = 全部销毁
            var bsr = go.AddComponent<SpriteRenderer>();
            bsr.sprite = sprite;
            bsr.color = bubbleColor;
            bsr.sortingLayerID = layer;
            bsr.sortingOrder = order + (i % 8);       // 稍微错开，避免同 order 交替闪
            go.SetActive(false);                      // 到出生时间再显示，不会堆在原点

            // 方向：以正上方为中心 ±halfAngle 扇形爆开
            float ang = Random.Range(Mathf.PI * 0.5f - halfAngle, Mathf.PI * 0.5f + halfAngle);
            float speed = Random.Range(speedMin, speedMax) * global;

            float size = Random.Range(sizeMin, sizeMax) * global;
            var b = new BurstBubble
            {
                tr = go.transform,
                sr = bsr,
                pos = new Vector2(Random.Range(-spreadR, spreadR) * 0.6f, Random.Range(-0.03f, 0.05f) * global),
                vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed,
                size = size / spriteWorld,
                delay = Random.Range(0f, 0.18f),
                life = baseLife * Random.Range(0.8f, 1.15f),
                swayAmp = Random.Range(0.03f, 0.10f) * global,
                swayFreq = Random.Range(1.5f, 3.5f),
                phase = Random.Range(0f, Mathf.PI * 2f)
            };
            bubbles[i] = b;
            maxDuration = Mathf.Max(maxDuration, b.delay + b.life);
        }

        float t = 0f;
        while (t < maxDuration)
        {
            if (root == null) yield break;      // FX 根被外部销毁（切场景/兜底清理）就直接结束
            float dt = Time.deltaTime;
            t += dt;

            for (int i = 0; i < count; i++)
            {
                var b = bubbles[i];
                if (b == null || b.tr == null) continue;
                float age = t - b.delay;
                if (age <= 0f) continue;

                float k = age / b.life;
                if (k >= 1f)
                {
                    bubbles[i] = null;
                    Destroy(b.tr.gameObject);
                    continue;
                }
                if (!b.tr.gameObject.activeSelf) b.tr.gameObject.SetActive(true);

                // 速度阻尼：爆发初速冲出去后迅速减速，变成缓浮
                b.vel *= Mathf.Clamp01(1f - 2.2f * dt);
                b.pos += b.vel * dt;

                float sway = Mathf.Sin(age * b.swayFreq + b.phase) * b.swayAmp;
                b.tr.localPosition = new Vector3(b.pos.x + sway, b.pos.y, 0f);

                // 上浮过程中略微撑开
                b.tr.localScale = Vector3.one * (b.size * Mathf.Lerp(0.75f, 1.25f, k));

                var c = bubbleColor;
                c.a = bubbleColor.a * Mathf.SmoothStep(1f, 0f, k);
                b.sr.color = c;
            }
            yield return null;
        }

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

        // 技能定义已统一为“一份母版 + 预制体默认不填”：
        // 引用为空是正常状态，静默跳过，避免每次放置都打一条警告。
        if (caster.skill == null) return;

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
    /// 收集“珊瑚底座贴在其表面”的石头。
    /// 底座用独立的落位小碰撞体，在珊瑚底边中段取一条窄探针，要求它和石头碰撞体实际重叠。
    ///
    /// 两种口径由 allowRockOverlap 决定：
    ///   · 关：顶面放置 —— 石头顶面必须就在珊瑚底边附近（高出太多=悬空，低太多=嵌在腰部），
    ///         并顺便排除塔身上那些细高的「窗口」碰撞体；
    ///   · 开（默认）：底座和石头有实际重叠就算命中，允许嵌进石头/压在石头身上。
    ///         只保留一条兜底：珊瑚整体都陷进石头包围盒（完全看不见）时拒绝。
    /// </summary>
    // 最近一次 FindSupportRocks 中，每块石头真正被底座探针命中的那个碰撞体的顶面 Y（世界坐标）。
    // 不能用"石头所有碰撞体的最高点"来吸附：多碰撞体/装饰子碰撞体会把珊瑚抬到空中。
    static readonly Dictionary<GameObject, float> supportTopY = new Dictionary<GameObject, float>();

    internal static List<GameObject> FindSupportRocks(GameObject coral, string placementTag, float touchTolerance)
    {
        var result = new List<GameObject>();
        supportTopY.Clear();
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
            // 石头的高宽比都在 2 以内，这里用 3 倍作为分界。两种口径都要排除。
            if (rockBounds.size.y > rockBounds.size.x * 3f) continue;

            // 口径差异：
            // · 允许重叠（默认）：探针和碰撞体实际重叠就算命中，可以嵌进石头/压在石头身上。
            //   不需要额外“防穿出”限制：底座探针是唯一命中依据，拖到石头另一侧后探针不再重叠 →
            //   立刻失去支撑（回到“没碰到石头”的拒绝态）；而珊瑚在 coral 层(3)、石头在 Platform/背景层(<=2)，
            //   珊瑚永远画在石头前面，不会被吞掉看不见。
            // · 不允许重叠：顶面放置，底边必须压在顶面附近。
            if (!AllowRockOverlap)
            {
                // 允许的最大“陷入深度”：小石头按 45% 高度、最少 0.3，避免把正常压边误判成悬空/嵌入
                float sinkLimit = Mathf.Max(0.3f, rockBounds.size.y * 0.45f);
                if (baseY > rockBounds.max.y + touchTolerance) continue; // 悬空
                if (baseY < rockBounds.max.y - sinkLimit) continue;      // 嵌在石头腰部/窗框中段
            }

            // 记录真正被探针命中的碰撞体顶面（同一块石头多个碰撞体时取最高的那个命中面）
            if (!supportTopY.ContainsKey(go) || rockBounds.max.y > supportTopY[go])
                supportTopY[go] = rockBounds.max.y;
            result.Add(go);
        }
        return result;
    }

    /// <summary>
    /// 底边离顶面多近仍按“站在顶面”处理（世界单位）。更低的视为已经嵌进石头，
    /// 走重叠口径：不吸附、不倾斜，保持玩家拖到的位置。
    /// </summary>
    static float TopSnapBand(Bounds coralBounds) => Mathf.Max(0.2f, coralBounds.size.y * 0.1f);

    /// <summary>珊瑚底边贴到石头顶面所需的 Y 位移（正值=往上抬）。静态版供搬家系统复用同一套吸附。
    /// 允许重叠、且底座已经嵌进石头腰部时返回 0：保持玩家拖到的位置，预览和落位自然一致。</summary>
    internal static float SnapDeltaY(GameObject coral, GameObject rock, float extraOffsetY = 0f)
    {
        if (coral == null || rock == null) return 0f;
        if (!TryGetCoralBaseProbe(coral, out _, out _, out Bounds coralBounds)) return 0f;

        float topY;
        if (!supportTopY.TryGetValue(rock, out topY))
        {
            // 兜底：没有探针记录时退回整块石头的最高碰撞体顶面
            topY = float.MinValue;
            foreach (var c in rock.GetComponentsInChildren<Collider2D>())
                if (c != null && c.enabled && c.bounds.max.y > topY) topY = c.bounds.max.y;
        }
        if (topY == float.MinValue) return 0f;

        // 重叠口径：底边已经明显低于顶面（嵌进石头）→ 不往顶面吸
        if (AllowRockOverlap && coralBounds.min.y < topY - TopSnapBand(coralBounds)) return 0f;

        return (topY - coralBounds.min.y) + extraOffsetY;
    }

    /// <summary>取珊瑚所有 SpriteRenderer 的世界包围盒，在底边生成一条横向探针</summary>
    static bool TryGetCoralBaseProbe(GameObject coral, out Vector2 center, out Vector2 size, out Bounds bounds)
    {
        center = Vector2.zero;
        size = Vector2.zero;
        bounds = default;

        // 有独立落位小碰撞体时，以它的包围盒为准（策划在预制体上标的落点形状），
        // 不再用精灵包围盒 —— 精灵的透明边距会把底座整体算低/算宽。
        //
        // ⚠️ 关键：先 SyncTransforms 再读 bounds。
        //    项目 Physics2DSettings 里 m_AutoSyncTransforms = 0，物理世界只在 FixedUpdate 同步。
        //    拖拽预览体是在 Update 里每帧瞬移的，不同步就读 bounds，拿到的是“上一个物理步”的位姿：
        //    这一帧用旧位姿算吸附量并写位置，下一帧再读又是另一份旧位姿 → 写值在两帧之间来回跳
        //    （珊瑚上下闪、描边/可放置数字跟着闪）。同步一次后 bounds 与本帧 Physics2D 查询同源。
        Physics2D.SyncTransforms();
        Collider2D placementCol = GetPlacementCollider(coral);
        if (placementCol != null)
        {
            bounds = placementCol.bounds;
            size = new Vector2(Mathf.Max(0.08f, bounds.size.x * 0.8f), 0.2f);
            center = new Vector2(bounds.center.x, bounds.min.y);
            return true;
        }

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

    /// <summary>底座半宽（世界单位）：直接用落位探针的宽度（已是底座宽度的 0.8 倍），采样点不会跑到石头外。</summary>
    static float GetBaseHalfWidth(GameObject coral)
    {
        if (!TryGetCoralBaseProbe(coral, out _, out Vector2 probeSize, out _)) return 0f;
        return probeSize.x * 0.5f;
    }

    // 坡度采样的射线结果缓冲（避免每帧 GC）
    static readonly RaycastHit2D[] tiltHitBuffer = new RaycastHit2D[16];

    /// <summary>
    /// 测量某块石头在 [centerX-halfWidth, centerX+halfWidth] 区间的支撑面坡度（角度，正值=左低右高）。
    ///
    /// 做法：在落点附近按底座宽度均匀取 N 个 x，每个 x 从顶面上方 0.35 往下打 0.9 的竖直射线，
    /// 只认这块石头自己的 Collider2D 命中（其余碰撞体全部过滤掉），
    /// 取该列最上面的命中点，再做最小二乘直线拟合求斜率。
    /// 这样量到的是美术/碰撞体实际摆出来的表面（不是整块 AABB 的矩形顶），
    /// 对斜顶石头、台阶边缘都能给出正确的倾角。
    ///
    /// 命中不足 2 个点（悬空、底座比石头宽、碰撞体没覆盖）时返回 0 → 保持水平，行为与改动前一致。
    /// </summary>
    internal static float MeasureSurfaceTilt(GameObject coral, GameObject rock, float centerX, float halfWidth, int samples, float maxAngle)
    {
        if (rock == null || halfWidth <= 0.0001f) return 0f;

        // 支撑面高度来自最近一次 FindSupportRocks 的探针命中记录；没有记录就不倾斜
        float topY;
        if (!supportTopY.TryGetValue(rock, out topY)) return 0f;

        // 重叠口径：珊瑚已经嵌进石头腰部（底边明显低于顶面）→ 贴在石头上，不按顶面坡度转
        if (AllowRockOverlap && coral != null &&
            TryGetCoralBaseProbe(coral, out _, out _, out Bounds coralBounds) &&
            coralBounds.min.y < topY - TopSnapBand(coralBounds)) return 0f;

        var cols = rock.GetComponentsInChildren<Collider2D>();
        if (cols == null || cols.Length == 0) return 0f;
        var rockCols = new HashSet<Collider2D>(cols);

        samples = Mathf.Clamp(samples, 3, 9);
        // 只在落点附近这一薄层里找表面：上方 0.35 起打，向下 0.9，避开塔身上方的窗口/悬挑
        const float startAbove = 0.35f;
        const float bandLength = 0.9f;
        float y0 = topY + startAbove;

        float sumX = 0f, sumY = 0f, sumXY = 0f, sumXX = 0f;
        int n = 0;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            float x = centerX - halfWidth + t * halfWidth * 2f;
            Vector2 origin = new Vector2(x, y0);

            int count = Physics2D.RaycastNonAlloc(origin, Vector2.down, tiltHitBuffer, bandLength);
            float bestY = float.MinValue;
            for (int k = 0; k < count; k++)
            {
                var h = tiltHitBuffer[k];
                if (h.collider == null || !rockCols.Contains(h.collider)) continue;
                if (h.point.y > bestY) bestY = h.point.y;
            }
            if (bestY == float.MinValue) continue;

            sumX += x; sumY += bestY; sumXY += x * bestY; sumXX += x * x;
            n++;
        }
        if (n < 2) return 0f;

        float den = n * sumXX - sumX * sumX;
        if (Mathf.Abs(den) < 1e-6f) return 0f;

        float slope = (n * sumXY - sumX * sumY) / den;
        float angle = Mathf.Atan(slope) * Mathf.Rad2Deg;
        return Mathf.Clamp(angle, -maxAngle, maxAngle);
    }

    // ==================== 两个碰撞体 ====================
    // 大碰撞体（面积最大）= 只做点击弹出 UI；
    // 小碰撞体（面积最小/实心）= 只做落位检测与吸附探针，两者不混用。
    // 所有判定都通过这两个函数显式取碰撞体，不再用 GetComponent<Collider2D>() 撞运气。

    /// <summary>点击用碰撞体：取世界包围盒面积最大的 Collider2D（=覆盖可见轮廓的大圈）。</summary>
    internal static Collider2D GetClickCollider(GameObject obj)
    {
        if (obj == null) return null;
        Collider2D best = null;
        float bestArea = -1f;
        foreach (var c in obj.GetComponentsInChildren<Collider2D>(true))
        {
            if (c == null || !c.enabled) continue;
            float area = c.bounds.size.x * c.bounds.size.y;
            if (area > bestArea) { bestArea = area; best = c; }
        }
        return best;
    }

    /// <summary>
    /// 落位用碰撞体：物体上存在两个以上碰撞体时，取面积最小的那个（优先实心非触发器）。
    /// 只有一个碰撞体（或没有）时返回 null —— 表示没有独立的落位小碰撞体，
    /// 落位继续沿用 SpriteRenderer 包围盒，不会因为“只有大圈”就把整块轮廓当成底座。
    /// </summary>
    internal static Collider2D GetPlacementCollider(GameObject obj)
    {
        if (obj == null) return null;
        int enabledCount = 0;
        Collider2D smallest = null, smallestSolid = null;
        float minArea = float.MaxValue, minSolidArea = float.MaxValue;
        foreach (var c in obj.GetComponentsInChildren<Collider2D>(true))
        {
            if (c == null || !c.enabled) continue;
            enabledCount++;
            float area = c.bounds.size.x * c.bounds.size.y;
            if (area < minArea) { minArea = area; smallest = c; }
            if (!c.isTrigger && area < minSolidArea) { minSolidArea = area; smallestSolid = c; }
        }
        if (enabledCount < 2) return null;
        return smallestSolid != null ? smallestSolid : smallest;
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
    /// 从碰到的多块石头里挑"主石头"——Y 坐标最靠下的那块（珊瑚真正站在上面的底座）。
    /// preferred 传上一帧选中的石头做迟滞：它只要还命中、Y 不比最低的那块高出 switchMargin，
    /// 就继续用它，避免珊瑚压在两块石头交界时主石头两帧来回跳（闪烁的来源之一）。
    /// internal 供 MoveSystem / RockCapacity 复用，保证三套系统选石头的规则完全一致。
    /// </summary>
    internal static GameObject FindPrimaryRock(List<GameObject> hitRocks, GameObject preferred = null,
                                               float switchMargin = PrimarySwitchMargin)
    {
        if (hitRocks == null || hitRocks.Count == 0) return null;

        GameObject best = null;
        float bestY = float.MaxValue;
        for (int i = 0; i < hitRocks.Count; i++)
        {
            var go = hitRocks[i];
            if (go == null) continue;
            float y = go.transform.position.y;
            if (y < bestY) { bestY = y; best = go; }
        }
        if (best == null) return null;

        if (preferred != null && preferred != best && hitRocks.Contains(preferred)
            && preferred.transform.position.y <= bestY + switchMargin)
            return preferred;

        return best;
    }
}