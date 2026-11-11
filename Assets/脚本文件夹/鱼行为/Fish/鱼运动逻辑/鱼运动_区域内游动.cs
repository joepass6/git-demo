using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 区域内游动：只在指定 Tag 的物体碰撞器范围内自由游动（完全继承自由游动方式）
/// 游到多个同 Tag 物体的交界处时，会丝滑地游到其它同 Tag 物体上（多个区域视为一整片连通区域）
/// 离开所有同 Tag 物体的范围时，自动平滑转向游回最近的区域内部
/// </summary>
public class 鱼运动_区域内游动 : 鱼运动
{
    public enum AreaMode
    {
        [Tooltip("模式一：在目标物体碰撞器内部自由游动（当前行为）")]
        Inside,
        [Tooltip("模式二：始终沿目标物体碰撞器的外表面游动")]
        Surface
    }

    [Header("区域内游动设置")]
    [Tooltip("区域模式")]
    public AreaMode areaMode = AreaMode.Inside;

    [Tooltip("要在其中移动的物体的 Tag（可跨多个相同 Tag 的物体丝滑游动）")]
    public string zoneTag = "coral";

    [Tooltip("出界转向速度（度/秒，越大转向越快，Surface 模式也控制切线对齐速度）")]
    public float steerSpeed = 360f;

    [Tooltip("同 Tag 物体列表的刷新间隔（秒，物体增减后自动更新）")]
    public float zoneRefreshInterval = 2f;

    [Tooltip("碰到障碍物时的偏转角度（度，实际偏转为该角度内的随机值，填180=最大随机掉头）")]
    public float obstacleTurnAngle = 45f;

    [Tooltip("【Surface 模式】鱼身体半宽度（世界单位）。鱼中心离表面 = 半宽 + 缝隙，使鱼左侧身体刚好贴墙")]
    public float halfBodyWidth = 0.12f;

    [Tooltip("【Surface 模式】身体一侧离表面的缝隙（世界单位，0=完全贴墙，0.01=轻微缝隙）")]
    public float surfaceGap = 0.01f;

    [Tooltip("【Surface 模式】表面粘连半径（世界单位）。离开当前物体超过此距离才切到下一个物体；设大一点可跨物体接缝不断连")]
    public float surfaceStickRange = 0.1f;

    private Collider2D[] zones = new Collider2D[0];  // 所有同 Tag 物体的碰撞器
    private float zoneTimer;

    // Surface 模式的运行时状态：用来跨物体保持连续运动
    private Collider2D currentSurface = null!;  // 当前贴的物体（= null! 抑制 CS8618 未初始化警告）
    private Vector2 lastSurfaceTangent;  // 上一帧沿这个物体的切线方向（跨物体时带过来保持连续）
    private bool hasLastTangent;

    protected override void Start()
    {
        base.Start();
        RefreshZones();
    }

    public override string GetModeName() => "区域内游动";

    /// <summary>
    /// 对象池复用时的重置（Start只在首次激活执行）：立即刷新区域列表
    /// </summary>
    protected override void OnFishReset()
    {
        RefreshZones();
        // Surface 状态清空，对象池复用时上一条鱼的 currentSurface / lastTangent 不再沿用
        currentSurface = null;
        hasLastTangent = false;
        lastSurfaceTangent = Vector2.zero;
    }

    protected override void HandleMovement()
    {
        // 定期刷新同 Tag 物体列表（物体生成/销毁后自动更新）
        zoneTimer += Time.deltaTime;
        if (zoneTimer >= zoneRefreshInterval)
        {
            zoneTimer = 0f;
            RefreshZones();
        }
        if (zones == null || zones.Length == 0) return;

        if (areaMode == AreaMode.Surface) { HandleSurface(); return; }

        // ===== 模式一（Inside）：完全继承自由游动方式，沿当前朝向前进 =====
        Vector2 forward = GetForward2D();
        if (forward.sqrMagnitude > 0.0001f)
        {
            transform.position += (Vector3)(forward * moveSpeed * Time.deltaTime);
        }

        // 在任意同 Tag 物体范围内 → 自由游动，不干预（跨物体交界自然丝滑通过）
        // 游出所有范围 → 平滑转向最近的区域内部
        if (!IsInsideAnyZone(transform.position))
        {
            SteerBackToNearestZone();
        }
    }

    /// <summary>
    /// 模式二（Surface）：始终沿目标物体碰撞器外表面游动
    /// 关键机制：
    /// 1) currentSurface 记住当前贴的物体——只有离它超过 surfaceStickRange 才切到下一个最近物体
    /// 2) lastSurfaceTangent 跨物体带过来——切线方向连续，接缝处不会抖动、不会突然翻向
    /// 3) 切线方向用 dot 选，但只在"方向变化不大"时才对齐，否则保持当前前进方向
    /// </summary>
    void HandleSurface()
    {
        Vector2 pos = transform.position;

        // 1) 决定沿哪个物体表面走：优先当前物体，只有离开够远才换
        Collider2D best = null;
        float bestSqrDist = float.MaxValue;
        foreach (var z in zones)
        {
            if (z == null) continue;
            Vector2 bestCp = z.ClosestPoint(pos);
            float d = ((Vector2)pos - bestCp).sqrMagnitude;
            if (d < bestSqrDist) { bestSqrDist = d; best = z; }
        }
        if (best == null) return;

        // 粘连：优先保持 currentSurface（跨物体接缝时不会跳）
        Collider2D targetCol = currentSurface;
        if (targetCol != null)
        {
            Vector2 curCp = targetCol.ClosestPoint(pos);
            float curDist = ((Vector2)pos - curCp).sqrMagnitude;
            // 粘住条件：还在粘连半径内 OR 比新的最近物体更近
            float stickSqr = surfaceStickRange * surfaceStickRange;
            if (curDist > stickSqr && bestSqrDist < curDist * 0.8f)
            {
                targetCol = best;  // 换物体
                hasLastTangent = false;  // 新物体的切线重新定（但马上会用 fish 当前朝向对齐，不会翻）
            }
        }
        else
        {
            targetCol = best;  // 首次进入
        }

        // 2) 算目标物体上的表面点 + 外法线
        // 关键：不管鱼在内部还是外部，outward 必须指向"从中心→表面→外部"的方向
        Vector2 cp = targetCol.ClosestPoint(pos);
        bool inside = targetCol.OverlapPoint(pos);
        Vector2 outward;
        if (inside)
        {
            // center→cp 方向 = 向外（cp 在表面上，center 在内部，所以 cp-center 是从表面向外的方向）
            outward = cp - (Vector2)targetCol.bounds.center;
        }
        else
        {
            // pos 在外部，pos-cp 就是向外
            outward = pos - cp;
        }
        if (outward.sqrMagnitude < 0.0001f) outward = Vector2.up;
        outward.Normalize();

        // 3) 把鱼拉到表面外：鱼中心 = cp + outward * (halfBodyWidth + surfaceGap)
        // 这样鱼的**左侧**身体（鱼左边方向 = -outward）刚好贴在表面 cp 上
        Vector2 targetPos = cp + outward * (halfBodyWidth + surfaceGap);
        if (((Vector2)transform.position - targetPos).sqrMagnitude > 0.00001f)
        {
            transform.position = (Vector3)targetPos;
        }

        // 4) 算该表面的两条切线，选方向（跨物体时用上一帧切线延续）
        Vector2 t1 = new Vector2(-outward.y, outward.x);
        Vector2 t2 = new Vector2(outward.y, -outward.x);

        // 切线方向选择优先级：上一帧切线 > 当前朝向
        Vector2 refDir;
        if (hasLastTangent) { refDir = lastSurfaceTangent; }
        else                { refDir = GetForward2D(); if (refDir.sqrMagnitude < 0.0001f) refDir = Vector2.right; }

        Vector2 tangent = (Vector2.Dot(refDir, t1) >= Vector2.Dot(refDir, t2)) ? t1 : t2;
        if (tangent.sqrMagnitude > 0.0001f) lastSurfaceTangent = tangent;

        currentSurface = targetCol;

        // 5) 对齐视觉朝向 + 沿 tangent 前进（注意：前进方向直接用 tangent，不要用 GetForward2D()）
        // 原因：RotateTowards 是渐进的（steerSpeed 360°/秒 → 本帧最多 5-6°），
        //   如果用 newFwd=GetForward2D() 前进，对齐前的中间朝向 ≠ tangent → 鱼侧着/背着游（"用鱼背移动"）
        // 修法：前进方向硬约束为 tangent，视觉朝向继续渐进对齐，两者解耦
        //
        // 朝向角由"腹部贴表面"唯一确定（素材朝左、腹部在贴图局部 -y）：
        //   旧逻辑只按 tangent.x 决定镜像，竖直墙面上 tangent.x 恒为 0 → 向下走时腹部朝外（背贴墙）
        float upAngle = Mathf.Atan2(-outward.y, -outward.x) * Mathf.Rad2Deg + 90f;

        // 选镜像：未镜像时素材头朝局部 -x，镜像后朝 +x，取与前进切线同向的那个
        float upRad = upAngle * Mathf.Deg2Rad;
        Vector2 headUnmirrored = new Vector2(-Mathf.Cos(upRad), -Mathf.Sin(upRad));
        bool needMirror = Vector2.Dot(headUnmirrored, tangent) < 0f;
        if (needMirror == isFacingLeft) FlipDirection();   // isFacingLeft=true 表示不镜像

        Quaternion targetQuat = Quaternion.Euler(0f, 0f, upAngle);
        if (Quaternion.Angle(transform.rotation, targetQuat) > 1f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, steerSpeed * Time.deltaTime);
        }

        // 前进：直接用 tangent，而不是 GetForward2D()（视觉朝向可能还没完全对齐）
        transform.position += (Vector3)(tangent * moveSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 刷新同 Tag 物体的碰撞器列表
    /// </summary>
    void RefreshZones()
    {
        GameObject[] objs;
        try { objs = GameObject.FindGameObjectsWithTag(zoneTag); }
        catch { zones = new Collider2D[0]; return; }  // Tag 未定义

        List<Collider2D> list = new List<Collider2D>();
        foreach (var o in objs)
        {
            if (o == null || !o.activeInHierarchy) continue;
            Collider2D col = o.GetComponent<Collider2D>();
            if (col != null) list.Add(col);
        }
        zones = list.ToArray();
    }

    /// <summary>
    /// 位置是否在任意一个同 Tag 物体的范围内
    /// </summary>
    bool IsInsideAnyZone(Vector2 pos)
    {
        foreach (var z in zones)
        {
            if (z == null) continue;
            if (z.OverlapPoint(pos)) return true;
        }
        return false;
    }

    /// <summary>
    /// 出界时：找到最近的区域，平滑转向游回其内部
    /// </summary>
    void SteerBackToNearestZone()
    {
        Collider2D nearest = null;
        Vector2 nearestPoint = Vector2.zero;
        float minDist = float.MaxValue;

        foreach (var z in zones)
        {
            if (z == null) continue;
            Vector2 cp = z.ClosestPoint(transform.position);
            float d = ((Vector2)transform.position - cp).sqrMagnitude;
            if (d < minDist)
            {
                minDist = d;
                nearest = z;
                nearestPoint = cp;
            }
        }
        if (nearest == null) return;  // 场景中没有目标物体，不动

        // 目标点 = 区域边界最近点再往区域中心偏移一点（回去后不再顶边界）
        Vector2 inward = ((Vector2)nearest.bounds.center - nearestPoint).normalized;
        if (inward.sqrMagnitude < 0.0001f) inward = Vector2.up;
        Vector2 target = nearestPoint + inward * 0.3f;

        SteerToward(target);
    }

    /// <summary>
    /// 碰到障碍物：在 obstacleTurnAngle 范围内随机偏转（替代基类的180度镜像掉头）
    /// </summary>
    protected override void ObstacleHitResponse(Transform obstacle)
    {
        float turn = Random.Range(-obstacleTurnAngle, obstacleTurnAngle);
        transform.rotation = Quaternion.Euler(0, 0, transform.eulerAngles.z + turn);
    }

    /// <summary>
    /// 平滑转向目标位置（按项目左右朝向约定，丝滑过渡不跳变）
    /// </summary>
    void SteerToward(Vector2 target)
    {
        Vector2 dir = target - (Vector2)transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Quaternion targetQuat;
        if (dir.x >= 0f)
        {
            if (isFacingLeft) FlipDirection();
            targetQuat = Quaternion.Euler(0, 0, angle);
        }
        else
        {
            if (!isFacingLeft) FlipDirection();
            targetQuat = Quaternion.Euler(0, 0, angle + 180f);
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, steerSpeed * Time.deltaTime);
    }
}
   