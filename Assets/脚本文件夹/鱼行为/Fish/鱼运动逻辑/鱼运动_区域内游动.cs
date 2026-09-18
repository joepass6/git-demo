using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 区域内游动：只在指定 Tag 的物体碰撞器范围内自由游动（完全继承自由游动方式）
/// 游到多个同 Tag 物体的交界处时，会丝滑地游到其它同 Tag 物体上（多个区域视为一整片连通区域）
/// 离开所有同 Tag 物体的范围时，自动平滑转向游回最近的区域内部
/// </summary>
public class 鱼运动_区域内游动 : 鱼运动
{
    [Header("区域内游动设置")]

    [Tooltip("要在其中移动的物体的 Tag（可跨多个相同 Tag 的物体丝滑游动）")]
    public string zoneTag = "coral";

    [Tooltip("出界转向速度（度/秒，越大转向越快）")]
    public float steerSpeed = 360f;

    [Tooltip("同 Tag 物体列表的刷新间隔（秒，物体增减后自动更新）")]
    public float zoneRefreshInterval = 2f;

    [Tooltip("碰到障碍物时的偏转角度（度，实际偏转为该角度内的随机值，填180=最大随机掉头）")]
    public float obstacleTurnAngle = 45f;

    private Collider2D[] zones = new Collider2D[0];  // 所有同 Tag 物体的碰撞器
    private float zoneTimer;

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

        // 完全继承自由游动方式：沿当前朝向前进（统一用GetForward2D）
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
