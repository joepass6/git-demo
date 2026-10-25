using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class DropItem : MonoBehaviour
{
    // ========== 教程事件钩子 ==========
    /// <summary>掉落物被点击拾取时触发</summary>
    public static event Action<DropItem> OnItemCollected;

    [Header("物品设置")]
    [Tooltip("物品类型（如：珊瑚、掉落物）。只有类型相同的掉落物碰到一起才会合并堆叠，拾取时也按这个类型计数")]
    public string itemType = "珊瑚";

    [Tooltip("当前这堆物品的数量。多个同类掉落物碰到一起时会自动累加")]
    public int stackCount = 1;

    [Tooltip("一堆最多能叠加的数量上限，超过后不再合并")]
    public int maxStack = 10000;

    [Header("防穿过设置")]
    [Tooltip("掉落物无法穿过挂有这些【标签】的物体——即使对方是触发器，或双方所在层没有在“物理2D层碰撞矩阵”里勾选。在掉落物预制体上填标签名即可，如：石头、边框")]
    public List<string> 阻挡标签 = new List<string>();

    [Tooltip("用来显示堆叠数量的文本（拖入预制体内部的 TextMeshPro / TextMeshProUGUI）。数量为 1 时不显示文字")]
    public TMP_Text countText;

    // ========== 多宝物计数静态 ==========
    private static Dictionary<string, int> _counts = new Dictionary<string, int>();
    private static Dictionary<string, List<TextMeshProUGUI>> _counters = new Dictionary<string, List<TextMeshProUGUI>>();

    // 所有存活的掉落物：新掉落物出生时与每个旧掉落物的“实心碰撞核”互相忽略，实现彼此穿过；
    // 触发大圈不忽略，同类合并的 OnTrigger 事件照常生效
    private static readonly List<DropItem> _allDrops = new List<DropItem>();

    // 每个计数文本自己的显示格式（如 "现有数量：{0}"，标签和数字在同一个文本里）；
    // 未登记的按 "{0}" 只显示数字，保持旧行为
    private static readonly Dictionary<TextMeshProUGUI, string> _counterFormats = new Dictionary<TextMeshProUGUI, string>();

    // 本次游戏生命周期内已经播种过初始值的类型（防止面板反复激活/多个图标重复赠送初始材料）
    private static readonly HashSet<string> _seededTypes = new HashSet<string>();

    /// <summary>
    /// 初始库存播种：每种类型在本次游戏中只生效一次（先到先得）。
    /// 存档加载在播种之后执行，会用存档值覆盖；悬停面板反复激活不会重复赠送。
    /// </summary>
    public static void SeedInitial(string type, int initialCount)
    {
        if (string.IsNullOrEmpty(type)) return;
        if (_seededTypes.Add(type)) SetCount(type, initialCount);
    }

    /// <summary>
    /// 在游戏启动最早期（读档等待之前）对场景中所有计数器（含未激活面板里的）播种一次初始库存，
    /// 保证“先播种初始值 → 再被存档覆盖”的固定顺序，与面板何时打开无关。
    /// 同一遍遍历里把全部数量文本也注册进同步表（哪怕它所在面板从未激活过）：
    /// 之后任何拾取/扣料/返还都会直接写到所有同类型文本，多个简介面板永远显示同一个数字。
    /// </summary>
    public static void SeedAllInitialInLoadedScene()
    {
        DropItemCounterUI[] all = Resources.FindObjectsOfTypeAll<DropItemCounterUI>();
        foreach (DropItemCounterUI counter in all)
        {
            // 只处理当前已加载场景中的物体，排除 Project 里的预制体资源等
            if (counter == null || !counter.gameObject.scene.IsValid()) continue;
            if (string.IsNullOrEmpty(counter.itemType)) continue;

            if (counter.resetEveryTime) SetCount(counter.itemType, counter.initialCount);
            else SeedInitial(counter.itemType, counter.initialCount);

            // 未激活物体的 Awake 尚未执行、counter 自己的缓存文本可能还是空的，这里直接取组件
            TextMeshProUGUI text = counter.GetComponent<TextMeshProUGUI>();
            if (text == null) text = counter.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) RegisterCounter(counter.itemType, text);
        }
    }

    public static void RegisterCounter(string type, TextMeshProUGUI text, string format = null)
    {
        if (string.IsNullOrEmpty(type) || text == null) return;
        if (!_counters.ContainsKey(type)) _counters[type] = new List<TextMeshProUGUI>();
        if (!_counters[type].Contains(text)) _counters[type].Add(text);
        _counterFormats[text] = string.IsNullOrEmpty(format) ? "{0}" : format;
        UpdateUIText(type);
    }

    public static void UnregisterCounter(string type, TextMeshProUGUI text)
    {
        if (string.IsNullOrEmpty(type) || text == null) return;
        if (_counters.ContainsKey(type)) _counters[type].Remove(text);
        _counterFormats.Remove(text);
    }

    public static int GetCount(string type) { return _counts.ContainsKey(type) ? _counts[type] : 0; }
    public static void SetCount(string type, int value)
    {
        _counts[type] = Mathf.Max(0, value);
        UpdateUIText(type);
    }
    public static void AddCount(string type, int add) { SetCount(type, GetCount(type) + add); }

    static void UpdateUIText(string type)
    {
        if (!_counts.ContainsKey(type)) _counts[type] = 0;
        if (!_counters.ContainsKey(type)) return;
        // 倒序遍历顺手清掉已销毁文本的残留引用（面板销毁/退出 Play 后静态列表可能残留 null）
        var list = _counters[type];
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null)
            {
                list.RemoveAt(i);
                continue;
            }
            string fmt;
            if (!_counterFormats.TryGetValue(list[i], out fmt) || string.IsNullOrEmpty(fmt)) fmt = "{0}";
            RenderCounter(list[i], fmt, _counts[type]);
        }
    }

    /// <summary>
    /// 按“中文前缀{0}”格式渲染计数文本：先把格式（如“现有数量：{0}”）翻成当前语言再填数字，
    /// 数字始终保留；顺手换成该语言字体。纯数字格式“{0}”表里查不到，就只显示数字（旧行为）。
    /// </summary>
    static void RenderCounter(TextMeshProUGUI text, string fmt, int value)
    {
        string tpl = LocalizationManager.Tr(fmt, fmt);
        try { text.text = string.Format(tpl, value); }
        catch (FormatException) { text.text = value.ToString(); }

        var inst = LocalizationManager.Instance;
        if (inst != null)
        {
            TMP_FontAsset f = inst.GetFont(inst.CurrentLanguage);
            if (f != null && text.font != f) text.font = f;
        }

        // 外文字更长时自动缩字号，保证“Owned: N”等不跑出文本框
        LocalizationManager.FitToBox(text);
    }

    // 切换语言后用新语言把所有库存文本重刷一遍（数字不变）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void SubscribeLanguageChanged()
    {
        LocalizationManager.OnLanguageChangedStatic -= OnLanguageChanged;
        LocalizationManager.OnLanguageChangedStatic += OnLanguageChanged;
    }

    static void OnLanguageChanged(LocalizationManager.Language lang)
    {
        foreach (var type in new List<string>(_counters.Keys)) UpdateUIText(type);
    }

    /// <summary>
    /// 清空所有类型的库存（清除存档用），并把所有已注册 UI 立即刷为 0，不用重启即可见。
    /// </summary>
    public static void ClearAllCounts()
    {
        _counts.Clear();
        foreach (var list in _counters.Values)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) { list.RemoveAt(i); continue; }
                string fmt;
                if (!_counterFormats.TryGetValue(list[i], out fmt) || string.IsNullOrEmpty(fmt)) fmt = "{0}";
                RenderCounter(list[i], fmt, 0);
            }
        }
    }

    // ========== 实例逻辑 ==========
    // 出生序号：同帧生成的多个掉落物 Time.time 完全相等，旧逻辑用时间严格比较会导致
    // “同帧双胞胎”永远不合并（它们出生位置完全相同，触发事件一生只来一次）。
    // 改用全局自增序号，任何两个实例都有确定的先后。
    private static int globalSpawnSerial = 0;
    private int spawnSerial;

    private Collider2D col;
    // 实心碰撞核（非触发器）：预制体通常是“0.25 触发大圈（合并/点击用）+ 0.08 实心小核（物理碰撞用）”。
    // 手动防穿透投射必须用实心小核——用触发大圈会在离墙一个圈位时就误判撞上，导致掉落物悬空停住。
    private Collider2D solidCol;
    private Rigidbody2D rb;
    private Camera cachedCam;

    // 主动邻近合并的扫描半径（取自身最大触发器/碰撞器半径，再留余量）
    private float mergeCheckRadius;

    // —— 物理降级：掉落物落地静止后刚体切 Static，不再参与物理求解（大量掉落物同屏时的主要开销）——
    bool physicsSettled;
    float restTime;
    const float RestVelocityThresholdSqr = 0.01f; // 速度平方阈值（0.1单位/秒）
    const float RestRequiredTime = 0.5f;         // 连续静止这么久才落地降级

    // 出生时一次性邻近合并扫描的复用缓冲（NonAlloc，零 GC）
    static readonly Collider2D[] spawnMergeBuffer = new Collider2D[32];

    // —— 按标签手动阻挡的复用缓冲：Physics2D 查询不受层碰撞矩阵影响，也能命中触发器 ——
    static readonly RaycastHit2D[] blockCastBuffer = new RaycastHit2D[16];
    static readonly Collider2D[] blockOverlapBuffer = new Collider2D[16];
    ContactFilter2D blockFilter;

    // —— 光晕随堆叠数量由白变金 ——
    SpriteRenderer glowRenderer;
    const int FullGoldStack = 99;                 // 堆到这个数量时光晕完全变金
    static readonly Color GoldGlow = new Color(1f, 0.84f, 0f); // #FFD700

    // —— 零弹性材质：掉落物撞障碍时不反弹，防止被 depenetrate 弹飞 ——
    static PhysicsMaterial2D _zeroMat;
    static PhysicsMaterial2D ZeroBounceMaterial
    {
        get
        {
            if (_zeroMat == null)
            {
                _zeroMat = new PhysicsMaterial2D("DropItem-NoBounce");
                _zeroMat.bounciness = 0f;
                _zeroMat.friction = 0.3f;
            }
            return _zeroMat;
        }
    }

    private void Awake()
    {
        spawnSerial = ++globalSpawnSerial;
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();

        // —— 关键修复：预制体是"0.25 触发大圈（合并/点击用）+ 0.08 实心小核（物理碰撞用）"的双碰撞器结构。
        // 实心小核直径只有 0.16，远小于下落时单步位移（~0.24），物理引擎会直接穿过去。
        // 修法：不碰 isTrigger（改它会触发 Unity 物理 glitch），只运行时把**实心圆形碰撞器**的半径放大到 0.25，
        // 让物理碰撞核直径 = 0.5 > 单步位移，直接防穿透；触发大圈保持原样继续负责合并感应。
        // 同时给所有碰撞器赋零弹性材质，防止动态刚体 depenetrate 时把掉落物弹飞。
        Collider2D[] allCols = GetComponents<Collider2D>();
        float maxCircleRadius = 0f;
        Collider2D triggerCol = null;
        solidCol = null;
        foreach (Collider2D c in allCols)
        {
            if (c.isTrigger) { if (triggerCol == null) triggerCol = c; }
            else { if (solidCol == null) solidCol = c; }
            if (c is CircleCollider2D cc && cc.radius > maxCircleRadius) maxCircleRadius = cc.radius;
        }
        // 把实心圆形碰撞器放大到和最大圆同半径
        if (solidCol is CircleCollider2D solidCc && maxCircleRadius > 0f)
        {
            solidCc.radius = maxCircleRadius;
        }
        else if (solidCol == null)
        {
            solidCol = col; // 回退：预制体意外只有一个碰撞器
        }
        // 统一选大圈（通常是 trigger）当默认 col；实心碰撞器赋零弹性材质防弹飞
        col = (triggerCol != null) ? triggerCol : solidCol;

        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.sharedMaterial = ZeroBounceMaterial;
        }
        PhysicsMaterial2D zeroMat = ZeroBounceMaterial;
        foreach (Collider2D c in allCols) c.sharedMaterial = zeroMat;

        // 手动阻挡查询：检测全部层、连触发器一起命中（标签过滤在拿到结果后做）
        blockFilter = new ContactFilter2D();
        blockFilter.useTriggers = true;
        blockFilter.SetLayerMask(Physics2D.AllLayers);

        // 计算主动合并扫描半径：取自身最大的圆形碰撞器（含触发器），再留一点余量
        mergeCheckRadius = 0f;
        foreach (Collider2D c in GetComponents<Collider2D>())
        {
            if (c is CircleCollider2D cc && cc.radius > mergeCheckRadius) mergeCheckRadius = cc.radius;
        }
        mergeCheckRadius = Mathf.Max(mergeCheckRadius * 2f + 0.05f, 0.3f);

        // 缓存预制件"发光"子物体的光晕，初始数量为1=白色
        Transform glowTf = transform.Find("发光");
        if (glowTf != null) glowRenderer = glowTf.GetComponent<SpriteRenderer>();
        UpdateGlowColor();

        EnsureCountText();

        // 掉落物彼此穿过：只忽略“实心碰撞核”两两之间的物理碰撞；
        // 触发大圈保持感应，靠近同类时的自动合并不受影响。
        if (solidCol != null)
        {
            foreach (DropItem other in _allDrops)
            {
                if (other == null || other.solidCol == null) continue;
                Physics2D.IgnoreCollision(solidCol, other.solidCol, true);
            }
        }
        _allDrops.Add(this);
    }

    private void OnDestroy()
    {
        _allDrops.Remove(this);
    }

    private void Start()
    {
        // 出生时主动扫一次身边同类：兜底"同帧同位置出生双胞胎互相错过首次触发事件"。
        // 之后的合并全部由碰撞/触发事件驱动，不再周期轮询（旧逻辑每个掉落物每0.25秒扫一次，数量多时开销大）。
        TryProximityMergeOnce();
    }

    /// <summary>扫描身边同类并尝试合并一次（仅出生时调用，NonAlloc 不产生 GC）</summary>
    void TryProximityMergeOnce()
    {
        int hitCount = Physics2D.OverlapCircleNonAlloc(transform.position, mergeCheckRadius, spawnMergeBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = spawnMergeBuffer[i];
            if (hit == null) continue;
            DropItem other = hit.GetComponent<DropItem>();
            if (other == null || other == this) continue;
            TryStack(this, other);
            if (this == null) return; // 合并中自己可能已被销毁
        }
    }

    // 头顶堆叠数字字体：VonwaonBitmap SDF（资源不在 Resources 下，
    // 但场景中收集计数器已大量引用它、运行时必然已加载，按名查找一次后缓存）
    private static TMP_FontAsset _stackCountFont;
    private static TMP_FontAsset StackCountFont
    {
        get
        {
            if (_stackCountFont == null)
            {
                foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (t.font != null && t.font.name == "VonwaonBitmap SDF")
                    {
                        _stackCountFont = t.font;
                        break;
                    }
                }
            }
            return _stackCountFont;
        }
    }

    /// <summary>
    /// 预制体没有拖数量文本时，运行时自动补一个世界空间 TMP 文本，
    /// 否则两堆合并后外观与单个完全一样，玩家无法判断是否合并成功。
    /// </summary>
    void EnsureCountText()
    {
        if (countText != null) return;
        TMP_FontAsset fontAsset = StackCountFont;
        if (fontAsset == null) fontAsset = TMPro.TMP_Settings.defaultFontAsset;
        if (fontAsset == null) return;

        GameObject go = new GameObject("堆叠数量");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        go.transform.localScale = Vector3.one;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.font = fontAsset;
        tmp.fontSize = 1.8f;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.sortingOrder = 5000;
        tmp.text = "";
        countText = tmp;
    }

    // ================= 堆叠合并 =================
    // 确定性规则：序号小的（先生成）优先吸收对方；它已满时反过来由对方吸收自己；
    // 两边都满则不动。绝不屏蔽碰撞——首次接触时若一方已满，之后空位出现仍要能合并。
    // 返回 true 表示本次调用中有实例被销毁（调用方若发现自己被销毁必须立刻停止访问自身）。
    static bool TryStack(DropItem a, DropItem b)
    {
        if (a == null || b == null || a == b) return false;
        if (a.itemType != b.itemType) return false;
        if (a.stackCount <= 0 || b.stackCount <= 0) return false;

        DropItem keeper = a.spawnSerial < b.spawnSerial ? a : b;
        DropItem source = keeper == a ? b : a;

        // 优先目标已满：若另一方还有容量，反向合并，保证新掉的物品总能并入旧堆
        if (keeper.stackCount >= keeper.maxStack && source.stackCount < source.maxStack)
        {
            DropItem tmp = keeper;
            keeper = source;
            source = tmp;
        }
        if (keeper.stackCount >= keeper.maxStack) return false;

        int moved = Mathf.Min(source.stackCount, keeper.maxStack - keeper.stackCount);
        if (moved <= 0) return false;

        keeper.stackCount += moved;
        source.stackCount -= moved;
        keeper.UpdateCount();
        source.UpdateCount();
        if (source.stackCount <= 0)
        {
            Destroy(source.gameObject);
            return true;
        }
        return false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Collider2D other = collision.collider;
        if (other == null) return;

        DropItem otherItem = other.GetComponent<DropItem>();
        if (otherItem != null)
        {
            TryStack(this, otherItem);
            return;
        }

        // 其他所有实体碰撞器（地面、石头、边框等）都保持正常物理碰撞，掉落物不会穿过
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;
        DropItem otherItem = other.GetComponent<DropItem>();
        if (!otherItem) return;
        TryStack(this, otherItem);
    }

    // 落地静止检测：掉落物停下来后把刚体降为 Static，从此不参与物理求解。
    // 新掉落物落到它身上时，Static 刚体一方仍会正常收到碰撞/触发回调，堆叠合并不受影响。
    private void FixedUpdate()
    {
        if (physicsSettled || rb == null) return;

        // 手动阻挡函数已禁用：交给物理引擎（实心核直径 0.5 + CCD 连续检测）全权处理。
        // 之前它和物理引擎同时 OverlapCollider / depenetrate 抢控制权，造成抽搐、弹飞。
        // 阻挡指定标签物体();

        if (rb.velocity.sqrMagnitude <= RestVelocityThresholdSqr)
        {
            restTime += Time.fixedDeltaTime;
            if (restTime >= RestRequiredTime)
            {
                physicsSettled = true;
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.bodyType = RigidbodyType2D.Static;
            }
        }
        else
        {
            restTime = 0f;
        }
    }

    /// <summary>物体是否挂了“阻挡标签”之一（直接比对 tag 字符串，避免 CompareTag 遇到未定义标签抛异常）</summary>
    bool IsBlockedTag(GameObject go)
    {
        if (go == null || 阻挡标签 == null || 阻挡标签.Count == 0) return false;
        string t = go.tag;
        for (int i = 0; i < 阻挡标签.Count; i++)
        {
            if (!string.IsNullOrEmpty(阻挡标签[i]) && 阻挡标签[i] == t) return true;
        }
        return false;
    }

    /// <summary>
    /// 对方是不是“物理引擎本来就能挡住”的实心碰撞器（非触发器 + 层矩阵允许碰撞）
    /// </summary>
    bool IsPhysicallySolid(Collider2D other)
    {
        return other != null && !other.isTrigger &&
               !Physics2D.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer);
    }

    /// <summary>
    /// 手动让掉落物无法穿过挂了阻挡标签的物体（空气墙/窗口/石头/海底）。
    /// 为什么物理引擎还不够：这些障碍物在场景里是“离散检测的 Dynamic 刚体”，
    /// 而掉落物的实心碰撞核直径只有约 0.16——下落速度一大，单个物理步位移超过 0.16，
    /// 离散检测整步跨过小核，连续碰撞检测(CCD)对“离散 Dynamic 对手”又不保证，就会穿透掉下去。
    /// 这里用实心小核自己做形状投射兜底：
    ///  - 对方是触发器/层矩阵忽略（物理完全不挡）：路径上一旦命中就钳速度，沿表面滑开；
    ///  - 对方是实心（物理本来会挡）：只有“本步必然撞上/跨越”时才接管，离得还远绝不干预，
    ///    避免和物理求解器抢控制权造成微抖、悬空；
    ///  - 已经嵌进对方体内（上一帧漏检/出生在里面）：沿最小分离向量推出。
    /// 原则：只钳速度，不改位置；只有真重叠才手动分离。
    /// </summary>
    void 阻挡指定标签物体()
    {
        if (阻挡标签 == null || 阻挡标签.Count == 0 || solidCol == null || rb == null) return;

        const float 表皮间距 = 0.02f;
        Vector2 velocity = rb.velocity;

        // 1) 形状投射：沿本帧速度方向扫过实心小核
        float stepLength = velocity.magnitude * Time.fixedDeltaTime;
        if (stepLength > 0.00001f)
        {
            int hits = solidCol.Cast(velocity.normalized, blockFilter, blockCastBuffer,
                                     stepLength + 表皮间距, true);
            Vector2 blockNormal = Vector2.zero;
            float nearest = float.MaxValue;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit2D hit = blockCastBuffer[i];
                if (hit.collider == null || !IsBlockedTag(hit.collider.gameObject)) continue;
                if (IsPhysicallySolid(hit.collider)) continue; // 实心障碍全交物理引擎

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    blockNormal = hit.normal;
                }
            }

            if (nearest < float.MaxValue && blockNormal != Vector2.zero)
            {
                // 去掉朝向表面的速度分量，保留切向（贴表面滑开）；不手动改位置
                velocity = Vector3.ProjectOnPlane(velocity, blockNormal);
            }
        }

        // 2) 重叠分离：真嵌进阻挡物内才推出。正常接触不算重叠，所以对实心物也安全。
        int overlaps = solidCol.OverlapCollider(blockFilter, blockOverlapBuffer);
        if (overlaps > 0)
        {
            for (int i = 0; i < overlaps; i++)
            {
                Collider2D other = blockOverlapBuffer[i];
                if (other == null || !IsBlockedTag(other.gameObject)) continue;
                if (IsPhysicallySolid(other)) continue; // 实心障碍交物理 depenetrate

                ColliderDistance2D d = solidCol.Distance(other);
                if (!d.isOverlapped) continue;

                // d.normal 从“对方”指向“自己”；重叠时 d.distance 为负数（嵌入深度）。
                float 嵌入深度 = -d.distance;
                Vector2 推出方向 = d.normal;
                if (推出方向 == Vector2.zero)
                {
                    // 极端同心情况下距离法线不可靠：朝两碰撞体中心连线方向推
                    推出方向 = ((Vector2)transform.position - (Vector2)other.bounds.center).normalized;
                    if (推出方向 == Vector2.zero) 推出方向 = Vector2.up;
                }
                rb.position += 推出方向 * (嵌入深度 + 表皮间距);

                // 去掉朝向阻挡物内部的速度分量，避免下一帧又扎回去
                velocity = Vector3.ProjectOnPlane(velocity, 推出方向);
            }
        }

        rb.velocity = velocity;
    }

    private void UpdateCount()
    {
        if (countText != null) countText.text = stackCount > 1 ? $"x{stackCount}" : "";
        UpdateGlowColor();
    }

    /// <summary>光晕颜色：1个=纯白，随数量变金，达到 FullGoldStack 时纯金。
    /// 用平方根曲线而非线性：99 的跨度很大，线性时前几十个几乎看不出变化，
    /// 开方后早期上色明显，末期逐渐收缓。</summary>
    void UpdateGlowColor()
    {
        if (glowRenderer == null) return;
        float t = Mathf.Clamp01((stackCount - 1f) / (FullGoldStack - 1f));
        t = Mathf.Sqrt(t);
        glowRenderer.color = Color.Lerp(Color.white, GoldGlow, t);
    }

    // ================= 点击拾取 =================
    private void Update()
    {
        if (MoveSystem.isInMoveMode) return; // ===== 搬家模式下禁止拾取 =====
        if (DragAndPlaceSystem.IsCarryingObject) return; // 携带珊瑚待放置时，点击用于放置，不拾取
        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverUI()) return;
            if (col == null) col = GetComponent<Collider2D>();
            if (col == null) return;

            if (cachedCam == null) cachedCam = Camera.main;
            if (cachedCam == null) return;

            Vector2 mousePos = cachedCam.ScreenToWorldPoint(Input.mousePosition);
            if (col.OverlapPoint(mousePos)) OnClickCollect();
        }
    }

    private void OnMouseDown()
    {
        if (MoveSystem.isInMoveMode) return; // ===== 搬家模式下禁止拾取 =====
        if (DragAndPlaceSystem.IsCarryingObject) return; // 携带珊瑚放置中不拾取
        if (IsPointerOverUI()) return;
        OnClickCollect();
    }

    bool IsPointerOverUI()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null) return false;
        return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    void OnClickCollect()
    {
        AddCount(itemType, stackCount);
        Debug.Log($"[DropItem] 收集 {itemType} x{stackCount}, 总数={GetCount(itemType)}");

        // ===== 触发教程事件 =====
        OnItemCollected?.Invoke(this);

        Destroy(gameObject);
    }
}