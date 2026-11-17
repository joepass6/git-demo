using UnityEngine;

/// <summary>
/// 自由游动：沿自身朝向直线前进，碰到障碍物后随机旋转改向
/// 可选范围限制：指定一个矩形框（BoxCollider2D）后，鱼临近/越界时会提前平滑转向回到框内，
/// 不会瞬移、也不会贴着边蹭出抖动。不指定范围框时行为与原来完全一致。
/// </summary>
public class 鱼运动_自由游动 : 鱼运动
{
    [Header("自由游动设置")]
    [Tooltip("碰到障碍物后额外旋转的随机角度范围（度）")]
    public float turnRandomAngle = 30f;

    [Header("游动范围限制（可选，两种指定方式二选一，留空=不限制）")]
    [Tooltip("范围框引用：把场景里挂了 BoxCollider2D（或其它 Collider2D）的物体拖进来。" +
        "注意：预制体上的引用指不到场景物体，鱼是运行时从预制体生成的请改用下面的 Tag")]
    public Collider2D swimArea;

    [Tooltip("范围框 Tag：鱼是运行时从预制体实例化时用这个找场景里的框（取第一个同名 Tag 的物体）；配了上面的引用则忽略此项")]
    public string swimAreaTag = "";

    [Tooltip("进入前的边距（世界单位）：鱼中心离框边小于这个距离时就开始往回转向，留得越足越不容易贴边")]
    public float areaMargin = 1f;

    [Tooltip("预判距离（世界单位）：沿当前朝向往前探这么远，探到框外就提前折向；0=按速度自动估算（约 0.25 秒的位移）")]
    public float areaLookAhead = 0f;

    [Tooltip("回框转向速度（度/秒）：越大转得越急，建议 180~360")]
    public float areaSteerSpeed = 240f;

    // Tag 方式的查找缓存（和基类 dropTargetTag 一样，避免每帧 FindWithTag）
    Collider2D areaFromTag;
    float areaFromTagTime = -999f;
    const float AreaRefreshInterval = 2f;

    protected override void Start()
    {
        base.Start();
        // 自由模式：初始随机旋转
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    public override string GetModeName() => "自由";

    /// <summary>
    /// 对象池复用时的重置（Start只在首次激活执行）：重新随机初始朝向
    /// </summary>
    protected override void OnFishReset()
    {
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
        // 范围框缓存清空：换了 Tag / 场景里换了框，复用这条鱼时按新配置重新找
        areaFromTag = null;
        areaFromTagTime = -999f;
    }

    protected override void HandleMovement()
    {
        float dt = Time.deltaTime;
        Collider2D area = ResolveSwimArea();
        bool limit = area != null && area.enabled && area.gameObject.activeInHierarchy;

        // 1) 范围限制：临近框边/已经出框时，先把朝向平滑转到框内。
        //    这里只改朝向、不直接改位置，所以不会瞬移；带提前量，鱼不会真的蹭到边再抖。
        if (limit && dt > 0f) SteerBackInsideArea(area, dt);

        // 2) 沿（转向后的）朝向直线前进
        Vector2 forward = GetForward2D();
        if (forward.sqrMagnitude > 0.0001f)
        {
            transform.position += (Vector3)(forward * moveSpeed * dt);
        }

        // 3) 越界兜底：被障碍物/其它外力顶出框外时，按速度上限逐步拉回（同样是平滑的，不是瞬移）
        if (limit && !area.OverlapPoint(transform.position))
        {
            Vector2 p = transform.position;
            Vector2 toInside = area.ClosestPoint(p) - p;
            float maxStep = Mathf.Max(0.01f, moveSpeed) * dt;
            transform.position = p + Vector2.ClampMagnitude(toInside, maxStep);
        }
    }

    protected override void ObstacleHitResponse(Transform obstacle)
    {
        base.ObstacleHitResponse(obstacle);
        // 碰障碍后随机旋转改向
        float angle = Mathf.Clamp(Random.Range(-turnRandomAngle, turnRandomAngle), -45f, 45f);
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    /// <summary>解析范围框：优先用 Inspector 里拖的引用，其次按 Tag 找场景里的框</summary>
    Collider2D ResolveSwimArea()
    {
        if (swimArea != null) return swimArea;
        if (string.IsNullOrEmpty(swimAreaTag)) return null;

        if (areaFromTag != null && Time.time - areaFromTagTime < AreaRefreshInterval) return areaFromTag;

        areaFromTagTime = Time.time;
        areaFromTag = null;
        try
        {
            GameObject go = GameObject.FindWithTag(swimAreaTag);
            if (go != null) areaFromTag = go.GetComponent<Collider2D>();
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[{name}] swimAreaTag \"{swimAreaTag}\" 没有在 Tag 管理器里定义，游动范围限制已忽略");
            swimAreaTag = "";
        }
        return areaFromTag;
    }

    /// <summary>
    /// 让鱼平滑转向框内：
    /// - 还在安全区：往前预判一段，探到「边距内的禁入区」就把对应方向分量翻向框内；
    /// - 已经进到边距区/出了框：朝框内最近的点转（本来就在往回游就不干预）。
    /// 转向用 RotateTowards 按 areaSteerSpeed 渐进；方向只在越界预判成立时翻，翻完目标方向就是朝内的，
    /// 所以不会每帧来回翻造成抖动。
    /// </summary>
    void SteerBackInsideArea(Collider2D area, float dt)
    {
        Bounds b = area.bounds;
        float m = Mathf.Max(0f, areaMargin);
        float minX = b.min.x + m, maxX = b.max.x - m;
        float minY = b.min.y + m, maxY = b.max.y - m;
        // 框比边距还小：可用区收成中线，逻辑仍然成立
        if (minX > maxX) { float cx = b.center.x; minX = cx; maxX = cx; }
        if (minY > maxY) { float cy = b.center.y; minY = cy; maxY = cy; }

        Vector2 pos = transform.position;
        Vector2 forward = GetForward2D();
        Vector2 want;

        bool inMarginZone = pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY;
        if (inMarginZone)
        {
            // 已进入边距区（或已出框）：朝框内最近的点转
            want = new Vector2(Mathf.Clamp(pos.x, minX, maxX), Mathf.Clamp(pos.y, minY, maxY)) - pos;
            // 已经在往回游了就不再干预，避免平行贴边时被硬掰成垂直
            if (Vector2.Dot(forward, want) >= 0f) return;
        }
        else
        {
            // 在安全区内：预判前方一小段，探到禁入区就翻向框内
            float probe = areaLookAhead > 0.01f ? areaLookAhead : Mathf.Max(0.1f, moveSpeed * 0.25f);
            Vector2 ahead = pos + forward * probe;
            want = forward;
            if (ahead.x < minX || ahead.x > maxX) want.x = -want.x;
            if (ahead.y < minY || ahead.y > maxY) want.y = -want.y;
            // 没探到框边：保持原方向，不干预
            if ((want - forward).sqrMagnitude < 0.0001f) return;
        }

        if (want.sqrMagnitude < 0.000001f) return;
        want.Normalize();

        // 平滑转向（沿用项目里其它运动脚本的左右朝向约定：右半平面不镜像，左半平面镜像 + 旋转 180°）
        float angle = Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg;
        Quaternion targetQuat;
        if (want.x >= 0f)
        {
            if (isFacingLeft) FlipDirection();
            targetQuat = Quaternion.Euler(0f, 0f, angle);
        }
        else
        {
            if (!isFacingLeft) FlipDirection();
            targetQuat = Quaternion.Euler(0f, 0f, angle + 180f);
        }

        float steer = Mathf.Max(30f, areaSteerSpeed);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, steer * dt);
    }
}
