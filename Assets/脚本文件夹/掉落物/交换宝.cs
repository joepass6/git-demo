using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 交换宝：消耗指定数量的当前掉落物，随机换得另一种掉落物（不会换到自己）。
///
/// 用法（脚本挂在“交换宝”根物体上）：
/// 1. 选择宝物图标：把你指定位置的 Image 拖到“选择宝物图标”；留空时自动找“选择宝物”槽里的子 Image（不会自动生成）；
/// 2. 选择宝物数量：把“选择宝物数量”文本拖进来；留空时自动按名字查找，数字实时跟随当前图标对应类型的库存；
/// 3. 转换按钮 / 更换按钮：留空时自动在子物体里找名字含“转换”“更换”的 Button；
/// 4. 换到宝物槽：留空时自动找子物体“换到宝物”，槽里没图标会自动建一个；
/// 5. 可交换的类型与图标自动从场景中所有 DropItemCounterUI 收集。
///
/// 点击“转换/更换”不会关闭窗口：按下时快照窗口激活状态，点击事件全部跑完后把被关掉的窗口重新打开。
///
/// 规则：
/// - “更换”只在当前数量大于 0 的类型之间循环切换；
/// - “转换”消耗“消耗数量”（默认100），随机给“换得数量”（默认1）个其他类型；
/// - 当前类型数量不足时不转换，选择图标会左右抖动提示。
/// </summary>
public class 交换宝 : MonoBehaviour
{
    [Header("按钮（可留空，自动查找）")]
    [Tooltip("执行交换。查找名字含 转换 的按钮")]
    public Button 转换按钮;
    [Tooltip("单个循环按钮（旧版兼容）。找不到上/下两个独立按钮时用这个")]
    public Button 更换按钮;
    [Tooltip("上一个按钮。查找名字含 上一个 / 前一个 的按钮")]
    public Button 上一个按钮;
    [Tooltip("下一个按钮。查找名字含 下一个 / 后一个 的按钮")]
    public Button 下一个按钮;

    [Header("宝物槽（可留空，自动查找 选择宝物 / 换到宝物）")]
    public Transform 选择槽;
    public Transform 结果槽;

    [Tooltip("选择宝物图标：拖入你指定位置的 Image（图像就在这个位置显示）。留空才自动查找")]
    public Image 选择宝物图标;

    [Tooltip("“选择宝物数量”文本：数字实时跟随当前图标对应类型的现有数量。留空时自动按名字查找")]
    public TMP_Text 选择数量文本;

    [Tooltip("点击转换/更换时必须保持打开的窗口（留空自动收集：自己、到Canvas之间的父级、名为“气泡”的子物体）")]
    public GameObject[] 保活窗口;

    [Header("交换设置")]
    [Tooltip("转换一次消耗当前掉落物的数量")]
    public int 消耗数量 = 100;

    [Tooltip("每次转换换得新掉落物的数量")]
    public int 换得数量 = 1;

    [Header("特效设置")]
    public float 放大时长 = 0.5f;
    [Tooltip("换到宝物放大发光展示多少秒后，结果槽自动切回原始图")]
    public float 结果展示时长 = 2f;
    public Color 发光颜色 = new Color(1f, 0.84f, 0f); // 与掉落物金色光晕一致 #FFD700

    // 运行时收集到的全部可交换类型（与库存 key 完全一致，不做 Trim，避免对错账）
    readonly List<string> 全部类型 = new List<string>();
    readonly Dictionary<string, Sprite> 类型图标 = new Dictionary<string, Sprite>();

    string 当前类型;
    string 已注册数量类型; // 数量文本当前注册到 DropItem 的类型，切换时先反注册

    Image 选择图标;
    Image 结果图标;
    Image 光晕;

    // 选择图标在编辑器里摆好的原始位置：抖动只围绕它偏移，快速连点也必须先归位，绝不累积漂移
    Vector3 选择图标初始位置;
    bool 已记录选择位置;

    // “换到宝物”在编辑模式指定的原始图：未转换时显示它（不能清空成空白）
    Sprite 原始结果图;
    // 当前正在展示的换到结果类型；null=结果槽处于原始图状态
    string 当前结果类型;
    // 结果展示结束的游戏时刻；Time.time 小于它时处于“换到宝物展示期”（窗口受保护，2 秒后自动切回原图）
    float 展示结束时刻 = -1f;

    Coroutine 结果特效协程;
    Coroutine 选择弹动协程;
    Coroutine 选择抖动协程;

    // —— 临时诊断：逐帧监视按钮/图标位置移动（定位“按钮抖动/图像跑位”后删除）——
    Coroutine 移动监视协程;
    void 启动移动监视()
    {
        if (移动监视协程 != null) StopCoroutine(移动监视协程);
        移动监视协程 = StartCoroutine(移动监视());
    }
    IEnumerator 移动监视()
    {
        var 部件 = new List<RectTransform>();
        void 收(Transform t) { if (t != null) 部件.Add(t as RectTransform); }
        收(transform);
        收(transform.Find("气泡"));
        收(transform.Find("指示箭头"));
        if (转换按钮 != null) 收(转换按钮.transform);
        if (更换按钮 != null) 收(更换按钮.transform);
        if (选择图标 != null) 收(选择图标.rectTransform);
        if (结果图标 != null) 收(结果图标.rectTransform);
        var 上次 = new Vector3[部件.Count];
        var 上次缩放 = new Vector3[部件.Count];
        for (int i = 0; i < 部件.Count; i++) { 上次[i] = 部件[i].localPosition; 上次缩放[i] = 部件[i].localScale; }
        float 剩余 = 2.5f;
        while (剩余 > 0f)
        {
            yield return null;
            剩余 -= Time.unscaledDeltaTime;
            for (int i = 0; i < 部件.Count; i++)
            {
                Vector3 p = 部件[i].localPosition;
                Vector3 s = 部件[i].localScale;
                if (Vector3.SqrMagnitude(p - 上次[i]) > 0.000001f
                    || Vector3.SqrMagnitude(s - 上次缩放[i]) > 0.000001f)
                {
                    Debug.LogWarning($"[抖动诊断] f{Time.frameCount} '{部件[i].name}' pos {上次[i]}→{p} scale {上次缩放[i]}→{s} " +
                                     $"抖动协程={(选择抖动协程 != null)} 特效协程={(结果特效协程 != null)} 弹动={(选择弹动协程 != null)}");
                    上次[i] = p; 上次缩放[i] = s;
                }
            }
        }
        移动监视协程 = null;
    }

    // —— 防关窗：按钮按下时记录各窗口激活状态，点击事件跑完后恢复被误关的窗口 ——
    readonly List<GameObject> 保活列表 = new List<GameObject>();
    readonly List<bool> 按下时激活 = new List<bool>();
    bool 本帧内部点击;

    // —— 通用防关窗：上一帧 LateUpdate 记录的激活基线，本帧任何点击关掉了开着的窗口就立即恢复 ——
    readonly List<bool> 上帧激活 = new List<bool>();
    bool 基线已建立;
    GameObject 气泡窗口;

    // ========== 外部调用接口（父级按钮点一下就用这个开/关面板） ==========

    /// <summary>打开面板（给外部按钮 OnClick 用）</summary>
    public void Show() { gameObject.SetActive(true); }

    /// <summary>关闭面板</summary>
    public void Close() { gameObject.SetActive(false); }

    /// <summary>切换面板显隐（按钮点一下）</summary>
    public void Toggle() { gameObject.SetActive(!gameObject.activeSelf); }

    // ========== 内部字段 ==========

    // 运行时生成的柔和径向光晕贴图（所有交换宝共用一份）
    static Sprite _光晕Sprite;
    static Sprite 光晕Sprite
    {
        get
        {
            if (_光晕Sprite != null) return _光晕Sprite;
            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // 中心亮、边缘柔和淡出的圆形光斑
                    byte a = r >= 1f ? (byte)0 : (byte)Mathf.RoundToInt(Mathf.Pow(1f - r, 2f) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            _光晕Sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _光晕Sprite;
        }
    }

    void Awake()
    {
        // 选择槽：多关键词兼容（旧名"选择宝物" + 新名"被交换物"）
        if (选择槽 == null)
            选择槽 = 深度查找多词(transform, new[] { "选择宝物", "被交换物", "选择" });
        // 结果槽：多关键词兼容
        if (结果槽 == null)
            结果槽 = 深度查找多词(transform, new[] { "换到宝物", "换得宝物", "交换到的", "交换到", "换到", "结果" });

        // 选择图标：优先用 Inspector 指定的 Image；否则只在槽里找现成的子 Image，绝不自动生成（图像必须在你指定的位置）
        选择图标 = 选择宝物图标 != null ? 选择宝物图标 : 查找槽内图标(选择槽);
        if (选择图标 == null)
            Debug.LogWarning("[交换宝] 未指定“选择宝物图标”，且“选择宝物”槽里找不到 Image，请在 Inspector 拖入你指定位置的 Image");
        else
        {
            选择宝物图标 = 选择图标;
            选择图标初始位置 = 选择图标.rectTransform.localPosition;
            已记录选择位置 = true;
        }

        结果图标 = 解析或创建图标(结果槽, "换到图标");
        规范化结果图标();
        // 截获编辑模式配好的原始图（必须在 Start 的空闲态刷新之前）
        if (结果图标 != null) 原始结果图 = 结果图标.sprite;
        创建光晕(结果槽);
    }

    /// <summary>多关键词查找：依次尝试每个关键词，找到第一个就返回</summary>
    static Transform 深度查找多词(Transform root, string[] 关键词)
    {
        if (root == null) return null;
        foreach (string kw in 关键词)
        {
            Transform found = 深度查找(root, kw);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// “换到宝物”是从别的 UI 复制来的，pivot 可能在角落（实测 1,0.92），放大动画绕角点缩放会看起来越来越歪；
    /// 同时不保持宽高比会把不同形状的掉落物图标拉伸。这里统一：pivot 居中（并补偿位置使框体原地不动）+ 保持比例。
    /// </summary>
    void 规范化结果图标()
    {
        if (结果图标 == null) return;
        var rt = 结果图标.rectTransform;
        结果图标.preserveAspect = true;

        // 仅对“非拉伸锚点”安全：锚点重合时移动 pivot 可精确补偿，框体视觉位置不变
        if (rt.anchorMin == rt.anchorMax && rt.pivot != new Vector2(0.5f, 0.5f))
        {
            Vector2 size = rt.rect.size;
            Vector2 oldPivot = rt.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition += Vector2.Scale(new Vector2(0.5f, 0.5f) - oldPivot, size);
        }
    }

    void Start()
    {
        收集类型与图标();
        解析数量文本();
        构建保活列表();

        // 转换按钮
        if (转换按钮 == null) 转换按钮 = 按名字找按钮("转换");
        // 上一个 / 下一个（优先独立按钮，没找到再用旧的"更换"单按钮兜底）
        if (上一个按钮 == null) 上一个按钮 = 按名字找按钮多词(new[] { "上一个", "前一个", "上", "前" });
        if (下一个按钮 == null) 下一个按钮 = 按名字找按钮多词(new[] { "下一个", "后一个", "下", "后" });
        if (上一个按钮 == null && 下一个按钮 == null && 更换按钮 == null)
            更换按钮 = 按名字找按钮("更换");

        // 守卫组件负责在"按下"瞬间快照窗口状态
        给按钮加守卫(转换按钮);
        给按钮加守卫(更换按钮);
        给按钮加守卫(上一个按钮);
        给按钮加守卫(下一个按钮);

        // 延迟到第一帧结束后再注册：保证我们的点击回调排在其他脚本（Start 里注册的开关页/翻页逻辑）之后，
        // 这样无论谁在同一次点击里关了窗口，我们都能在最后把它恢复
        StartCoroutine(延迟绑定按钮());

        刷新选择显示();
        刷新结果显示();
    }

    IEnumerator 延迟绑定按钮()
    {
        yield return null; // 等所有 Start 执行完
        if (转换按钮 != null) 转换按钮.onClick.AddListener(点击转换);
        else Debug.LogWarning("[交换宝] 找不到“转换”按钮，请在 Inspector 手动指定");

        // 上一个/下一个优先；都没找到才用旧的更换按钮兜底
        if (上一个按钮 != null) 上一个按钮.onClick.AddListener(点击上一个);
        if (下一个按钮 != null) 下一个按钮.onClick.AddListener(点击下一个);
        if (上一个按钮 == null && 下一个按钮 == null)
        {
            if (更换按钮 != null) 更换按钮.onClick.AddListener(点击更换);
            else Debug.LogWarning("[交换宝] 找不到“上一个/下一个/更换”按钮，请在 Inspector 手动指定");
        }
    }

    Button 按名字找按钮多词(string[] 关键词)
    {
        foreach (var b in GetComponentsInChildren<Button>(true))
            foreach (string kw in 关键词)
                if (b.name.Contains(kw)) return b;
        return null;
    }

    void 给按钮加守卫(Button b)
    {
        if (b == null) return;
        var guard = b.GetComponent<交换宝按钮守卫>();
        if (guard == null) guard = b.gameObject.AddComponent<交换宝按钮守卫>();
        guard.owner = this;

        规范化按钮点击区域(b);
    }

    /// <summary>
    /// 按钮上的文字（TMP/旧 Text）不参与点击检测：否则点到字体时命中路径与点按钮图片不一致，
    /// 会被“点空白关闭面板”误判成空白，把整个珊瑚UI 关掉。点击直接落在按钮自身图片上，
    /// Button 响应与空白关闭判定的结果就永远一致。
    /// </summary>
    void 规范化按钮点击区域(Button b)
    {
        // 按钮主响应图、按钮物体自身的图片：保证可命中
        if (b.targetGraphic != null) b.targetGraphic.raycastTarget = true;
        var 自身图 = b.GetComponent<Graphic>();
        if (自身图 != null) 自身图.raycastTarget = true;

        // 若按钮连一张可命中的图都没有，说明它本来就靠子物体（含文字）接收点击，
        // 这种结构不能关文字 raycast，否则按钮直接失效
        bool 按钮可命中 = (b.targetGraphic != null && b.targetGraphic.raycastTarget) || 自身图 != null;
        if (!按钮可命中) return;

        foreach (var g in b.GetComponentsInChildren<Graphic>(true))
        {
            if (g is TMP_Text || g.GetType() == typeof(Text))
                g.raycastTarget = false;
        }
    }

    float 选择检查计时;

    void OnEnable()
    {
        // 面板可能在读档完成后才打开：下次 Update 立刻重新校准一次当前选择
        选择检查计时 = 0f;

        // 面板隐藏时 Unity 会直接杀掉正在播的抖动协程（结尾的归位不会执行），重开时先把图标复位
        if (已记录选择位置 && 选择图标 != null)
            选择图标.rectTransform.localPosition = 选择图标初始位置;

        // 面板被隐藏时 Unity 会停掉协程：重开后按当前状态刷新——
        // 换过就重新播放放大发光，没换过就稳稳显示原始图（原始图绝不能被清空）
        if (全部类型.Count > 0) 刷新结果显示();

        // 面板重新打开后重新绑定数量文本（OnDisable 时反注册过）
        if (全部类型.Count > 0) 注册数量文本(当前类型);
    }

    void Update()
    {
        // 每 0.5 秒校准一次：覆盖读档延迟恢复、转换后当前类型被扣光等情况（零 GC，只做遍历不建列表）
        选择检查计时 -= Time.deltaTime;
        if (选择检查计时 > 0f) return;
        选择检查计时 = 0.5f;

        if (全部类型.Count == 0) return;
        if (string.IsNullOrEmpty(当前类型) || DropItem.GetCount(当前类型) <= 0)
        {
            string first = 找第一个可用类型();
            if (first != 当前类型)
            {
                当前类型 = first;
                刷新选择显示();
            }
        }
    }

    string 找第一个可用类型()
    {
        foreach (string t in 全部类型)
            if (DropItem.GetCount(t) > 0) return t;
        return null;
    }

    void OnDisable()
    {
        反注册数量文本();
    }

    void LateUpdate()
    {
        // 兜底1：结果展示期内，转换/更换按钮自己的点击链路中若关窗，本帧渲染前恢复
        if (本帧内部点击)
        {
            本帧内部点击 = false;
            if (正在展示结果()) 恢复保活窗口();
        }

        // 兜底2（通用）：换到宝物的 2 秒展示期内，屏幕上【任何】鼠标点击都不允许关掉它所在的窗口。
        // 展示期一过即恢复正常，珊瑚按钮随时可以正常关闭珊瑚UI。
        // UI 的 PointerClick 在抬起帧由 EventSystem 派发，这里用“上一帧 LateUpdate 的基线”对比，
        // 与 EventSystem 和本脚本的执行先后无关；恢复发生在渲染前，肉眼无闪烁。
        if (基线已建立 && 正在展示结果()
            && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0)))
        {
            for (int i = 0; i < 保活列表.Count && i < 上帧激活.Count; i++)
            {
                GameObject w = 保活列表[i];
                // activeSelf=false 才是“被直接关闭”的那个节点；父级被关时子级 activeSelf 仍为 true，恢复父级即可
                if (w != null && 上帧激活[i] && !w.activeSelf)
                {
                    w.SetActive(true);
                    Debug.Log($"[交换宝] 已阻止点击外部关闭窗口：{w.name}");
                }
            }
        }

        // 记录本帧基线（在恢复之后采样：被救回的窗口按“开着”记账）
        for (int i = 0; i < 保活列表.Count; i++)
        {
            bool on = 保活列表[i] != null && 保活列表[i].activeInHierarchy;
            if (i < 上帧激活.Count) 上帧激活[i] = on;
            else 上帧激活.Add(on);
        }
        基线已建立 = true;
    }

    /// <summary>交换窗口此刻是否处于显示状态（交换宝根可见，且有“气泡”子物体时气泡也要可见）</summary>
    public bool 窗口当前可见()
    {
        if (!gameObject.activeInHierarchy) return false;
        return 气泡窗口 == null || 气泡窗口.activeInHierarchy;
    }

    /// <summary>
    /// 是否正处于“换到宝物”的短暂展示期（放大发光中，默认 2 秒后自动切回原始图）。
    /// 只有这段时间内窗口才受保护，平时珊瑚等按钮可以正常开关面板。
    /// </summary>
    public bool 正在展示结果()
    {
        return Application.isPlaying && Time.time < 展示结束时刻;
    }

    /// <summary>
    /// 供外部开关系统（如 LocalTimeManager 的按钮互斥/toggle/点空白关闭）调用：
    /// 换到宝物展示期内，交换宝自己和所有父级窗口不允许被关；展示期结束保护自动解除。
    /// </summary>
    public bool 保活中(GameObject 窗口)
    {
        if (窗口 == null || !正在展示结果()) return false;
        return 保活列表.Contains(窗口);
    }

    // ================= 防关窗 =================

    /// <summary>由按钮守卫在按下瞬间调用：快照所有保活窗口当前的激活状态</summary>
    public void 按钮按下()
    {
        按下时激活.Clear();
        foreach (GameObject w in 保活列表)
            按下时激活.Add(w != null && w.activeInHierarchy);
        本帧内部点击 = true;
    }

    /// <summary>点击处理完后调用：把“按下时开着、现在却被关掉”的窗口重新打开</summary>
    void 恢复保活窗口()
    {
        for (int i = 0; i < 保活列表.Count && i < 按下时激活.Count; i++)
        {
            GameObject w = 保活列表[i];
            if (w != null && 按下时激活[i] && !w.activeSelf)
            {
                w.SetActive(true);
                Debug.Log($"[交换宝] 阻止了点击导致的窗口关闭：{w.name}");
            }
        }
        按下时激活.Clear();
    }

    void 构建保活列表()
    {
        保活列表.Clear();
        void 加入(GameObject go)
        {
            if (go != null && !保活列表.Contains(go)) 保活列表.Add(go);
        }

        // 手动指定的优先
        if (保活窗口 != null)
            foreach (GameObject w in 保活窗口) 加入(w);

        // 自己 + 向上直到 Canvas（不含 Canvas 本身）的全部父级：气泡/交换宝/珊瑚UI 等都覆盖
        加入(gameObject);
        Transform t = transform.parent;
        while (t != null && t.GetComponent<Canvas>() == null)
        {
            加入(t.gameObject);
            t = t.parent;
        }

        // 截图层级里“气泡”是交换宝的直接子物体，通常它才是可见窗口本体
        Transform bubble = transform.Find("气泡");
        if (bubble != null)
        {
            加入(bubble.gameObject);
            气泡窗口 = bubble.gameObject;
        }
    }

    // ================= 数量文本 =================

    void 解析数量文本()
    {
        if (选择数量文本 != null) return;
        // 注意："数量" 关键词不能用——层级里"交换到的数量"也包含"数量"，会抢。用更精确的关键词
        string[] 关键词 = new[] { "选择宝物数量", "宝物数量", "拥有的数量", "拥有数量" };
        foreach (var tmp in GetComponentsInChildren<TMP_Text>(true))
        {
            if (匹配名字或父级(tmp.transform, 关键词))
            {
                选择数量文本 = tmp;
                break;
            }
        }
        if (选择数量文本 == null)
            Debug.LogWarning("[交换宝] 找不到“拥有的数量”文本，可在 Inspector 手动拖入");
    }

    /// <summary>递归查自身及父物体名字是否匹配任一关键词（最多往上爬 5 层防死循环）</summary>
    static bool 匹配名字或父级(Transform t, string[] 关键词)
    {
        int 层数 = 0;
        while (t != null && 层数++ < 5)
        {
            foreach (string kw in 关键词)
                if (t.name.Contains(kw)) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>把数量文本绑定到 DropItem 的计数器通道：之后拾取/扣料由库存系统实时刷新这个文本</summary>
    void 注册数量文本(string type)
    {
        if (选择数量文本 == null) return;
        if (已注册数量类型 == type) return;

        反注册数量文本();
        if (string.IsNullOrEmpty(type))
        {
            选择数量文本.text = "0";
            return;
        }

        var ugui = 选择数量文本 as TextMeshProUGUI;
        if (ugui != null)
        {
            // 纯数字显示（不要小数/前缀）
            DropItem.RegisterCounter(type, ugui, "{0}");
        }
        else
        {
            选择数量文本.text = DropItem.GetCount(type).ToString();
        }
        已注册数量类型 = type;
    }

    void 反注册数量文本()
    {
        if (选择数量文本 == null || string.IsNullOrEmpty(已注册数量类型)) return;
        var ugui = 选择数量文本 as TextMeshProUGUI;
        if (ugui != null) DropItem.UnregisterCounter(已注册数量类型, ugui);
        已注册数量类型 = null;
    }

    // ================= 绑定与收集 =================

    static Transform 深度查找(Transform root, string 名字关键词)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            if (t.name == 名字关键词 || t.name.Contains(名字关键词)) return t;
        }
        return null;
    }

    Button 按名字找按钮(string 名字关键词)
    {
        foreach (Button b in GetComponentsInChildren<Button>(true))
            if (b.name.Contains(名字关键词)) return b;
        return null;
    }

    /// <summary>只找现成图标，不自动生成：优先子物体里名字带“图标”的，其次任意子 Image，最后才用槽自己</summary>
    static Image 查找槽内图标(Transform 槽)
    {
        if (槽 == null) return null;

        Image 任意 = null;
        foreach (Image child in 槽.GetComponentsInChildren<Image>(true))
        {
            if (child.transform == 槽) continue;
            if (任意 == null) 任意 = child;
            if (child.name.Contains("图标")) return child;
        }
        if (任意 != null) return 任意;
        return 槽.GetComponent<Image>();
    }

    static Image 解析或创建图标(Transform 槽, string 新图标名)
    {
        if (槽 == null) return null;

        Image found = 查找槽内图标(槽);
        if (found != null) return found;

        // 结果槽允许自动建一个铺满槽位的图标
        var go = new GameObject(新图标名, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(槽, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    void 创建光晕(Transform 槽)
    {
        if (槽 == null) return;

        var go = new GameObject("交换光晕", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(槽, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        var 槽rt = 槽 as RectTransform;
        Vector2 槽尺寸 = 槽rt != null ? 槽rt.rect.size : Vector2.zero;
        if (槽尺寸.x <= 1f) 槽尺寸.x = 120f;
        if (槽尺寸.y <= 1f) 槽尺寸.y = 120f;
        rt.sizeDelta = 槽尺寸 * 1.9f;

        光晕 = go.AddComponent<Image>();
        光晕.sprite = 光晕Sprite;
        光晕.raycastTarget = false;
        光晕.preserveAspect = true;
        光晕.color = new Color(发光颜色.r, 发光颜色.g, 发光颜色.b, 0f);
        go.SetActive(false);
    }

    /// <summary>
    /// 从场景所有 DropItemCounterUI 收集类型；图标取同一简介面板里的“掉落物头像”Image。
    /// 用 Resources.FindObjectsOfTypeAll 才能找到藏在未激活面板里的计数器（与播种逻辑一致）。
    /// </summary>
    void 收集类型与图标()
    {
        全部类型.Clear();
        类型图标.Clear();

        DropItemCounterUI[] all = Resources.FindObjectsOfTypeAll<DropItemCounterUI>();
        foreach (DropItemCounterUI c in all)
        {
            if (c == null || !c.gameObject.scene.IsValid()) continue;
            string t = c.itemType;
            if (string.IsNullOrEmpty(t)) continue;

            if (!全部类型.Contains(t)) 全部类型.Add(t);

            if (!类型图标.ContainsKey(t))
            {
                Sprite s = 找面板头像(c.transform);
                if (s != null) 类型图标[t] = s;
            }
        }
    }

    static Sprite 找面板头像(Transform counterTf)
    {
        Transform panel = counterTf.parent;
        if (panel == null) return null;

        Transform avatarTf = panel.Find("掉落物头像");
        Image avatar = avatarTf != null ? avatarTf.GetComponent<Image>() : null;

        // 兜底：个别面板结构不同，就在同级里找第一个不是计数器自己的 Image
        if (avatar == null)
        {
            foreach (Image img in panel.GetComponentsInChildren<Image>(true))
            {
                if (img.transform != counterTf) { avatar = img; break; }
            }
        }
        return avatar != null ? avatar.sprite : null;
    }

    // ================= 业务逻辑 =================

    List<string> 有数量的类型()
    {
        var list = new List<string>();
        foreach (string t in 全部类型)
            if (DropItem.GetCount(t) > 0) list.Add(t);
        return list;
    }

    /// <summary>“更换”按钮：在所有还有数量的类型之间循环切换（向后）</summary>
    public void 点击更换() { 循环切换(1); }

    /// <summary>“下一个”按钮：向前（索引+1）切换</summary>
    public void 点击下一个() { 循环切换(1); }

    /// <summary>“上一个”按钮：向后（索引-1）切换</summary>
    public void 点击上一个() { 循环切换(-1); }

    /// <summary>按方向在所有有数量的类型里循环切换。dir=+1 下一个，dir=-1 上一个</summary>
    void 循环切换(int dir)
    {
        启动移动监视(); // 临时诊断
        准备点击();

        var available = 有数量的类型();
        if (available.Count == 0)
        {
            当前类型 = null;
            刷新选择显示();
            Debug.Log("[交换宝] 没有任何有数量的掉落物可更换");
            return;
        }

        int i = available.IndexOf(当前类型);
        i = (i + dir + available.Count) % available.Count; // 数学上保证不越界
        当前类型 = available[i];
        刷新选择显示();

        if (选择图标 != null && 选择图标.enabled)
        {
            if (选择弹动协程 != null) StopCoroutine(选择弹动协程);
            选择弹动协程 = StartCoroutine(轻弹(选择图标.rectTransform, 0.18f, 1.18f));
        }
        Debug.Log($"[交换宝] 当前选择：{当前类型}，数量={DropItem.GetCount(当前类型)}");
    }

    /// <summary>“转换”按钮：消耗当前类型，随机换得其他类型</summary>
    public void 点击转换()
    {
        启动移动监视(); // 临时诊断
        准备点击();

        if (string.IsNullOrEmpty(当前类型) || DropItem.GetCount(当前类型) <= 0)
        {
            当前类型 = 找第一个可用类型();
            刷新选择显示();
            Debug.Log("[交换宝] 没有可转换的掉落物");
            return;
        }

        if (DropItem.GetCount(当前类型) < 消耗数量)
        {
            if (选择图标 != null && 选择图标.enabled)
                触发选择抖动();
            Debug.Log($"[交换宝] {当前类型} 数量不足 {消耗数量}（当前 {DropItem.GetCount(当前类型)}），无法转换");
            return;
        }

        // 候选池：除自己以外的全部类型
        var 候选 = new List<string>(全部类型);
        候选.Remove(当前类型);
        if (候选.Count == 0)
        {
            Debug.LogWarning("[交换宝] 除当前类型外没有其他掉落物类型");
            return;
        }

        DropItem.AddCount(当前类型, -消耗数量);

        string 结果类型 = 候选[Random.Range(0, 候选.Count)];
        DropItem.AddCount(结果类型, 换得数量);

        显示结果(结果类型);
        Debug.Log($"[交换宝] 转换：{当前类型} -{消耗数量} → {结果类型} +{换得数量}");
    }

    /// <summary>
    /// 点击处理开始前：先把排在我们前面的监听（Inspector 持久事件等）已经关掉的窗口恢复，
    /// 再重新快照一次——这样排在我们后面的监听若关窗，LateUpdate 还能再恢复一次。
    /// </summary>
    void 准备点击()
    {
        恢复保活窗口();
        按钮按下();
    }

    // ================= 显示 =================

    void 刷新选择显示()
    {
        Sprite s = null;
        if (!string.IsNullOrEmpty(当前类型)) 类型图标.TryGetValue(当前类型, out s);

        if (选择图标 != null)
        {
            选择图标.sprite = s;
            选择图标.enabled = s != null;
            选择图标.rectTransform.localScale = Vector3.one;
        }

        // 数量文本跟随当前图标对应的类型（无论图标有没有找到都要刷新）
        注册数量文本(当前类型);
    }

    /// <summary>
    /// 按当前状态刷新结果槽：
    /// 正处于换到宝物的展示期 → 显示换到的图标并播放放大发光（到点自动切回原始图）；
    /// 平时 → 显示编辑模式配好的“原始图”（原始图永远不能被清空成空白）。
    /// </summary>
    void 刷新结果显示()
    {
        if (结果图标 == null) return;

        if (结果特效协程 != null) { StopCoroutine(结果特效协程); 结果特效协程 = null; }

        Sprite s = null;
        if (正在展示结果() && !string.IsNullOrEmpty(当前结果类型))
            类型图标.TryGetValue(当前结果类型, out s);

        if (s != null)
        {
            结果图标.sprite = s;
            结果图标.enabled = true;

            // 光晕始终垫在图标下面
            if (光晕 != null)
            {
                光晕.transform.SetAsFirstSibling();
                // 按槽位当前实际尺寸再校准一次（Awake 时布局可能还没完成）
                var 槽rt = 结果槽 as RectTransform;
                if (槽rt != null && 槽rt.rect.size.x > 1f)
                    光晕.rectTransform.sizeDelta = 槽rt.rect.size * 1.9f;
                光晕.gameObject.SetActive(true);
            }

            结果特效协程 = StartCoroutine(放大发光(展示结束时刻));
        }
        else
        {
            // 非展示态：稳稳显示原始图，无光晕、无缩放动画
            结果图标.sprite = 原始结果图;
            结果图标.enabled = 原始结果图 != null;
            结果图标.rectTransform.localScale = Vector3.one;
            if (光晕 != null) 光晕.gameObject.SetActive(false);
        }
    }

    /// <summary>转换成功：展示换到的宝物，结果展示时长（默认 2 秒）后由协程自动切回原始图</summary>
    void 显示结果(string 结果类型)
    {
        当前结果类型 = 结果类型;
        展示结束时刻 = Time.time + Mathf.Max(0.1f, 结果展示时长);
        刷新结果显示();
    }

    /// <summary>展示期结束：结果槽恢复原始图、关闭光晕、解除窗口保护</summary>
    void 还原结果槽()
    {
        当前结果类型 = null;
        展示结束时刻 = -1f;
        结果特效协程 = null;

        if (结果图标 == null) return;
        结果图标.sprite = 原始结果图;
        结果图标.enabled = 原始结果图 != null;
        结果图标.rectTransform.localScale = Vector3.one;
        if (光晕 != null) 光晕.gameObject.SetActive(false);
    }

    // ================= 特效 =================

    /// <summary>换到宝物：先弹性放大入场，随后金色光晕呼吸，到展示结束时刻自动切回原始图</summary>
    IEnumerator 放大发光(float 结束时刻)
    {
        RectTransform iconRt = 结果图标.rectTransform;
        RectTransform haloRt = 光晕 != null ? 光晕.rectTransform : null;

        iconRt.localScale = Vector3.zero;
        if (haloRt != null) haloRt.localScale = Vector3.one * 0.3f;

        // 1. 弹性放大入场（Back 缓动，有一点过冲更有“蹦出来”的感觉）；入场时长不超过剩余展示时间
        float 开始时刻 = Time.time;
        float 入场结束 = Mathf.Min(开始时刻 + 放大时长, 结束时刻);
        float 入场跨度 = Mathf.Max(0.0001f, 入场结束 - 开始时刻);
        while (Time.time < 入场结束)
        {
            float k = Mathf.Clamp01((Time.time - 开始时刻) / 入场跨度);
            iconRt.localScale = Vector3.one * BackEaseOut(k);
            if (haloRt != null) haloRt.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, k);
            yield return null;
        }
        iconRt.localScale = Vector3.one;
        if (haloRt != null) haloRt.localScale = Vector3.one;

        // 2. 金色光晕呼吸发光，直到展示期结束
        const float 周期 = 1.1f;
        while (Time.time < 结束时刻)
        {
            float p = (Mathf.Sin(Time.time / 周期 * Mathf.PI * 2f - Mathf.PI / 2f) + 1f) * 0.5f; // 0..1
            if (光晕 != null)
            {
                光晕.color = new Color(发光颜色.r, 发光颜色.g, 发光颜色.b, Mathf.Lerp(0.25f, 0.85f, p));
                haloRt.localScale = Vector3.one * Mathf.Lerp(1.1f, 1.55f, p);
            }
            iconRt.localScale = Vector3.one * (1f + 0.04f * p);
            yield return null;
        }

        // 3. 到点：自动切回原始图，窗口保护同时解除
        还原结果槽();
    }

    /// <summary>切换选择时的小弹动</summary>
    IEnumerator 轻弹(RectTransform rt, float 时长, float 峰值)
    {
        float t = 0f;
        while (t < 时长)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 时长);
            // 快速放大再回落
            float s = k < 0.5f ? Mathf.Lerp(1f, 峰值, k * 2f) : Mathf.Lerp(峰值, 1f, (k - 0.5f) * 2f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;
        选择弹动协程 = null;
    }

    /// <summary>
    /// 播放数量不足的左右抖动。快速连点时：旧抖动先停掉、图标立刻归位，
    /// 再围绕【固定的原始位置】开始新抖动——基准永远不在抖动中途重新采样，位置不会越点越跑。
    /// </summary>
    void 触发选择抖动()
    {
        if (选择图标 == null) return;
        RectTransform rt = 选择图标.rectTransform;

        if (!已记录选择位置)
        {
            选择图标初始位置 = rt.localPosition;
            已记录选择位置 = true;
        }

        if (选择抖动协程 != null) StopCoroutine(选择抖动协程);
        Debug.LogWarning($"[抖动诊断] f{Time.frameCount} 触发抖动：基准={选择图标初始位置} 当前={rt.localPosition} 父级={rt.parent?.name}");
        rt.localPosition = 选择图标初始位置; // 杀掉旧抖动后强制归位，消除上一次未还原的偏移
        选择抖动协程 = StartCoroutine(左右抖动(rt, 选择图标初始位置));
    }

    /// <summary>数量不足时左右抖动提示：只围绕传入的固定基准位置偏移，结束精确还原</summary>
    IEnumerator 左右抖动(RectTransform rt, Vector3 基准位置)
    {
        const float 时长 = 0.35f;
        float t = 0f;
        while (t < 时长)
        {
            t += Time.deltaTime;
            float k = t / 时长;
            float dx = Mathf.Sin(k * Mathf.PI * 18f) * 8f * (1f - k);
            rt.localPosition = 基准位置 + new Vector3(dx, 0f, 0f);
            yield return null;
        }
        rt.localPosition = 基准位置;
        Debug.LogWarning($"[抖动诊断] f{Time.frameCount} 抖动结束：最终={rt.localPosition} 基准={基准位置}");
        选择抖动协程 = null;
    }

    // f(0)=0, f(1)=1，末端带轻微过冲
    static float BackEaseOut(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}

/// <summary>
/// 挂在“转换/更换”按钮上：在鼠标按下的瞬间（先于任何 onClick 开关逻辑）通知交换宝快照窗口状态。
/// 由交换宝在运行时自动添加，无需手动挂载。
/// </summary>
public class 交换宝按钮守卫 : MonoBehaviour, IPointerDownHandler
{
    [System.NonSerialized] public 交换宝 owner;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (owner != null) owner.按钮按下();
    }
}
