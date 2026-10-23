using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 鱼运动基类：包含所有鱼的公共功能（掉落、定时销毁、点击粒子、鱼信息、图鉴、数量上限）
/// 运动方式已拆分为5个独立脚本，鱼预制体上挂其中之一：
///   鱼运动_自由游动 | 鱼运动_底部横向 | 鱼运动_区域内游动 | 鱼运动_群游 | 鱼运动_小型鱼
/// </summary>
public class 鱼运动 : MonoBehaviour
{
    // ===== 掉宝图鉴（appearedFishNames + RegisterFish 已在下方定义，这里只补掉宝侧）=====
    static readonly HashSet<string> droppedFishNames = new HashSet<string>();
    public static event System.Action<鱼运动> OnDropProduced;

    public static void RegisterFishDrop(string name) { if(!string.IsNullOrEmpty(name)) droppedFishNames.Add(name); }
    public static bool HasFishDropped(string name) { return !string.IsNullOrEmpty(name) && droppedFishNames.Contains(name); }
    public static void ResetCodex() { appearedFishNames.Clear(); droppedFishNames.Clear(); }

    [Header("移动设置")]
    [Tooltip("鱼的移动速度")]
    public float moveSpeed = 2f;

    [Tooltip("障碍物的 Tag 名称")]
    public string obstacleTag = "Obstacle";

    [Tooltip("鱼的 Tag 名称")]
    public string fishTag = "fish";

    [Header("掉落设置")]
    [Tooltip("每次掉落数量")]
    public int dropCount = 1;

    [Tooltip("掉落间隔（秒）")]
    public float dropInterval = 10f;

    [Tooltip("掉落物品预制件")]
    public GameObject itemPrefab;

    [Tooltip("下落速度")]
    public float fallSpeed = 2f;

    [Header("消失设置")]
    [Tooltip("存在时间（秒），到时自动销毁（0=永不销毁）")]
    public float destroyTime = 30f;

    [Header("性能设置")]
    [Tooltip("全局最大鱼数量（所有鱼共享），超过会自动销毁最早生成的鱼")]
    public static int maxFishCount = 30;

    [Tooltip("最大同时掉落物数量（所有鱼共享），超过会自动清除最旧的掉落物")]
    public static int maxDropItemCount = 50;

    [Header("粒子特效设置")]
    [Tooltip("点击鱼时是否触发粒子特效")]
    public bool enableParticle = true;

    [Tooltip("每次点击发射的粒子数量")]
    public int emitCount = 1;

    [Header("鱼信息")]
    [Tooltip("鱼的名称")]
    public string fishName = "未命名鱼";

    [Tooltip("鱼的图像（用于UI显示）")]
    public Sprite fishSprite;

    [Tooltip("玩耍时间（秒）")]
    public float playTime = 0f;

    [Tooltip("筑宝时间（秒）")]
    public float treasureBuildTime = 0f;

    // ====== 受保护字段（子类可使用） ======
    protected bool isFacingLeft;
    protected Rigidbody2D rb;
    protected float dropTimer;
    protected float spawnTime;
    protected Vector3 spawnPosition;
    protected List<ParticleSystem> allParticles = new List<ParticleSystem>();

    // 预制体初始缩放（避免朝向翻转时把预制体大小覆盖掉）
    private Vector3 prefabScale;

    // 所有存活鱼的队列（按生成顺序）
    private static Queue<鱼运动> aliveFish = new Queue<鱼运动>();

    /// <summary>
    /// 当前场上存活鱼的数量（生成器补鱼前查询全局上限用，查询时顺便清理已销毁/已回池的引用）
    /// </summary>
    public static int AliveFishCount
    {
        get
        {
            PurgeQueue();
            return aliveFish.Count;
        }
    }

    // ==================== 图鉴登记 ====================

    // 静态集合：记录所有在游戏中出现过的鱼名
    private static HashSet<string> appearedFishNames = new HashSet<string>();

    /// <summary>
    /// 登记鱼名到图鉴（鱼出现时调用）
    /// </summary>
    public static void RegisterFish(string name)
    {
        if (!string.IsNullOrEmpty(name) && !appearedFishNames.Contains(name))
        {
            appearedFishNames.Add(name);
            Debug.Log($"[图鉴] 登记新鱼种: {name}, 总数: {appearedFishNames.Count}");
        }
    }

    /// <summary>
    /// 检查某种鱼是否在游戏中出现过
    /// </summary>
    public static bool HasFishAppeared(string name)
    {
        return !string.IsNullOrEmpty(name) && appearedFishNames.Contains(name);
    }

    // ==================== 生命周期 ====================

    protected virtual void Start()
    {
        InitFish();
    }

    /// <summary>
    /// 初始化（首次Start 和 对象池复用时 都会执行）
    /// </summary>
    private void InitFish()
    {
        rb = GetComponent<Rigidbody2D>();

        // 保存预制体初始缩放（第一次UpdateScale之前先记录，之后翻转只改x方向）
        prefabScale = transform.localScale;

        isFacingLeft = Random.value > 0.5f;
        UpdateScale();

        spawnPosition = transform.position;

        GetComponentsInChildren<ParticleSystem>(true, allParticles);

        spawnTime = Time.time;
        RegisterFish(fishName);
        dropTimer = 0f;

        // 到时回池（±15%随机错开：同批鱼同时集体消失又集体重生，看起来就像在快速瞎刷）
        if (destroyTime > 0)
        {
            lifeEndTime = Time.time + destroyTime * Random.Range(0.85f, 1.15f);
        }
        else
        {
            lifeEndTime = 0f;  // 0 = 永不销毁
        }

        // ============ 性能：鱼数量上限（超限的鱼回池而不是销毁） ============
        aliveFish.Enqueue(this);
        PurgeQueue();
        while (aliveFish.Count > maxFishCount && aliveFish.Count > 1)
        {
            鱼运动 oldest = aliveFish.Dequeue();
            if (oldest != null && oldest != this) oldest.ReturnToPool();
        }
    }

    // 到时回池逻辑已移到 Update 中用 lifeEndTime 绝对时间戳判断

    // ==================== 对象池 ====================
    // 鱼不再销毁重建：到时/超上限时隐藏回池，生成器需要时重置复用，省掉反复Instantiate/Destroy的开销

    // 池：来源预制体 → 隐藏中的鱼队列
    private static Dictionary<GameObject, Queue<鱼运动>> fishPools = new Dictionary<GameObject, Queue<鱼运动>>();
    private GameObject sourcePrefab;   // 来源预制体（回池分组用，池化生成时记录）
    private float lifeEndTime;         // 存活结束的绝对时间戳（Time.time + 存活秒数）

    /// <summary>
    /// 从池里取一条同预制体的鱼（池空则新实例化），重置状态后启用
    /// 新实例化的鱼由Unity的Start自动初始化；复用的鱼在这里手动重置（Start不会再执行）
    /// </summary>
    /// <param name="configure">激活/初始化之前对鱼实例的配置回调（用来覆盖 destroyTime/dropCount 等，
    /// 保证池里复用过的旧鱼也能吃到珊瑚技能的最新永久修正）</param>
    public static 鱼运动 SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation,
        System.Action<鱼运动> configure = null)
    {
        鱼运动 f = null;
        Queue<鱼运动> q;
        if (fishPools.TryGetValue(prefab, out q))
        {
            while (q.Count > 0)
            {
                鱼运动 c = q.Dequeue();
                if (c != null && c.gameObject != null) { f = c; break; }
            }
        }

        if (f == null)
        {
            // 池空：新实例化（激活后Start自动初始化）
            GameObject go = Object.Instantiate(prefab, position, rotation);
            f = go.GetComponent<鱼运动>();
            f.sourcePrefab = prefab;
            // Start/InitFish 执行前配置好，新鱼直接用修正值
            configure?.Invoke(f);
        }
        else
        {
            // 复用：摆位 → 配置 → 启用 → 手动初始化（配置必须在 InitFish 前，lifeEndTime 才会按新值算）
            f.transform.position = position;
            f.transform.rotation = rotation;
            configure?.Invoke(f);
            f.gameObject.SetActive(true);
            f.InitFish();
            f.OnFishReset();
        }
        return f;
    }

    /// <summary>
    /// 回池：不销毁，藏起来待复用
    /// </summary>
    public void ReturnToPool()
    {
        if (!gameObject.activeInHierarchy) return;   // 已在池中
        OnReturnToPool();

        if (sourcePrefab == null) { Destroy(gameObject); return; }   // 没有来源记录的鱼（外部生成的）直接销毁

        Queue<鱼运动> q;
        if (!fishPools.TryGetValue(sourcePrefab, out q)) { q = new Queue<鱼运动>(); fishPools[sourcePrefab] = q; }
        q.Enqueue(this);
        gameObject.SetActive(false);
    }

    /// <summary>回池前的子类清理钩子（需要退出群组等清理时重写）</summary>
    protected virtual void OnReturnToPool() { }

    /// <summary>池复用时的子类重置钩子（Start只在首次激活执行，复用必须在这里重做初始化）</summary>
    protected virtual void OnFishReset() { }

    // 清理队列里已销毁/已回池（隐藏）的引用
    private static void PurgeQueue()
    {
        int n = aliveFish.Count;
        for (int i = 0; i < n; i++)
        {
            鱼运动 f = aliveFish.Dequeue();
            if (f != null && f.gameObject.activeInHierarchy) aliveFish.Enqueue(f);
        }
    }

    private void Update()
    {
        HandleMovement();

        // 到期回池（用绝对时间戳，支持 ExtendLife 动态延长/缩短）
        if (lifeEndTime > 0f && Time.time >= lifeEndTime)
        {
            ReturnToPool();
            return;
        }

        dropTimer += Time.deltaTime;
        if (dropTimer >= dropInterval)
        {
            dropTimer = 0;
            Drop();
        }
    }

    /// <summary>
    /// 运动方式：子类重写此方法实现自己的移动逻辑（基类默认不移动）
    /// </summary>
    protected virtual void HandleMovement() { }

    /// <summary>运动方式名称（UI显示用，子类可重写）</summary>
    public virtual string GetModeName() => "鱼";

    // ==================== 点击与粒子 ====================

    private void OnMouseDown()
    {
        if (enableParticle && allParticles.Count > 0)
        {
            int index = Random.Range(0, allParticles.Count);
            ParticleSystem ps = allParticles[index];
            if (ps != null)
            {
                if (!ps.isPlaying) ps.Play();
                ps.Emit(emitCount);
            }
        }
    }

    // ==================== 碰撞处理 ====================

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(fishTag)) return;
        if (other.CompareTag(obstacleTag)) ObstacleHitResponse(other.transform);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag(fishTag)) return;
        if (collision.gameObject.CompareTag(obstacleTag)) ObstacleHitResponse(collision.transform);
    }

    /// <summary>
    /// 碰到障碍物：默认只更新朝向，子类可重写添加额外反应
    /// </summary>
    protected virtual void ObstacleHitResponse(Transform obstacle)
    {
        Vector2 dir = (obstacle.position - transform.position).normalized;
        if (dir.x > 0) isFacingLeft = true;
        else if (dir.x < 0) isFacingLeft = false;
        else isFacingLeft = !isFacingLeft;

        UpdateScale();
    }

    // ==================== 朝向 ====================

    protected void FlipDirection()
    {
        isFacingLeft = !isFacingLeft;
        UpdateScale();
    }

    protected void UpdateScale()
    {
        // 基于预制体初始缩放翻转方向，避免把预制体大小覆盖成1
        Vector3 scale = prefabScale;
        // Mathf.Abs处理初始x为负的情况，只控制左右朝向
        float absX = Mathf.Abs(scale.x);
        scale.x = isFacingLeft ? absX : -absX;
        transform.localScale = scale;
    }

    /// <summary>
    /// 获取当前鱼脸朝向的 2D 单位向量（基于 isFacingLeft + 旋转，不再依赖 localScale.x 的大小）
    /// 所有子类统一调用此方法计算前进方向，避免预制体缩放导致方向/速度错乱
    /// </summary>
    protected Vector2 GetForward2D()
    {
        // 基础水平方向：isFacingLeft = 朝左(-1), 朝右(+1)
        Vector2 baseDir = new Vector2(isFacingLeft ? -1f : 1f, 0f);
        // 乘以当前旋转
        Vector2 rotated = Quaternion.Euler(0, 0, transform.eulerAngles.z) * baseDir;
        if (rotated.sqrMagnitude > 0.0001f) rotated.Normalize();
        return rotated;
    }

    /// <summary>
    /// 预制体初始缩放的绝对X（子类做平滑翻转时的固定目标尺寸）
    /// 不能用当前 localScale.x 当目标：翻转中途被打断或朝向来回抖动时，尺寸会越翻越小最后变成薄片
    /// </summary>
    protected float PrefabAbsScaleX { get { return Mathf.Abs(prefabScale.x); } }

    // ==================== 掉落 ====================

    void Drop()
    {
        for (int i = 0; i < dropCount; i++)
        {
            if (itemPrefab != null)
            {
                GameObject obj = Instantiate(itemPrefab, transform.position, Quaternion.identity);
                Rigidbody2D itemRb = obj.GetComponent<Rigidbody2D>();
                if (itemRb != null) itemRb.velocity = Vector2.down * fallSpeed;
            }
        }
    }

    // ==================== 鱼信息接口 ====================

    /// <summary>
    /// 获取鱼从生成到现在的存活时间（秒），作为玩耍时间（从1开始）
    /// </summary>
    public float GetAliveTime()
    {
        return Time.time - spawnTime + 1f;
    }

    /// <summary>
    /// 延长或缩短这条鱼的剩余存活时间（秒）
    /// 正数延长，负数缩短；缩短到 0 或以下会立刻回池
    /// 当 destroyTime=0（永不销毁）时调用无效
    /// </summary>
    public void ExtendLife(float deltaSeconds)
    {
        if (lifeEndTime <= 0f) return;  // 永不销毁的鱼不处理
        lifeEndTime += deltaSeconds;
        destroyTime += deltaSeconds;    // 同步给面板剩余时间显示用（FishTagScanner 按 destroyTime-存活时间 算）
        if (lifeEndTime <= Time.time)
        {
            // 缩短到过期了 → 立刻回池
            ReturnToPool();
        }
    }

    /// <summary>
    /// 获取距离下次掉落的倒计时（秒），作为掉宝时间
    /// </summary>
    public float GetDropCountdown()
    {
        return Mathf.Max(0f, dropInterval - dropTimer);
    }
}
