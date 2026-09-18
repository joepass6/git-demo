using UnityEngine;

/// <summary>
/// 没有鱼时点击按钮弹出指定物体
/// 挂在任意物体上，把按钮拖到 Inspector 或在 OnClick 中调用 TriggerPopupIfNoFish
/// </summary>
public class PopupOnNoFish : MonoBehaviour
{
    [Header("设置")]
    [Tooltip("要弹出的物体")]
    public GameObject popupObject;

    [Tooltip("弹出后自动关闭延迟（秒）")]
    public float autoCloseDelay = 2f;

    /// <summary>
    /// 供按钮 OnClick 调用：没鱼时弹出，有鱼时不弹
    /// </summary>
    public void TriggerPopupIfNoFish()
    {
        FishTagScanner scanner = FindObjectOfType<FishTagScanner>();
        if (scanner != null && scanner.GetFishCount() == 0)
        {
            ShowPopup();
        }
    }

    void ShowPopup()
    {
        if (popupObject == null) return;
        popupObject.SetActive(true);
        StartCoroutine(AutoHide());
    }

    System.Collections.IEnumerator AutoHide()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        if (popupObject != null) popupObject.SetActive(false);
    }
}