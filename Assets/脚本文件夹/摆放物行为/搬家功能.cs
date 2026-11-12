using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// 搬家系统：让物体（挂载物/鱼）临时跟随鼠标，碰到Obstacle时放下。
/// 支持两种启动方式：
///   1) 将脚本挂在场景物体上，Inspector里配"搬家按钮"UI → 按钮自动绑定事件
///   2) 代码调用 MoveSystem.StartMove(obj, panelToHide) 静态入口（推荐，最可靠）
/// </summary>
public class MoveSystem : MonoBehaviour
{
    // ==================== 互斥标志：全局只有一个物体在搬家 ====================
    public static bool isInMoveMode = false;      // DropItem拾取互斥用
    static MoveSystem activeMover;                // 当前正在搬家的MoveSystem（防止重叠物体被拖多个）

    [Header("放置设置")]
    [Tooltip("只有碰到带此Tag的Collider2D才能放下（例如Obstacle地面）")]
    public string placementTag = "Obstacle";

    [Header("页面")]
    [Tooltip("搬家时自动隐藏的详情页（可选）。隐藏后自动重新显示CanvasGroup")]
    public GameObject uiPanel;

    // ==================== 运行时状态 ====================
    Camera mainCam;
    [HideInInspector]
    public bool isMoveMode;
    Vector3 originalPosition;
    float zDepth;

    Rigidbody2D rb;
    bool rbWasKinematic;
    bool rbWasSimulated;

    MonoBehaviour[] conflictScripts; // 搬家期间临时禁用的运动脚本
    int skipFrames;
    GameObject currentHoverRock;     // 当前悬停的石头

    // sortingOrder + Sorting Layer 抬升/恢复
    SpriteRenderer[] allSr;
    Dictionary<SpriteRenderer, int> originalOrders;
    Dictionary<SpriteRenderer, string> originalLayerNames;

    // ==================== 静态入口（推荐用这个） ====================
    /// <summary>
    /// 让指定的场景物体进入"搬家模式"：飞起来跟随鼠标，放到Obstacle上。
    /// </summary>
    public static bool StartMove(GameObject obj, GameObject panelToHide = null)
    {
        Debug.Log("[MoveSystem] StartMove called: obj=" + (obj != null ? obj.name : "null") + " panel=" + (panelToHide != null ? panelToHide.name : "null") + " isInMoveMode=" + isInMoveMode);

        if (obj == null)
        {
            Debug.LogError("[MoveSystem] StartMove: obj为null!");
            return false;
        }
        if (!obj.scene.IsValid())
        {
            Debug.LogError("[MoveSystem] StartMove: obj是Prefab资源，不是场景实例! name=" + obj.name);
            return false;
        }

        // 强制打断任何正在进行的搬家（包括这个物体自己的）
        if (isInMoveMode)
        {
            Debug.LogWarning("[MoveSystem] 已有物体在搬家，强制打断");
            CancelCurrentMove();
        }

        // 关键修复：如果物体上已有MoveSystem且isMoveMode为true，先强制Disable再Enable
        // 解决prefab上序列化的脏状态（isMoveMode=true导致EnableMoveMode直接return）
        MoveSystem ms = obj.GetComponent<MoveSystem>();
        if (ms != null)
        {
            if (ms.isMoveMode)
            {
                Debug.LogWarning("[MoveSystem] 发现已有MoveSystem且isMoveMode=true（可能是prefab脏状态），强制重置");
                ms.DisableMoveMode();
            }
        }
        else
        {
            Debug.Log("[MoveSystem] 在" + obj.name + "上添加MoveSystem组件");
            ms = obj.AddComponent<MoveSystem>();
        }

        ms.uiPanel = panelToHide;
        ms.EnableMoveMode();
        Debug.Log("[MoveSystem] StartMove完成: obj=" + obj.name + " isInMoveMode=" + isInMoveMode);
        return true;
    }

    /// <summary>
    /// 取消当前正在搬家的物体（外部其他系统想打断搬家时调用）
    /// </summary>
    public static void CancelCurrentMove()
    {
        if (activeMover != null)
        {
            activeMover.transform.position = activeMover.originalPosition;
            activeMover.DisableMoveMode();
            activeMover = null;
        }
        isInMoveMode = false;
    }

    // ==================== 开关搬家模式 ====================
    public void EnableMoveMode()
    {
        Debug.Log("[MoveSystem] EnableMoveMode called on: " + gameObject.name + " isMoveMode=" + isMoveMode);
        if (isMoveMode) return;
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[MoveSystem] EnableMoveMode: Camera.main为null! 请确保场景有MainCamera");
            return;
        }

        originalPosition = transform.position;
        zDepth = originalPosition.z;

        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rbWasKinematic = rb.isKinematic;
            rbWasSimulated = rb.simulated;
            rb.isKinematic = true;
            rb.simulated = false;
        }

        DisableConflictScripts();
        HideInfoPanel();

        // ===== RockCapacity 联动 =====
        RockCapacity.EnsureAll(placementTag);
        RockCapacity.SetCarryIgnore(gameObject); // 搬家期间不要把自己算在石头容量里
        RockCapacity.RefreshAll(placementTag);

        // ===== sortingOrder 抬升：珊瑚搬家时必须在所有场景物体最前面 =====
        BubbleSortingToFront();

        skipFrames = 3;
        isMoveMode = true;
        isInMoveMode = true;
        activeMover = this;

        Debug.Log("[MoveSystem] EnableMoveMode done: " + gameObject.name + " pos=" + transform.position + " isInMoveMode=" + isInMoveMode);
        UpdateSpawnerPos();
    }

    void DisableMoveMode()
    {
        // 注意：不要用 if (!isMoveMode) return; 保护，因为可能被StartMove强制调用来重置脏状态
        isMoveMode = false;
        isInMoveMode = false;
        if (activeMover == this) activeMover = null;
        if (rb != null)
        {
            rb.isKinematic = rbWasKinematic;
            rb.simulated = rbWasSimulated;
        }
        EnableConflictScripts();
        ShowInfoPanel();

        // ===== 恢复 sortingOrder =====
        RestoreSortingOrder();
        // 恢复后必须再抬一次：原始值就是预制体的 order=0，
        // 而场景里的「窗口_1/2/3」是 Obstacle 且在 coral 层 order=2，不抬就会搬完被窗口压住。
        DragAndPlaceSystem.BringToFront(gameObject);

        // ===== RockCapacity 联动：隐藏 UI + 描边 + 取消 carryIgnore + 重算 =====
        if (currentHoverRock != null)
        {
            RockCapacity.SetVisible(currentHoverRock, false);
            RockCapacity.SetOutline(currentHoverRock, false);
            currentHoverRock = null;
        }
        RockCapacity.SetCarryIgnore(null);
        RockCapacity.RefreshAll(placementTag);
    }

    // ==================== 冲突脚本（运动类）临时禁用 ====================
    static readonly HashSet<string> CONFLICT_TYPE_NAMES = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        "鱼运动", "Move", "Movement", "MoveTowards", "AutoMove", "RandomMove",
        "Swim", "SwimMovement", "Patrol", "Follower", "Homing",
        "EnemyMovement", "CharacterController2D", "TopDownController"
    };

    void DisableConflictScripts()
    {
        MonoBehaviour[] all = GetComponentsInChildren<MonoBehaviour>(true);
        List<MonoBehaviour> list = new List<MonoBehaviour>();
        StringBuilder sb = new StringBuilder();
        foreach (var s in all)
        {
            if (s == null) continue;
            System.Type t = s.GetType();
            if (t == typeof(MoveSystem)) continue;

            bool isConflict = CONFLICT_TYPE_NAMES.Contains(t.Name);
            if (!isConflict)
            {
                string tn = t.Name ?? "";
                if (tn.IndexOf("鱼运动", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("move", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("swim", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("patrol", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("follow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isConflict = true;
                }
            }

            if (isConflict)
            {
                list.Add(s);
                sb.Append(s.GetType().Name).Append(", ");
                s.enabled = false;
            }
        }
        conflictScripts = list.ToArray();
        if (sb.Length > 0) sb.Length -= 2;
        Debug.Log("[MoveSystem] 禁用冲突脚本 " + list.Count + " 个: [" + sb + "]");
    }

    void EnableConflictScripts()
    {
        if (conflictScripts == null) return;
        StringBuilder sb = new StringBuilder();
        foreach (var s in conflictScripts)
        {
            if (s == null) continue;
            s.enabled = true;
            sb.Append(s.GetType().Name).Append(", ");
        }
        if (sb.Length > 0) sb.Length -= 2;
        Debug.Log("[MoveSystem] 恢复脚本 " + conflictScripts.Length + " 个: [" + sb + "]");
        conflictScripts = null;
    }

    // ==================== UI页面 隐藏/显示 ====================
    void HideInfoPanel()
    {
        if (uiPanel == null) return;
        CanvasGroup cg = uiPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }
        else
        {
            uiPanel.SetActive(false);
        }
    }

    void ShowInfoPanel()
    {
        if (uiPanel == null) return;
        CanvasGroup cg = uiPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
        else
        {
            uiPanel.SetActive(true);
        }
    }

    // ==================== sortingOrder + Sorting Layer 抬升/恢复 ====================
    /// <summary>
    /// Unity 2D 里 Sorting Layer 名 优先级 > sortingOrder。
    /// 只抬 sortingOrder 不够——如果别的珊瑚/鱼在更高层名（比如"前景" vs "游戏对象"），
    /// 哪怕 sortingOrder 加 100 也会被挡。
    /// 所以搬家时：先找场景里优先级最高的 Sorting Layer 名，把自己的所有 SpriteRenderer
    /// 同时搬到那个层 + sortingOrder 设成 9999（该层内最顶），确保绝对压在所有场景物体前。
    /// Disable 时恢复到原来的层名 + sortingOrder。
    /// UI Canvas 不受影响（用 Canvas.sortingOrder 不走 SpriteRenderer 层）。
    /// </summary>
    void BubbleSortingToFront()
    {
        allSr = GetComponentsInChildren<SpriteRenderer>(true);
        Debug.Log($"[BubbleSortingToFront] {name}: 找到 {allSr?.Length ?? 0} 个 SpriteRenderer");
        if (allSr == null || allSr.Length == 0) return;

        // 先打印自己原来的 sorting 状态
        foreach(var sr in allSr){
            Debug.Log($"  自身 SR: layer={sr.sortingLayerName}(id={sr.sortingLayerID}) order={sr.sortingOrder}");
        }

        // 1) 找场景最高优先级的 Sorting Layer
        // Unity Sorting Layer 的优先级 = SortingLayer.layers 数组索引（越靠后越靠前）
        // uniqueID (sortingLayerID) 是随机 int，**不能用来比优先级**！
        var allLayers = SortingLayer.layers;
        string topLayerName = allLayers.Length > 0 ? allLayers[allLayers.Length - 1].name : "Default";
        int topLayerIndex = allLayers.Length - 1;
        Debug.Log($"[BubbleSortingToFront] 最高优先级层: {topLayerName}（索引 {topLayerIndex}/{allLayers.Length - 1}）");

        // 2) 缓存原始值 + 统一抬到最高层 + sortingOrder 9999
        originalOrders = new Dictionary<SpriteRenderer, int>();
        originalLayerNames = new Dictionary<SpriteRenderer, string>();
        foreach (var sr in allSr)
        {
            if (sr == null) continue;
            originalOrders[sr] = sr.sortingOrder;
            originalLayerNames[sr] = sr.sortingLayerName;
            sr.sortingLayerName = topLayerName;
            sr.sortingOrder = 9999;
            Debug.Log($"  抬升后: layer={sr.sortingLayerName} order={sr.sortingOrder}");
        }
    }

    void RestoreSortingOrder()
    {
        if (originalOrders == null) return;
        foreach (var kv in originalOrders)
        {
            if (kv.Key != null) kv.Key.sortingOrder = kv.Value;
        }
        if (originalLayerNames != null)
        {
            foreach (var kv in originalLayerNames)
            {
                if (kv.Key != null) kv.Key.sortingLayerName = kv.Value;
            }
        }
        originalOrders = null;
        originalLayerNames = null;
        allSr = null;
    }

    // ==================== 跟随鼠标 ====================
    void UpdateSpawnerPos()
    {
        if (mainCam == null) return;
        Vector3 screen = Input.mousePosition;
#if UNITY_EDITOR
        Vector3 world = mainCam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, mainCam.WorldToScreenPoint(transform.position).z));
#else
        float sw = Screen.width, sh = Screen.height;
        float rw = DesktopWindowController.RTWidth > 0 ? DesktopWindowController.RTWidth : mainCam.pixelWidth;
        float rh = DesktopWindowController.RTHeight > 0 ? DesktopWindowController.RTHeight : mainCam.pixelHeight;
        Vector2 scaled = new Vector2(screen.x, screen.y);
        if (sw > 0 && sh > 0 && rw > 0 && rh > 0)
        {
            scaled.x = screen.x * rw / sw;
            scaled.y = screen.y * rh / sh;
        }
        Vector3 world = mainCam.ScreenToWorldPoint(new Vector3(scaled.x, scaled.y, mainCam.WorldToScreenPoint(transform.position).z));
#endif
        transform.position = new Vector3(world.x, world.y, zDepth);
    }

    // ==================== 主循环 ====================
    void Update()
    {
        if (!isMoveMode) return;
        if (mainCam == null) mainCam = Camera.main;

        // 每帧打印一次当前 sortingOrder（调试用，找到就停）
        if (Time.frameCount % 30 == 0)
        {
            var srs = GetComponentsInChildren<SpriteRenderer>(true);
            foreach(var sr in srs){
                Debug.Log($"[MoveSystem Update] {name} 拖家中: SR layer={sr.sortingLayerName} order={sr.sortingOrder}");
            }
        }

        UpdateSpawnerPos();
        if (skipFrames > 0) { skipFrames--; return; }

        // ===== RockCapacity：每帧追踪悬停的石头 =====
        UpdateHoverRock();

        if (Input.GetMouseButtonDown(1))
        {
            transform.position = originalPosition;
            DisableMoveMode();
            Debug.Log("[MoveSystem] 右键取消搬家");
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            TryPlace();
        }
    }

    /// <summary>
    /// 每帧收集底座探针命中的石头 → 选主石头（Y 最靠下 + 迟滞）→ 吸附到石头顶面。
    /// 🔑 与 DragAndPlaceSystem / RockCapacity.RefreshAll 共用同一套
    ///    FindSupportRocks + FindPrimaryRock + SnapDeltaY，保证搬家看到的位置就是松手后的结果。
    /// </summary>
    void UpdateHoverRock()
    {
        List<GameObject> allHit = DragAndPlaceSystem.FindSupportRocks(
            gameObject, placementTag, SkillSystem.SkillCaster.RockTouchTolerance);

        // 和 DragAndPlaceSystem/RefreshAll 用完全相同的 FindPrimaryRock（带迟滞）
        GameObject newRock = DragAndPlaceSystem.FindPrimaryRock(
            allHit, currentHoverRock, DragAndPlaceSystem.PrimarySwitchMargin);

        // 与拖拽放置一致：吸附到石头顶面，松手就是现在看到的位置
        if (newRock != null)
        {
            Vector3 p = transform.position;
            p.y += DragAndPlaceSystem.SnapDeltaY(gameObject, newRock);
            transform.position = p;
        }

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

    // ==================== 放置检测 ====================
    void TryPlace()
    {
        // 🔑 与 EndDrag/RefreshAll 完全相同的底座探针 FindSupportRocks
        List<GameObject> allHit = DragAndPlaceSystem.FindSupportRocks(
            gameObject, placementTag, SkillSystem.SkillCaster.RockTouchTolerance);

        // 和 DragAndPlaceSystem.EndDrag 完全一致：选主石头（Y 最靠下 + 迟滞）→ 检查主石头容量
        GameObject primaryRock = DragAndPlaceSystem.FindPrimaryRock(
            allHit, currentHoverRock, DragAndPlaceSystem.PrimarySwitchMargin);

        // ====== 关键修复：搬家放置也必须检查主石头容量 ======
        if (primaryRock != null && RockCapacity.IsAvailable(primaryRock))
        {
            // 落位吸附：底座贴到石头顶面（UpdateHoverRock 已吸过一次，这里再对齐一次保证落点精确）
            Vector3 p = transform.position;
            p.y += DragAndPlaceSystem.SnapDeltaY(gameObject, primaryRock);
            transform.position = p;

            Debug.Log("[MoveSystem] 放置成功: 主石头 " + primaryRock.name + " (共命中 " + allHit.Count + " 块)");

            // 复用珊瑚放置事件：AudioManager 订阅播音效，教程系统也用
            // 注意：C# event 的 Invoke 只能在声明类内部调，外部通过代理方法触发放置事件
            DragAndPlaceSystem.触发放置事件(gameObject);

            // ===== 先清理白色描边 + UI + 恢复子物体（在 SetCarryIgnore 之前，避免容量判定还在忽略自己）=====
            if (currentHoverRock != null)
            {
                RockCapacity.SetVisible(currentHoverRock, false);   // UI 隐藏 + HighlightChildren(false) 恢复子物体
                RockCapacity.SetOutline(currentHoverRock, false);   // 撤白色描边
                currentHoverRock = null;
            }

            isMoveMode = false;
            isInMoveMode = false;
            if (activeMover == this) activeMover = null;
            if (rb != null) { rb.isKinematic = rbWasKinematic; rb.simulated = rbWasSimulated; }
            EnableConflictScripts();
            ShowInfoPanel();
            // 刷新容量统计：先取消 carryIgnore（自己也算进去），再重算
            RockCapacity.SetCarryIgnore(null);
            RockCapacity.RefreshAll(placementTag);
        }
        else if (primaryRock != null)
        {
            // 碰到了石头但主石头满了 → 不取消搬家，让用户继续找别的石头
            Debug.LogWarning("[MoveSystem] 主石头已满! 继续搬找别的石头: " + primaryRock.name);
        }
        else
        {
            Debug.Log("[MoveSystem] 放置失败: 未碰到Tag=" + placementTag + "的物体，回原位");
            transform.position = originalPosition;
            DisableMoveMode();
        }
    }
}
