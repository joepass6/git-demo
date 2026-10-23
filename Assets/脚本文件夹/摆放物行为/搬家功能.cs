using UnityEngine;
using System.Collections.Generic;
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

    // ==================== 跟随鼠标 ====================
    void UpdateSpawnerPos()
    {
        if (mainCam == null) return;
        Vector3 screen = Input.mousePosition;
        Vector3 world = mainCam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, mainCam.WorldToScreenPoint(transform.position).z));
        transform.position = new Vector3(world.x, world.y, zDepth);
    }

    // ==================== 主循环 ====================
    void Update()
    {
        if (!isMoveMode) return;
        if (mainCam == null) mainCam = Camera.main;

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

    /// <summary>每帧 OverlapBox 找当前悬停的 placementTag 石头</summary>
    void UpdateHoverRock()
    {
        GameObject newRock = null;
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>();
        HashSet<Collider2D> checkedOthers = new HashSet<Collider2D>();

        foreach (var col in myCols)
        {
            if (col == null) continue;
            Collider2D[] overlaps = Physics2D.OverlapBoxAll(col.bounds.center, col.bounds.size, 0f);
            foreach (var other in overlaps)
            {
                if (checkedOthers.Contains(other)) continue;
                checkedOthers.Add(other);
                if (other.transform.IsChildOf(transform) || other.gameObject == gameObject) continue;
                if (other.CompareTag(placementTag)) { newRock = other.gameObject; break; }
            }
            if (newRock != null) break;
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
        Collider2D[] myCols = GetComponentsInChildren<Collider2D>();
        Collider2D hit = null;
        HashSet<Collider2D> checkedOthers = new HashSet<Collider2D>();

        foreach (var col in myCols)
        {
            Collider2D[] overlaps = Physics2D.OverlapBoxAll(col.bounds.center, col.bounds.size, 0f);
            foreach (var other in overlaps)
            {
                if (checkedOthers.Contains(other)) continue;
                checkedOthers.Add(other);
                if (other.transform.IsChildOf(transform) || other.gameObject == gameObject) continue;
                if (other.CompareTag(placementTag)) { hit = other; break; }
            }
            if (hit != null) break;
        }

        // ====== 关键修复：搬家放置也必须检查石头容量 ======
        if (hit != null && RockCapacity.IsAvailable(hit.gameObject))
        {
            Debug.Log("[MoveSystem] 放置成功: 接触 " + hit.gameObject.name);

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
        else if (hit != null)
        {
            // 碰到了石头但满了 → 不取消搬家，让用户继续找别的石头
            Debug.LogWarning("[MoveSystem] 这块石头已满! 继续搬找别的石头: " + hit.gameObject.name);
        }
        else
        {
            Debug.Log("[MoveSystem] 放置失败: 未碰到Tag=" + placementTag + "的物体，回原位");
            transform.position = originalPosition;
            DisableMoveMode();
        }
    }
}
