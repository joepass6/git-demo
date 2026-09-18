using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 群游：同预制体名的鱼自动分为一群
/// 状态1（孤单）：完全继承自由游动方式（沿当前朝向前进，碰障碍自己随机转向）
/// 状态2（有同类但还没聚拢）：加速朝最近的同类游过去（平滑转向同类）
/// 状态3（已聚拢）：一起移动（沿共享方向前进，不每帧调整朝向）
///           可以有重叠但不能完全重合（只在完全重合时推开位置）
///           碰到障碍物时一条鱼转向 → 整群一起转向
/// </summary>
public class 鱼运动_群游 : 鱼运动
{
    [Header("群游设置")]

    [Tooltip("群游鱼之间的间距（进入这个距离算聚拢，调大更容易聚拢成团）")]
    public float schoolSeparationDistance = 1.0f;

    [Tooltip("群游鱼一起游动的速度（聚拢后用这个速度）")]
    public float schoolSwimSpeed = 2f;

    [Tooltip("加速游向同类时的速度倍数（大于1表示加速，如1.5=1.5倍速）")]
    public float schoolGatherSpeedMultiplier = 1.5f;

    [Tooltip("转向平滑速度（度/秒，越大转向越快越利落）")]
    public float schoolTurnSpeed = 540f;

    [Tooltip("完全重合距离（小于这个距离才推开位置，允许重叠但不完全重合）")]
    public float schoolMinDistance = 0.1f;

    [Header("活动区域设置")]
    [Tooltip("活动区域的Tag（鱼游出该区域会自动平滑转向游回，防止孤单鱼沿直线游出屏幕消失）")]
    public string swimAreaTag = "SpawnArea";

    [Tooltip("出界后转向游回的速度（度/秒，越大转向越快）")]
    public float areaSteerSpeed = 360f;

    [Tooltip("离画面边缘的边距（世界单位），防止鱼半个身子贴在画面边缘")]
    public float viewMargin = 0.5f;

    private string prefabBaseName;
    private static List<Bounds> sharedSwimBoundsList = new List<Bounds>();  // 全局共享的活动范围缓存（避免每条鱼各自刷新）
    private bool hasGathered;  // 这条鱼是否已聚拢
    private Coroutine schoolTurnRoutine;  // 整群转向协程（防止多个协程叠加打架）
    private float lastDeflectTime;  // 上次随机偏转时间（状态1/2避障限频用）

    // 活动区域全局刷新标记和间隔（用 Time.time 比较，避免 N 条鱼同时累加计时器导致刷新频率错乱）
    private static float lastSharedAreaRefreshTime;
    const float SharedAreaRefreshInterval = 2f;

    // 群游共享状态（按预制体名分组，同群同步）
    private static Dictionary<string, Vector2> schoolDirections = new Dictionary<string, Vector2>();
    private static Dictionary<string, List<鱼运动_群游>> schoolGroups = new Dictionary<string, List<鱼运动_群游>>();
    private static Dictionary<string, float> schoolFlipCooldowns = new Dictionary<string, float>(); // 整群掉头冷却

    protected override void Start()
    {
        base.Start();

        // 预制体基础名（群游分组用，实例化后名字为 "预制体名(Clone)"）
        prefabBaseName = gameObject.name.Replace("(Clone)", "").Trim();
        if (string.IsNullOrEmpty(prefabBaseName)) prefabBaseName = fishName;

        // 初始化该群的共享状态（仅第一条鱼初始化）
        if (!schoolDirections.ContainsKey(prefabBaseName))
        {
            schoolDirections[prefabBaseName] = Random.insideUnitCircle.normalized;
            schoolFlipCooldowns[prefabBaseName] = 0f;
            schoolGroups[prefabBaseName] = new List<鱼运动_群游>();
        }
        if (!schoolGroups[prefabBaseName].Contains(this))
            schoolGroups[prefabBaseName].Add(this);

        // 缓存活动区域（鱼游出该区域时自动游回，保证一直在画面内）
        RefreshSwimArea();
        if (sharedSwimBoundsList.Count > 0)
            Debug.Log($"[群游] {fishName} 有效活动范围: x[{sharedSwimBoundsList[0].min.x:F1}~{sharedSwimBoundsList[0].max.x:F1}] y[{sharedSwimBoundsList[0].min.y:F1}~{sharedSwimBoundsList[0].max.y:F1}]，共{sharedSwimBoundsList.Count}个区域");

        // 初始随机朝向（孤单时自由游动）
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    private void OnDestroy()
    {
        RemoveFromSchool();
    }

    void RemoveFromSchool()
    {
        if (!string.IsNullOrEmpty(prefabBaseName) && schoolGroups.ContainsKey(prefabBaseName) && schoolGroups[prefabBaseName] != null)
            schoolGroups[prefabBaseName].Remove(this);
    }

    /// <summary>
    /// 回池前的清理：退出群组（OnFishReset 复用时会重新登记）
    /// </summary>
    protected override void OnReturnToPool()
    {
        RemoveFromSchool();
    }

    /// <summary>
    /// 对象池复用时的重置（Start只在首次激活执行）：退出聚拢状态、确保已登记进群、刷新活动区域
    /// </summary>
    protected override void OnFishReset()
    {
        hasGathered = false;
        lastDeflectTime = 0f;
        if (schoolTurnRoutine != null) { StopCoroutine(schoolTurnRoutine); schoolTurnRoutine = null; }

        if (!string.IsNullOrEmpty(prefabBaseName))
        {
            if (!schoolGroups.ContainsKey(prefabBaseName))
            {
                schoolDirections[prefabBaseName] = Random.insideUnitCircle.normalized;
                schoolFlipCooldowns[prefabBaseName] = 0f;
                schoolGroups[prefabBaseName] = new List<鱼运动_群游>();
            }
            if (!schoolGroups[prefabBaseName].Contains(this))
                schoolGroups[prefabBaseName].Add(this);
        }

        RefreshSwimArea();
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    public override string GetModeName() => "群游";

    protected override void HandleMovement()
    {
        // 初始化共享状态（防止 Start 未初始化的情况）
        if (!schoolDirections.ContainsKey(prefabBaseName))
        {
            schoolDirections[prefabBaseName] = Random.insideUnitCircle.normalized;
            schoolFlipCooldowns[prefabBaseName] = 0f;
            schoolGroups[prefabBaseName] = new List<鱼运动_群游>();
            if (!schoolGroups[prefabBaseName].Contains(this))
                schoolGroups[prefabBaseName].Add(this);
        }

        // ===== 检测同类 =====
        List<鱼运动_群游> group = schoolGroups[prefabBaseName];
        Vector2 nearestPos = Vector2.zero;
        float nearestDist = float.MaxValue;
        int sameKindCount = 0;

        for (int i = group.Count - 1; i >= 0; i--)
        {
            鱼运动_群游 other = group[i];
            if (other == null) { group.RemoveAt(i); continue; }
            if (other == this) continue;
            if (!other.gameObject.activeInHierarchy) continue;

            sameKindCount++;
            float dist = Vector2.Distance(transform.position, other.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestPos = other.transform.position;
            }
        }

        // 统一用GetForward2D计算前进方向（不再依赖localScale.x）
        Vector2 forward = GetForward2D();

        // 定期刷新活动区域（全局只刷新一次，所有鱼共享结果）
        if (Time.time - lastSharedAreaRefreshTime >= SharedAreaRefreshInterval)
        {
            lastSharedAreaRefreshTime = Time.time;
            RefreshSwimArea();
        }

        // 游出活动区域 → 继续前进的同时平滑转向游回区域内（防止鱼游出玩家能看到的画面）
        if (sharedSwimBoundsList.Count > 0 && !IsInsideAnyArea(transform.position))
        {
            if (hasGathered)
            {
                // 已聚拢：整群一起掉头往回游（各自转向会把群搅散，还容易在边界来回蹭）
                FlipSchoolDirection();
                transform.position += (Vector3)(schoolDirections[prefabBaseName] * schoolSwimSpeed * Time.deltaTime);
            }
            else
            {
                if (forward.sqrMagnitude > 0.0001f)
                    transform.position += (Vector3)(forward * moveSpeed * Time.deltaTime);
                SteerBackIntoArea();
            }
            return;
        }

        if (sameKindCount == 0)
        {
            // ===== 状态1：孤单 → 完全继承自由游动（不调整朝向）=====
            hasGathered = false;
            if (forward.sqrMagnitude > 0.0001f)
            {
                transform.position += (Vector3)(forward * moveSpeed * Time.deltaTime);
            }
        }
        else if (!hasGathered && nearestDist > schoolSeparationDistance)
        {
            // ===== 状态2：有同类但还没聚拢 → 加速朝最近的同类游 =====
            if (forward.sqrMagnitude > 0.0001f)
            {
                transform.position += (Vector3)(forward * moveSpeed * schoolGatherSpeedMultiplier * Time.deltaTime);
            }
            // 平滑转向同类位置
            SmoothTurnTowardPosition(nearestPos);
        }
        else
        {
            // ===== 状态3：已聚拢 → 一起移动 =====
            if (!hasGathered)
            {
                // 刚聚拢：设置朝向共享方向，和群体保持一致
                hasGathered = true;
                SetRotationToDirection(schoolDirections[prefabBaseName]);
            }

            // 检查是否脱离群体（离最近同类太远则回到状态2重新聚拢）
            if (nearestDist > schoolSeparationDistance * 2f)
            {
                hasGathered = false;
            }
            else
            {
                // 弱间距分离：只在完全重合时推开位置（允许重叠，但不完全重合）
                for (int i = group.Count - 1; i >= 0; i--)
                {
                    鱼运动_群游 other = group[i];
                    if (other == null) { group.RemoveAt(i); continue; }
                    if (other == this) continue;
                    if (!other.gameObject.activeInHierarchy) continue;

                    Vector2 toOther = (Vector2)other.transform.position - (Vector2)transform.position;
                    float dist = toOther.magnitude;
                    if (dist < schoolMinDistance && dist > 0.0001f)
                    {
                        float push = (schoolMinDistance - dist) * 0.5f;
                        transform.position -= (Vector3)(toOther / dist * push);
                    }
                }

                // 全群沿同一个共享方向移动（同时、同速、同向，保证整群一起游）
                // 不能用各自的forward：转向途中每条鱼角度不同，会各游各的散开
                Vector2 schoolDir = schoolDirections[prefabBaseName];
                transform.position += (Vector3)(schoolDir * schoolSwimSpeed * Time.deltaTime);
            }
        }
    }

    /// <summary>
    /// 刷新有效活动范围 = 生成区域 ∩ 相机画面
    /// 生成框比画面大的部分鱼游过去玩家也看不见，直接排除掉，保证鱼永远在画面内
    /// </summary>
    void RefreshSwimArea()
    {
        sharedSwimBoundsList.Clear();

        // ===== 相机可见范围 =====
        Bounds view = default(Bounds);
        bool hasView = false;
        Camera cam = Camera.main;
        if (cam != null)
        {
            float w, h;
            if (cam.orthographic)
            {
                h = cam.orthographicSize * 2f;
                w = h * cam.aspect;
            }
            else
            {
                float dist = Mathf.Abs(cam.transform.position.z);
                if (dist < 0.01f) dist = 10f;
                Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, dist));
                Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, dist));
                w = tr.x - bl.x;
                h = tr.y - bl.y;
            }
            // 留出边距，防止鱼半个身子贴在画面外
            w = Mathf.Max(0.5f, w - viewMargin * 2f);
            h = Mathf.Max(0.5f, h - viewMargin * 2f);
            view = new Bounds(new Vector3(cam.transform.position.x, cam.transform.position.y, 0f), new Vector3(w, h, 1f));
            hasView = true;
        }

        // ===== 收集生成区域（ObjectSpawner手动指定的 + Tag补充的）=====
        List<Bounds> areas = new List<Bounds>();
        ObjectSpawner[] spawners = FindObjectsOfType<ObjectSpawner>();
        foreach (var s in spawners)
        {
            if (s != null && s.spawnArea != null)
                areas.Add(ToFlatBounds(s.spawnArea.bounds));
        }
        GameObject[] objs;
        try { objs = GameObject.FindGameObjectsWithTag(swimAreaTag); }
        catch { objs = new GameObject[0]; }  // Tag 未定义
        foreach (var o in objs)
        {
            if (o == null || !o.activeInHierarchy) continue;
            Collider2D col = o.GetComponent<Collider2D>();
            if (col != null) areas.Add(ToFlatBounds(col.bounds));
        }

        // 没配置生成区域：直接用相机画面作为活动范围
        if (areas.Count == 0)
        {
            if (hasView) sharedSwimBoundsList.Add(view);
            return;
        }

        // ===== 有效范围 = 生成区域 ∩ 相机画面 =====
        foreach (var a in areas)
        {
            if (!hasView) { sharedSwimBoundsList.Add(a); continue; }

            float minX = Mathf.Max(a.min.x, view.min.x);
            float maxX = Mathf.Min(a.max.x, view.max.x);
            float minY = Mathf.Max(a.min.y, view.min.y);
            float maxY = Mathf.Min(a.max.y, view.max.y);
            if (maxX > minX && maxY > minY)
            {
                sharedSwimBoundsList.Add(new Bounds(
                    new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f),
                    new Vector3(maxX - minX, maxY - minY, 1f)));
            }
        }
    }

    /// <summary>
    /// 把碰撞器bounds转成z=0平面的2D包围盒（避免z轴影响判断）
    /// </summary>
    Bounds ToFlatBounds(Bounds b)
    {
        return new Bounds(new Vector3(b.center.x, b.center.y, 0f), new Vector3(b.size.x, b.size.y, 1f));
    }

    /// <summary>
    /// 位置是否在任意一个有效活动范围内
    /// </summary>
    bool IsInsideAnyArea(Vector2 pos)
    {
        Vector3 p = new Vector3(pos.x, pos.y, 0f);
        foreach (var b in sharedSwimBoundsList)
        {
            if (b.Contains(p)) return true;
        }
        return false;
    }

    /// <summary>
    /// 游出活动区域时：转向最近的区域边界点并往内部偏移（回去后不再顶边界）
    /// </summary>
    void SteerBackIntoArea()
    {
        Vector2 p = transform.position;
        Bounds nearest = default(Bounds);
        Vector2 nearestPoint = Vector2.zero;
        float minDist = float.MaxValue;
        bool found = false;

        foreach (var b in sharedSwimBoundsList)
        {
            Vector2 cp = new Vector2(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.y, b.min.y, b.max.y));
            float d = (p - cp).sqrMagnitude;
            if (d < minDist) { minDist = d; nearest = b; nearestPoint = cp; found = true; }
        }
        if (!found) return;

        // 目标点 = 边界最近点再往区域中心偏移一点（回去后不再顶边界）
        Vector2 inward = ((Vector2)nearest.center - nearestPoint).normalized;
        if (inward.sqrMagnitude < 0.0001f) inward = Vector2.up;
        SteerToward(nearestPoint + inward * 0.3f);
    }

    /// <summary>
    /// 平滑转向目标位置（带左右翻转，丝滑过渡不跳变）
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
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, areaSteerSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 平滑转向目标位置（状态2用，避免突然跳变）
    /// </summary>
    void SmoothTurnTowardPosition(Vector3 target)
    {
        Vector2 dir = target - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // 带滞后决定朝向（防止 dir.x 在0附近抖动导致来回翻转）
        if (isFacingLeft && dir.x > 0.05f) isFacingLeft = false;
        else if (!isFacingLeft && dir.x < -0.05f) isFacingLeft = true;

        // 平滑过渡 scale：目标尺寸用预制体初始大小（固定值）
        // 之前用当前缩放当目标：翻转中断/朝向抖动时会越翻越小，最后变成薄片消失
        float absX = PrefabAbsScaleX;
        float targetScaleX = isFacingLeft ? absX : -absX;
        Vector3 scale = transform.localScale;
        scale.x = Mathf.MoveTowards(scale.x, targetScaleX, Mathf.Max(absX, 1f) * 8f * Time.deltaTime);
        transform.localScale = scale;

        // 平滑过渡 rotation
        float finalTargetAngle = isFacingLeft ? targetAngle + 180f : targetAngle;
        Quaternion targetQuat = Quaternion.Euler(0, 0, finalTargetAngle);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, schoolTurnSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 直接设置朝向朝指定方向（聚拢时初始化用）
    /// </summary>
    void SetRotationToDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;

        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (dir.x >= 0f)
        {
            isFacingLeft = false;
            transform.rotation = Quaternion.Euler(0, 0, targetAngle);
        }
        else
        {
            isFacingLeft = true;
            transform.rotation = Quaternion.Euler(0, 0, targetAngle + 180f);
        }
        UpdateScale();
    }

    /// <summary>
    /// 整群掉头：翻转该鱼群的共享方向，让所有已聚拢的鱼转向新方向
    /// </summary>
    void FlipSchoolDirection()
    {
        if (!schoolDirections.ContainsKey(prefabBaseName)) return;
        if (Time.time - schoolFlipCooldowns[prefabBaseName] < 0.5f) return;  // 冷却中
        schoolFlipCooldowns[prefabBaseName] = Time.time;

        // 反向 + 少量随机偏转，避免原路折返卡死
        Vector2 d = -schoolDirections[prefabBaseName];
        d = Quaternion.Euler(0, 0, Random.Range(-30f, 30f)) * d;
        schoolDirections[prefabBaseName] = d.normalized;

        // 让所有已聚拢的鱼同步转向新方向
        if (schoolGroups.ContainsKey(prefabBaseName))
        {
            foreach (var fish in schoolGroups[prefabBaseName])
            {
                if (fish != null && fish.gameObject.activeInHierarchy && fish.hasGathered)
                {
                    // 先停掉上一轮没转完的转向协程再启动新的（协程叠加会互相打架，导致全群转向不同步）
                    if (fish.schoolTurnRoutine != null) fish.StopCoroutine(fish.schoolTurnRoutine);
                    fish.schoolTurnRoutine = fish.StartCoroutine(fish.SmoothTurnToDirection(schoolDirections[prefabBaseName]));
                }
            }
        }
    }

    /// <summary>
    /// 协程：平滑转向目标方向（整群掉头时用）
    /// </summary>
    System.Collections.IEnumerator SmoothTurnToDirection(Vector2 targetDir)
    {
        if (targetDir.sqrMagnitude < 0.0001f) yield break;

        float targetAngle = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;

        // 带滞后决定朝向
        if (isFacingLeft && targetDir.x > 0.05f) isFacingLeft = false;
        else if (!isFacingLeft && targetDir.x < -0.05f) isFacingLeft = true;

        // 目标尺寸用预制体初始大小（固定值），不能用当前缩放（否则中断后越翻越小变成薄片）
        float absX = PrefabAbsScaleX;
        float targetScaleX = isFacingLeft ? absX : -absX;
        float finalTargetAngle = isFacingLeft ? targetAngle + 180f : targetAngle;
        Quaternion targetQuat = Quaternion.Euler(0, 0, finalTargetAngle);

        // 平滑过渡到目标朝向
        float timer = 0f;
        while (timer < 1f)
        {
            timer += Time.deltaTime;

            Vector3 scale = transform.localScale;
            scale.x = Mathf.MoveTowards(scale.x, targetScaleX, Mathf.Max(absX, 1f) * 8f * Time.deltaTime);
            transform.localScale = scale;

            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, schoolTurnSpeed * Time.deltaTime);

            if (Mathf.Abs(transform.localScale.x - targetScaleX) < 0.01f &&
                Quaternion.Angle(transform.rotation, targetQuat) < 1f)
                break;

            yield return null;
        }
    }

    protected override void ObstacleHitResponse(Transform obstacle)
    {
        if (hasGathered)
        {
            // 状态3：整群转向（防止卡住）
            FlipSchoolDirection();
        }
        else
        {
            // 状态1/2：在当前前进方向基础上随机偏转，并同步朝向标记
            // （之前直接设绝对角度，移动方向和朝向脱节：鱼看着朝A走实际朝B，会游出画面=消失）
            // 限频0.4秒：贴在障碍上时Stay每帧触发，不限频会反复乱转=抽搐
            if (Time.time - lastDeflectTime < 0.4f) return;
            lastDeflectTime = Time.time;

            Vector2 dir = Quaternion.Euler(0, 0, Random.Range(-60f, 60f)) * GetForward2D();
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (dir.x >= 0f)
            {
                if (isFacingLeft) FlipDirection();
                transform.rotation = Quaternion.Euler(0, 0, angle);
            }
            else
            {
                if (!isFacingLeft) FlipDirection();
                transform.rotation = Quaternion.Euler(0, 0, angle + 180f);
            }
        }
    }

    // ==================== 贴墙/持续接触处理 ====================
    // Enter只在进入瞬间触发一次：整群贴在障碍物上时再也没有新事件，谁也转不了向=全群卡死
    // Stay持续触发，配合 FlipSchoolDirection 自带0.5秒冷却：卡住时全群每0.5秒尝试掉头直到脱困

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag(fishTag)) return;
        if (other.CompareTag(obstacleTag)) ObstacleHitResponse(other.transform);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag(fishTag)) return;
        if (collision.gameObject.CompareTag(obstacleTag)) ObstacleHitResponse(collision.transform);
    }
}
