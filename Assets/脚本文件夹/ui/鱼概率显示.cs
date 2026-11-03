using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 鱼槽显示组件（v6 - 鱼图在页面根的直接子物体上）
///
/// 用户实际层级（关键！）：
///   1页（页面根，CodexPageBinder 挂这）
///   ├─ 珊瑚图像 (Image)
///   ├─ 页数 (TMP)
///   ├─ 鱼1 (Image)              ← 显示鱼图像（页面根直接子物体）
///   ├─ 鱼2 (Image)
///   ├─ 鱼3 (Image)
///   ├─ 鱼4 (Image)
///   └─ 容器
///      ├─ 上一个
///      ├─ 下一个
///      ├─ 鱼1                    ← FishSlotDisplay 挂这（边框/概率/时间/掉落物）
///      │  └─ 容器
///      │     ├─ 边框 (Image)
///      │     ├─ 掉落图像 (Image)
///      │     ├─ 出现概率 (TMP)
///      │     ├─ 停留时间 (TMP)
///      │     └─ 捕鱼时间 (TMP)
///      ├─ 鱼2
///      ├─ 鱼3
///      └─ 鱼4
///
/// 鱼图查找：沿父链找到页面根节点 → 找与自己同名的直接子物体 Image
/// 文本查找：自己子树里按关键词找 TMP
/// 掉落物查找：自己子树里按关键词找 Image
/// </summary>
public class FishSlotDisplay : MonoBehaviour
{
    [Header("自动查找（留空=自动扫）")]
    public Image fishImage;                    // 鱼图像（从页面根找同名 Image）
    public TextMeshProUGUI probabilityText;    // 出现概率
    public TextMeshProUGUI stayTimeText;       // 停留时间
    public TextMeshProUGUI catchTimeText;      // 捕鱼时间
    public Image dropItemImage;                // 掉落物图片

    [Header("运行时注入")]
    public int slotIndex;
    public ObjectSpawner objectSpawner;
    public Transform pageRoot;  // CodexPageBinder 会注入（不再自己爬！）

    void Reset() { AutoFindAll(); }
    void OnEnable() { AutoFindAll(); RefreshDisplay(); }
    void Start()   { AutoFindAll(); RefreshDisplay(); }

    void AutoFindAll()
    {
        // ====== 鱼图：找页面根的同名 Image ======
        if (fishImage == null)
        {
            // 优先用 CodexPageBinder 注入的 pageRoot（最可靠）
            Transform root = pageRoot;
            // 兜底：自己往上爬
            if (root == null) root = FindPageRoot();

            if (root != null)
            {
                string myName = gameObject.name;
                Transform sibling = root.Find(myName);
                if (sibling != null)
                {
                    fishImage = sibling.GetComponent<Image>();
                    if (fishImage == null)
                        fishImage = sibling.GetComponentInChildren<Image>(true);
                }
                if (fishImage == null)
                    Debug.LogWarning($"[FishSlotDisplay] {gameObject.name}: 页面根 '{root.name}' 下找不到同名 '{myName}' 的 Image 子物体");
            }
            else
            {
                Debug.LogWarning($"[FishSlotDisplay] {gameObject.name}: 找不到页面根节点！");
            }
        }

        // ====== 文本：自己子树里按关键词找 ======
        if (probabilityText == null) probabilityText = FindTmpContaining("概率");
        if (stayTimeText == null)    stayTimeText    = FindTmpContaining("停留");
        if (stayTimeText == null)    stayTimeText    = FindTmpContaining("存在");
        if (catchTimeText == null)   catchTimeText   = FindTmpContaining("捕鱼");
        if (catchTimeText == null)   catchTimeText   = FindTmpContaining("筑宝");
        if (catchTimeText == null)   catchTimeText   = FindTmpContaining("掉落");

        // ====== 掉落物图片 ======
        if (dropItemImage == null)
        {
            dropItemImage = FindImageContaining("掉落");
            if (dropItemImage == null) dropItemImage = FindImageContaining("物品");
        }
    }

    /// <summary>沿父链向上爬，找到页面根（含多个 "鱼X" 直接子物体的那个）</summary>
    Transform FindPageRoot()
    {
        // 一直往上找，直到某个父节点下面有多个叫 "鱼X" 的直接子物体
        Transform t = transform;
        while (t.parent != null)
        {
            // 检查这个节点是不是页面根：它下面是否有 2+ 个名字匹配 "鱼数字" 的直接子物体
            int fishChildCount = 0;
            foreach (Transform c in t)
            {
                if (c.name.StartsWith("鱼") && c.name.Length >= 2 && char.IsDigit(c.name[1]))
                    fishChildCount++;
            }
            if (fishChildCount >= 2) return t;
            t = t.parent;
        }
        return null;
    }

    // ====== 查找辅助 ======
    Image FindImageContaining(string keyword)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            var found = FindImageInChildren(transform.GetChild(i), keyword);
            if (found != null) return found;
        }
        return null;
    }
    static Image FindImageInChildren(Transform parent, string keyword)
    {
        if (parent.name.Contains(keyword))
        {
            var img = parent.GetComponent<Image>();
            if (img != null) return img;
            img = parent.GetComponentInChildren<Image>(true);
            if (img != null) return img;
        }
        for (int i = 0; i < parent.childCount; i++)
        {
            var hit = FindImageInChildren(parent.GetChild(i), keyword);
            if (hit != null) return hit;
        }
        return null;
    }

    TextMeshProUGUI FindTmpContaining(string keyword)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            var found = FindTmpInChildren(transform.GetChild(i), keyword);
            if (found != null) return found;
        }
        return null;
    }
    static TextMeshProUGUI FindTmpInChildren(Transform parent, string keyword)
    {
        if (parent.name.Contains(keyword))
        {
            var tmp = parent.GetComponent<TextMeshProUGUI>();
            if (tmp != null) return tmp;
            tmp = parent.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) return tmp;
        }
        for (int i = 0; i < parent.childCount; i++)
        {
            var hit = FindTmpInChildren(parent.GetChild(i), keyword);
            if (hit != null) return hit;
        }
        return null;
    }

    // ====== 主入口 ======
    public void RefreshDisplay()
    {
        if (objectSpawner == null) { ClearUI(); return; }

        // 🔥 关键补绑：ShowCoral 刚注入了 pageRoot，现在如果 fishImage 还没绑到正确位置，立刻绑！
        if (fishImage == null && pageRoot != null)
        {
            Transform sibling = pageRoot.Find(gameObject.name);
            if (sibling != null)
            {
                fishImage = sibling.GetComponent<Image>();
                if (fishImage == null) fishImage = sibling.GetComponentInChildren<Image>(true);
            }
            Debug.Log($"[FishSlotDisplay] 补绑鱼图: pageRoot={pageRoot.name} 找 '{gameObject.name}' → fishImage={fishImage?.gameObject.name ?? "(null)"}");
        }

        var entries = objectSpawner.GetSpawnEntries();
        if (slotIndex < 0 || slotIndex >= entries.Count) { ClearUI(); return; }

        var entry = entries[slotIndex];

        // ====== 诊断日志 ======
        string fishName = entry.prefab != null ? entry.prefab.name : "(null prefab)";
        string imgName = fishImage != null ? fishImage.gameObject.name : "(没找到Image)";
        Debug.Log($"[FishSlotDisplay] 槽位={gameObject.name} slotIndex={slotIndex}/{entries.Count} → 鱼预制体={fishName} → 显示到Image={imgName}");
        if (entry.prefab == null) { ClearUI(); return; }

        鱼运动 fish = entry.prefab.GetComponent<鱼运动>();
        if (fish != null)
        {
            // 鱼图
            if (fishImage != null)
            {
                fishImage.sprite = fish.fishSprite;
                fishImage.enabled = fish.fishSprite != null;
            }

            // 停留时间
            if (stayTimeText != null)
                stayTimeText.text = fish.destroyTime <= 0f ? "∞" : $"{Mathf.RoundToInt(fish.destroyTime)}s";

            // 捕鱼/筑宝时间
            if (catchTimeText != null)
                catchTimeText.text = $"{Mathf.RoundToInt(fish.dropInterval)}s";

            // 掉落物图片
            if (dropItemImage != null)
            {
                Sprite dropSprite = null;
                if (fish.itemPrefab != null)
                {
                    var sr = fish.itemPrefab.GetComponent<SpriteRenderer>();
                    if (sr != null) dropSprite = sr.sprite;
                    if (dropSprite == null)
                    {
                        var img = fish.itemPrefab.GetComponent<Image>();
                        if (img != null) dropSprite = img.sprite;
                    }
                    if (dropSprite == null)
                    {
                        var img = fish.itemPrefab.GetComponentInChildren<Image>(true);
                        if (img != null) dropSprite = img.sprite;
                    }
                }
                dropItemImage.sprite = dropSprite;
                dropItemImage.enabled = dropSprite != null;
            }
        }
        else
        {
            if (fishImage != null) fishImage.enabled = false;
            if (stayTimeText != null) stayTimeText.text = "";
            if (catchTimeText != null) catchTimeText.text = "";
            if (dropItemImage != null) { dropItemImage.sprite = null; dropItemImage.enabled = false; }
        }

        // 概率
        if (probabilityText != null)
        {
            float totalWeight = 0f;
            foreach (var e in entries)
                if (e.prefab != null) totalWeight += e.weight;

            float pct = totalWeight > 0f ? entry.weight / totalWeight * 100f : 0f;
            probabilityText.text = $"{Mathf.RoundToInt(pct)}%";
        }
    }

    void ClearUI()
    {
        if (fishImage != null)    { fishImage.sprite = null; fishImage.enabled = false; }
        if (probabilityText != null) probabilityText.text = "";
        if (stayTimeText != null)    stayTimeText.text = "";
        if (catchTimeText != null)   catchTimeText.text = "";
        if (dropItemImage != null) { dropItemImage.sprite = null; dropItemImage.enabled = false; }
    }
}
