using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// 继承 GraphicRaycaster，修正 WorldSpace Canvas 下的点击坐标。
/// 问题：相机有 targetTexture（RT），内部 pixelWidth/pixelHeight = RT 尺寸，
/// 但 Input.mousePosition 是窗口 Screen 坐标。两者不匹配 → 点击位置偏移。
/// 修复：在 Raycast 中把 eventPosition 从 Screen 坐标缩放到 RT 坐标。
/// </summary>
public class CustomGraphicRaycaster : GraphicRaycaster
{
    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        Canvas cvs = GetComponent<Canvas>();
        if (cvs == null) return;

        // ScreenSpaceCamera / WorldSpace 必须有相机才能算射线；
        // 优先用画布自己的 worldCamera（EnsureCanvasesOnTop 写的是缩放相机，未必是 Camera.main）
        if (cvs.renderMode != RenderMode.ScreenSpaceOverlay && cvs.worldCamera == null && Camera.main == null)
            return;

        // 计算坐标缩放比：Screen → targetTexture
        // 统一走 DesktopWindowController 的唯一换算入口，避免这里和鱼/珊瑚各写一套。
        Vector2 pos = DesktopWindowController.ScreenToRenderTexturePoint(eventData.position);

        // 临时替换 eventData.position 做 base raycast
        Vector2 origPos = eventData.position;
        eventData.position = pos;
        base.Raycast(eventData, resultAppendList);
        eventData.position = origPos;
    }
}
