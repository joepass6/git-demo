using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 卖掉按钮
/// 点击后：销毁指定的物体 + 按这株珊瑚放置时真实消耗的宝物逐条退回库存
///
/// 使用方法：
/// 1. 把 SellFishItem 脚本挂到"卖掉"按钮上
/// 2. 在 Inspector 中：
///    - Target Object: 拖入要删除的物体（场景中的物体）
/// 3. 选中"卖掉"按钮 → Button → On Click → + → 拖入"卖掉"按钮物体 → 选 SellFishItem → Sell()
/// </summary>
public class SellFishItem : MonoBehaviour
{
    [Header("引用配置")]
    [Tooltip("要卖掉的ObjectSpawner物体（如果不填，会自动查找场景中所有ObjectSpawner）")]
    public ObjectSpawner targetSpawner;

    [Tooltip("要关闭的UI页面物体")]
    public GameObject uiPanel;

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

        // 3. 按这株珊瑚放置时真实消耗的宝物逐条退回。
        //    放置来源是场景里的 DragAndPlaceSystem 图标：用「预制体名」把这株实例和它的图标对上，
        //    再读该图标的 需求列表（每条 itemType + cost），多框需求逐条退回、同类型自动累加。
        DragAndPlaceSystem source = FindSourcePlacement(objToDestroy);
        if (source != null)
        {
            source.按需求退回库存();
        }
        else
        {
            // 历史上这里写死 AddCount("珊瑚", ...)，而场景里根本没有这个类型，数字全加到了死键上。
            // 现在找不到真实需求时宁可不退，也不写死类型，避免再次污染存档。
            Debug.LogWarning($"[SellFishItem] 找不到 '{objToDestroy.name}' 对应的放置图标（按 prefab 同名匹配），本次不返还");
        }

        // 4. 关闭页面（先关页面再销毁物体）
        ObjectSpawner.CloseAllPanels();
        Debug.Log("[SellFishItem] 已关闭所有面板");

        // 5. 销毁 ObjectSpawner 物体本身
        Debug.Log($"[SellFishItem] 销毁ObjectSpawner物体: {objToDestroy.name}");
        Destroy(objToDestroy);
    }

    /// <summary>
    /// 找到生成这株珊瑚的放置图标：放置时实例名就是 prefab 名（读档会还原成原名），
    /// 所以用「prefabToSpawn.name == 实例名（去掉 Clone 后缀）」匹配，场景里未激活的图标也算。
    /// </summary>
    static DragAndPlaceSystem FindSourcePlacement(GameObject placed)
    {
        if (placed == null) return null;
        string placedName = placed.name.Replace("(Clone)", "").Trim();

        DragAndPlaceSystem[] all = FindObjectsOfType<DragAndPlaceSystem>(true);
        for (int i = 0; i < all.Length; i++)
        {
            DragAndPlaceSystem icon = all[i];
            if (icon == null || icon.prefabToSpawn == null) continue;
            if (icon.prefabToSpawn.name.Trim() == placedName) return icon;
        }
        return null;
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


