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
/// 3. 按钮：留空时自动在子物体里找名字含“转换”“上一个/前一个”“下一个/后一个”的 Button；
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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void 启动时打标记() { Debug.Log("🚀 [交换宝] RuntimeInitialize 触发！脚本已加载进 DLL"); }

    [Header("按钮（可留空，自动查找）")]
    [Tooltip("执行交换。查找名字含 转换 的按钮")]
    public Button 转换按钮;
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

    // 缓存：Awake 里用手动递归找到的面板根，Start 里直接复用（Unity GetComponentsInChildren<Transform>(true) 对 inactive 子物体有 bug）
    Transform 缓存面板根;
    // 手动递归收集到的所有子物体（含 inactive）
    readonly List<Transform> 全量子物体 = new List<Transform>();

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
        // ⚠️ 强制默认隐藏！不管场景文件/Prefab override/其他脚本怎么设，Awake 一进来就关掉！
        gameObject.SetActive(false);
        Debug.Log("====== [交换宝 Awake] 强制 SetActive(false) ======");
        Debug.Log($"初始挂在: {name} / activeSelf={gameObject.activeSelf} / activeInHierarchy={gameObject.activeInHierarchy}");
        Debug.Log($"transform.childCount = {transform.childCount}");
        for(int i=0; i<transform.childCount; i++){
            var c = transform.GetChild(i);
            Debug.Log($"  child[{i}] = {c.name} activeSelf={c.gameObject.activeSelf} childCount={c.childCount}");
        }
        
        // ============ 关键修复：全场景扫找"交换宝"面板（绕开父子关系！）===========
        // YAML 写的是 Text (TMP) + 交换宝，但运行时是 消耗 + 消耗的数字
        // 说明场景层级可能被 Unity/Prefab 实例化搞乱了，不要依赖 parent-child
        Transform 查找起点 = null;
        // 先扫自己下面
        全量子物体.Clear();
        手动递归收集(transform, 全量子物体);
        foreach(var t in 全量子物体)
            if(t != transform && t.name == "交换宝") { 查找起点 = t; break; }
        
        // 自己下面没找到 → 全场景扫！
        if(查找起点 == null){
            Debug.Log("[交换宝] 脚本下面找不到交换宝面板 → 全场景扫...");
            var allInScene = FindObjectsOfType<Transform>(true);
            foreach(var t in allInScene){
                if(t.name == "交换宝") { 查找起点 = t; break; }
                if(t.name.Contains("交换宝") && t != transform) { 查找起点 = t; }
            }
        }
        
        // 最后兜底：用自己
        if(查找起点 == null){
            Debug.LogWarning("[交换宝] 全场景扫也找不到交换宝面板！用自己代替");
            查找起点 = transform;
        }
        
        // 重新收集面板根下面的所有子物体
        全量子物体.Clear();
        手动递归收集(查找起点, 全量子物体);
        缓存面板根 = 查找起点;
        Debug.Log($"✅ 面板根 = {缓存面板根.name} (全量子物体={全量子物体.Count})");
        foreach(var t in 全量子物体)
            Debug.Log($"  {(t.gameObject.activeSelf ? "🟢" : "🔴")} {t.name}");
        
        // 选槽自动查找
        if (选择槽 == null)
            选择槽 = 深度查找多词(查找起点, new[] { "选择宝物", "被交换物", "选择" });
        Debug.Log($"选择槽 = {(选择槽?.name ?? "null")}");
        
        // 结果槽自动查找
        if (结果槽 == null)
            结果槽 = 深度查找多词(查找起点, new[] { "换到宝物", "换得宝物", "交换到的", "交换到", "换到", "结果" });
        Debug.Log($"结果槽 = {(结果槽?.name ?? "null")}");

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

        // ========== 入口按钮绑定必须在 Awake 里！==========
        // 面板默认 inactive → Start 不会执行 → 入口按钮永远绑不上 → 面板永远打不开！
        // Awake 不管 active 与否都会执行，所以入口按钮必须在这绑
        绑定入口按钮();
    }

    /// <summary>绑定打开/关闭面板的外部入口按钮。必须在 Awake 里调用（面板默认 inactive 时 Start 不会执行）</summary>
    void 绑定入口按钮()
    {
        Transform 面板根 = 缓存面板根 != null ? 缓存面板根 : transform;

        Button 入口按钮 = null;
        foreach(var btn in FindObjectsOfType<Button>(true))
        {
            if(btn == null) continue;
            if(btn.transform.IsChildOf(面板根)) continue; // 跳过面板内部按钮
            if(btn.name.Contains("多余鱼宝转换") || btn.name.Contains("换宝") || btn.name.Contains("交换"))
            {
                入口按钮 = btn;
                break;
            }
        }
        if(入口按钮 != null)
        {
            Debug.Log($"[交换宝] ✅ Awake 绑定入口按钮: {入口按钮.name} → Toggle 面板显隐");
            var btnImg = 入口按钮.GetComponent<Image>();
            var btnRaycast = btnImg != null && btnImg.raycastTarget;
            Debug.Log($"[交换宝] 入口按钮诊断: activeSelf={入口按钮.gameObject.activeSelf} interactable={入口按钮.interactable} raycastTarget={btnRaycast}");
            
            入口按钮.onClick.AddListener(() => {
                bool nowActive = 面板根.gameObject.activeSelf;
                面板根.gameObject.SetActive(!nowActive);
                Debug.Log($"[交换宝] 🖱️ 入口点击！面板 active={!nowActive}");
                if(!nowActive) 刷新选择显示();
            });
        }
        else
        {
            Debug.LogWarning("[交换宝] ⚠️ Awake 没找到入口按钮！请检查场景里有没有名字含'交换'/'换宝'/'多余鱼宝转换'的外部按钮");
        }
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

    /// <summary>手动递归收集所有子物体（含 inactive）——绕过 Unity GetComponentsInChildren<Transform>(true) 对 inactive 的 bug</summary>
    static void 手动递归收集(Transform root, List<Transform> list)
    {
        if(root == null) return;
        list.Add(root);
        for(int i=0; i<root.childCount; i++)
            手动递归收集(root.GetChild(i), list);
    }

    /// <summary>用全量子物体列表做关键词查找（确保 inactive 也能找到）</summary>
    Transform 手动查找多词(Transform root, string[] 关键词)
    {
        if(root == null) return null;
        foreach(var t in 全量子物体)
        {
            if(t != root && !t.IsChildOf(root)) continue;
            foreach(var kw in 关键词)
                if(t.name.Contains(kw)) return t;
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
        // 复用 Awake 里手动递归找到的面板根（不要重新 GetComponentsInChildren！对 inactive 有 bug）
        Transform 面板根 = 缓存面板根 != null ? 缓存面板根 : transform;
        Debug.Log($"[交换宝] Start 面板根复用 = {面板根.name}");

        // 用面板根找面板内按钮（转换/上一个/下一个这些面板内的按钮）
        if (转换按钮 == null) 转换按钮 = 按名字找按钮多词(面板根, new[] { "转换按钮", "转换", "更换" });
        if (上一个按钮 == null) 上一个按钮 = 按名字找按钮多词(面板根, new[] { "上一个", "前一个" });
        if (下一个按钮 == null) 下一个按钮 = 按名字找按钮多词(面板根, new[] { "下一个", "后一个" });

        Debug.Log($"[交换宝] Start 查找结果 - 转换={转换按钮?.name ?? "null"} 上一个={上一个按钮?.name ?? "null"} 下一个={下一个按钮?.name ?? "null"}");

        收集类型与图标();
        Debug.Log($"收集到类型数: {全部类型.Count} — {string.Join(", ", 全部类型)}");
        Debug.Log($"图标数: {类型图标.Count}");
        解析数量文本();
        Debug.Log($"解析后 选择数量文本 = {(选择数量文本 != null ? 选择数量文本.name : "null")}");
        构建保活列表();

        // 守卫组件负责在"按下"瞬间快照窗口状态
        给按钮加守卫(转换按钮);
        给按钮加守卫(上一个按钮);
        给按钮加守卫(下一个按钮);

        // 延迟到第一帧结束后再注册：保证我们的点击回调排在其他脚本（Start 里注册的开关页/翻页逻辑）之后，
        // 这样无论谁在同一次点击里关了窗口，我们都能在最后把它恢复
        StartCoroutine(延迟绑定按钮());

        // ⚠️ 关键！先设当前类型，再刷新显示——否则当前类型=null→图标被设为null→enabled=false→消失！
        if (string.IsNullOrEmpty(当前类型)) 当前类型 = 找第一个可用类型();
        刷新选择显示();
        刷新结果显示();
    }

    IEnumerator 延迟绑定按钮()
    {
        yield return null; // 等所有 Start 执行完
        var am = AudioManager.Instance;

        if (转换按钮 != null)
        {
            转换按钮.onClick.AddListener(点击转换);
            if (am != null) 转换按钮.onClick.AddListener(am.播放按钮音效);
        }
        else Debug.LogWarning("[交换宝] 找不到“转换”按钮，请在 Inspector 手动指定");

        if (上一个按钮 != null)
        {
            上一个按钮.onClick.AddListener(点击上一个);
            if (am != null) 上一个按钮.onClick.AddListener(am.播放按钮音效);
        }
        else Debug.LogWarning("[交换宝] 找不到“上一个”按钮");

        if (下一个按钮 != null)
        {
            下一个按钮.onClick.AddListener(点击下一个);
            if (am != null) 下一个按钮.onClick.AddListener(am.播放按钮音效);
        }
        else Debug.LogWarning("[交换宝] 找不到“下一个”按钮");

        // 入口按钮也要音效
        if (am != null && 转换按钮 != null) am.绑定场景里所有按钮音效();
    }

    Button 按名字找按钮多词(string[] 关键词)
    {
        foreach (var t in 全量子物体)
        {
            var b = t.GetComponent<Button>();
            if(b == null) continue;
            foreach (string kw in 关键词)
                if (b.name.Contains(kw)) return b;
        }
        return null;
    }
    // 带起始 Transform 的重载
    Button 按名字找按钮多词(Transform 起点, string[] 关键词)
    {
        if(起点 == null) return 按名字找按钮多词(关键词);
        foreach (var t in 全量子物体)
        {
            if(t != 起点 && !t.IsChildOf(起点)) continue;
            var b = t.GetComponent<Button>();
            if(b == null) continue;
            foreach (string kw in 关键词)
                if (b.name.Contains(kw)) return b;
        }
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
        // ============ 手动鼠标点击检测（绕过 Button.onClick 的所有坑）============
        if(Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            var am = AudioManager.Instance;
            
            // 面板打开时：手动 Hit Test 面板内按钮
            if(缓存面板根 != null && 缓存面板根.gameObject.activeSelf)
            {
                if(上一个按钮 != null && RectTransformUtility.RectangleContainsScreenPoint(上一个按钮.GetComponent<RectTransform>(), mousePos))
                {
                    Debug.Log("[交换宝] 🖱️ 手动命中 上一个鱼宝");
                    if(am != null) am.播放按钮音效();
                    点击上一个();
                }
                else if(下一个按钮 != null && RectTransformUtility.RectangleContainsScreenPoint(下一个按钮.GetComponent<RectTransform>(), mousePos))
                {
                    Debug.Log("[交换宝] 🖱️ 手动命中 下一个鱼宝");
                    if(am != null) am.播放按钮音效();
                    点击下一个();
                }
                else if(转换按钮 != null && RectTransformUtility.RectangleContainsScreenPoint(转换按钮.GetComponent<RectTransform>(), mousePos))
                {
                    Debug.Log("[交换宝] 🖱️ 手动命中 转换按钮");
                    if(am != null) am.播放按钮音效();
                    点击转换();
                }
            }
        }
        
        // 每 0.5 秒校准一次
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
            if (DropItem.GetCount(t) > 0 && 类型图标.ContainsKey(t)) return t;
        return null;
    }

    void OnDisable()
    {
        反注册数量文本();
    }

    void LateUpdate()
    {
        // 兜底1：结果展示期内，按钮点击链路中若关窗，本帧渲染前恢复
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
        // ⚠️ 关键！用【后序遍历】（先递归子物体再判断自己），让最深层的精确匹配先返回！
        // 场景里"交换到的数量"(父级) 和 "交换到的"(子级) 都包含关键词，深层优先才能命中真正的槽位
        for(int i=0; i<root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            Transform found = 深度查找(c, 名字关键词);  // 先递归子物体
            if(found != null) return found;  // 深层匹配优先返回
        }
        // 所有子物体都没找到才检查自己
        if (root.name == 名字关键词 || root.name.Contains(名字关键词)) return root;
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
        // ⚠️ 必须挂在 槽 的 PARENT 下！不能挂在 槽 下面！
        // 因为 槽 很可能就是结果图标本身（查找槽内图标 返回 槽.GetComponent<Image>()），
        // 子物体永远画在父物体前面——SetAsFirstSibling 对父子关系无效！
        rt.SetParent(槽.parent != null ? 槽.parent : 槽, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        // 结果槽可能不是拉伸锚点，用它的实际 rect 尺寸
        var 槽rt = 槽 as RectTransform;
        Vector2 槽尺寸 = 槽rt != null ? 槽rt.rect.size : Vector2.zero;
        if (槽尺寸.x <= 1f) 槽尺寸.x = 120f;
        if (槽尺寸.y <= 1f) 槽尺寸.y = 120f;
        rt.sizeDelta = 槽尺寸 * 1.9f;
        rt.anchoredPosition = 槽rt != null ? 槽rt.anchoredPosition : Vector2.zero;
        // 对齐锚点到结果槽的锚点
        rt.anchorMin = 槽rt != null ? 槽rt.anchorMin : new Vector2(0.5f, 0.5f);
        rt.anchorMax = 槽rt != null ? 槽rt.anchorMax : new Vector2(0.5f, 0.5f);

        光晕 = go.AddComponent<Image>();
        光晕.sprite = 光晕Sprite;
        光晕.raycastTarget = false;
        光晕.preserveAspect = true;
        光晕.color = new Color(发光颜色.r, 发光颜色.g, 发光颜色.b, 0f);
        go.SetActive(false);
        
        // ⚠️ 关键！光晕必须排在结果槽的兄弟序列里在它前面，这样才会被结果槽盖住！
        // SetAsFirstSibling 让它排在兄弟序列的最底层（UI 里先画的在下面）
        光晕.transform.SetAsFirstSibling();
        // 再确保结果槽排在光晕后面（画在光晕上面盖住它）
        if(槽.parent != null) 槽.SetAsLastSibling();
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
        Debug.Log($"[交换宝] 场景里 DropItemCounterUI 共 {all.Length} 个");
        foreach (DropItemCounterUI c in all)
        {
            if (c == null || !c.gameObject.scene.IsValid()) continue;
            string t = c.itemType;
            if (string.IsNullOrEmpty(t)) continue;
            if (!全部类型.Contains(t)) 全部类型.Add(t);

            // 每个 CounterUI 所在面板里肯定有鱼的 Image——往上爬到面板根，找里面的 Image
            if (!类型图标.ContainsKey(t))
            {
                var img = 找CounterUI面板里的图标(c);
                if (img != null)
                {
                    类型图标[t] = img;
                    Debug.Log($"[交换宝]   ✅ 图标 {t} → {img.name} (从面板 Image)");
                }
                else
                {
                    // 兜底：场景里已有的 DropItem 实例
                    var drops = Resources.FindObjectsOfTypeAll<DropItem>();
                    foreach (var d in drops)
                    {
                        if (d == null) continue;
                        if (d.itemType == t)
                        {
                            var sr = d.GetComponent<SpriteRenderer>();
                            if (sr != null && sr.sprite != null)
                            {
                                类型图标[t] = sr.sprite;
                                Debug.Log($"[交换宝]   ✅ 图标 {t} → {sr.sprite.name} (从场景 DropItem)");
                                break;
                            }
                        }
                    }
                }
            }
        }

        Debug.Log($"[交换宝] 图标数: {类型图标.Count} / 类型总数: {全部类型.Count}");
    }

    /// <summary>从 DropItemCounterUI 往上爬到面板里，返回鱼图标的 sprite（排除面板背景框！）</summary>
    static Sprite 找CounterUI面板里的图标(DropItemCounterUI c)
    {
        Transform t = c.transform;
        Transform 面板根 = null;
        for (int i = 0; i < 5 && t != null; i++)
        {
            foreach (var img in t.GetComponentsInChildren<Image>(true))
            {
                if (img.transform == c.transform) continue;
                if (img.sprite != null) { 面板根 = t; break; }
            }
            if (面板根 != null) break;
            t = t.parent;
        }
        if (面板根 == null) return null;

        // 收集面板里所有有 sprite 且不是自己的 Image，按优先级排序
        var candidates = new List<Image>();
        foreach (var img in 面板根.GetComponentsInChildren<Image>(true))
        {
            if (img.transform == c.transform) continue;
            if (img.sprite == null) continue;
            candidates.Add(img);
        }

        if (candidates.Count == 0) return null;

        // 打印诊断：面板里有哪些 Image（帮助理解为什么选错了）
        if (candidates.Count > 1)
        {
            Debug.Log($"[交换宝] 面板 {面板根.name} 里有 {candidates.Count} 个 Image：");
            foreach (var img in candidates)
                Debug.Log($"  → {img.name} sprite={img.sprite.name}");
        }

        // 优先级：挑"鱼图"而不是"背景框"
        // 1. 名字含"头像"/"鱼"/"icon"/"Icon"（鱼图的典型命名）
        foreach (var img in candidates)
        {
            string n = img.name;
            if (n.Contains("头像") || n.Contains("鱼") || n.Contains("icon") || n.Contains("Icon") || n.Contains("avatar"))
                return img.sprite;
        }
        // 2. sprite.name 含 CounterUI.itemType 里的关键词（itemType 里去掉"宝"/"宝宝"后匹配）
        string typeKw = c.itemType.Replace("宝宝", "").Replace("宝", "").Trim();
        if (!string.IsNullOrEmpty(typeKw))
        {
            foreach (var img in candidates)
            {
                if (img.sprite.name.Contains(typeKw) || img.sprite.name.StartsWith(typeKw))
                    return img.sprite;
            }
        }
        // 3. 排除明显是背景的（名字含"框"/"背景"/"Frame"/"Bg"/"panel"/"Panel"）
        foreach (var img in candidates)
        {
            string n = img.name;
            if (!n.Contains("框") && !n.Contains("背景") && !n.Contains("Frame") && !n.Contains("Bg") && !n.Contains("panel") && !n.Contains("Panel"))
                return img.sprite;
        }
        // 4. 兜底：第一个（但大概率是背景框，打个 warning）
        Debug.LogWarning($"[交换宝] 面板里没找到鱼图候选，fallback 到第一个（可能是背景框！）");
        return candidates[0].sprite;
    }

    // ================= 业务逻辑 =================

    List<string> 有数量的类型()
    {
        var list = new List<string>();
        foreach (string t in 全部类型)
            if (DropItem.GetCount(t) > 0 && 类型图标.ContainsKey(t)) list.Add(t);
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

            // 光晕始终垫在结果图标下面（兄弟顺序：光晕先画，结果槽后画盖住中心）
            if (光晕 != null)
            {
                光晕.transform.SetAsFirstSibling(); // 最底层先画
                if(结果槽 != null && 结果槽.parent != null) 结果槽.SetAsLastSibling(); // 结果槽最后画盖在上面
                // 按槽位当前实际尺寸再校准一次（Awake 时布局可能还没完成）
                var 槽rt = 结果槽 as RectTransform;
                Vector2 槽尺寸 = 槽rt != null ? 槽rt.rect.size : Vector2.zero;
                if (槽尺寸.x <= 1f) 槽尺寸.x = 120f;  // 兜底：空Transform用默认值
                if (槽尺寸.y <= 1f) 槽尺寸.y = 120f;
                光晕.rectTransform.sizeDelta = 槽尺寸 * 1.9f;
                光晕.rectTransform.anchoredPosition = 槽rt != null ? 槽rt.anchoredPosition : Vector2.zero;
                光晕.gameObject.SetActive(true);
            }

            结果特效协程 = StartCoroutine(放大发光(展示结束时刻));
        }
        else
        {
            // 非展示态：稳稳显示原始图，无光晕、无缩放、无残留旋转
            结果图标.sprite = 原始结果图;
            结果图标.enabled = 原始结果图 != null;
            结果图标.rectTransform.localScale = Vector3.one;
            结果图标.rectTransform.localRotation = Quaternion.identity; // ⚠️ 清零旋转，防止中途切换残留
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
        结果图标.rectTransform.localRotation = Quaternion.identity; // ⚠️ 旋转归零
        if (光晕 != null) 光晕.gameObject.SetActive(false);
    }

    // ================= 特效 =================

    /// <summary>换到宝物：弹性放大 + 旋转翻牌入场，金色光晕呼吸，到展示结束时刻自动切回原始图</summary>
    IEnumerator 放大发光(float 结束时刻)
    {
        RectTransform iconRt = 结果图标.rectTransform;
        RectTransform haloRt = 光晕 != null ? 光晕.rectTransform : null;

        // ⚠️ 入场前重置状态（防止上一次动画残留）
        iconRt.localScale = Vector3.zero;
        iconRt.localRotation = Quaternion.Euler(0, 0, 180); // 从180°翻转状态开始
        if (haloRt != null)
        {
            haloRt.localScale = Vector3.one * 0.3f;
            haloRt.localRotation = Quaternion.identity;
        }

        // 1. 弹性放大 + 旋转翻牌入场（Back 缓动，有一点过冲更有"蹦出来"的感觉）
        float 开始时刻 = Time.time;
        float 入场结束 = Mathf.Min(开始时刻 + 放大时长, 结束时刻);
        float 入场跨度 = Mathf.Max(0.0001f, 入场结束 - 开始时刻);
        while (Time.time < 入场结束)
        {
            float k = Mathf.Clamp01((Time.time - 开始时刻) / 入场跨度);
            float backK = BackEaseOut(k);
            iconRt.localScale = Vector3.one * backK;
            // 旋转：180° → 0°，也用Back缓动让翻牌有惯性感
            iconRt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(180f, 0f, backK));
            if (haloRt != null) haloRt.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, k);
            yield return null;
        }
        iconRt.localScale = Vector3.one;
        iconRt.localRotation = Quaternion.identity; // 确保归零
        if (haloRt != null) haloRt.localScale = Vector3.one;

        // 2. 金色光晕呼吸发光 + 图标轻微脉动，直到展示期结束
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
