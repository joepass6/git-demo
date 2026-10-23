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

    // 私有字段
    private Color originalImageColor;
    private Color originalTextColor;
    private GameObject currentDragObject;
    private Camera mainCam;
    private bool canPlace = true;
    private bool isDragging = false;
    private GameObject currentHoverRock; // 当前预览体正悬停的石头（用于切换 SetVisible/SetOutline）

    // ===== 桌面窗口拖动排除（DesktopWindowController 调用）=====
    // 图标可能没开 Raycast Target，EventSystem 射线扫不到，所以这里直接登记实例供窗口脚本查询
    static readonly System.Collections.Generic.List<DragAndPlaceSystem> all = new System.Collections.Generic.List<DragAndPlaceSystem>();
    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { all.Remove(this); }

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

        mainCam = Camera.main;
        Debug.Log($"[DragAndPlace] targetImage={targetImage}, targetText={targetText}, mainCam={mainCam}, prefab={prefabToSpawn}");
    }

    void Update()
    {
        // ===== 检查可放置状态（不改变颜色，颜色只在拖动时变）=====
        CheckCanPlace();

        // ===== 拖拽逻辑 =====
        if (isDragging && currentDragObject != null)
        {
            // 拖拽中：预览物体跟随鼠标
            Vector3 worldPos = mainCam.ScreenToWorldPoint(Input.mousePosition);
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
            // 检测鼠标是否点中了这个UI物体（无论数量是否充足都可以开始拖动）
            if (IsPointerOverThisUI())
            {
                BeginDrag();
            }
        }
    }

    /// <summary>
    /// 预览体每帧 OverlapBox，找当前悬停的 placementTag 石头。
    /// 切换石头时自动 SetVisible/SetOutline。
    /// </summary>
    void UpdateHoverRock()
    {
        GameObject newRock = null;

        Collider2D[] myCols = currentDragObject.GetComponentsInChildren<Collider2D>();
        HashSet<Collider2D> checkedOthers = new HashSet<Collider2D>();
        if (myCols.Length > 0)
        {
            foreach (var col in myCols)
            {
                if (col == null) continue;
                Collider2D[] overlaps = Physics2D.OverlapBoxAll(col.bounds.center, col.bounds.size, 0f);
                foreach (var other in overlaps)
                {
                    if (checkedOthers.Contains(other)) continue;
                    checkedOthers.Add(other);
                    if (other.transform.IsChildOf(currentDragObject.transform) || other.gameObject == currentDragObject) continue;
                    if (other.CompareTag(placementTag)) { newRock = other.gameObject; break; }
                }
                if (newRock != null) break;
            }
        }
        else
        {
            // 兜底：SpriteRenderer bounds
            SpriteRenderer sr = currentDragObject.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                Collider2D[] hits = Physics2D.OverlapAreaAll(sr.bounds.min, sr.bounds.max);
                foreach (var other in hits)
                {
                    if (other.transform.IsChildOf(currentDragObject.transform) || other.gameObject == currentDragObject) continue;
                    if (other.CompareTag(placementTag)) { newRock = other.gameObject; break; }
                }
            }
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

        // 拖动开始：数量不足时变红提示
        if (!canPlace)
        {
            UpdateColor();
            Debug.LogWarning("[DragAndPlace] 珊瑚不足，拖动中显示红色提示");
        }

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
        Vector3 worldPos = mainCam.ScreenToWorldPoint(Input.mousePosition);
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

        // 用物体的体积检测（和搬家功能一样）：碰到带 placementTag 的物体才能放置
        Vector3 placePos = currentDragObject.transform.position;
        Collider2D target = null;
        Collider2D[] myCols = currentDragObject.GetComponentsInChildren<Collider2D>();
        HashSet<Collider2D> checkedOthers = new HashSet<Collider2D>();

        Debug.Log($"[DragAndPlace] 体积检测: 位置={placePos}, 自身碰撞器数量={myCols.Length}");

        if (myCols.Length == 0)
        {
            // 兜底：预制体没带Collider2D的话用SpriteRenderer包围盒退化成OverlapArea
            SpriteRenderer previewSr = currentDragObject.GetComponentInChildren<SpriteRenderer>();
            if (previewSr == null)
            {
                Debug.LogWarning("[DragAndPlace] 预览物体既没有Collider2D也没有SpriteRenderer，无法做体积检测!");
                Destroy(currentDragObject);
                currentDragObject = null;
                return;
            }
            Bounds b = previewSr.bounds;
            Collider2D[] fallbackHits = Physics2D.OverlapAreaAll(b.min, b.max);
            foreach (var other in fallbackHits)
            {
                if (checkedOthers.Contains(other)) continue;
                checkedOthers.Add(other);
                if (other.transform.IsChildOf(currentDragObject.transform) || other.gameObject == currentDragObject) continue;
                if (other.CompareTag(placementTag)) { target = other; break; }
            }
        }
        else
        {
            foreach (var col in myCols)
            {
                if (col == null) continue;
                Collider2D[] overlaps = Physics2D.OverlapBoxAll(col.bounds.center, col.bounds.size, 0f);
                foreach (var other in overlaps)
                {
                    if (checkedOthers.Contains(other)) continue;
                    checkedOthers.Add(other);
                    if (other.transform.IsChildOf(currentDragObject.transform) || other.gameObject == currentDragObject) continue;
                    if (other.CompareTag(placementTag))
                    {
                        target = other;
                        Debug.Log($"[DragAndPlace] 命中目标: {target.name}");
                        break;
                    }
                }
                if (target != null) break;
            }
        }

        // ====== 关键修复：成功判定必须同时满足三个条件 ======
        // 1. target != null（碰到了石头）
        // 2. canPlace（玩家珊瑚道具数量够）
        // 3. RockCapacity.IsAvailable(target)（目标石头还有空位，没满！）
        bool hasRoom = target != null && RockCapacity.IsAvailable(target.gameObject);
        bool allPass = target != null && canPlace && hasRoom;

        Debug.Log($"[DragAndPlace] 放置判定: target={target?.name}, canPlace={canPlace}, hasRoom={hasRoom}, pass={allPass}");

        if (allPass)
        {
            Debug.Log($"[DragAndPlace] 放置成功! 目标: {target.name}");

            // 1. 销毁预览物体
            Destroy(currentDragObject);

            // 2. 放置成功才生成真实物体，位置用预览物体当前位置（Start正常运行，会生成鱼）
            currentDragObject = Instantiate(prefabToSpawn, placePos, Quaternion.identity);
            Debug.Log($"[DragAndPlace] 生成真实物体: {currentDragObject.name}");

            // 3. 设置图层为最靠前（递增排序值，后生成的在前面）
            SetToTopLayer(currentDragObject);

            // 4. 直接触发放置特效（不依赖Start，确保一定播放）
            PlayPlaceEffect(currentDragObject);

            // 5. 触发技能施放器（如果预制体上挂了 SkillCaster）
            TriggerCoralSkill(currentDragObject, target);

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
            if (target == null)
                Debug.LogWarning("[DragAndPlace] 放置失败: 没碰到石头");
            else if (!canPlace)
                Debug.LogWarning("[DragAndPlace] 放置失败: 珊瑚道具数量不足");
            else if (!hasRoom)
                Debug.LogWarning("[DragAndPlace] 放置失败: 石头已满! 目标=" + target.name);
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
    /// 播放放置特效（查找所有粒子系统并播放，带调试日志）
    /// </summary>
    void PlayPlaceEffect(GameObject obj)
    {
        ParticleSystem[] allPs = obj.GetComponentsInChildren<ParticleSystem>(true);
        Debug.Log($"[DragAndPlace] 查找特效: 在 {obj.name} 上找到 {allPs.Length} 个 ParticleSystem");

        if (allPs.Length == 0)
        {
            Debug.LogWarning($"[DragAndPlace] 未找到 ParticleSystem! 请确认特效是粒子系统类型");
            return;
        }

        foreach (var ps in allPs)
        {
            if (ps != null)
            {
                ps.gameObject.SetActive(true);
                ps.Play();
                Debug.Log($"[DragAndPlace] 已播放特效: {ps.gameObject.name}");
            }
        }
    }

    /// <summary>
    /// 触发技能：查找新放置物体上的 SkillCaster，施放它引用的技能定义
    /// </summary>
    void TriggerCoralSkill(GameObject placedCoral, Collider2D rockCollider)
    {
        if (placedCoral == null || rockCollider == null) return;

        var caster = placedCoral.GetComponent<SkillCaster>();
        if (caster == null)
        {
            Debug.Log($"[DragAndPlace] {placedCoral.name} 没有挂载 SkillCaster，跳过技能触发");
            return;
        }

        Debug.Log($"[DragAndPlace] 触发技能: {placedCoral.name} → 石头 {rockCollider.gameObject.name}");

        try
        {
            caster.Cast(rockCollider.gameObject);
            Debug.Log($"[DragAndPlace]   技能施放完毕");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DragAndPlace]   技能施放出错: {e.Message}");
        }
    }
}