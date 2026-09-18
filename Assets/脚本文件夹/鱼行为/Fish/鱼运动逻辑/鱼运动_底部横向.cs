using UnityEngine;

/// <summary>
/// 底部横向移动：只左右走，Y 固定，碰到左右边界自动掉头
/// </summary>
public class 鱼运动_底部横向 : 鱼运动
{
    [Header("底部横向设置")]
    [Tooltip("固定的Y坐标，0=自动用当前Y值")]
    public float fixedY = 0f;

    [Tooltip("左边界X坐标，碰到自动掉头；-9999=不限制")]
    public float leftBoundaryX = -9999f;

    [Tooltip("右边界X坐标，碰到自动掉头；9999=不限制")]
    public float rightBoundaryX = 9999f;

    private float runtimeY;  // 实际使用的Y（fixedY=0时每次生成/复用时捕获当前位置，不改动检查器里的值）

    protected override void Start()
    {
        base.Start();
        transform.rotation = Quaternion.identity;
        CaptureFixedY();
    }

    public override string GetModeName() => "底部";

    /// <summary>
    /// 对象池复用时的重置（Start只在首次激活执行）：旋转归零、重新捕获固定Y
    /// </summary>
    protected override void OnFishReset()
    {
        transform.rotation = Quaternion.identity;
        CaptureFixedY();
    }

    void CaptureFixedY()
    {
        runtimeY = Mathf.Abs(fixedY) < 0.0001f ? transform.position.y : fixedY;
        Vector3 p = transform.position;
        p.y = runtimeY;
        transform.position = p;
    }

    protected override void HandleMovement()
    {
        // 只动X轴，Y轴固定，不旋转
        Vector3 p = transform.position;
        p.x += (isFacingLeft ? -1f : 1f) * moveSpeed * Time.deltaTime;
        p.y = runtimeY;
        transform.position = p;

        // 边界自动掉头
        if (p.x <= leftBoundaryX && isFacingLeft) { FlipDirection(); }
        else if (p.x >= rightBoundaryX && !isFacingLeft) { FlipDirection(); }
    }
}
