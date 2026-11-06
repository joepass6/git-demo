using UnityEngine;

/// <summary>
/// 固定位置不动：贴在某一个点上，身体随朝向会有轻微呼吸摆动（scaleY 在 0.95~1.05 之间缓动），
/// 整体位置、朝向都不变。适合"锚定"型鱼——比如吸盘鱼、寄居蟹这种基本不动的生物。
/// </summary>
public class 鱼运动_固定位置 : 鱼运动
{
    [Header("固定位置设置")]

    [Tooltip("初始朝向角度（度，0 = 朝右）。运行时点击可以让它转向其他方向")]
    public float fixedAngle = 0f;

    [Tooltip("点击鱼时是否允许它转向点击位置")]
    public bool turnOnClick = true;

    // 点击时的临时转向目标（允许玩家把这条鱼转到其他方向）
    private float? pendingAngle;
    private float breathT;

    void Start()
    {
        transform.rotation = Quaternion.Euler(0, 0, fixedAngle);
        // 锁定 Rigidbody：Kinematic + 全轴位置旋转冻结 → 彻底不受物理/重力影响
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY |
                             RigidbodyConstraints2D.FreezeRotation;
            rb.simulated = false;
        }
    }

    void Update()
    {
        // 身体呼吸：只动 scaleY（身体厚度），整体位置不动
        breathT += Time.deltaTime * 2f;  // 2 是呼吸速度，可以调
        float sy = 1f + Mathf.Sin(breathT) * 0.05f;  // ±5% 幅度
        Vector3 s = transform.localScale;
        s.y = sy;
        transform.localScale = s;

        // 点击允许转向
        if (turnOnClick && Input.GetMouseButtonDown(0))
        {
            Vector3 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            world.z = 0f;
            Vector2 dir = ((Vector2)world - (Vector2)transform.position);
            if (dir.sqrMagnitude < 0.3f * 0.3f)  // 点击命中范围内才转
            {
                float clickAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                pendingAngle = clickAngle;
            }
        }

        // 平滑转向到 pendingAngle
        if (pendingAngle.HasValue)
        {
            float target = pendingAngle.Value;
            float cur = transform.rotation.eulerAngles.z;
            // 规范到 [-180, 180)
            float delta = Mathf.DeltaAngle(cur, target);
            float step = 360f * Time.deltaTime;  // 转向速度 360°/秒
            float newZ;
            if (Mathf.Abs(delta) < step) { newZ = target; pendingAngle = null; }
            else { newZ = cur + Mathf.Sign(delta) * step; }
            transform.rotation = Quaternion.Euler(0, 0, newZ);
        }
    }

    protected override void HandleMovement()
    {
        // 什么都不做 → 位置完全不变
    }

    public override string GetModeName() => "固定位置";
}
