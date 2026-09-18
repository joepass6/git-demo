using UnityEngine;
using TMPro;

/// <summary>
/// 把这个脚本挂在要显示宝物数量的文本上
/// 用法：挂在 TextMeshProUGUI 物体上 → 填写 itemType → 完事
/// </summary>
public class DropItemCounterUI : MonoBehaviour
{
    [Tooltip("要显示的宝物类型（如 珊瑚、珍珠、贝壳...）要和 DropItem 上的 itemType 一致")]
    public string itemType = "珊瑚";

    [Tooltip("游戏开始时的初始数量")]
    public int initialCount = 100;

    [Tooltip("是否每次进入场景都重置为initialCount（勾选会每次重置，不勾选只首次设置）")]
    public bool resetEveryTime = false;

    private TextMeshProUGUI _text;

    private void Awake()
    {
        _text = GetComponent<TextMeshProUGUI>();
        if (_text == null) _text = GetComponentInChildren<TextMeshProUGUI>(true);
        if (_text == null) { Debug.LogWarning($"[CounterUI] {gameObject.name} 上找不到 TextMeshProUGUI"); return; }

        if (resetEveryTime || DropItem.GetCount(itemType) == 0)
        {
            DropItem.SetCount(itemType, initialCount);
        }

        DropItem.RegisterCounter(itemType, _text);
        Debug.Log($"[CounterUI] 已注册: {itemType} 初始={DropItem.GetCount(itemType)} -> {gameObject.name}");
    }

    private void OnDestroy()
    {
        if (_text != null) DropItem.UnregisterCounter(itemType, _text);
    }
}