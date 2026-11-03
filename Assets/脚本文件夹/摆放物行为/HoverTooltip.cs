using System.Collections.Generic;
using UnityEngine;

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
    [Header("★ 悬停时要显示的物体（可多个，从 Inspector 拖）")]
    public List<GameObject> targets = new List<GameObject>();

    [Header("隐藏设置")]
    [Tooltip("鼠标移开后延迟多久消失（秒）。0 = 立刻消失")]
    public float hideDelay = 0.5f;

    [Header("进阶（3D/2D 用，UI 一般不用管）")]
    [Tooltip("强制每帧射线检测（3D/2D OnMouseEnter 死活不触发时勾）")]
    public bool forceRaycastMode = false;

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
        if (!_isHovering && hideDelay > 0f)
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
        _isHovering = true;
        _exitTime = 0f;
        foreach (var t in targets) if (t != null) t.SetActive(true);
        OnHoverEnter?.Invoke(this);
    }

    private void Exit()
    {
        _isHovering = false;
        _exitTime = Time.time;
        if (hideDelay <= 0f)
        {
            foreach (var t in targets) if (t != null) t.SetActive(false);
            _exitTime = 0f;
        }
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
