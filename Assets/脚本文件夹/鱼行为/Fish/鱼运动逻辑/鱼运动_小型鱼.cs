using UnityEngine;

/// <summary>
/// 小型鱼：继承自由游动方式（沿当前朝向前进，用 moveSpeed）
/// 先游回生成它的珊瑚（tag=coral）旁边，然后只在珊瑚周围范围内移动
/// 出界时钳制回边界并立刻掉头返回
/// 可穿过障碍物（生成时自动把碰撞器设为触发器、刚体设为运动学）
/// </summary>
public class 鱼运动_小型鱼 : 鱼运动
{
    [Header("小型鱼设置")]
    [Tooltip("珊瑚的 Tag（找不到父挂载物时，按Tag找最近的 coral 作为锚点）")]
    public string coralTag = "coral";

    [Tooltip("活动范围（围绕珊瑚的半径）")]
    public float smallRange = 2f;

    [Tooltip("出界转向时附加的随机角度（让转向更自然）")]
    public float turnRandomAngle = 30f;

    [Tooltip("回到珊瑚身边阶段加速倍率（默认2=两倍速）")]
    public float gatherSpeedMultiplier = 2f;

    [Tooltip("回到珊瑚身边阶段的转向速度（度/秒，越大越快）")]
    public float gatherSteerSpeed = 540f;

    private Transform smallCoralTf;    // 生成它的珊瑚/挂载物
    private bool smallReachedCoral;    // 是否已游回珊瑚旁

    /// <summary>
    /// 由生成器（ObjectSpawner）在 Instantiate 后直接调用，把挂载物传进来
    /// 避免鱼生成在spawnArea随机位置时，距离珊瑚太远找不到
    /// </summary>
    public void SetMountObject(Transform mountTf)
    {
        if (mountTf == null) return;
        smallCoralTf = mountTf;
    }

    /// <summary>
    /// 对象池复用时的重置（Start只在首次激活执行）：重置阶段状态、重新找珊瑚
    /// </summary>
    protected override void OnFishReset()
    {
        smallReachedCoral = false;
        if (smallCoralTf == null) smallCoralTf = FindNearestCoral();
        transform.rotation = Quaternion.identity;
    }

    protected override void Start()
    {
        base.Start();

        transform.rotation = Quaternion.identity;

        // 优先使用生成器传入的挂载物（ObjectSpawner会调用SetMountObject）
        if (smallCoralTf == null)
        {
            smallCoralTf = FindNearestCoral();
        }

        // 可穿过障碍物：碰撞器全部设为触发器 + 刚体设为运动学
        foreach (var c in GetComponentsInChildren<Collider2D>())
            c.isTrigger = true;
        if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;
    }

    public override string GetModeName() => "小型";

    protected override void HandleMovement()
    {
        // 锚点：珊瑚（生成器传入或启动时按Tag找到的）；珊瑚被销毁则退回生成位置
        Vector3 anchor = (smallCoralTf != null) ? smallCoralTf.position : spawnPosition;

        // 继承自由游动方式：沿当前朝向前进（统一用GetForward2D）
        Vector2 forward = GetForward2D();

        if (!smallReachedCoral)
        {
            // ====== 阶段1：加速游回珊瑚旁边 ======
            float distToAnchor = Vector2.Distance(transform.position, anchor);
            if (distToAnchor <= smallRange * 0.5f)
            {
                smallReachedCoral = true;
                // 到达珊瑚后给一个随机旋转，避免只横着游
                transform.rotation = Quaternion.Euler(0, 0, Random.Range(-60f, 60f));
            }
            else
            {
                // 前进：加速（不乘 forward 的 normalized 直接乘倍率）
                if (forward.sqrMagnitude > 0.0001f)
                {
                    transform.position += (Vector3)(forward * moveSpeed * gatherSpeedMultiplier * Time.deltaTime);
                }
                // 平滑转向目标（不每帧直接跳角度，避免鬼畜蠕动）
                TurnTowardPositionSmooth(anchor, 0f);
            }
        }
        else
        {
            // 阶段2：在珊瑚周围范围内自由游动，碰到边界立刻掉头
            if (forward.sqrMagnitude > 0.0001f)
            {
                transform.position += (Vector3)(forward * moveSpeed * Time.deltaTime);
            }

            Vector2 offset = (Vector2)transform.position - (Vector2)anchor;
            float distToAnchor = offset.magnitude;
            if (distToAnchor > smallRange && distToAnchor > 0.0001f)
            {
                Vector2 outward = offset / distToAnchor;

                // 钳制回边界内一点（留10%缓冲，避免贴着边界反复触发）
                transform.position = (Vector2)anchor + outward * (smallRange * 0.9f);

                // 掉头返回：FlipDirection 会翻转运动方向（GetForward2D 取反）
                // 只在"还在往外游"时才翻转：掉头后方向指向圈内，下一帧不再满足条件，
                // 因此每次触边只翻一次，不会在边缘每帧来回翻转
                if (Vector2.Dot(forward, outward) > 0.0001f)
                    FlipDirection();
            }
        }
    }

    /// <summary>
    /// 查找生成它的珊瑚：只找距离生成位置非常近的 coral（阈值0.5）
    /// 确保是生成它的那个珊瑚，不被其他相同tag的物体影响
    /// 找不到则返回null（用生成位置作为锚点）
    /// </summary>
    Transform FindNearestCoral()
    {
        // tag没定义或没物体时返回null
        GameObject[] corals;
        try { corals = GameObject.FindGameObjectsWithTag(coralTag); }
        catch { return null; }
        if (corals == null || corals.Length == 0) return null;

        float minDist = float.MaxValue;
        Transform nearest = null;
        foreach (var c in corals)
        {
            if (c == null || !c.activeInHierarchy) continue;
            float d = Vector3.Distance(c.transform.position, spawnPosition);
            if (d < minDist)
            {
                minDist = d;
                nearest = c.transform;
            }
        }
        return nearest;
    }

    /// <summary>
    /// 平滑转向目标位置（和MoveSystem一致，不直接跳角度）
    /// 先根据dir.x翻转左右朝向，再用RotateTowards平滑过渡到目标旋转
    /// </summary>
    void TurnTowardPositionSmooth(Vector3 target, float extraAngle)
    {
        Vector2 dir = target - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + extraAngle;
        Quaternion targetQuat;

        // 用归一化后的水平分量判断朝向，并留一段死区：
        // 目标几乎在正上/正下时 dir.x 会在 0 附近来回过零，原来会每帧 FlipDirection（左右翻面+旋转目标跳180°），
        // 贴边时尤其明显（切线接近竖直）。死区内保持当前朝向不动，避免抽搐。
        float normX = dir.x / Mathf.Sqrt(dir.sqrMagnitude);
        const float FaceDeadband = 0.2f;

        if (normX > FaceDeadband)
        {
            // 朝右：isFacingLeft=false，旋转=目标角
            if (isFacingLeft) FlipDirection();
            targetQuat = Quaternion.Euler(0, 0, targetAngle);
        }
        else if (normX < -FaceDeadband)
        {
            // 朝左：isFacingLeft=true，旋转=目标角+180
            if (!isFacingLeft) FlipDirection();
            targetQuat = Quaternion.Euler(0, 0, targetAngle + 180f);
        }
        else
        {
            // 接近正上/正下：保持当前朝向，只做平滑旋转
            targetQuat = Quaternion.Euler(0, 0, isFacingLeft ? targetAngle + 180f : targetAngle);
        }

        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, gatherSteerSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 小型鱼可穿过障碍物（碰撞器全是触发器+运动学刚体），不需要避障响应。
    /// 响应障碍会和绕圈转向打架：碰到多个同类物体时反复改朝向=抽搐，所以故意留空
    /// </summary>
    protected override void ObstacleHitResponse(Transform obstacle)
    {
    }

    // 旧方法保留（兼容编译），内部直接调用平滑版
    void TurnTowardPosition(Vector3 target, float extraAngle)
    {
        TurnTowardPositionSmooth(target, extraAngle);
    }
}
