using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 椭圆轨道运动（带群游特性）
/// 轨道参数（大小/倾斜/中心）随鱼游动圈数正弦呼吸变化，画面更有动感
/// 游动方向朝下时自动切到背景 Sorting Layer + sortingOrder=-9999，穿透障碍物
/// </summary>
public class 鱼运动_椭圆轨道 : 鱼运动
{
    [Header("椭圆轨道设置（基准值）")]
    [Tooltip("环绕方向：勾选=顺时针，不勾=逆时针")]
    public bool orbitClockwise = true;

    [Tooltip("椭圆中心（世界坐标）。(0,0)=自动取相机画面中心")]
    public Vector2 orbitCenter = Vector2.zero;

    [Tooltip("椭圆长半轴（世界单位）。0=自动取相机画面宽度的45%")]
    public float ellipseSemiMajor = 0f;

    [Tooltip("椭圆短半轴（世界单位）。0=自动取相机画面高度的45%")]
    public float ellipseSemiMinor = 0f;

    [Tooltip("椭圆倾斜角基准（度）。0=水平")]
    [Range(-90f, 90f)] public float ellipseTilt = 0f;

    [Header("轨道呼吸（随圈数变化）")]
    [Tooltip("长半轴呼吸振幅。0=不变，1=每圈长半轴在 基准±1 之间呼吸")]
    public float majorBreath = 0.5f;

    [Tooltip("短半轴呼吸振幅")]
    public float minorBreath = 0.5f;

    [Tooltip("倾斜角摆动振幅（度）")]
    [Range(0f, 45f)] public float tiltBreath = 8f;

    [Tooltip("中心漂移振幅（世界单位）")]
    public float centerBreath = 0.5f;

    [Tooltip("呼吸频率。1=转1圈呼吸1次，0.5=2圈呼吸1次，2=每圈呼吸2次")]
    public float breathRate = 0.5f;

    [Header("群游设置")]
    [Tooltip("整群沿轨道游动的速度（世界单位/秒）")]
    public float schoolOrbitSpeed = 2f;

    [Tooltip("转向平滑速度（度/秒），越大转向越利落")]
    public float turnSpeed = 540f;

    [Tooltip("鱼之间的推开距离（世界单位），防止重叠穿模")]
    public float minDistance = 0.3f;

    [Tooltip("位置拉向轨道目标的速度（越大越快贴合轨道）")]
    public float trackPullSpeed = 20f;

    [Header("遮挡切换（方向驱动）")]
    [Tooltip("游动方向Y分量小于此值时切背景层。0=正朝下切")]
    public float hiddenDirectionThreshold = 0f;

    [Tooltip("缓冲带（切线Y分量的绝对值差）：防图层切换抖动，默认0.05")]
    public float hiddenHysteresis = 0.05f;

    [Tooltip("正常可见时的 Sorting Layer 名称")]
    public string layerNormal = "fish";

    [Tooltip("被遮挡时的 Sorting Layer 名称（Default 层比 back ground 层更靠后渲染）")]
    public string layerHidden = "Default";

    [Tooltip("被遮挡时的 sortingOrder（越小越靠后渲染）")]
    public int hiddenSortingOrder = -9999;

    // ========== 每条鱼自己的状态 ==========
    private string prefabBaseName;
    private float phaseOffset;

    // ========== 静态群共享 ==========
    private static Dictionary<string, List<鱼运动_椭圆轨道>> schoolGroups = new Dictionary<string, List<鱼运动_椭圆轨道>>();
    private static Dictionary<string, float> groupLeaderPhase = new Dictionary<string, float>();      // 累计相位（不 mod，用来算呼吸）

    // ========== 遮挡图层 ==========
    private int layerNormalID, layerHiddenID;
    private SpriteRenderer[] spriteRenderers;
    private int[] normalSortingOrder;
    private bool wasHidden;

    // ==================== 生命周期 ====================

    protected override void Start()
    {
        base.Start();
        if (rb != null) { rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f; }

        prefabBaseName = gameObject.name.Replace("(Clone)", "").Trim();
        if (string.IsNullOrEmpty(prefabBaseName)) prefabBaseName = fishName;

        if (!schoolGroups.ContainsKey(prefabBaseName))
        {
            schoolGroups[prefabBaseName] = new List<鱼运动_椭圆轨道>();
            groupLeaderPhase[prefabBaseName] = 0f;
        }
        if (!schoolGroups[prefabBaseName].Contains(this))
            schoolGroups[prefabBaseName].Add(this);

        phaseOffset = Random.Range(-0.02f, 0.02f);

        layerNormalID = SortingLayer.NameToID(layerNormal);
        layerHiddenID = SortingLayer.NameToID(layerHidden);
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        wasHidden = false;
        normalSortingOrder = new int[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            normalSortingOrder[i] = spriteRenderers[i] != null ? spriteRenderers[i].sortingOrder : 0;

        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    private void OnDestroy() { RemoveFromSchool(); }
    protected override void OnReturnToPool() { RemoveFromSchool(); }

    void RemoveFromSchool()
    {
        if (!string.IsNullOrEmpty(prefabBaseName) || !schoolGroups.ContainsKey(prefabBaseName)) return;
        schoolGroups[prefabBaseName].Remove(this);
    }

    protected override void OnFishReset()
    {
        if (rb != null) { rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f; }
        if (!schoolGroups.ContainsKey(prefabBaseName))
        {
            schoolGroups[prefabBaseName] = new List<鱼运动_椭圆轨道>();
            groupLeaderPhase[prefabBaseName] = 0f;
        }
        if (!schoolGroups[prefabBaseName].Contains(this))
            schoolGroups[prefabBaseName].Add(this);

        phaseOffset = Random.Range(-0.02f, 0.02f);

        wasHidden = false;
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        normalSortingOrder = new int[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            normalSortingOrder[i] = spriteRenderers[i] != null ? spriteRenderers[i].sortingOrder : 0;

        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    public override string GetModeName() => "椭圆轨道群游（呼吸）";

    // ==================== 主运动 ====================

    protected override void HandleMovement()
    {
        // 1) 解析基准参数
        Vector2 baseCenter = orbitCenter;
        float baseA = Mathf.Max(1f, ellipseSemiMajor);
        float baseB = Mathf.Max(1f, ellipseSemiMinor);

        Camera cam = Camera.main;
        if (cam != null)
        {
            if (orbitCenter == Vector2.zero)
                baseCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
            if (ellipseSemiMajor <= 0f) { float ch = cam.orthographic ? cam.orthographicSize * 2f : 10f; baseA = Mathf.Max(2f, ch * cam.aspect * 0.45f); }
            if (ellipseSemiMinor <= 0f) { float ch = cam.orthographic ? cam.orthographicSize * 2f : 10f; baseB = Mathf.Max(2f, ch * 0.45f); }
        }
        else { baseA = Mathf.Max(2f, baseA); baseB = Mathf.Max(2f, baseB); }

        // 2) 群领袖相位推进（累计值，不 mod，用来驱动呼吸）
        float totalCount = schoolGroups.ContainsKey(prefabBaseName) ? schoolGroups[prefabBaseName].Count : 1;
        float avgR = (baseA + baseB) * 0.5f;
        float omega = schoolOrbitSpeed / Mathf.Max(0.5f, avgR) / Mathf.Max(1f, totalCount);
        if (orbitClockwise) omega = -omega;

        if (!groupLeaderPhase.ContainsKey(prefabBaseName))
            groupLeaderPhase[prefabBaseName] = 0f;

        groupLeaderPhase[prefabBaseName] += omega * Time.deltaTime;
        float rawPhase = groupLeaderPhase[prefabBaseName];  // 累计相位，可无限增长

        // 3) 轨道呼吸变化（用累计相位 + breathRate 驱动正弦）
        //    呼吸周期 = 2π / (breathRate * 2π) = 1 / breathRate 圈
        float breathPhase = rawPhase * breathRate;
        float a = baseA + Mathf.Sin(breathPhase) * majorBreath;
        float b = baseB + Mathf.Sin(breathPhase) * minorBreath;
        float tiltDeg = ellipseTilt + Mathf.Sin(breathPhase * 0.5f) * tiltBreath;  // 倾斜角更慢摆动
        Vector2 center = baseCenter + new Vector2(
            Mathf.Sin(breathPhase * 0.7f) * centerBreath,
            Mathf.Sin(breathPhase * 1.3f) * centerBreath * 0.6f);  // XY 不同频漂移更自然

        a = Mathf.Max(0.5f, a);
        b = Mathf.Max(0.5f, b);
        float tiltRad = tiltDeg * Mathf.Deg2Rad;
        float cosT = Mathf.Cos(tiltRad);
        float sinT = Mathf.Sin(tiltRad);

        // 4) 先更新位置（Lerp 到轨道目标 + separation 推开）
        float currentPhase = rawPhase + phaseOffset;
        float lx = a * Mathf.Cos(currentPhase);
        float ly = b * Mathf.Sin(currentPhase);
        Vector2 targetPos = center + new Vector2(lx * cosT - ly * sinT, lx * sinT + ly * cosT);

        transform.position = Vector2.Lerp(transform.position, targetPos, Time.deltaTime * trackPullSpeed);
        ApplySeparation();

        // 5) 轨道几何切线（纯，不混 Lerp/呼吸干扰）
        Vector2 localTangent = orbitClockwise
            ? new Vector2(a * Mathf.Sin(currentPhase), -b * Mathf.Cos(currentPhase))
            : new Vector2(-a * Mathf.Sin(currentPhase), b * Mathf.Cos(currentPhase));
        Vector2 tangent = new Vector2(
            localTangent.x * cosT - localTangent.y * sinT,
            localTangent.x * sinT + localTangent.y * cosT);
        if (tangent.sqrMagnitude > 0.0001f) tangent.Normalize();

        SmoothTurnToTangent(tangent);

        // 6) 遮挡图层切换：用切线 ty 判断
        float tY = tangent.sqrMagnitude > 0.0001f ? tangent.y : 0f;
        // 兼容旧预制体：如果 hiddenHysteresis > 1，说明是旧的角度值，自动转成 sin(角度) 数值
        float hyst = hiddenHysteresis;
        if (Mathf.Abs(hyst) > 1f) hyst = Mathf.Sin(hyst * Mathf.Deg2Rad);
        bool shouldHide;
        if (!wasHidden) shouldHide = tY < hiddenDirectionThreshold - hyst;
        else             shouldHide = tY < hiddenDirectionThreshold + hyst;

        if (shouldHide != wasHidden)
        {
            if (spriteRenderers == null || spriteRenderers.Length == 0)
            {
                spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
                normalSortingOrder = new int[spriteRenderers.Length];
                for (int i = 0; i < spriteRenderers.Length; i++)
                    normalSortingOrder[i] = spriteRenderers[i] != null ? spriteRenderers[i].sortingOrder : 0;
            }

            int targetLayer = shouldHide ? layerHiddenID : layerNormalID;
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                var sr = spriteRenderers[i];
                if (sr == null) continue;
                sr.sortingLayerID = targetLayer;
                sr.sortingOrder = shouldHide ? hiddenSortingOrder : normalSortingOrder[i];
            }
            wasHidden = shouldHide;
        }
    }

    // ==================== 辅助 ====================

    void ApplySeparation()
    {
        if (!schoolGroups.ContainsKey(prefabBaseName)) return;
        var g = schoolGroups[prefabBaseName];
        for (int i = g.Count - 1; i >= 0; i--)
        {
            鱼运动_椭圆轨道 o = g[i];
            if (o == null) { g.RemoveAt(i); continue; }
            if (o == this) continue;
            if (!o.gameObject.activeInHierarchy) continue;
            Vector2 d = (Vector2)o.transform.position - (Vector2)transform.position;
            float dist = d.magnitude;
            if (dist < minDistance && dist > 0.0001f)
            {
                float push = (minDistance - dist) * 0.3f;
                transform.position -= (Vector3)(d / dist * push);
            }
        }
    }

    void SmoothTurnToTangent(Vector2 tangent)
    {
        if (tangent.sqrMagnitude < 0.0001f) return;
        float targetAngle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        const float h = 0.05f;
        if (isFacingLeft && tangent.x > h) isFacingLeft = false;
        else if (!isFacingLeft && tangent.x < -h) isFacingLeft = true;
        float absX = PrefabAbsScaleX;
        float targetScaleX = isFacingLeft ? absX : -absX;
        Vector3 scale = transform.localScale;
        scale.x = Mathf.MoveTowards(scale.x, targetScaleX, Mathf.Max(absX, 1f) * 8f * Time.deltaTime);
        transform.localScale = scale;
        float finalAngle = isFacingLeft ? targetAngle + 180f : targetAngle;
        Quaternion targetQuat = Quaternion.Euler(0, 0, finalAngle);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetQuat, turnSpeed * Time.deltaTime);
    }

    protected override void ObstacleHitResponse(Transform obstacle) { }
    private void OnTriggerStay2D(Collider2D other) { }
    private void OnCollisionStay2D(Collision2D collision) { }
}
