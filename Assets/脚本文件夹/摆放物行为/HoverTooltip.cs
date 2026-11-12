using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 鼠标悬停显隐指定物体 —— 同时支持 UI / 3D / 2D
///
/// 核心逻辑（跟原版一致，只加了多物体和 3D/2D 兼容）：
///   UI 物体  → Update 里矩形判定鼠标是否在物体上（不依赖 EventSystem，最稳）
///   3D/2D   → OnMouseEnter/Exit（需 Collider + MainCamera）
///              若不触发 → 勾 Force Raycast Mode 走射线检测
///
/// 旧版 tooltipUI / containerUI 自动迁移到 targets。
/// 脚本**只负责显隐**，不碰 target 的位置 —— 由你在 Canvas 里布置好即可。
/// </summary>
public class HoverTooltip : MonoBehaviour
{
    /// <summary>任意 HoverTooltip 被鼠标进入时触发（教程用）</summary>
    public static event System.Action<HoverTooltip> OnHoverEnter;

    // ======= 新 API =======
    [Header("★ 显示/隐藏功能")]
    [Tooltip("勾上才启用显隐。关掉=悬停时不显示任何物体（只做放大或什么都不做）")]
    public bool enableShow = true;

    [Tooltip("悬停时要显示的物体（可多个，从 Inspector 拖）")]
    public List<GameObject> targets = new List<GameObject>();

    [Header("隐藏设置")]
    [Tooltip("鼠标移开后延迟多久消失（秒）。0 = 立刻消失")]
    public float hideDelay = 0.5f;

    [Header("进阶（3D/2D 用，UI 一般不用管）")]
    [Tooltip("强制每帧射线检测（3D/2D OnMouseEnter 死活不触发时勾）")]
    public bool forceRaycastMode = false;

    [Header("悬停放大效果（可选）")]
    [Tooltip("勾上才启用放大效果。关掉=悬停时不放大任何物体")]
    public bool enableScale = true;

    [Tooltip("要放大的物体列表。每个元素独立放大，互不干扰。留空=默认放大自己（挂脚本的物体）")]
    public List<Transform> scaleTargets = new List<Transform>();

    [Tooltip("放大多少倍（1=不变，1.1=放大10%，1.2=放大20%）。列表里每个物体都会放大这倍数")]
    public float hoverScale = 1.1f;

    [Tooltip("放大/缩回动画时长（秒）。0=瞬间跳变")]
    public float scaleDuration = 0.15f;

    // ======= 旧 API 自动迁移 =======
#pragma warning disable 0414
    [Tooltip("(旧版字段，将自动迁移到 targets)")] public GameObject tooltipUI;
    [Tooltip("(旧版字段，将自动迁移到 targets)")] public GameObject containerUI;
#pragma warning restore 0414

    // ======= 内部缓存 =======
    private enum Mode { UI_FrameCheck, Collider_Msg, Collider_Raycast }
    private Mode _mode;

    private RectTransform _selfRT;
    private Canvas _parentCanvas;
    private Camera _canvasCam;
    private Collider _col3D;
    private Collider2D _col2D;

    private bool _isHovering;
    private float _exitTime;
    private bool _hidePending;   // 本次离开后还有一次待执行的延迟隐藏
    private bool _enteredOnce;

    // ======= 悬停放大效果内部缓存 =======
    /// <summary>
    /// 一个“要放大的节点”（按钮根 / 底图 / 图标 / 文字）。
    /// 放大时会把它压小，但把它下面的【直接子级】钉在世界原处、原尺寸。
    /// </summary>
    private class ScaleEntry
    {
        public Transform transform;
        public string typeLabel;              // 仅用于日志标注类型
        public Vector3 originalScale;         // Awake 记下的 localScale
        public bool inheritsFromAncestor;     // 父级也是缩放目标 → 自己不额外乘，避免 m×m 叠乘
        public List<PinnedChild> children = new List<PinnedChild>();
    }

    /// <summary>被“钉住”的直接子级：父级放大后世界位置、世界尺寸都不变</summary>
    private class PinnedChild
    {
        public Transform transform;
        public Vector3 authoredLocalScale;    // 场景里设计好的 localScale（首次 Awake 记录，永不被放大污染）
        public Vector3 authoredLocalPosition; // 场景里设计好的 localPosition
        public Vector3 restLocalPosition;     // 进入 hover 时的 localPosition（还原用）
        public Vector3 restWorldPosition;     // 进入 hover 时的世界坐标（反算用）
        public bool activeAtHover;            // 进入 hover 时是否可见；inactive 的不参与钉住
        public bool compensatedWhileHover;    // 悬停中才被激活的子级已按设计值补偿过（只补一次）
    }

    private List<ScaleEntry> _entries = new List<ScaleEntry>();
    private Coroutine _scaleCoroutine;


    // ============================================================
    private void Awake()
    {
        _selfRT = GetComponent<RectTransform>();
        _parentCanvas = GetComponentInParent<Canvas>();
        _col3D = GetComponent<Collider>();
        _col2D = GetComponent<Collider2D>();

        _canvasCam = ResolveCanvasCamera();

        if (forceRaycastMode)
            _mode = (_col3D != null || _col2D != null) ? Mode.Collider_Raycast : Mode.UI_FrameCheck;
        else if (_selfRT != null && _parentCanvas != null)
            _mode = Mode.UI_FrameCheck;
        else if (_col3D != null || _col2D != null)
            _mode = Mode.Collider_Msg;
        else
        {
            _mode = Mode.UI_FrameCheck;
            Debug.LogWarning($"[HoverTooltip] '{gameObject.name}' 没 RectTransform/Canvas 也没 Collider，悬停大概率不触发。");
        }

        // 捕获所有放大目标的原始值
        // 优先级：你手动拖的 → 自己身上挂的 Image/Text/TMP → 自己
        // 💡 不递归子物体！只看自己这一个 Transform 上挂了什么组件
        _entries.Clear();
        var source = new List<Transform>();
        if (scaleTargets != null && scaleTargets.Count > 0)
        {
            // 1. 你手动拖了 → 只放大你拖的
            foreach (var t in scaleTargets) if (t != null) source.Add(t);
        }
        else
        {
            // 2. 没拖 → 自动检测自己身上挂的 Image + Text + TMP
            //    （不递归子物体！不碰子物体！就看自己身上有啥）
            bool found = false;
            foreach (var img in GetComponents<Image>()) { source.Add(img.transform); found = true; }
            foreach (var txt in GetComponents<Text>()) { source.Add(txt.transform); found = true; }
            foreach (var tmp in GetComponents<TMP_Text>()) { source.Add(tmp.transform); found = true; }
            // 3. 自己身上啥都没挂 → 放大自己（兜底）
            if (!found) source.Add(transform);
        }

        // 按层级从浅到深处理：“父级跳过 → 子级一起跳过”才能稳定生效
        source.Sort(delegate (Transform a, Transform b) { return DepthOf(a).CompareTo(DepthOf(b)); });

        var rawEntries = new List<ScaleEntry>();
        foreach (var t in source)
        {
            if (t == null) continue;

            // 同一个 Transform 被重复拖入时只保留一份
            bool dup = false;
            foreach (var r in rawEntries) if (r.transform == t) { dup = true; break; }
            if (dup) continue;

            rawEntries.Add(new ScaleEntry { transform = t, typeLabel = DescribeType(t), originalScale = t.localScale });
        }

        // 目标之间可能有父子关系（例如根节点 + 它下面的图标/文字）。
        // 父级已经放大 m 倍，子级再乘 m 就会叠成 m²，所以子级目标只继承、不额外乘。
        foreach (var e in rawEntries)
        {
            bool inherited = false;
            foreach (var other in rawEntries)
            {
                if (other == e || other.transform == null) continue;
                if (e.transform.IsChildOf(other.transform)) { inherited = true; break; }
            }
            e.inheritsFromAncestor = inherited;

            // 直接子级：不是缩放目标的（面板/容器/弹窗）一律钉住；
            // 是缩放目标的（图标/文字/提示）跟着按钮一起变大，不钉。
            for (int i = 0; i < e.transform.childCount; i++)
            {
                var c = e.transform.GetChild(i);
                bool isScaleTarget = false;
                foreach (var r in rawEntries) if (r.transform == c) { isScaleTarget = true; break; }
                if (isScaleTarget) continue;

                e.children.Add(new PinnedChild
                {
                    transform = c,
                    authoredLocalScale = c.localScale,
                    authoredLocalPosition = c.localPosition,
                    restLocalPosition = c.localPosition,
                    restWorldPosition = c.position,
                    activeAtHover = c.gameObject.activeSelf
                });
            }

            string childNames = "";
            foreach (var c in e.children) childNames += c.transform.name + ", ";
            Debug.Log($"[HoverTooltip] Awake 缩放目标: {e.transform.name} {e.typeLabel}" +
                      (e.inheritsFromAncestor ? "（父级已放大，自己只继承）" : $"（localScale ×{hoverScale}）") +
                      (e.children.Count > 0 ? $"；钉住 {e.children.Count} 个直接子级: {childNames}" : "；无子级需钉住"));
        }

        _entries = rawEntries;

        if (_entries.Count == 0)
            Debug.Log($"[HoverTooltip] Awake '{gameObject.name}' 没有可缩放目标，跳过放大（enableScale={enableScale}）。");
    }

    /// <summary>层级深度：根 = 0，用于让父级先于子级被判定</summary>
    static int DepthOf(Transform t)
    {
        int d = 0;
        for (var p = t; p != null; p = p.parent) d++;
        return d;
    }

    /// <summary>日志用：这个节点上挂了什么可见组件</summary>
    static string DescribeType(Transform t)
    {
        string type = "";
        if (t.GetComponent<Text>() != null) type += "[UI Text]";
        if (t.GetComponent<TMP_Text>() != null) type += "[TMP]";
        if (t.GetComponent<Image>() != null) type += "[Image]";
        if (type.Length == 0) type = (t is RectTransform) ? "[RectTransform]" : "[3D]";
        return type;
    }

    private void Start()
    {
        // 旧字段自动迁移（只迁移一次，防止反复添加）
        if (!_enteredOnce)
        {
            if (tooltipUI != null && !targets.Contains(tooltipUI)) targets.Add(tooltipUI);
            if (containerUI != null && !targets.Contains(containerUI)) targets.Add(containerUI);
            _enteredOnce = true;
        }

        if (targets.Count == 0)
        {
            // 只配了“悬停放大”、没有配显隐目标（外层按钮基本都是这种），
            // 以前这里直接 enabled=false，把放大逻辑一起关掉了。
            if (!enableScale)
            {
                Debug.LogWarning($"[HoverTooltip] '{gameObject.name}' 未配置 targets，自动禁用。");
                enabled = false;
                return;
            }
        }

        foreach (var t in targets)
            if (t != null && t.activeSelf)
            {
                Debug.Log($"[HoverTooltip] Start 隐藏目标: {t.name}");
                t.SetActive(false);
            }

        Debug.Log($"[HoverTooltip] '{gameObject.name}' 模式={_mode} targets={targets.Count} 就绪 canvas={(_parentCanvas != null ? _parentCanvas.name + "/" + _parentCanvas.renderMode : "无")} cam={(_canvasCam != null ? _canvasCam.name : "null")}");
    }

    /// <summary>
    /// 取出真正渲染当前 UI 的相机。
    /// Overlay 必须返回 null（传相机会让 ScreenPointToLocalPointInRectangle 算错）；
    /// 嵌套 Canvas（如「显示面板」序列化成 WorldSpace）自身 worldCamera 通常为空，
    /// 真正渲染它的是根 Canvas 的 worldCamera —— 该值由 DesktopWindowController.EnsureCanvasesOnTop
    /// 在运行时设成上屏相机。旧代码只读自身 worldCamera，得到 null，
    /// 于是外层按钮的悬停判定坐标整体偏移、永远进不了 Enter。
    /// </summary>
    private Camera ResolveCanvasCamera()
    {
        if (_parentCanvas == null) return null;
        if (_parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;

        var selfCam = _parentCanvas.worldCamera;
        if (selfCam != null) return selfCam;

        var root = _parentCanvas.rootCanvas;
        if (root != null && root != _parentCanvas &&
            root.renderMode != RenderMode.ScreenSpaceOverlay && root.worldCamera != null)
            return root.worldCamera;

        return Camera.main;
    }

    // ====== Collider 消息（3D/2D 主路径）======
    private void OnMouseEnter() { if (_mode == Mode.Collider_Msg) Enter(); }
    private void OnMouseExit()  { if (_mode == Mode.Collider_Msg) Exit(); }

    // ====== Update：UI 矩形检测 + 3D/2D 射线兜底 + 延迟隐藏 ======
    private void Update()
    {
        // 延迟隐藏倒计时：只在“这一次真的离开过”之后隐藏一次。
        // ⚠️ 旧写法把 _exitTime 归 0，而 Time.time - 0 永远 ≥ hideDelay，
        //    于是隐藏过一次之后每帧都重新隐藏 —— 任何被别处 SetActive(true) 的目标
        //    都会在下一帧被这里再关掉（“刚点开就没了”）。
        if (enableShow && _hidePending && !_isHovering && hideDelay > 0f)
        {
            if (Time.time - _exitTime >= hideDelay)
            {
                _hidePending = false;
                foreach (var t in targets)
                    if (t != null && t.activeSelf)
                    {
                        Debug.Log($"[HoverTooltip] 延迟隐藏目标: {t.name}（hideDelay={hideDelay}）");
                        t.SetActive(false);
                    }
            }
        }

        switch (_mode)
        {
            case Mode.UI_FrameCheck:
                if (_selfRT != null)
                {
                    bool inside = IsMouseInsideRect();
                    if (inside && !_isHovering) Enter();
                    else if (!inside && _isHovering) Exit();
                }
                break;

            case Mode.Collider_Raycast:
                CheckByRaycast();
                break;
        }

        // 悬停期间才被其它脚本打开的子级（面板/容器/提示）：
        // 它刚激活的这一刻会继承父级的 m 倍，这里同一帧按设计值 ÷ m 补偿一次。
        if (_isHovering) CompensateNewlyActivated();
    }

    /// <summary>
    /// 进入 hover 时还是隐藏的子级，若悬停期间被激活，按【场景设计值 ÷ m】换算一次：
    ///   世界尺寸 = m × (设计 ÷ m) = 设计；世界位置 = 父级 + 设计偏移，和没悬停时一致。
    /// 只补一次，避免每帧覆盖别处对该子级的正常移动。
    /// </summary>
    void CompensateNewlyActivated()
    {
        if (_entries == null || Mathf.Approximately(_currentMultiplier, 1f)) return;
        float inv = 1f / Mathf.Max(0.01f, _currentMultiplier);
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.transform == null) continue;
            for (int j = 0; j < e.children.Count; j++)
            {
                var c = e.children[j];
                if (c.transform == null || c.activeAtHover || c.compensatedWhileHover) continue;
                if (!c.transform.gameObject.activeInHierarchy) continue;

                c.transform.localScale = new Vector3(c.authoredLocalScale.x * inv, c.authoredLocalScale.y * inv, c.authoredLocalScale.z * inv);
                c.transform.localPosition = new Vector3(c.authoredLocalPosition.x * inv, c.authoredLocalPosition.y * inv, c.authoredLocalPosition.z * inv);
                c.compensatedWhileHover = true;
                Debug.Log($"[HoverTooltip] 悬停中新激活子级，已按设计值÷{_currentMultiplier:F2} 补偿: {e.transform.name}/{c.transform.name}");
            }
        }
    }

    // ====== 核心 ======
    private void Enter()
    {
        Debug.Log($"[HoverTooltip] ✅ Enter() 触发! gameObject={gameObject.name}");
        _isHovering = true;
        _exitTime = 0f;
        _hidePending = false;

        // 显隐功能（开关控制）
        if (enableShow)
            foreach (var t in targets)
                if (t != null)
                {
                    Debug.Log($"[HoverTooltip] Enter 显示目标: {t.name}（activeSelf {t.activeSelf}→true）");
                    t.SetActive(true);
                }

        // 悬停放大效果（开关控制）
        // 先记录子级此刻的世界坐标/本地缩放，再放父级 —— 反算才是“钉在原处”
        SnapshotPinnedChildren();
        PlayScaleAnim(hoverScale);

        OnHoverEnter?.Invoke(this);
    }

    private void Exit()
    {
        Debug.Log($"[HoverTooltip] ❌ Exit() 触发! gameObject={gameObject.name}");
        _isHovering = false;
        _exitTime = Time.time;
        _hidePending = hideDelay > 0f;

        // 缩回原始大小（无论 hideDelay 多少都立刻缩回，视觉反馈更清晰）
        PlayScaleAnim(1f);

        // 显隐功能（开关控制）
        if (enableShow && hideDelay <= 0f)
        {
            _hidePending = false;
            foreach (var t in targets)
                if (t != null)
                {
                    Debug.Log($"[HoverTooltip] Exit 隐藏目标: {t.name}（activeSelf {t.activeSelf}→false）");
                    t.SetActive(false);
                }
            _exitTime = 0f;
        }
    }

    /// <summary>
    /// 悬停到一半 UI 被关掉时，按钮不会卡在放大状态。
    /// 缩放目标和被钉住的子级都用【记录值】写回，不做任何 ÷m 运算。
    /// </summary>
    private void OnDisable() { RestoreAll(); }
    private void OnDestroy() { RestoreAll(); }

    /// <summary>进入 hover 前：记录每个被钉住子级的本地值与世界坐标（只记录当时可见的）</summary>
    void SnapshotPinnedChildren()
    {
        if (_entries == null) return;
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.transform == null) continue;
            for (int j = 0; j < e.children.Count; j++)
            {
                var c = e.children[j];
                if (c.transform == null) continue;
                // ⚠️ 隐藏中的子级不记录：inactive 物体的布局可能还没刷新，
                //    记下来的世界坐标一旦被激活后反算回去，容器就会被甩到屏幕外/被压成 1/m。
                c.activeAtHover = c.transform.gameObject.activeInHierarchy;
                c.compensatedWhileHover = c.activeAtHover;
                if (!c.activeAtHover) continue;

                c.restLocalPosition = c.transform.localPosition;
                c.restWorldPosition = c.transform.position;
            }
        }
    }

    /// <summary>还原：缩放目标与所有被钉住的子级都写回记录值（不做任何 ÷m）</summary>
    void RestoreAll()
    {
        if (_entries == null) return;
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.transform == null) continue;
            e.transform.localScale = e.originalScale;
            for (int j = 0; j < e.children.Count; j++)
            {
                var c = e.children[j];
                if (c.transform == null) continue;
                // 统一用【场景设计值】还原，杜绝上一次悬停残留的 1/m 或 m 被当成“原始值”。
                c.transform.localScale = c.authoredLocalScale;
                c.transform.localPosition = c.activeAtHover ? c.restLocalPosition : c.authoredLocalPosition;
            }
        }
        _currentMultiplier = 1f;
    }

    /// <summary>
    /// 同时放大/缩回所有目标（目标可以为按钮根节点，允许带着底图变大）。
    /// 子级由 ApplyTarget 逐帧反向缩放 + 位置反算，世界尺寸/世界位置保持不变。
    /// </summary>
    void PlayScaleAnim(float targetMultiplier)
    {
        if (!enableScale) return;
        if (hoverScale <= 1.0001f && targetMultiplier >= 0.999f) return;
        if (_entries == null || _entries.Count == 0) return;

        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);

        if (scaleDuration <= 0.001f)
        {
            _currentMultiplier = targetMultiplier;
            foreach (var e in _entries) ApplyTarget(e, targetMultiplier);
            return;
        }
        _scaleCoroutine = StartCoroutine(ScaleAllLerp(targetMultiplier, scaleDuration));
    }

    /// <summary>
    /// 把单个目标设为目标倍数，并把它的直接子级钉回世界原位。
    /// 💡 目标：localScale = 原始值 × m（父级也是目标时只继承，避免 m×m）。
    /// 💡 子级：localScale = 记录的原始 localScale ÷ m；
    ///          localPosition = 父级.InverseTransformPoint(记录的世界坐标)，
    ///          于是世界尺寸 = m × (原始/m) = 原始，世界位置 = 记录值，逐像素不变。
    /// 💡 每次都由记录值直接算出，不做递增乘除，中途反向也不留残值。
    /// </summary>
    void ApplyTarget(ScaleEntry e, float multiplier)
    {
        if (e.transform == null) return;

        e.transform.localScale = e.inheritsFromAncestor ? e.originalScale : e.originalScale * multiplier;

        float inv = 1f / Mathf.Max(0.01f, multiplier);
        var parent = e.transform;
        for (int i = 0; i < e.children.Count; i++)
        {
            var c = e.children[i];
            if (c.transform == null) continue;

            if (!c.activeAtHover)
            {
                // 进入 hover 时还是隐藏的子级（典型：点击「珊瑚调节」才打开的 珊瑚调整页面容器）：
                // 绝不拿 inactive 时记下的世界坐标去反算，否则一激活就被甩到屏幕外 / 压成 1/m。
                // 若它在悬停期间被打开，用【场景设计值】做同样的补偿：
                //   localScale = 设计值 ÷ m、localPosition = 设计值 ÷ m
                //   → 世界尺寸 = m × (1/m) = 1，世界位置 = 父级 + 设计偏移，和没悬停时逐像素一致。
                if (!c.transform.gameObject.activeInHierarchy) continue;
                c.transform.localScale = new Vector3(c.authoredLocalScale.x * inv, c.authoredLocalScale.y * inv, c.authoredLocalScale.z * inv);
                c.transform.localPosition = new Vector3(c.authoredLocalPosition.x * inv, c.authoredLocalPosition.y * inv, c.authoredLocalPosition.z * inv);
                continue;
            }

            c.transform.localScale = new Vector3(c.authoredLocalScale.x * inv, c.authoredLocalScale.y * inv, c.authoredLocalScale.z * inv);
            c.transform.localPosition = parent.InverseTransformPoint(c.restWorldPosition);
        }
    }

    /// <summary>当前缩放倍数（诊断用；1 = 未放大）</summary>
    public float CurrentMultiplier { get { return _currentMultiplier; } }
    /// <summary>当前是否处于悬停状态（诊断用）</summary>
    public bool IsHoveringNow { get { return _isHovering; } }

    /// <summary>
    /// 立刻按当前倍数重新应用一次（用于悬停期间刚被 SetActive(true) 的子级：
    /// 同一帧就把补偿算好，不会先以放大态闪一帧）。
    /// </summary>
    public void RepinNow()
    {
        if (_entries == null) return;
        for (int i = 0; i < _entries.Count; i++) ApplyTarget(_entries[i], _currentMultiplier);
    }

    /// <summary>诊断用：把缩放目标与被钉住子级的当前值拼成一行</summary>
    public string DumpScaleState()
    {
        if (_entries == null || _entries.Count == 0) return "无缩放目标";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.transform == null) continue;
            sb.Append($"{e.transform.name}(scale={e.transform.localScale})");
            if (e.inheritsFromAncestor) sb.Append("[继承父级]");
            for (int j = 0; j < e.children.Count; j++)
            {
                var c = e.children[j];
                if (c.transform == null) continue;
                sb.Append($" 子:{c.transform.name}(act={c.transform.gameObject.activeInHierarchy} scale={c.transform.localScale} pin={c.activeAtHover})");
            }
            if (i < _entries.Count - 1) sb.Append(" | ");
        }
        return sb.ToString();
    }

    /// <summary>上次动画停在的倍数：中途反向时从这里接着动，不会闪一下回到 1 倍</summary>
    private float _currentMultiplier = 1f;

    System.Collections.IEnumerator ScaleAllLerp(float targetMultiplier, float duration)
    {
        float from = _currentMultiplier;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            k = 1f - (1f - k) * (1f - k); // EaseOutQuad

            _currentMultiplier = Mathf.Lerp(from, targetMultiplier, k);
            foreach (var e in _entries) ApplyTarget(e, _currentMultiplier);
            yield return null;
        }

        _currentMultiplier = targetMultiplier;
        foreach (var e in _entries) ApplyTarget(e, targetMultiplier); // 精确到位
        _scaleCoroutine = null;
    }

    // ====== 检测辅助 ======
    /// <summary>
    /// 把屏幕鼠标坐标换算到相机实际渲染的分辨率。
    /// 本项目的相机渲染到 RenderTexture（桌宠窗口上屏），Camera.pixelWidth/Height 是 RT 尺寸，
    /// 而 Input.mousePosition 是窗口坐标。两者不一致时不换算，UI 矩形判定和射线都会整体偏移，
    /// 打包版里就会出现“鼠标明明在按钮上却没反应”。编辑器里 Screen 与 RT 通常一致，所以看不出问题。
    /// </summary>
    private static Vector3 ToCameraScreenPoint(Camera cam, Vector3 screenPos)
    {
        if (cam == null || cam.targetTexture == null) return screenPos;
        float tw = cam.targetTexture.width;
        float th = cam.targetTexture.height;
        if (tw <= 0f || th <= 0f || Screen.width <= 0 || Screen.height <= 0) return screenPos;

        screenPos.x *= tw / Screen.width;
        screenPos.y *= th / Screen.height;
        return screenPos;
    }

    private bool IsMouseInsideRect()
    {
        // Awake 时根 Canvas 的 worldCamera 可能还没被 DesktopWindowController 赋上（脚本执行顺序不定），
        // 这里补一次解析，避免相机为 null 时整块外层按钮永远判不到悬停。
        if (_canvasCam == null && _parentCanvas != null && _parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            _canvasCam = ResolveCanvasCamera();

        Vector2 mouse = ToCameraScreenPoint(_canvasCam, Input.mousePosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _selfRT, mouse, _canvasCam, out var local);
        return _selfRT.rect.Contains(local);
    }

    private void CheckByRaycast()
    {
        var cam = Camera.main != null ? Camera.main : Camera.current;
        if (cam == null) return;

        Vector3 mouse = ToCameraScreenPoint(cam, Input.mousePosition);
        bool hitSelf = false;
        if (_col3D != null)
        {
            var ray = cam.ScreenPointToRay(mouse);
            if (Physics.Raycast(ray, out var hit) && hit.collider == _col3D) hitSelf = true;
        }
        else if (_col2D != null)
        {
            // ScreenToWorldPoint 的 z 是“距相机的距离”，传 0 会得到相机自身位置，
            // 之前这里 2D 判定永远打不中；取到物体所在的 z=0 平面。
            float depth = Mathf.Abs(cam.transform.position.z);
            var worldPt = cam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, depth));
            // 同一物体可能挂“大点击圈 + 小落位圈”两个碰撞体，OverlapPoint 只返回其中一个，
            // 直接比引用会在两个圈之间随机判不中；改成判断“命中的是不是本物体/子物体”。
            var hit2D = Physics2D.OverlapPoint(worldPt);
            hitSelf = hit2D != null && (hit2D == _col2D || hit2D.transform == transform || hit2D.transform.IsChildOf(transform));
        }

        if (hitSelf && !_isHovering) Enter();
        else if (!hitSelf && _isHovering) Exit();
    }
}
