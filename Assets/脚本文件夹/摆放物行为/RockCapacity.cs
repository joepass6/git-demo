using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 石头可摆放容量：挂在 Tag=可放置(Obstacle) 的石头/地面上。
/// "可以放置N"数字只在拖拽放置/搬家期间、接触到某块石头时显示在珊瑚UI 面板顶部；
/// 已放数量不手动手动记账，而是每次刷新时按场景中实际压在石头上的珊瑚实时派生
/// （放置、搬家、卖出、读档后统一重算，天然不会出现账实不符）。
/// 组件不需要手动挂：DragAndPlaceSystem/MoveSystem 启动或开始拖拽时会给所有 placementTag 物体自动补上。
/// </summary>
public class RockCapacity : MonoBehaviour
{
    /// <summary>容量上限的基础值（也是字段默认值）；技能扩容在此之上叠加</summary>
    public const int BaseMaxCapacity = 3;

    [Tooltip("这块石头最多能放几个珊瑚")]
    public int maxCapacity = BaseMaxCapacity;

    // 以下两个字段为旧版"头顶 3D 数字"的位置参数，数字已搬进珊瑚UI 面板，字段保留仅为兼容旧序列化数据
    public float worldHeight = 0.45f;
    public float worldTopOffset = 0.22f;

    [Tooltip("白色描边壳的放大量（相对石头精灵尺寸，0.09≈每边多出4.5%）")]
    public float outlineScaleRatio = 0.09f;

    // 当前状态
    int used;                       // 已放置数量（enabled ObjectSpawner）

    // ===== 石头子物体高亮状态追踪 =====
    // 记录每次 SetVisible(true) 时被我们临时激活 + 抬层级的子物体，
    // SetVisible(false) 时只恢复这些（不动策划本来就 active 的子物体）
    HashSet<GameObject> activatedChildren = new HashSet<GameObject>();
    Dictionary<SpriteRenderer, int> originalChildOrders = new Dictionary<SpriteRenderer, int>();

    // ===== 搬家期间统计占用时要忽略的物体（正被搬起的珊瑚自己，否则它无法放回原石头）=====
    static GameObject carryIgnore;

    // ===== "放置信息" UI：策划预先摆好的层级结构 =====
    // 放置信息（容器，跟随石头头顶移动）
    // ├── 可放置（子树：可放时显示）
    // │   ├── 文字（TMP，标题）
    // │   └── 可放置数字（TMP，剩余数量，脚本写）
    // ├── 不可放置（子树：满了时显示）
    // │   └── 文字（TMP，"已放满"）
    // └── 透明窗口
    const string HostNodeName = "放置信息";
    const string CanPlaceNodeName = "可放置";
    const string CanPlaceNumberNodeName = "可放置数字";
    const string FullNodeName = "不可放置";
    /// <summary>"可放置N"数字的放大倍数</summary>
    const float LabelFontScale = 1.6f;

    static GameObject uiLabelGo;                 // 整个容器（显隐它=显隐整个UI）
    static RectTransform hostRect;               // 容器矩形（每帧改 anchoredPosition 跟随石头）
    static Canvas hostCanvas;                    // 容器所在 Canvas（决定屏幕坐标转换用的相机）
    static TMP_Text numberTmp;                   // "可放置/可放置数字" 上的 TMP
    static GameObject canPlaceGo;                // "可放置" 子树
    static GameObject fullGo;                    // "不可放置" 子树
    static bool uiSearched;                      // 是否已查找过（找不到也只查一次/警告一次）
    static RockCapacity currentShown;            // 当前正借共享文本显示数字的石头

    /// <summary>剩余名额（只读）</summary>
    public int Remaining => Mathf.Max(0, maxCapacity - used);

    // ===== 触碰白色描边（SpriteMask 裁出的白色剪影壳，零自定义 Shader，打包不会品红）=====
    const float OutlineExtraCover = 0.15f; // 白色底板比放大壳再多出的比例，保证外圈不露矩形角
    static Sprite whiteSprite;
    List<GameObject> shells;

    /// <summary>是否已满</summary>
    public bool IsFull => used >= maxCapacity;

    /// <summary>
    /// 技能修正容量上限（正=扩容，负=撤回扩容）。返回修正后的上限；
    /// 该物体没有容量组件时返回 null（不自动添加，保持与其他静态入口一致）。
    /// </summary>
    public static int? AddMax(GameObject rock, int delta)
    {
        return CapOf(rock)?.AddMax(delta);
    }

    /// <summary>修正本石头上限并立即刷新面板数字（下限 1，防止异常叠加成 0/负数）</summary>
    public int AddMax(int delta)
    {
        maxCapacity = Mathf.Max(1, maxCapacity + delta);
        RefreshLabel();
        return maxCapacity;
    }

    /// <summary>
    /// 给场景中所有带指定 Tag 的可放置物体补上容量组件（幂等：已有组件的不重复添加，
    /// 可重复调用以覆盖"石头后期才激活"的情况；场景只有几十上百个碰撞体，开销可忽略）。
    /// </summary>
    public static void EnsureAll(string placementTag)
    {
        if (string.IsNullOrEmpty(placementTag)) return;

        Collider2D[] cols = Object.FindObjectsOfType<Collider2D>();
        foreach (var c in cols)
        {
            if (c == null) continue;
            GameObject go = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
            if (go == null || !go.CompareTag(placementTag)) continue;
            if (go.GetComponent<RockCapacity>() == null) go.AddComponent<RockCapacity>();
        }
    }

    /// <summary>该石头当前是否还能放珊瑚。没挂容量组件的物体视为无限容量（兼容无组件石头）</summary>
    public static bool IsAvailable(GameObject rock)
    {
        if (rock == null) return false;
        var cap = CapOf(rock);
        return cap == null || cap.Remaining > 0;
    }

    /// <summary>取接触物体对应的容量组件：先自身再向父级找（碰撞体可能挂在子节点）</summary>
    static RockCapacity CapOf(GameObject rock)
    {
        return rock == null ? null : (rock.GetComponent<RockCapacity>() ?? rock.GetComponentInParent<RockCapacity>());
    }

    // ==================== 占用重算（唯一事实来源）====================

    /// <summary>搬家开始/进行中设置"统计忽略物"（正在搬的珊瑚），结束或取消时传 null</summary>
    public static void SetCarryIgnore(GameObject obj)
    {
        carryIgnore = obj;
    }

    /// <summary>
    /// 把所有石头的上限重置回基础值（读档重放技能前调用）。
    /// 石头跨读档持久存在，旧珊瑚是直接销毁而不会撤回技能；不重置的话，
    /// 同一会话内每读一次档，大砗磲的"容量+1"就会在石头上多叠一层。
    /// </summary>
    public static void ResetAllMax()
    {
        foreach (var cap in Object.FindObjectsOfType<RockCapacity>())
        {
            if (cap != null) cap.maxCapacity = BaseMaxCapacity;
        }
    }

    /// <summary>
    /// 按场景实际情况重算所有石头的"已放数量"并刷新数字。
    /// 口径与放置/技能完全一致：对每个珊瑚用 SkillCaster.DetectRocksUnder 求它接触的石头集合。
    /// coralTag 为珊瑚的标签（默认 coral）；拖拽预览的 ObjectSpawner 已被禁用，自动不计入。
    /// </summary>
    public static void RefreshAll(string placementTag, string coralTag = "coral")
    {
        if (string.IsNullOrEmpty(placementTag)) return;
        EnsureAll(placementTag);

        // 1. 统计每个容量组件所在石头被多少珊瑚压着
        var counts = new Dictionary<RockCapacity, int>();
        foreach (var sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            // ===== 关键：跳过 disabled 的！（拖拽预览体/UI 占位预制体 enabled=false）=====
            if (!sp.enabled) continue;

            GameObject coralGo = sp.gameObject;
            if (!string.IsNullOrEmpty(coralTag) && !coralGo.CompareTag(coralTag)) continue;
            if (carryIgnore != null && (coralGo == carryIgnore || coralGo.transform.IsChildOf(carryIgnore.transform))) continue;

            // 与读档/技能解析相同的紧贴容差，保证"显示的数量"和"技能/放置判定"是同一批珊瑚
            List<GameObject> rocks = SkillSystem.SkillCaster.DetectRocksUnder(coralGo, placementTag,
                SkillSystem.SkillCaster.RockTouchTolerance);

            // ===== 关键：一个珊瑚语义上只占一个容量槽（底座那块石头）=====
            // 哪怕它的体积同时蹭到了旁边另一块石头，也只给底座那块扣减容量
            if (rocks.Count == 0) continue;
            // 挑 Y 最靠下的那块（= 底座）
            GameObject baseRock = rocks[0];
            float baseY = baseRock.transform.position.y;
            for (int i = 1; i < rocks.Count; i++)
            {
                float y = rocks[i].transform.position.y;
                if (y < baseY) { baseY = y; baseRock = rocks[i]; }
            }

            RockCapacity cap = CapOf(baseRock);
            if (cap == null) continue;
            counts.TryGetValue(cap, out int n);
            counts[cap] = n + 1;
        }

        // 2. 写回每个石头（没被任何珊瑚压着的为 0）并刷新数字
        RockCapacity[] all = Object.FindObjectsOfType<RockCapacity>();
        foreach (var cap in all)
        {
            if (cap == null) continue;
            counts.TryGetValue(cap, out int n);
            cap.SetUsed(n);
        }
    }

    void SetUsed(int n)
    {
        used = n;
        RefreshLabel();
    }

    // ==================== 全局显隐（拖拽/搬家期间）====================

    /// <summary>
    /// 拖拽/搬家结束时统一收起面板里的容量数字。
    /// 面板内只有一个共享数字位（只显示当前接触的那一块石头），没有"全部同时显示"的场景，
    /// 所以全量接口只需处理隐藏。
    /// </summary>
    public static void SetAllVisible(bool visible)
    {
        if (visible) return;
        currentShown = null;
        if (uiLabelGo != null) uiLabelGo.SetActive(false);
    }

    /// <summary>显示/隐藏某块石头的容量数字。没挂组件的物体忽略</summary>
    public static void SetVisible(GameObject rock, bool visible)
    {
        CapOf(rock)?.SetVisible(visible);
    }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            if (!ResolveUILabel()) return;
            currentShown = this;
            RefreshLabel();
            uiLabelGo.SetActive(true);
            PositionAtRock(); // 激活同帧立刻到位，不等 LateUpdate，避免在旧位置闪一帧

            // ===== 激活岩石自身所有默认 inactive 的子物体 + 抬 sortingOrder 到父级之上 =====
            HighlightChildren(true);
        }
        else
        {
            // 只有"当前正显示的是我"时才收起：接触点从一块石头切到另一块时，
            // 旧石头的 SetVisible(false) 不能把新石头刚刷出的数字关掉
            if (currentShown != this) return;
            currentShown = null;
            if (uiLabelGo != null) uiLabelGo.SetActive(false);

            // ===== 恢复之前激活的子物体 =====
            HighlightChildren(false);
        }
    }

    /// <summary>
    /// visible=true：激活岩石所有默认 inactive 的子物体，并把它们的 SpriteRenderer.sortingOrder
    /// 抬到父岩石 +10，保证显示在最上层。
    /// visible=false：恢复之前由本组件激活的子物体状态和层级。
    /// </summary>
    void HighlightChildren(bool visible)
    {
        if (visible)
        {
            // 缓存父岩石的 sortingOrder（取自身所有 SpriteRenderer 里最大的）
            SpriteRenderer[] parentSrs = GetComponentsInChildren<SpriteRenderer>(true);
            int parentOrder = -9999;
            foreach (var sr in parentSrs) if (sr != null && sr.transform == transform)
            {
                if (sr.sortingOrder > parentOrder) parentOrder = sr.sortingOrder;
            }
            if (parentOrder == -9999) parentOrder = 0;

            // 遍历所有子物体（含未激活），激活那些默认 inactive 的 + 抬层级
            activatedChildren.Clear();
            originalChildOrders.Clear();

            for (int i = 0; i < transform.childCount; i++)
            {
                GameObject child = transform.GetChild(i).gameObject;
                bool wasInactive = !child.activeSelf;
                if (wasInactive)
                {
                    child.SetActive(true);
                    activatedChildren.Add(child);
                }

                // 所有子物体的 SpriteRenderer 都抬到父级之上（不管原本 active 还是 inactive）
                foreach (var sr in child.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == null) continue;
                    originalChildOrders[sr] = sr.sortingOrder;
                    sr.sortingOrder = parentOrder + 10;
                }
            }
        }
        else
        {
            // 恢复子物体 active 状态
            foreach (var child in activatedChildren)
                if (child != null) child.SetActive(false);
            activatedChildren.Clear();

            // 恢复 sortingOrder
            foreach (var kv in originalChildOrders)
                if (kv.Key != null) kv.Key.sortingOrder = kv.Value;
            originalChildOrders.Clear();
        }
    }

    /// <summary>给/撤某块石头的白色描边（拖拽和搬家接触提亮时调用）。没挂组件的物体忽略</summary>
    public static void SetOutline(GameObject rock, bool on)
    {
        CapOf(rock)?.ApplyOutline(on);
    }

    /// <summary>
    /// 创建/销毁白色描边。结构（均为石头渲染器的子物体）：
    ///   描边壳(1.09倍) ─ SpriteMask：用石头精灵的 alpha 形状当遮罩
    ///                 └ 白色矩形：只在遮罩内可见 → 得到放大的纯白石头剪影；
    ///                            本体在上层绘制，遮住中间，只露外圈白边。
    /// 全程使用 Unity 内置精灵材质和 SpriteMask，不引入任何自定义 Shader。
    /// </summary>
    public void ApplyOutline(bool on)
    {
        if (on)
        {
            if (shells != null) return; // 已在描边状态
            shells = new List<GameObject>();

            if (whiteSprite == null)
            {
                // 4px 白图 / 4 PPU = 1 世界单位，localScale 直接等于世界尺寸
                whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            }

            // 只给当前激活的 SR 建描边壳：美术后加的“点亮用白色子图片”默认隐藏，
            // 它自身就是白色亮图、不需要再描边；若包含它会多出一层错位的白圈
            SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(false);
            foreach (SpriteRenderer sr in srs)
            {
                if (sr == null || sr.sprite == null) continue;

                int shellOrder = sr.sortingOrder - 1;

                var shellGo = new GameObject("描边壳");
                shellGo.transform.SetParent(sr.transform, false);
                shellGo.layer = sr.gameObject.layer;
                shellGo.transform.localPosition = new Vector3(0f, 0f, 0.05f); // 略靠后（相机在 -z）
                shellGo.transform.localRotation = Quaternion.identity;
                shellGo.transform.localScale = Vector3.one;

                // 1. 遮罩节点：放大的石头精灵形状（白边就来自放大的这一圈）
                float maskScale = 1f + outlineScaleRatio;
                var maskGo = new GameObject("遮罩");
                maskGo.transform.SetParent(shellGo.transform, false);
                maskGo.layer = sr.gameObject.layer;
                maskGo.transform.localScale = new Vector3(maskScale, maskScale, 1f);
                var mask = maskGo.AddComponent<SpriteMask>();
                mask.sprite = sr.sprite;
                mask.alphaCutoff = 0.5f;
                mask.isCustomRangeActive = true;
                mask.frontSortingLayerID = sr.sortingLayerID;
                mask.backSortingLayerID = sr.sortingLayerID;
                mask.frontSortingOrder = shellOrder;
                mask.backSortingOrder = shellOrder;

                // 2. 白色底板：比放大遮罩再大一圈保证全覆盖，形状由遮罩裁成石头剪影
                var whiteGo = new GameObject("白边");
                whiteGo.transform.SetParent(shellGo.transform, false);
                whiteGo.layer = sr.gameObject.layer;
                float k = maskScale + OutlineExtraCover;
                Vector2 spriteSize = sr.sprite.bounds.size; // 渲染器局部空间下的精灵尺寸
                whiteGo.transform.localScale = new Vector3(spriteSize.x * k, spriteSize.y * k, 1f);

                var whiteSr = whiteGo.AddComponent<SpriteRenderer>();
                whiteSr.sprite = whiteSprite;
                whiteSr.color = Color.white;
                whiteSr.sortingLayerID = sr.sortingLayerID;
                whiteSr.sortingOrder = shellOrder;
                whiteSr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

                shells.Add(shellGo);
            }
        }
        else
        {
            if (shells == null) return;
            foreach (var go in shells)
            {
                if (go != null) Destroy(go);
            }
            shells = null;
        }
    }

    void OnDestroy()
    {
        ApplyOutline(false);
        // 恢复子物体状态（石头销毁时不要留下 active=true 的残留）
        HighlightChildren(false);
        // 数字正挂在本石头上时石头被销毁（读档清场等），同步收起面板文本
        if (currentShown == this)
        {
            currentShown = null;
            if (uiLabelGo != null) uiLabelGo.SetActive(false);
        }
    }

    // ==================== 面板内容量数字（只驱动策划摆好的已有 UI）====================

    void Start()
    {
        // 开局先收起整个UI，只有拖拽/搬家接触石头时才显示
        ResolveUILabel();
    }

    /// <summary>
    /// 查找"放置信息"容器 + 缓存"可放置/不可放置/可放置数字"子节点。
    /// 层级约定：
    ///   放置信息 / 可放置 / 可放置数字 → TMP，脚本写数字
    ///   放置信息 / 可放置 / 文字 → 标题（策划写）
    ///   放置信息 / 不可放置 / 文字 → 满了时显示（策划写）
    /// 失败不锁死：每次 SetVisible 都会重试查找。
    /// </summary>
    static bool ResolveUILabel()
    {
        if (uiLabelGo != null) return true;

        Transform hostNode = null;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                hostNode = FindDeepChild(root.transform, HostNodeName);
                if (hostNode != null) break;
            }
        }
        if (hostNode == null)
        {
            if (!uiSearched)
            {
                uiSearched = true;
                Debug.LogWarning("[RockCapacity] 找不到容量提示UI 容器（" + HostNodeName
                    + "），请确认场景已保存、节点名字一致。");
            }
            return false;
        }

        // 找"可放置/可放置数字"子节点（TMP，脚本写数字）
        Transform canPlaceNode = FindDeepChild(hostNode, CanPlaceNodeName);
        Transform numberNode = canPlaceNode != null
            ? FindDeepChild(canPlaceNode, CanPlaceNumberNodeName)
            : null;

        if (numberNode == null)
        {
            if (!uiSearched)
            {
                uiSearched = true;
                Debug.LogWarning("[RockCapacity] \"" + HostNodeName + "\"下找不到\""
                    + CanPlaceNodeName + "/" + CanPlaceNumberNodeName + "\"子节点。");
            }
            return false;
        }

        TMP_Text tmp = numberNode.GetComponent<TMP_Text>();
        if (tmp == null)
        {
            if (!uiSearched)
            {
                uiSearched = true;
                Debug.LogWarning("[RockCapacity] \"" + HostNodeName + "/" + CanPlaceNodeName
                    + "/" + CanPlaceNumberNodeName + "\"上没有 TextMeshProUGUI 组件。");
            }
            return false;
        }

        var rt = hostNode as RectTransform;
        if (rt == null)
        {
            if (!uiSearched) { uiSearched = true; Debug.LogWarning("[RockCapacity] \"" + HostNodeName + "\"必须是 UI 节点（RectTransform）。"); }
            return false;
        }

        // 重新挂到当前激活的根 Canvas 下（避免它原本在隐藏层级里）
        Canvas target = FindActiveRootCanvas(hostNode.GetComponentInParent<Canvas>());
        if (target == null)
        {
            if (!uiSearched) { uiSearched = true; Debug.LogWarning("[RockCapacity] 场景中没有可用的激活 Canvas。"); }
            return false;
        }
        if (rt.parent != target.transform) rt.SetParent(target.transform, false);

        // ===== 把根 Canvas 的 sortingOrder 抬到场景最高 SpriteRenderer 之上，
        // 保证"放置信息"UI 不会被石头/珊瑚的 SpriteRenderer 挡住（ScreenSpaceCamera/WorldSpace 模式）=====
        int maxSrOrder = 0;
        foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
            if (sr != null && sr.sortingOrder > maxSrOrder) maxSrOrder = sr.sortingOrder;
        target.sortingOrder = maxSrOrder + 100;
        // 如果 Canvas 是 WorldSpace 模式，也强制 overrideSorting 确保 sortingOrder 生效
        if (target.renderMode == RenderMode.WorldSpace) target.overrideSorting = true;

        // 跟随用锚点：父矩形中心定位、容器底部中点贴石头头顶
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);

        // 缓存节点
        numberTmp = tmp;
        canPlaceGo = canPlaceNode != null ? canPlaceNode.gameObject : null;
        Transform fullNode = FindDeepChild(hostNode, FullNodeName);
        fullGo = fullNode != null ? fullNode.gameObject : null;

        hostRect = rt;
        hostCanvas = target;
        uiLabelGo = hostNode.gameObject;
        uiSearched = true;

        // 放大数字
        numberTmp.transform.localScale = Vector3.one * LabelFontScale;
        numberTmp.fontStyle = FontStyles.Bold;

        // 关掉所有 Graphic 的射线，避免挡住石头上的点击放置
        foreach (var g in hostNode.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;

        uiLabelGo.SetActive(false);
        Debug.Log("[RockCapacity] 容量提示UI 已就位：" + GetNodePath(hostNode));
        return true;
    }

    /// <summary>选一个当前激活、可显示的根 Canvas：优先主界面 Canvas，其次任意激活根画布，最后退回原画布</summary>
    static Canvas FindActiveRootCanvas(Canvas current)
    {
        Canvas fallback = null;
        foreach (var c in Object.FindObjectsOfType<Canvas>(true))
        {
            if (c == null || !c.isRootCanvas) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            if (c.name == "界面ui") return c;
            if (fallback == null) fallback = c;
        }
        if (fallback != null) return fallback;
        return current != null && current.isRootCanvas ? current : (current != null ? current.rootCanvas : null);
    }

    static string GetNodePath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    /// <summary>按名字深度查找子节点（包含未激活节点）</summary>
    static Transform FindDeepChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var hit = FindDeepChild(c, name);
            if (hit != null) return hit;
        }
        return null;
    }

    void LateUpdate()
    {
        // 只有当前正显示数字的那块石头负责移动UI（搬家时石头在动，需要每帧跟随）
        if (currentShown == this && uiLabelGo != null && uiLabelGo.activeSelf)
            PositionAtRock();
    }

    /// <summary>把整个容量UI 移到本石头头顶正上方（世界→屏幕→UI 局部坐标）</summary>
    void PositionAtRock()
    {
        Camera worldCam = Camera.main;
        if (worldCam == null || hostRect == null || hostCanvas == null) return;

        // 头顶世界点：精灵上沿 + 旧的头顶偏移字段（世界单位）
        Vector3 topWorld = transform.position;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) topWorld.y = sr.bounds.max.y;
        topWorld += Vector3.up * worldTopOffset;

        Vector3 screen = worldCam.WorldToScreenPoint(topWorld);
        if (screen.z <= 0f) return; // 石头在相机背后时不动

        // Overlay 画布矩形转换不传相机；ScreenSpaceCamera/WorldSpace 用根画布的相机
        Canvas root = hostCanvas.rootCanvas;
        Camera uiCam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null :
            (root.worldCamera != null ? root.worldCamera : worldCam);

        var parentRect = hostRect.parent as RectTransform;
        if (parentRect == null) return;
        Vector2 localPos;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screen, uiCam, out localPos))
        {
            hostRect.anchoredPosition = localPos;
        }
    }

    void RefreshLabel()
    {
        // Resolve 失败时重试（每次显示都调 RefreshLabel，懒加载）
        if (uiLabelGo == null)
        {
            ResolveUILabel();
            if (uiLabelGo == null) return;
        }

        bool hasRoom = Remaining > 0;

        // 切换子树：可放时显示"可放置" + 隐藏"不可放置"；满时相反
        if (canPlaceGo != null) canPlaceGo.SetActive(hasRoom);
        if (fullGo != null) fullGo.SetActive(!hasRoom);

        // 可放置时写剩余数字；满时不需要写（策划的"不可放置/文字"已经写好）
        if (hasRoom && numberTmp != null)
        {
            numberTmp.text = Remaining.ToString();
        }
    }
}
