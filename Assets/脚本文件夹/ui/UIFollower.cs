using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class UIFollower : MonoBehaviour
{
    public Transform target; // 需要跟随的目标物体
    [Tooltip("世界单位偏移：跟随点相对 target 的偏移，随缩放一起缩放（钉在场景物体上用这个）")]
    public Vector2 worldOffset;
    public Vector2 offset = new Vector2(0, 50); // 屏幕像素偏移（不随缩放，做屏幕间隙用）

    private RectTransform rectTransform;
    private Canvas canvas;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    void LateUpdate()
    {
        // 1. 空引用检查
        if (target == null || rectTransform == null) return;
        if (canvas == null) canvas = GetComponentInParent<Canvas>();

        // 2. 用渲染这块画布的相机做世界→屏幕投影，和画布保持一致，缩放/分辨率变化时才不会偏
        Camera projCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            ? canvas.worldCamera
            : Camera.main;
        if (projCam == null) return;

        // 3. 获取目标在世界空间中的位置（加上世界单位偏移，随缩放一起缩放）
        Vector3 targetWorldPos = target.position + (Vector3)worldOffset;

        // 4. 转换为屏幕坐标
        Vector3 screenPos = projCam.WorldToScreenPoint(targetWorldPos);

        // 5. 物体在摄像机后方时隐藏 UI
        if (screenPos.z < 0)
        {
            rectTransform.position = new Vector3(-1000, -1000, 0);
            return;
        }

        // 6. 添加偏移量
        screenPos += (Vector3)offset;

        // 7. 屏幕坐标 → UI 位置（按画布渲染模式区分）
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
        {
            // ScreenSpaceCamera：position 是世界坐标，必须转换
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    rectTransform, screenPos, canvas.worldCamera, out Vector3 worldPos))
            {
                rectTransform.position = worldPos;
            }
        }
        else
        {
            // Overlay：屏幕像素坐标恰好就是世界坐标
            rectTransform.position = screenPos;
        }
    }
}