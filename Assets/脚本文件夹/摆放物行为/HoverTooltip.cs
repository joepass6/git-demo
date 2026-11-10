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
    private bool _enteredOnce;

    // ======= 悬停放大效果内部缓存 =======
    // 每个目标的原始值：不同组件类型用不同放大方式
    private class ScaleEntry
    {
        public Transform transform;
        public RectTransform rt;
        public Image image;                   // Image → 改 sizeDelta（不影响子物体）
        public Text uiText;                   // 旧版 UI Text → 改 scale + fontSize（Text 没子物体，安全）
        public TMP_Text tmpText;              // TextMeshPro → 同上
        public Vector2 originalSizeDelta;     // Image / 纯 UI 容器用
        public Vector3 originalScale;         // Text / 3D 用
        public float originalFontSize;        // Text/TMP 用
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

        if (_parentCanvas != null && _parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            _canvasCam = _parentCanvas.worldCamera;

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

        foreach (var t in source)
        {
            var rt = t as RectTransform;
            var entry = new ScaleEntry { transform = t, rt = rt };

            entry.image = t.GetComponent<Image>();
            entry.uiText = t.GetComponent<Text>();
            entry.tmpText = t.GetComponent<TMP_Text>();

            entry.originalSizeDelta = rt != null ? rt.sizeDelta : Vector2.zero;
            entry.originalScale = t.localScale;
            if (entry.uiText != null) entry.originalFontSize = entry.uiText.fontSize;
            else if (entry.tmpText != null) entry.originalFontSize = entry.tmpText.fontSize;

            _entries.Add(entry);
        }

        foreach (var e in _entries)
        {
            string type = "";
            if (e.uiText != null) type += "[UI Text]";
            if (e.tmpText != null) type += "[TMP]";
            if (e.image != null) type += "[Image]";
            if (e.rt != null && e.uiText == null && e.tmpText == null && e.image == null) type += "[RectTransform]";
            if (e.rt == null) type += "[3D]";
            Debug.Log($"[HoverTooltip] Awake 捕获: {e.transform.name} {type}");
        }
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
            Debug.LogWarning($"[HoverTooltip] '{gameObject.name}' 未配置 targets，自动禁用。");
            enabled = false;
            return;
        }

        foreach (var t in targets) if (t != null) t.SetActive(false);

        Debug.Log($"[HoverTooltip] '{gameObject.name}' 模式={_mode} targets={targets.Count} 就绪");
    }

    // ====== Collider 消息（3D/2D 主路径）======
    private void OnMouseEnter() { if (_mode == Mode.Collider_Msg) Enter(); }
    private void OnMouseExit()  { if (_mode == Mode.Collider_Msg) Exit(); }

    // ====== Update：UI 矩形检测 + 3D/2D 射线兜底 + 延迟隐藏 ======
    private void Update()
    {
        // 延迟隐藏倒计时
        if (enableShow && !_isHovering && hideDelay > 0f)
        {
            if (Time.time - _exitTime >= hideDelay)
            {
                foreach (var t in targets) if (t != null && t.activeSelf) t.SetActive(false);
                _exitTime = 0f;
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
    }

    // ====== 核心 ======
    private void Enter()
    {
        Debug.Log($"[HoverTooltip] ✅ Enter() 触发! gameObject={gameObject.name}");
        _isHovering = true;
        _exitTime = 0f;

        // 显隐功能（开关控制）
        if (enableShow)
            foreach (var t in targets) if (t != null) t.SetActive(true);

        // 悬停放大效果（开关控制）
        PlayScaleAnim(hoverScale);

        OnHoverEnter?.Invoke(this);
    }

    private void Exit()
    {
        Debug.Log($"[HoverTooltip] ❌ Exit() 触发! gameObject={gameObject.name}");
        _isHovering = false;
        _exitTime = Time.time;

        // 缩回原始大小（无论 hideDelay 多少都立刻缩回，视觉反馈更清晰）
        PlayScaleAnim(1f);

        // 显隐功能（开关控制）
        if (enableShow && hideDelay <= 0f)
        {
            foreach (var t in targets) if (t != null) t.SetActive(false);
            _exitTime = 0f;
        }
    }

    /// <summary>
    /// 同时放大/缩回所有目标。
    /// 💡 Image 背景 → 改 sizeDelta（不影响子物体 Text！）
    /// 💡 Text/TMP → 改 fontSize + localScale（Text 没子物体，安全放大）
    /// 💡 3D → 改 localScale
    /// </summary>
    void PlayScaleAnim(float targetMultiplier)
    {
        Debug.Log($"[HoverTooltip] PlayScaleAnim({targetMultiplier}) enableScale={enableScale} hoverScale={hoverScale} entries={_entries?.Count}");
        if (!enableScale) return;
        if (hoverScale <= 1.0001f && targetMultiplier >= 0.999f) return;
        if (_entries == null || _entries.Count == 0) return;

        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);

        if (scaleDuration <= 0.001f)
        {
            foreach (var e in _entries) ApplyTarget(e, targetMultiplier);
            return;
        }
        _scaleCoroutine = StartCoroutine(ScaleAllLerp(targetMultiplier, scaleDuration));
    }

    /// <summary>
    /// 把单个条目设为目标倍数的大小。
    /// 💡 Image 背景 → 改 sizeDelta（不影响子物体 Text！）
    /// 💡 Text/TMP → 改 fontSize + localScale（Text 没子物体，安全放大）
    /// 💡 3D → 改 localScale
    /// </summary>
    void ApplyTarget(ScaleEntry e, float multiplier)
    {
        if (e.transform == null) return;

        // 💡 注意：各个组件互不排斥！同一个 Transform 可能同时挂 Image + TMP，两个都要改

        // Text / TMP：改 fontSize（字体变清楚）
        if (e.uiText != null)
        {
            e.uiText.fontSize = Mathf.RoundToInt(e.originalFontSize * multiplier);
            Debug.Log($"[HoverTooltip] ApplyTarget: {e.transform.name} → UI Text fontSize={e.uiText.fontSize}");
        }
        if (e.tmpText != null)
        {
            e.tmpText.fontSize = e.originalFontSize * multiplier;
            Debug.Log($"[HoverTooltip] ApplyTarget: {e.transform.name} → TMP fontSize={e.tmpText.fontSize}");
        }
        // Image：改 sizeDelta（子物体完全不受影响）
        if (e.image != null && e.rt != null)
        {
            e.rt.sizeDelta = e.originalSizeDelta * multiplier;
            Debug.Log($"[HoverTooltip] ApplyTarget: {e.transform.name} → Image sizeDelta={e.rt.sizeDelta}");
        }

        // Text/TMP 需要额外改 scale（让视觉放大更明显）；3D 物体也改 scale
        if (e.uiText != null || e.tmpText != null || (e.rt == null && e.image == null))
        {
            e.transform.localScale = e.originalScale * multiplier;
            Debug.Log($"[HoverTooltip] ApplyTarget: {e.transform.name} → localScale={e.transform.localScale}");
        }
    }

    System.Collections.IEnumerator ScaleAllLerp(float targetMultiplier, float duration)
    {
        float elapsed = 0f;

        // 记录每个目标的"起始当前值"（上一段动画可能停在半路）
        var fromSizes = new Dictionary<ScaleEntry, Vector2>();  // Image / 容器 sizeDelta
        var fromScales = new Dictionary<ScaleEntry, Vector3>();  // Text / 3D scale
        var fromFonts = new Dictionary<ScaleEntry, float>();    // Text fontSize
        foreach (var e in _entries)
        {
            if (e.transform == null) continue;
            if (e.uiText != null) fromFonts[e] = e.uiText.fontSize;
            else if (e.tmpText != null) fromFonts[e] = e.tmpText.fontSize;
            if (e.uiText != null || e.tmpText != null || e.image == null)
                fromScales[e] = e.transform.localScale;
            if (e.rt != null && (e.image != null || (e.uiText == null && e.tmpText == null)))
                fromSizes[e] = e.rt.sizeDelta;
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            k = 1f - (1f - k) * (1f - k); // EaseOutQuad

            foreach (var e in _entries)
            {
                if (e.transform == null) continue;

                // Text / TMP → fontSize + localScale 同时 Lerp
                if (e.uiText != null)
                {
                    float fromFont = fromFonts.ContainsKey(e) ? fromFonts[e] : e.uiText.fontSize;
                    e.uiText.fontSize = Mathf.RoundToInt(Mathf.Lerp(fromFont, e.originalFontSize * targetMultiplier, k));
                    Vector3 fromScale = fromScales.ContainsKey(e) ? fromScales[e] : e.transform.localScale;
                    e.transform.localScale = Vector3.Lerp(fromScale, e.originalScale * targetMultiplier, k);
                }
                else if (e.tmpText != null)
                {
                    float fromFont = fromFonts.ContainsKey(e) ? fromFonts[e] : e.tmpText.fontSize;
                    e.tmpText.fontSize = Mathf.Lerp(fromFont, e.originalFontSize * targetMultiplier, k);
                    Vector3 fromScale = fromScales.ContainsKey(e) ? fromScales[e] : e.transform.localScale;
                    e.transform.localScale = Vector3.Lerp(fromScale, e.originalScale * targetMultiplier, k);
                }
                // Image → sizeDelta Lerp（子物体不受影响）
                else if (e.image != null && e.rt != null)
                {
                    Vector2 fromSize = fromSizes.ContainsKey(e) ? fromSizes[e] : e.rt.sizeDelta;
                    e.rt.sizeDelta = Vector2.Lerp(fromSize, e.originalSizeDelta * targetMultiplier, k);
                }
                // 其他 RectTransform 容器
                else if (e.rt != null)
                {
                    Vector2 fromSize = fromSizes.ContainsKey(e) ? fromSizes[e] : e.rt.sizeDelta;
                    e.rt.sizeDelta = Vector2.Lerp(fromSize, e.originalSizeDelta * targetMultiplier, k);
                }
                // 3D
                else
                {
                    Vector3 fromScale = fromScales.ContainsKey(e) ? fromScales[e] : e.transform.localScale;
                    e.transform.localScale = Vector3.Lerp(fromScale, e.originalScale * targetMultiplier, k);
                }
            }
            yield return null;
        }

        foreach (var e in _entries) ApplyTarget(e, targetMultiplier); // 精确到位
        _scaleCoroutine = null;
    }

    // ====== 检测辅助 ======
    private bool IsMouseInsideRect()
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _selfRT, Input.mousePosition, _canvasCam, out var local);
        return _selfRT.rect.Contains(local);
    }

    private void CheckByRaycast()
    {
        var cam = Camera.main != null ? Camera.main : Camera.current;
        if (cam == null) return;

        bool hitSelf = false;
        if (_col3D != null)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit) && hit.collider == _col3D) hitSelf = true;
        }
        else if (_col2D != null)
        {
            var worldPt = cam.ScreenToWorldPoint(Input.mousePosition);
            hitSelf = Physics2D.OverlapPoint(worldPt) == _col2D;
        }

        if (hitSelf && !_isHovering) Enter();
        else if (!hitSelf && _isHovering) Exit();
    }
}
