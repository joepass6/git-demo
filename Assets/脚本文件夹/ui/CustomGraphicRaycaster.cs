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

        // WorldSpace Canvas 需要正确的 worldCamera
        Camera cam = cvs.renderMode == RenderMode.WorldSpace && cvs.worldCamera != null
            ? cvs.worldCamera
            : Camera.main;
        if (cam == null) return;

        // 计算坐标缩放比：Screen → targetTexture
        Vector2 pos = eventData.position;
        float screenW = Screen.width;
        float screenH = Screen.height;
        float texW = cam.pixelWidth;
        float texH = cam.pixelHeight;
        if (screenW > 0 && screenH > 0 && texW > 0 && texH > 0)
        {
            pos.x = pos.x * texW / screenW;
            pos.y = pos.y * texH / screenH;
        }

        // 临时替换 eventData.position 做 base raycast
        Vector2 origPos = eventData.position;
        eventData.position = pos;
        base.Raycast(eventData, resultAppendList);
        eventData.position = origPos;
    }
}
