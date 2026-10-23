using UnityEngine;

/// <summary>
/// 鼠标悬停提示
/// 挂在"1框/2框/3框/4框"上
/// 鼠标移入 → 显示tooltipUI
/// 鼠标移出 → 延迟hideDelay秒后隐藏
/// </summary>
public class HoverTooltip : MonoBehaviour
{
    /// <summary>任意 HoverTooltip 被鼠标进入时触发（教程用）</summary>
    public static event System.Action<HoverTooltip> OnHoverEnter;

    [Header("设置")]
    [Tooltip("拖入要显示的弹窗UI物体")]
    public GameObject tooltipUI;

    [Tooltip("拖入悬停时要打开的容器UI物体（可选）")]
    public GameObject containerUI;

    [Tooltip("鼠标移开后，延迟多久消失（秒）")]
    public float hideDelay = 0.5f;

    [Tooltip("弹窗相对于挂载物的位置偏移 (X, Y)")]
    public Vector2 positionOffset = new Vector2(0, 50);

    private RectTransform rt;
    private Canvas parentCanvas;
    private bool isMouseOver = false;
    private float mouseExitTime = 0;

    void Start()
    {
        rt = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (rt == null || parentCanvas == null)
        {
            Debug.LogError("[HoverTooltip] 必须挂在UI物体上（需要RectTransform和Canvas父物体）");
            enabled = false;
            return;
        }

        // 未配置弹窗 → 自动禁用，避免悬停时反复刷错误
        if (tooltipUI == null)
        {
            Debug.LogWarning($"[HoverTooltip] '{gameObject.name}' 未配置 tooltipUI，该悬停组件已自动禁用。如不需要可直接移除。");
            enabled = false;
            return;
        }

        if (tooltipUI != null)
        {
            tooltipUI.SetActive(false);
        }

        if (containerUI != null)
        {
            containerUI.SetActive(false);
        }
    }

    void Update()
    {
        if (rt == null || parentCanvas == null) return;

        bool mouseInside = IsMouseInsideRect();

        if (mouseInside && !isMouseOver)
        {
            isMouseOver = true;
            ShowTooltip();
            OnHoverEnter?.Invoke(this);
        }
        else if (!mouseInside && isMouseOver)
        {
            isMouseOver = false;
            mouseExitTime = Time.time;
        }

        if (!mouseInside && tooltipUI != null && tooltipUI.activeSelf)
        {
            if (Time.time - mouseExitTime >= hideDelay)
            {
                tooltipUI.SetActive(false);
                if (containerUI != null) containerUI.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 检测鼠标是否在物体矩形内
    /// </summary>
    bool IsMouseInsideRect()
    {
        Vector2 localPoint;
        Camera cam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt, Input.mousePosition, cam, out localPoint);

        return rt.rect.Contains(localPoint);
    }

    /// <summary>
    /// 显示弹窗
    /// </summary>
    void ShowTooltip()
    {
        if (tooltipUI == null)
        {
            Debug.LogError("[HoverTooltip] tooltipUI 未配置!");
            return;
        }

        // 显示弹窗
        tooltipUI.SetActive(true);

        // 打开对应的容器
        if (containerUI != null)
        {
            containerUI.SetActive(true);
        }

        // 设置位置
        RectTransform tooltipRT = tooltipUI.GetComponent<RectTransform>();
        if (tooltipRT != null)
        {
            Camera cam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;

            Vector3 screenPos = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
            screenPos.x += positionOffset.x;
            screenPos.y += positionOffset.y;

            Vector2 localPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.transform as RectTransform, screenPos, cam, out localPos);

            tooltipRT.anchoredPosition = localPos;
        }
    }
}
