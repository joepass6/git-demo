using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;

/// <summary>
/// 鱼图鉴页面显示组件（挂在每个鱼槽位上）
/// </summary>
public class FishJournalPage : MonoBehaviour
{
    [Header("必选")]
    public GameObject fishPrefab;
    public ObjectSpawner spawner;

    [Header("可选 UI 绑定（留空自动扫）")]
    public Image fishImage;                    // 鱼图（挂在自己身上的那个 Image）
    public TextMeshProUGUI probabilityText;   // 出现概率
    public TextMeshProUGUI stayTimeText;      // 停留时间
    public TextMeshProUGUI catchTimeText;     // 掉宝时间
    public Image dropItemImage;               // 掉落物图片
    public TextMeshProUGUI pageNumberText;    // 页码

    // ===== 图鉴收集：哪些鱼被发现过 =====
    static readonly string FoundPrefix = "FishFound_";
    public static void MarkFound(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.SetInt(FoundPrefix + key, 1);
        PlayerPrefs.Save();
        // 自动刷新所有 FishJournalPage（图鉴页面跟着变色）
        foreach (var page in FindObjectsOfType<FishJournalPage>(true))
        {
            if (page != null && page.fishPrefab != null && page.fishPrefab.name == key)
                page.Refresh();
        }
    }
    public static bool IsFound(string key) { return string.IsNullOrEmpty(key) ? false : PlayerPrefs.GetInt(FoundPrefix + key, 0) == 1; }
    public static void ClearAllFound() { PlayerPrefs.DeleteKey(FoundPrefix); }

    void Reset() { AutoBind(); Refresh(); }
    void OnValidate() { AutoBind(); Refresh(); }
    void OnEnable() { AutoBind(); Refresh(); }
    void Start()   { AutoBind(); Refresh(); }

    void AutoBind()
    {
        // fishImage 就是挂在自己身上的那个 Image 组件
        if (fishImage == null) fishImage = GetComponent<Image>();
        if (probabilityText == null) probabilityText = FindTmp(new[] { "概率", "几率" });
        if (stayTimeText == null)    stayTimeText    = FindTmp(new[] { "停留", "存在" });
        if (catchTimeText == null)   catchTimeText   = FindTmp(new[] { "捕鱼", "筑宝", "掉宝", "掉落" });
        if (dropItemImage == null)   dropItemImage   = FindImg(new[] { "掉落", "物品" });
        if (pageNumberText == null)  pageNumberText  = FindTmp(new[] { "页数", "页码" });
        if (pageNumberText == null)
        {
            Transform t = transform.Find("页数");
            if (t != null) pageNumberText = t.GetComponent<TextMeshProUGUI>();
        }
        if (pageNumberText != null)
        {
            var m = Regex.Match(gameObject.name, @"\d+");
            if (m.Success) pageNumberText.text = m.Value;
        }
    }

    TextMeshProUGUI FindTmp(string[] kws)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            var hit = FindTmpDeep(transform.GetChild(i), kws);
            if (hit != null) return hit;
        }
        return null;
    }
    static TextMeshProUGUI FindTmpDeep(Transform t, string[] kws)
    {
        foreach (var kw in kws)
            if (t.name.Contains(kw))
            {
                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null) return tmp;
                tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) return tmp;
            }
        for (int i = 0; i < t.childCount; i++)
        {
            var hit = FindTmpDeep(t.GetChild(i), kws);
            if (hit != null) return hit;
        }
        return null;
    }
    Image FindImg(string[] kws)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            var hit = FindImgDeep(transform.GetChild(i), kws);
            if (hit != null) return hit;
        }
        return null;
    }
    static Image FindImgDeep(Transform t, string[] kws)
    {
        foreach (var kw in kws)
            if (t.name.Contains(kw))
            {
                var img = t.GetComponent<Image>();
                if (img != null) return img;
                img = t.GetComponentInChildren<Image>(true);
                if (img != null) return img;
            }
        for (int i = 0; i < t.childCount; i++)
        {
            var hit = FindImgDeep(t.GetChild(i), kws);
            if (hit != null) return hit;
        }
        return null;
    }

    public void Refresh()
    {
        if (fishPrefab == null) { ClearAll(); return; }

        鱼运动 fish = fishPrefab.GetComponent<鱼运动>();
        if (fish == null) { ClearAll(); return; }

        string key = fishPrefab.name;
        bool found = IsFound(key);

        // ========== 鱼图 ==========
        if (fishImage != null)
        {
            // 不管有没有发现，sprite 都设成鱼的图（保证有图可显示/变黑）
            if (fish.fishSprite != null)
                fishImage.sprite = fish.fishSprite;
            else if (fishImage.sprite == null)
                fishImage.enabled = false;

            // 发现了：正常颜色；没发现：纯黑
            fishImage.color = found ? Color.white : Color.black;

            Debug.Log($"[FishJournalPage] {gameObject.name} → 鱼预制体={key} → found={found} → fishImage.color={(found ? "白" : "黑")}");
        }

        // ========== 停留时间 ==========
        if (stayTimeText != null)
            stayTimeText.text = fish.destroyTime <= 0f ? "∞" : $"{Mathf.RoundToInt(fish.destroyTime)}s";

        // ========== 掉宝时间 ==========
        if (catchTimeText != null)
            catchTimeText.text = $"{Mathf.RoundToInt(fish.dropInterval)}s";

        // ========== 掉落物图片 ==========
        if (dropItemImage != null)
        {
            Sprite drop = null;
            if (fish.itemPrefab != null)
            {
                var sr = fish.itemPrefab.GetComponent<SpriteRenderer>();
                if (sr != null) drop = sr.sprite;
                if (drop == null)
                {
                    var img = fish.itemPrefab.GetComponent<Image>();
                    if (img != null) drop = img.sprite;
                }
                if (drop == null) drop = fish.itemPrefab.GetComponentInChildren<Image>(true)?.sprite;
            }
            dropItemImage.sprite = drop;
            dropItemImage.enabled = drop != null;
            dropItemImage.color = found ? Color.white : Color.black;
        }

        // ========== 出现概率 ==========
        if (probabilityText != null && spawner != null)
        {
            var entries = spawner.GetSpawnEntries();
            float total = 0f, mine = 0f;
            foreach (var e in entries)
            {
                if (e.prefab == null) continue;
                total += e.weight;
                var ef = e.prefab.GetComponent<鱼运动>();
                if (e.prefab.name == fishPrefab.name
                    || (ef != null && ef.fishName == fish.fishName))
                    mine += e.weight;
            }
            float pct = total > 0f ? mine / total * 100f : 0f;
            probabilityText.text = $"{Mathf.RoundToInt(pct)}%";
        }
        else if (probabilityText != null)
        {
            probabilityText.text = "";
        }
    }

    void ClearAll()
    {
        if (fishImage != null) { fishImage.sprite = null; fishImage.enabled = false; fishImage.color = Color.white; }
        if (probabilityText != null) probabilityText.text = "";
        if (stayTimeText != null)    stayTimeText.text = "";
        if (catchTimeText != null)   catchTimeText.text = "";
        if (dropItemImage != null) { dropItemImage.sprite = null; dropItemImage.enabled = false; dropItemImage.color = Color.white; }
    }
}
