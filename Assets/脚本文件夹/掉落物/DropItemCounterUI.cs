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

        // 只在“本类型本次游戏还没播种过”时给初始值。
        // 不能用 GetCount==0 判断：玩家把某种宝用光（存档值就是 0）后，
        // 读档后再打开该面板会重新 Awake，0==0 会把数量又回满成初始值。
        if (resetEveryTime) DropItem.SetCount(itemType, initialCount);
        else DropItem.SeedInitial(itemType, initialCount);

        DropItem.RegisterCounter(itemType, _text);
        Debug.Log($"[CounterUI] 已注册: {itemType} 初始={DropItem.GetCount(itemType)} -> {gameObject.name}");
    }

    private void OnDestroy()
    {
        if (_text != null) DropItem.UnregisterCounter(itemType, _text);
    }
}