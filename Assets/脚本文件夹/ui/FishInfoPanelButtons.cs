using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 鱼信息面板 - 按钮管理，面板跟随挂载物
/// </summary>
public class FishInfoPanelButtons : MonoBehaviour
{
    [Header("按钮引用")]
    public Button moveButton;
    public Button sellButton;
    public GameObject closeButton;

    [Header("跟随设置")]
    public bool followSpawner = true;
    public float offsetMultiplier = 1f;

    [Header("漂浮效果")]
    public bool floatEnabled = true;
    public float floatAmplitude = 12f;
    public float floatFrequency = 1.5f;

    private RectTransform rootPanelRect;
    private Canvas parentCanvas;
    private float logTimer;

    void Awake()
    {
        BindMoveButton();
        BindSellButton();
        FindRoot();
        Debug.Log($"[FishInfoPanelButtons] Awake: rootRect={rootPanelRect?.name}, canvas={parentCanvas?.name}");
    }

    void FindRoot()
    {
        RectTransform rt = GetComponent<RectTransform>();
        parentCanvas = rt.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogError("[FishInfoPanelButtons] 找不到Canvas!");
            return;
        }

        // 向上找到Canvas的直接子物体
        Transform t = rt;
        while (t.parent != null && t.parent != parentCanvas.transform)
            t = t.parent;

        rootPanelRect = t as RectTransform;

        // 禁用从root到canvas之间所有物体上的布局组件
        Transform check = rootPanelRect;
        while (check != null && check != parentCanvas.transform)
        {
            DisableLayoutsOn(check.gameObject);
            check = check.parent;
        }

        DisableLayoutsOn(parentCanvas.gameObject);

        // 关键修复: 如果根节点的锚点是stretch（填充），强制改为top-left
        // 这样position/anchoredPosition才能生效
        if (rootPanelRect != null)
        {
            bool isStretchH = rootPanelRect.anchorMin.x != rootPanelRect.anchorMax.x;
            bool isStretchV = rootPanelRect.anchorMin.y != rootPanelRect.anchorMax.y;

            if (isStretchH || isStretchV)
            {
                // 改为top-left锚点
                rootPanelRect.anchorMin = new Vector2(0f, 1f);
                rootPanelRect.anchorMax = new Vector2(0f, 1f);
                // 保持原尺寸
                Vector2 size = rootPanelRect.sizeDelta;
                rootPanelRect.sizeDelta = size;
                Debug.Log($"[FishInfoPanelButtons] 锚点已改为top-left, sizeDelta={size}");
            }
        }

        Debug.Log($"[FishInfoPanelButtons] FindRoot: root={rootPanelRect?.name}, canvas={parentCanvas.name}");
    }

    void DisableLayoutsOn(GameObject go)
    {
        foreach (var lg in go.GetComponents<HorizontalLayoutGroup>()) lg.enabled = false;
        foreach (var lg in go.GetComponents<VerticalLayoutGroup>()) lg.enabled = false;
        foreach (var lg in go.GetComponents<GridLayoutGroup>()) lg.enabled = false;
        foreach (var csf in go.GetComponents<ContentSizeFitter>()) csf.enabled = false;
        foreach (var les in go.GetComponentsInChildren<LayoutElement>()) les.ignoreLayout = true;
    }

    void LateUpdate()
    {
        if (!followSpawner) return;
        if (ObjectSpawner.CurrentSpawner == null) return;
        if (!gameObject.activeInHierarchy) return;
        if (rootPanelRect == null) FindRoot();
        if (rootPanelRect == null || parentCanvas == null) return;

        Vector2 offset = ObjectSpawner.CurrentSpawner.popupOffset * offsetMultiplier;
        Vector3 worldPos = ObjectSpawner.CurrentSpawner.transform.position;

        // 用渲染画布的相机做世界→屏幕投影，和画布保持一致，缩放时位置才不会偏
        Camera projCam = (parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay && parentCanvas.worldCamera != null)
            ? parentCanvas.worldCamera : Camera.main;
        Vector2 screenPos = projCam.WorldToScreenPoint(worldPos);
        Vector2 targetScreenPos = screenPos + offset;

        // 屏幕坐标 → Canvas局部坐标
        Camera cam = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        if (cam == null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = Camera.main;

        Vector2 localPos;
        bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            targetScreenPos,
            cam,
            out localPos);

        if (ok)
        {
            rootPanelRect.anchoredPosition = localPos;

            if (floatEnabled)
            {
                float y = Mathf.Sin(Time.time * floatFrequency * Mathf.PI * 2f) * floatAmplitude;
                rootPanelRect.anchoredPosition += new Vector2(0, y);
            }
        }
    }

    void BindMoveButton()
    {
        if (moveButton == null)
        {
            Debug.LogError("[FishInfoPanelButtons] moveButton 未设置！");
            return;
        }
        moveButton.onClick = new Button.ButtonClickedEvent();
        moveButton.onClick.AddListener(OnMoveButtonClick);
    }

    void BindSellButton()
    {
        if (sellButton == null)
        {
            Debug.LogError("[FishInfoPanelButtons] sellButton 未设置！");
            return;
        }
    }

    void OnMoveButtonClick()
    {
        if (ObjectSpawner.CurrentSpawner == null) return;
        MoveSystem.StartMove(ObjectSpawner.CurrentSpawner.gameObject, gameObject);
    }

    public void OnPanelShown(ObjectSpawner spawner)
    {
        if (spawner == null) return;
        rootPanelRect = null;
        FindRoot();
        if (sellButton != null)
        {
            SellFishItem sellItem = sellButton.GetComponent<SellFishItem>();
            if (sellItem == null) sellItem = sellButton.GetComponentInParent<SellFishItem>();
            if (sellItem == null) sellItem = sellButton.GetComponentInChildren<SellFishItem>();
            if (sellItem != null)
            {
                sellItem.targetSpawner = spawner;
            }
        }
    }
    public void ClickCloseButton()
    {
        closeButton.SetActive(false);
    }
}

