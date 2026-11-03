using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// 极简鱼图鉴绑定器（v2 - 适配用户实际层级）
///
/// 用户实际层级：
///   鱼图鉴（根）
///   ├─ Image
///   ├─ 按钮提示
///   └─ 容器 ← 挂 FishInfoPager
///      ├─ 下一个
///      ├─ 上一个
///      ├─ 1页 ← CodexPageBinder 应该挂在这里！
///      │  ├─ 鱼4 ← 子物体名就是 "鱼1"/"鱼2"/"鱼3"/"鱼4"
///      │  │  └─ 容器
///      │  │     ├─ 掉落物 (TMP)
///      │  │     ├─ 出现概率 (TMP)
///      │  │     ├─ 停留时间 (TMP)
///      │  │     └─ 捕鱼时间 (TMP)
///      │  ├─ 鱼3
///      │  ├─ 鱼2
///      │  ├─ 鱼1
///      │  ├─ 珊瑚图像 (Image)
///      │  └─ 页数 (TMP)
///      └─ 2页
///
/// 使用：给【每一页】根节点挂 CodexPageBinder（1页挂一个、2页挂一个）
///      ObjectSpawner.OpenPanel() 会自动搜所有 CodexPageBinder 并调用 ShowCoral
/// </summary>
public class CodexPageBinder : MonoBehaviour
{
    [Header("槽位名字（按 spawnEntries 索引顺序对应）")]
    public string[] slotNames = new string[] { "鱼1", "鱼2", "鱼3", "鱼4" };

    [Header("页码显示（可选，留空自动找子物体'页数'）")]
    public TextMeshProUGUI pageNumberText;

    // 找到的 FishSlotDisplay 列表
    readonly List<FishSlotDisplay> cachedSlots = new List<FishSlotDisplay>();

    static readonly Regex NumberInName = new Regex(@"\d+", RegexOptions.Compiled);

    void Awake()
    {
        ScanSlots();
        AutoBindPageNumber();
    }

    /// <summary>扫描子节点上的 FishSlotDisplay（递归找！）</summary>
    void ScanSlots()
    {
        cachedSlots.Clear();

        if (slotNames != null && slotNames.Length > 0)
        {
            foreach (var name in slotNames)
            {
                // 递归找子物体（不只是直接子物体！）
                Transform child = FindChildRecursive(transform, name);
                if (child == null)
                {
                    Debug.LogWarning($"[CodexPageBinder] {gameObject.name}: 递归找不到 '{name}'");
                    continue;
                }
                // FishSlotDisplay 可能挂在 child 自己身上，也可能在子节点上
                var slot = child.GetComponent<FishSlotDisplay>();
                if (slot == null) slot = child.GetComponentInChildren<FishSlotDisplay>(true);
                if (slot != null)
                    cachedSlots.Add(slot);
                else
                    Debug.LogWarning($"[CodexPageBinder] {gameObject.name}: '{name}' 上没有 FishSlotDisplay 组件");
            }
        }
        else
        {
            // 兜底：自动找所有 FishSlotDisplay
            cachedSlots.AddRange(GetComponentsInChildren<FishSlotDisplay>(true));
            cachedSlots.Sort((a, b) => a.slotIndex.CompareTo(b.slotIndex));
        }

        Debug.Log($"[CodexPageBinder] {gameObject.name}: 扫描完成 → {cachedSlots.Count} 个 FishSlotDisplay");
    }

    void AutoBindPageNumber()
    {
        if (pageNumberText != null) return;
        Transform t = FindChildRecursive(transform, "页数");
        if (t != null) pageNumberText = t.GetComponent<TextMeshProUGUI>();
    }

    /// <summary>运行时指定珊瑚 → 刷新所有槽位显示</summary>
    public void ShowCoral(ObjectSpawner spawner)
    {
        if (spawner == null) { Debug.LogWarning("[CodexPageBinder] ShowCoral spawner=null"); return; }

        if (cachedSlots.Count == 0) ScanSlots();

        var entries = spawner.GetSpawnEntries();

        // 页码
        AutoBindPageNumber();
        if (pageNumberText != null)
        {
            Match m = NumberInName.Match(gameObject.name);
            if (m.Success) pageNumberText.text = m.Value;
        }

        Debug.Log($"[CodexPageBinder] {gameObject.name}: ShowCoral({spawner.name}) → entries={entries.Count} slots={cachedSlots.Count}");

        for (int i = 0; i < cachedSlots.Count; i++)
        {
            if (cachedSlots[i] == null) continue;
            cachedSlots[i].slotIndex = i;
            cachedSlots[i].objectSpawner = spawner;
            cachedSlots[i].pageRoot = transform;  // 🔥 关键：告诉它页面根就是我！
            cachedSlots[i].RefreshDisplay();

            // 鱼数量不够的槽位隐藏
            cachedSlots[i].gameObject.SetActive(i < entries.Count);
        }
    }

    // ======= 递归找子物体 =======
    static Transform FindChildRecursive(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name) return child;
            Transform hit = FindChildRecursive(child, name);
            if (hit != null) return hit;
        }
        return null;
    }
}
