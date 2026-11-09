using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 轨道环绕运动（均匀分布 + 图层切换）
///
/// 特点：
///   1. omega 恒定 = orbitSpeed / avgR，不随鱼数量变
///   2. phaseOffset = 2π / 群数量 × 索引 → 均匀排开一圈，不聚集
///   3. separation 推开后投影回轨道法向 → 消除切线分量累积加速（鱼越多越快的 bug 根因）
///   4. 可选背景图层切换：切线方向 y 分量判断鱼应该在前景还是后景
/// </summary>
public class 鱼运动_轨道环绕 : 鱼运动
{
    [Header("轨道设置")]
    [Tooltip("环绕方向：勾选=顺时针，不勾=逆时针")]
    public bool orbitClockwise = true;

    [Tooltip("环绕中心（世界坐标）。(0,0)=自动取相机画面中心")]
    public Vector2 orbitCenter = Vector2.zero;

    [Tooltip("椭圆长半轴（世界单位）。0=自动取相机画面宽度的45%。长=短=圆轨道")]
    public float semiMajor = 0f;

    [Tooltip("椭圆短半轴（世界单位）。0=自动取相机画面高度的45%。长=短=圆轨道")]
    public float semiMinor = 0f;

    [Tooltip("整群沿轨道游动的线速度（世界单位/秒）。恒定，不随鱼数量变化")]
    public float orbitSpeed = 2f;

    [Header("群游设置")]
    [Tooltip("转向平滑速度（度/秒），越大转向越利落")]
    public float turnSpeed = 540f;

    [Tooltip("鱼之间的推开距离（世界单位），防止重叠穿模；0=关闭")]
    public float minDistance = 0.3f;

    [Header("背景图层切换")]
    [Tooltip("前景 Sorting Layer 名（鱼在画面前景时用）。空=不切换")]
    public string layerNormal = "Foreground";

    [Tooltip("后景 Sorting Layer 名（鱼在画面后景时用）。空=不切换")]
    public string layerHidden = "Background";

    [Tooltip("切线方向 y 分量 < 此阈值时切后景，> 此阈值时切前景")]
    [Range(-1f, 1f)]
    public float hiddenDirectionThreshold = -0.1f;

    [Tooltip("切换迟滞：已经在后景时要切回来需要 ty > threshold + hysteresis。填角度值（度）会自动转 sin")]
    public float hiddenHysteresis = 0.1f;

    // ========== 每条鱼自己的状态 ==========
    private string prefabBaseName;
    private float phaseOffset;       // 均匀分布：2π / 群数量 × 索引 + 轻微随机

    // ========== 静态群共享（按预制体名分组） ==========
    private static Dictionary<string, List<鱼运动_轨道环绕>> schoolGroups = new Dictionary<string, List<鱼运动_轨道环绕>>();
    private static Dictionary<string, float> groupPhase = new Dictionary<string, float>();   // 全群共享的累计相位
    private static Dictionary<string, int> _groupCount = new Dictionary<string, int>();      // 检测群数量变化触发整群重分布

    // ========== 遮挡图层 ==========
    private int layerNormalID, layerHiddenID;
    private SpriteRenderer[] spriteRenderers;
    private int[] normalSortingOrder;
    private bool wasHidden = false;

    // ==================== 生命周期 ====================

    protected override void Start()
    {
        base.Start();
        if (rb != null) { rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f; }

        prefabBaseName = gameObject.name.Replace("(Clone)", "").Trim();
        if (string.IsNullOrEmpty(prefabBaseName)) prefabBaseName = fishName;

        if (!schoolGroups.ContainsKey(prefabBaseName))
        {
            schoolGroups[prefabBaseName] = new List<鱼运动_轨道环绕>();
            groupPhase[prefabBaseName] = Random.Range(0f, Mathf.PI * 2f);
        }
        if (!schoolGroups[prefabBaseName].Contains(this))
            schoolGroups[prefabBaseName].Add(this);

        phaseOffset = Random.Range(-0.02f, 0.02f);

        // 图层初始化
        layerNormalID = string.IsNullOrEmpty(layerNormal) ? SortingLayer.NameToID("Default") : SortingLayer.NameToID(layerNormal);
        layerHiddenID = string.IsNullOrEmpty(layerHidden) ? layerNormalID : SortingLayer.NameToID(layerHidden);
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        normalSortingOrder = new int[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            normalSortingOrder[i] = spriteRenderers[i] != null ? spriteRenderers[i].sortingOrder : 0;
        wasHidden = false;

        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    private void OnDestroy() { RemoveFromSchool(); }
    protected override void OnReturnToPool() { RemoveFromSchool(); }

    void RemoveFromSchool()
    {
        if (string.IsNullOrEmpty(prefabBaseName) || !schoolGroups.ContainsKey(prefabBaseName)) return;
        schoolGroups[prefabBaseName].Remove(this);
    }

    protected override void OnFishReset()
    {
        if (rb != null) { rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f; }
        if (!schoolGroups.ContainsKey(prefabBaseName))
        {
            schoolGroups[prefabBaseName] = new List<鱼运动_轨道环绕>();
            groupPhase[prefabBaseName] = Random.Range(0f, Mathf.PI * 2f);
        }
        if (!schoolGroups[prefabBaseName].Contains(this))
            schoolGroups[prefabBaseName].Add(this);

        phaseOffset = Random.Range(-0.02f, 0.02f);
        wasHidden = false;
        if (spriteRenderers != null)
            for (int i = 0; i < spriteRenderers.Length; i++)
                spriteRenderers[i].sortingLayerID = layerNormalID;

        transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));
    }

    /// <summary>群数量变化时，整群所有鱼重新均匀分布 phaseOffset</summary>
    static void RedistributeAllPhases(string baseName)
    {
        if (!schoolGroups.TryGetValue(baseName, out var g) || g == null || g.Count == 0) return;
        int n = g.Count;
        float step = Mathf.PI * 2f / n;
        for (int i = 0; i < g.Count; i++)
        {
            var f = g[i];
            if (f == null) continue;
            f.phaseOffset = step * i + Random.Range(-0.02f, 0.02f);
        }
    }

    public override string GetModeName() => "轨道环绕（均匀+图层）";

    // ==================== 主运动 ====================

    protected override void HandleMovement()
    {
        // === 群数量变化 → 整群重新均匀分布 ===
        if (schoolGroups.TryGetValue(prefabBaseName, out var g) && g != null)
        {
            int cur = g.Count;
            if (!_groupCount.ContainsKey(prefabBaseName) || _groupCount[prefabBaseName] != cur)
            {
                _groupCount[prefabBaseName] = cur;
                RedistributeAllPhases(prefabBaseName);
            }
        }

        // 1) 解析轨道参数（0 = 自动取相机画面）
        Vector2 center = orbitCenter;
        float a = Mathf.Max(1f, semiMajor);
        float b = Mathf.Max(1f, semiMinor);

        Camera cam = Camera.main;
        if (cam != null)
        {
            if (orbitCenter == Vector2.zero)
                center = new Vector2(cam.transform.position.x, cam.transform.position.y);
            if (semiMajor <= 0f) { float ch = cam.orthographic ? cam.orthographicSize * 2f : 10f; a = Mathf.Max(2f, ch * cam.aspect * 0.45f); }
            if (semiMinor <= 0f) { float ch = cam.orthographic ? cam.orthographicSize * 2f : 10f; b = Mathf.Max(2f, ch * 0.45f); }
        }
        else { a = Mathf.Max(2f, a); b = Mathf.Max(2f, b); }

        // 2) 推进全群共享相位
        // omega = orbitSpeed / 平均半径（线速度恒定，鱼数量不影响 omega → 鱼多变慢/快的 bug 根治）
        float avgR = (a + b) * 0.5f;
        float omega = orbitSpeed / Mathf.Max(0.5f, avgR);
        if (orbitClockwise) omega = -omega;

        if (!groupPhase.ContainsKey(prefabBaseName))
            groupPhase[prefabBaseName] = Random.Range(0f, Mathf.PI * 2f);

        groupPhase[prefabBaseName] += omega * Time.deltaTime;
        float sharedPhase = groupPhase[prefabBaseName];

        // 3) 这条鱼的轨道相位和目标点 + 切线方向
        float selfPhase = sharedPhase + phaseOffset;
        Vector2 targetPos = center + new Vector2(a * Mathf.Cos(selfPhase), b * Mathf.Sin(selfPhase));

        Vector2 tangent = orbitClockwise
            ? new Vector2(a * Mathf.Sin(selfPhase), -b * Mathf.Cos(selfPhase))
            : new Vector2(-a * Mathf.Sin(selfPhase), b * Mathf.Cos(selfPhase));
        if (tangent.sqrMagnitude > 0.0001f) tangent.Normalize();

        // 4) 位置：直接定位到轨道 + separation 推开 → 投影回法向消除切线分量累积加速
        //    separation 推开方向可能带切线分量 → 鱼越多越快的 bug 根因
        Vector2 outward = targetPos - center;
        if (outward.sqrMagnitude > 0.0001f) outward.Normalize();

        transform.position = targetPos;
        if (minDistance > 0.01f) ApplySeparation();

        Vector2 deviation = (Vector2)transform.position - targetPos;
        float normalOffset = Vector2.Dot(deviation, outward);
        transform.position = targetPos + outward * normalOffset;

        // 5) 转向切线方向
        SmoothTurnToTangent(tangent);

        // 6) 遮挡图层切换：切线 y 分量决定前景/后景
        if (tangent.sqrMagnitude > 0.0001f && spriteRenderers != null && spriteRenderers.Length > 0)
        {
            float hyst = hiddenHysteresis;
            if (Mathf.Abs(hyst) > 1f) hyst = Mathf.Sin(hyst * Mathf.Deg2Rad);
            float tY = tangent.y;
            bool shouldHide;
            if (!wasHidden) shouldHide = tY < hiddenDirectionThreshold - hyst;
            else             shouldHide = tY < hiddenDirectionThreshold + hyst;

            if (shouldHide != wasHidden)
            {
                wasHidden = shouldHide;
                int layerID = shouldHide ? layerHiddenID : layerNormalID;
                for (int i = 0; i < spriteRenderers.Length; i++)
                {
                    if (spriteRenderers[i] == null) continue;
                    spriteRenderers[i].sortingLayerID = layerID;
                    spriteRenderers[i].sortingOrder = normalSortingOrder[i];
                }
            }
        }
    }

    // ==================== 辅助 ====================

    void ApplySeparation()
    {
        if (!schoolGroups.TryGetValue(prefabBaseName, out var g)) return;
        for (int i = g.Count - 1; i >= 0; i--)
        {
            var o = g[i];
            if (o == null) { g.RemoveAt(i); continue; }
            if (o == this) continue;
            if (!o.gameObject.activeInHierarchy) continue;

            Vector2 d = (Vector2)o.transform.position - (Vector2)transform.position;
            float dist = d.magnitude;
            if (dist < minDistance && dist > 0.0001f)
            {
                float push = (minDistance - dist) * 0.5f;
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
