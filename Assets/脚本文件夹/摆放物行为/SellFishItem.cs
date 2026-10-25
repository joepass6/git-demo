using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 卖掉按钮
/// 点击后：销毁指定的物体 + 把返还数量加到指定的文本数字上
///
/// 使用方法：
/// 1. 把 SellFishItem 脚本挂到"卖掉"按钮上
/// 2. 在 Inspector 中：
///    - Target Object: 拖入要删除的物体（场景中的物体）
///    - Count Text: 拖入显示数字的TextMeshProUGUI（返还数量会加到这个文本上）
///    - Refund Amount: 返还数量（默认1）
/// 3. 选中"卖掉"按钮 → Button → On Click → + → 拖入"卖掉"按钮物体 → 选 SellFishItem → Sell()
/// </summary>
public class SellFishItem : MonoBehaviour
{
    [Header("引用配置")]
    [Tooltip("数字会加到这个TextMeshProUGUI上（和DragAndPlaceSystem的珊瑚数量文本是同一个）")]
    public TextMeshProUGUI countText;

    [Tooltip("消耗数量文本（和DragAndPlaceSystem的消耗文本是同一个），卖掉时读取此数值作为返还量")]
    public TextMeshProUGUI costText;

    [Tooltip("要卖掉的ObjectSpawner物体（如果不填，会自动查找场景中所有ObjectSpawner）")]
    public ObjectSpawner targetSpawner;

    [Tooltip("要关闭的UI页面物体")]
    public GameObject uiPanel;

    [Header("设置")]
    [Tooltip("返还的数量（当Cost Text未配置时使用此固定值）")]
    public int refundAmount = 1;

    /// <summary>
    /// 卖掉（绑定到按钮OnClick）
    /// 删除ObjectSpawner及其生成的所有鱼，只改变数值
    /// </summary>
    public void Sell()
    {
        Debug.Log("[SellFishItem] Sell() 被调用");

        // 预制体实例化后自动查找丢失的引用
        AutoFindMissingReferences();

        // 1. 获取要卖掉的 ObjectSpawner 物体
        // 优先使用 ObjectSpawner.CurrentSpawner（当前点击的挂载物）
        if (targetSpawner == null && ObjectSpawner.CurrentSpawner != null)
        {
            targetSpawner = ObjectSpawner.CurrentSpawner;
            Debug.Log("[SellFishItem] 使用ObjectSpawner.CurrentSpawner: " + targetSpawner.gameObject.name);
        }
        if (targetSpawner == null)
        {
            targetSpawner = FindObjectOfType<ObjectSpawner>();
        }

        if (targetSpawner == null)
        {
            Debug.LogError("[SellFishItem] 找不到 ObjectSpawner 物体!");
            return;
        }

        GameObject objToDestroy = targetSpawner.gameObject;
        Debug.Log($"[SellFishItem] 要卖掉的物体: {objToDestroy.name}");

        // 2. 删除 ObjectSpawner 生成的所有鱼
        List<鱼运动> fishList = targetSpawner.GetSpawnedFish();
        Debug.Log($"[SellFishItem] 要删除 {fishList.Count} 条鱼");
        foreach (var fish in fishList)
        {
            if (fish != null)
            {
                Debug.Log($"[SellFishItem] 删除鱼: {fish.name}");
                Destroy(fish.gameObject);
            }
        }

        // 3. 计算返还数量：优先从消耗文本读取，否则用固定值
        int actualRefund = refundAmount;
        if (costText != null)
        {
            int cost = 0;
            int.TryParse(costText.text, out cost);
            if (cost > 0) actualRefund = cost;
        }

        // 4. 返还珊瑚（通过 DropItem 静态系统，自动同步所有 UI 计数器）
        DropItem.AddCount("珊瑚", actualRefund);
        Debug.Log($"[SellFishItem] 返还珊瑚: +{actualRefund}, 当前={DropItem.GetCount("珊瑚")}");

        // 4b. 如果旧的 countText 还配置着，同步更新一下（兼容旧场景配置）
        if (countText != null)
        {
            countText.text = DropItem.GetCount("珊瑚").ToString();
        }

        // 3. 关闭页面（先关页面再销毁物体）
        ObjectSpawner.CloseAllPanels();
        Debug.Log("[SellFishItem] 已关闭所有面板");

        // 4. 销毁 ObjectSpawner 物体本身
        Debug.Log($"[SellFishItem] 销毁ObjectSpawner物体: {objToDestroy.name}");
        Destroy(objToDestroy);
    }

    /// <summary>
    /// 自动查找预制体实例化后丢失的场景引用
    /// </summary>
    void AutoFindMissingReferences()
    {
        // 自动查找 ObjectSpawner：查找本物体或子物体上的
        if (targetSpawner == null)
        {
            targetSpawner = GetComponent<ObjectSpawner>();
            if (targetSpawner == null) targetSpawner = GetComponentInChildren<ObjectSpawner>();
            if (targetSpawner == null) targetSpawner = FindObjectOfType<ObjectSpawner>();
            if (targetSpawner != null)
                Debug.Log($"[SellFishItem] 自动绑定 TargetSpawner: {targetSpawner.name}");
        }

        // 自动查找消耗文本：场景里找第一个 TextMeshProUGUI 名字含"消耗"或"cost"的
        // （DragAndPlaceSystem 本身没有 costText 字段，这里只自动查场景 UI）
        if (costText == null)
        {
            foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>())
            {
                if (tmp != null && (tmp.name.ToLower().Contains("cost") || tmp.name.Contains("消耗")))
                {
                    costText = tmp;
                    break;
                }
            }
            if (costText != null)
                Debug.Log($"[SellFishItem] 自动绑定 CostText: {costText.name}");
        }

        // 自动查找 UI 面板（从 FishTagScanner 获取）
        if (uiPanel == null)
        {
            FishTagScanner scanner = FindObjectOfType<FishTagScanner>();
            if (scanner != null)
            {
                uiPanel = scanner.panelRoot;
                if (uiPanel == null) uiPanel = scanner.gameObject;
            }
            if (uiPanel != null)
                Debug.Log($"[SellFishItem] 自动绑定 UIPanel: {uiPanel.name}");
        }
    }
}


