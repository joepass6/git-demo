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

    [Tooltip("出生时的大小随机幅度：实际缩放 = 预制体原始缩放 × (1 ± 此值)，0=固定大小")]
    [Range(0f, 0.9f)]
    public float scaleVariation = 0.3f;

    [Tooltip("障碍物的 Tag 名称（标准障碍物）")]
    public string obstacleTag = "Obstacle";

    [Tooltip("额外障碍物 Tag 名称（留空=不启用）。挂此标签的物体与 obstacleTag 走完全相同的碰撞反应")]
    public string extraObstacleTag = "";

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

    [Header("定点掉落（可选）")]
    [Tooltip("勾选后：到时间把掉落物掉到场景里指定的目标点/区域，而不是从鱼当前位置掉")]
    public bool useTargetDrop = false;
    [Tooltip("目标点 Transform：只有当这条鱼本身是场景物体时才能直接拖（预制体的引用不能指向场景物体）")]
    public Transform dropTarget;
    [Tooltip("目标点 Tag：鱼是运行时从预制体实例化的，用 Tag 找场景里的目标空物体；多个时随机取一个。目标上挂了 Collider2D 就按区域随机取点")]
    public string dropTargetTag = "";
    [Tooltip("定点掉落时从目标点上方多高处生成，落下过程就是下落动画（世界单位）")]
    public float targetDropHeight = 0.8f;

    // 按 Tag 找目标点的缓存（避免每条鱼每帧 GameObject.FindGameObjectsWithTag）
    static readonly List<Transform> s_dropTargets = new List<Transform>();
    static float s_dropTargetStamp = -999f;
    const float DropTargetRefreshInterval = 3f;

    /// <summary>解析定点掉落目标：优先手动拖的 Transform，其次按 Tag 找场景里的目标点。</summary>
    Transform ResolveDropTarget()
    {
        if (dropTarget != null) return dropTarget;
        if (string.IsNullOrEmpty(dropTargetTag)) return null;

        if (Time.time - s_dropTargetStamp > DropTargetRefreshInterval)
        {
            s_dropTargetStamp = Time.time;
            s_dropTargets.Clear();
            try
            {
                foreach (GameObject go in GameObject.FindGameObjectsWithTag(dropTargetTag))
                    if (go != null) s_dropTargets.Add(go.transform);
            }
            catch (UnityException)
            {
                Debug.LogWarning($"[{name}] dropTargetTag \"{dropTargetTag}\" 没有在 Tag 管理器里定义，定点掉落退回鱼当前位置");
                dropTargetTag = "";
                return null;
            }
        }

        for (int i = s_dropTargets.Count - 1; i >= 0; i--)
            if (s_dropTargets[i] == null) s_dropTargets.RemoveAt(i);
        if (s_dropTargets.Count == 0) return null;
        return s_dropTargets[UnityEngine.Random.Range(0, s_dropTargets.Count)];
    }

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

    [Tooltip("点击特效粒子整体缩放系数（1=预制体原始大小）。只改 startSize 倍率，不动位置/发射范围")]
    [Range(0.1f, 2f)]
    public float particleScale = 0.6f;

    [Tooltip("粒子缩小后补的数量倍率，保持视觉密度（实际发射数 = emitCount × 该系数，向上取整）")]
    [Range(1f, 4f)]
    public float particleDensityBoost = 1.5f;

    [Tooltip("点击特效粒子的存活时间（秒）。统一压短，避免长拖尾/慢慢消散；12 套粒子共用这一个值")]
    [Range(0.05f, 2f)]
    public float particleLifetime = 0.35f;

    [Tooltip("预制体里误把鱼本体放在 coral 层的，初始化时纠正到这个 Sorting Layer（fish 层整体渲染在 coral 之上）")]
    public string fishSortingLayer = "fish";

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

    // ====== 粒子特效：排序跟随 + 单次播放 ======
    // 规则：特效渲染器的 sortingLayerID 跟随该鱼本体 SpriteRenderer，
    //       sortingOrder = 本体 order + 粒子排序偏移（稳定压在鱼身上方）。
    const int 粒子排序偏移 = 1;

    SpriteRenderer 鱼本体渲染器;
    readonly List<ParticleSystemRenderer> 特效渲染器 = new List<ParticleSystemRenderer>();
    // 预制体原始的 startSize 倍率（默认 1）；对象池复用会重复初始化，必须只捕获一次再乘系数，
    // 否则 0.6 会被反复叠乘成 0.36、0.216……
    readonly Dictionary<ParticleSystem, float> 原始粒子大小倍率 = new Dictionary<ParticleSystem, float>();
    int 上次特效层 = int.MinValue;
    int 上次特效序 = int.MinValue;

    // 最近一次播放点击反馈的帧号：内置 OnMouseDown 和手动补偿判定可能在同一帧都命中，
    // 用它保证一次点击只结算一次（否则会重复播音效、连发粒子）
    private int lastClickFrame = -1;

    // 预制体原始缩放（对象池复用时不能被上一次的随机结果污染，所以只在首次捕获一次）
    private Vector3 baseScale;
    private bool baseScaleCaptured;

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

    protected virtual void Awake()
    {
        // 预制体里的粒子是 playOnAwake=1、loop=1、emission 常开，
        // 激活瞬间就会自己一直放；这里在首帧渲染前统一关掉，改为点击时才放一次。
        GetComponentsInChildren<ParticleSystem>(true, allParticles);
        for (int i = 0; i < allParticles.Count; i++) 关闭自动播放(allParticles[i]);
    }

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

        // 所有鱼都关重力 + 设 Kinematic：重力会把鱼往下拉，和每帧 transform.position 手动定位对抗
        // Inside 模式靠钳位覆盖了重力，Surface 模式贴 coral 侧面时会和重力打架导致"卡住"
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.None;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        }

        // 保存预制体初始缩放（第一次UpdateScale之前先记录，之后翻转只改x方向）
        // 注意只能用首次捕获的原始缩放：对象池复用时 transform.localScale 已经是上一条鱼的随机结果，
        // 拿它当基准会把随机系数反复叠乘，体型越用越离谱
        if (!baseScaleCaptured)
        {
            baseScale = transform.localScale;
            baseScaleCaptured = true;
        }

        // 出生随机大小：围绕预制体原始缩放 ±scaleVariation 随机
        float sv = Mathf.Clamp(scaleVariation, 0f, 0.9f);
        float sizeK = sv > 0f ? Random.Range(1f - sv, 1f + sv) : 1f;
        prefabScale = new Vector3(baseScale.x * sizeK, baseScale.y * sizeK, baseScale.z);

        isFacingLeft = Random.value > 0.5f;
        UpdateScale();

        spawnPosition = transform.position;

        GetComponentsInChildren<ParticleSystem>(true, allParticles);
        纠正鱼排序层();
        初始化特效();

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

        // 打包版里补一条坐标补偿过的点击判定（内置 OnMouseDown 在 RT≠窗口尺寸时会偏移）
        HandleClickInput();

        // 本体的 sortingLayer/order 可能被子类动态改（椭圆轨道/轨道环绕的遮挡换层），每帧同步给粒子
        同步特效排序();

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
        TryPlayClickFeedback();
    }

    /// <summary>
    /// 点击反馈（音效 + 粒子）。两条入口共用：Unity 内置 OnMouseDown，
    /// 以及 HandleClickInput 里做了窗口→RT 坐标补偿的手动判定。
    /// </summary>
    private void TryPlayClickFeedback()
    {
        if (lastClickFrame == Time.frameCount) return; // 同一帧两条入口只结算一次
        lastClickFrame = Time.frameCount;

        // 点击鱼音效（直接调 AudioManager，没挂也不报错）
        if (AudioManager.Instance != null)
            AudioManager.Instance.播放鱼被点击音效();

        if (enableParticle && allParticles.Count > 0)
        {
            // 只让随机挑中的这一套放一次；其余 11 套保持停止，避免“常驻播放”把点击效果淹没
            int index = Random.Range(0, allParticles.Count);
            ParticleSystem ps = allParticles[index];
            if (ps != null)
            {
                关闭自动播放(ps, false);
                // 缩小后粒子稀疏，按补偿倍率多发射一点，视觉密度基本不变
                int 发射数 = Mathf.Max(1, Mathf.CeilToInt(emitCount * particleDensityBoost));
                ps.Clear(true);
                ps.Play();
                ps.Emit(发射数);
            }
        }
    }

    /// <summary>
    /// 关闭粒子的自动/循环播放。停止=true 时顺带清空当前残留粒子（用于初始化）。
    /// </summary>
    static void 关闭自动播放(ParticleSystem ps, bool 停止 = true)
    {
        if (ps == null) return;

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;

        var emission = ps.emission;
        emission.enabled = false;

        if (停止) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>
    /// 预制体里有一批鱼（含 4 条小型鱼）本体在 coral 层、order 6。
    /// 珊瑚放置/读档/搬家结束时会调用 珊瑚放置.BringToFront 把珊瑚 order 抬到 100+，
    /// 同层下 6 < 100+，鱼就被珊瑚盖住。fish 层整体排在 coral 层之后渲染，
    /// 所以只把「当前在 coral 层」的鱼本体切到 fish 层，order 保持原值（6）不变。
    /// 已经是 fish 层的不动；椭圆轨道/轨道环绕的 layerNormal 也配的是 fish，切换后仍一致。
    /// </summary>
    void 纠正鱼排序层()
    {
        if (鱼本体渲染器 == null) 鱼本体渲染器 = GetComponentInChildren<SpriteRenderer>(true);
        if (鱼本体渲染器 == null) return;

        int 珊瑚层 = SortingLayer.NameToID("coral");
        if (鱼本体渲染器.sortingLayerID != 珊瑚层) return;

        string 层名 = string.IsNullOrEmpty(fishSortingLayer) ? "fish" : fishSortingLayer;
        int 目标层 = SortingLayer.NameToID(层名);
        if (目标层 == 珊瑚层) return;

        SpriteRenderer[] 所有 = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < 所有.Length; i++)
        {
            if (所有[i] != null) 所有[i].sortingLayerID = 目标层;
        }
    }

    /// <summary>
    /// 收集该鱼的本体渲染器和所有特效渲染器，并关掉 12 套粒子的自动播放。
    /// </summary>
    void 初始化特效()
    {
        鱼本体渲染器 = GetComponentInChildren<SpriteRenderer>(true);

        特效渲染器.Clear();
        for (int i = 0; i < allParticles.Count; i++)
        {
            ParticleSystem ps = allParticles[i];
            if (ps == null) continue;

            关闭自动播放(ps);
            应用粒子缩放(ps);
            应用粒子寿命(ps);

            ParticleSystemRenderer pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr != null) 特效渲染器.Add(pr);
        }

        同步特效排序(true);
    }

    /// <summary>
    /// 粒子整体缩放：只改 MainModule.startSizeMultiplier（缩放每颗粒子的初始尺寸），
    /// 不动 transform.localScale，所以粒子世界位置、发射形状和半径都不受影响。
    /// 原始倍率只捕获一次，对象池复用时不会反复叠乘。
    /// </summary>
    void 应用粒子缩放(ParticleSystem ps)
    {
        if (ps == null) return;

        float 原始倍率;
        if (!原始粒子大小倍率.TryGetValue(ps, out 原始倍率))
        {
            原始倍率 = ps.main.startSizeMultiplier;
            原始粒子大小倍率[ps] = 原始倍率;
        }

        var main = ps.main;
        main.startSizeMultiplier = 原始倍率 * Mathf.Clamp(particleScale, 0.05f, 2f);
    }

    /// <summary>
    /// 粒子存活时间：预制体里 12 套粒子都是 5 秒，会拖尾、慢慢消散。
    /// 统一改成常量 startLifetime（默认 0.35 秒），12 套共用 particleLifetime，改一处即可全改。
    /// </summary>
    void 应用粒子寿命(ParticleSystem ps)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startLifetime = Mathf.Clamp(particleLifetime, 0.05f, 2f);
    }

    /// <summary>
    /// 让特效跟随本条鱼的排序层：同一 sortingLayerID，order = 本体 order + 粒子排序偏移。
    /// 只在层/序变化时写一次，避免每帧无谓赋值。
    /// </summary>
    void 同步特效排序(bool 强制 = false)
    {
        if (鱼本体渲染器 == null) return;

        int 层 = 鱼本体渲染器.sortingLayerID;
        int 序 = 鱼本体渲染器.sortingOrder + 粒子排序偏移;

        if (!强制 && 层 == 上次特效层 && 序 == 上次特效序) return;
        上次特效层 = 层;
        上次特效序 = 序;

        for (int i = 0; i < 特效渲染器.Count; i++)
        {
            ParticleSystemRenderer pr = 特效渲染器[i];
            if (pr == null) continue;
            pr.sortingLayerID = 层;
            pr.sortingOrder = 序;
        }
    }

    /// <summary>
    /// 手动点击判定（只打包版需要）。
    /// 桌面窗口把画面渲染进 RenderTexture，窗口像素尺寸和 RT 尺寸不一致时，
    /// 内置 OnMouseDown 的射线会整体偏移，鱼就点不中（UI 悬停之前也是同一个原因）。
    /// 这里先把窗口坐标换算到 RT 坐标再做 Physics2D 命中；两者一致时换算系数为 1，
    /// 行为与原来完全等价。
    /// </summary>
    private void HandleClickInput()
    {
#if !UNITY_EDITOR
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // 统一走 DesktopWindowController 的换算（窗口/RT 尺寸不一致时自动补偿），
        // 不再在这里单独复制一份缩放公式。
        Vector2 world = DesktopWindowController.ScreenToWorldPointCorrect(cam, Input.mousePosition);

        Collider2D hit = Physics2D.OverlapPoint(world);
        if (hit == null) return;
        if (hit.transform != transform && !hit.transform.IsChildOf(transform)) return;

        TryPlayClickFeedback();
#endif
    }

    // ==================== 碰撞处理 ====================

    /// <summary>判定物体是否属于障碍物（匹配 obstacleTag 或 extraObstacleTag 任一即算）</summary>
    protected bool IsObstacle(GameObject go)
    {
        if (go == null) return false;
        if (!string.IsNullOrEmpty(obstacleTag) && go.CompareTag(obstacleTag)) return true;
        if (!string.IsNullOrEmpty(extraObstacleTag) && extraObstacleTag != obstacleTag
            && go.CompareTag(extraObstacleTag)) return true;
        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(fishTag)) return;
        if (IsObstacle(other.gameObject)) ObstacleHitResponse(other.transform);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag(fishTag)) return;
        if (IsObstacle(collision.gameObject)) ObstacleHitResponse(collision.transform);
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

    // 掉落物出生时用的检测半径（和 DropItem 预制体的实心碰撞核对齐）
    const float DropSpawnCheckRadius = 0.3f;
    // 向上偏移的步进（出生在障碍物里时，每步往上挪这么多再检测）
    const float DropSpawnStepUp = 0.2f;
    // 最多往上挪多少步（超过就放弃偏移，用原始位置保底）
    const int DropSpawnMaxSteps = 15;

    // 有效掉落区域的边界（懒加载：第一次 Drop 时查 SpawnArea 或相机视口）
    static Bounds? s_validDropBounds;
    static float s_boundsCacheTime;
    const float BoundsRefreshInterval = 3f; // 每 3 秒刷新一次（珊瑚可能被移动）

    /// <summary>检测位置是否和障碍物（Obstacle 或 extraObstacleTag）的碰撞体重叠</summary>
    bool IsPositionBlocked(Vector2 pos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, DropSpawnCheckRadius);
        foreach (var h in hits)
        {
            if (h == null) continue;
            if (IsObstacle(h.gameObject)) return true;
        }
        return false;
    }

    /// <summary>
    /// 查找有效掉落区域：优先用 Tag=SpawnArea 的 BoxCollider2D，
    /// 没有就用主相机视口的世界坐标边界，留一点内缩边距。
    /// </summary>
    static Bounds GetValidDropBounds()
    {
        float now = Time.time;
        if (s_validDropBounds.HasValue && (now - s_boundsCacheTime) < BoundsRefreshInterval)
            return s_validDropBounds.Value;

        // 1. 找 SpawnArea
        GameObject area = GameObject.FindGameObjectWithTag("SpawnArea");
        if (area != null)
        {
            BoxCollider2D box = area.GetComponent<BoxCollider2D>();
            if (box != null)
            {
                // BoxCollider2D.bounds 会自动包含 offset 和 size
                // 内缩一点，防止掉落物贴边出生后往下掉刚好越过边界
                Bounds b = box.bounds;
                float pad = 0.3f;
                b.Expand(-pad * 2f);
                s_validDropBounds = b;
                s_boundsCacheTime = now;
                return b;
            }
        }

        // 2. 兜底：用主相机视口
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0, 0, -cam.transform.position.z));
            Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1, 1, -cam.transform.position.z));
            Bounds b = new Bounds();
            b.SetMinMax(new Vector3(bl.x, bl.y, 0), new Vector3(tr.x, tr.y, 0));
            float pad = 0.3f;
            b.Expand(-pad * 2f);
            s_validDropBounds = b;
            s_boundsCacheTime = now;
            return b;
        }

        // 3. 最后兜底：一个大到离谱的区域（不会触发钳位）
        Bounds fallback = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1f));
        s_validDropBounds = fallback;
        s_boundsCacheTime = now;
        return fallback;
    }

    /// <summary>
    /// 找一个安全的掉落物出生位置：先钳位到有效区域内，再避开障碍物碰撞体。
    /// </summary>
    Vector3 FindSafeDropSpawn(Vector3 basePos)
    {
        // Step 1: 钳位到有效区域内
        Bounds bounds = GetValidDropBounds();
        Vector3 clamped = basePos;
        clamped.x = Mathf.Clamp(clamped.x, bounds.min.x, bounds.max.x);
        clamped.y = Mathf.Clamp(clamped.y, bounds.min.y, bounds.max.y);

        // Step 2: 如果钳位后的位置已经在障碍物里，向上偏移直到出来
        // （鱼碰到障碍物后会反向游走，但短时间内可能还在障碍物碰撞体内）
        Vector2 testPos = new Vector2(clamped.x, clamped.y);
        if (IsPositionBlocked(testPos))
        {
            for (int step = 1; step <= DropSpawnMaxSteps; step++)
            {
                testPos.y = clamped.y + step * DropSpawnStepUp;
                // 偏移也不能超出区域顶部
                if (testPos.y > bounds.max.y) break;
                if (!IsPositionBlocked(testPos))
                {
                    clamped = new Vector3(testPos.x, testPos.y, clamped.z);
                    return clamped;
                }
            }
        }

        return clamped;
    }

    void Drop()
    {
        for (int i = 0; i < dropCount; i++)
        {
            if (itemPrefab != null)
            {
                // 定点掉落：把“生成原点”换成场景目标点（+上方高度，靠刚体下落播动画）；
                // 没配目标/没找到目标时自动退回原来的“鱼当前位置掉落”。
                Vector3 origin = transform.position;
                Transform target = useTargetDrop ? ResolveDropTarget() : null;
                if (target != null)
                {
                    origin = target.position;
                    // 目标上挂了碰撞体 => 当成区域，在里面随机取点
                    Collider2D area = target.GetComponent<Collider2D>();
                    if (area != null)
                    {
                        Bounds b = area.bounds;
                        origin = new Vector3(
                            UnityEngine.Random.Range(b.min.x, b.max.x),
                            UnityEngine.Random.Range(b.min.y, b.max.y),
                            origin.z);
                    }
                    origin += Vector3.up * Mathf.Max(0f, targetDropHeight);
                }

                Vector3 safePos = FindSafeDropSpawn(origin);
                GameObject obj = Instantiate(itemPrefab, safePos, Quaternion.identity);
                Rigidbody2D itemRb = obj.GetComponent<Rigidbody2D>();
                if (itemRb != null) itemRb.velocity = Vector2.down * fallSpeed;
            }
        }

        // 通知外部（AudioManager 用这个播音效，教程也用）
        OnDropProduced?.Invoke(this);
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
  