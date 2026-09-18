     using UnityEngine;

/// <summary>
/// 自由游动：沿自身朝向直线前进，碰到障碍物后随机旋转改向
/// </summary>
public class 鱼运动_自由游动 : 鱼运动
{
    [Header("自由游动设置")]
    [Tooltip("碰到障碍物后额外旋转的随机角度范围（度）")]
    public float turnRandomAngle = 30f;

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
    }

    protected override void HandleMovement()
    {
        // 旋转矩阵计算前进方向（统一用GetForward2D，不再依赖localScale.x）
        Vector2 forward = GetForward2D();
        if (forward.sqrMagnitude > 0.0001f)
        {
            transform.position += (Vector3)(forward * moveSpeed * Time.deltaTime);
        }
    }

    protected override void ObstacleHitResponse(Transform obstacle)
    {
        base.ObstacleHitResponse(obstacle);
        // 碰障碍后随机旋转改向
        float angle = Mathf.Clamp(Random.Range(-turnRandomAngle, turnRandomAngle), -45f, 45f);
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }
}
