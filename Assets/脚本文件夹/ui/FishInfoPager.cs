using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// “场景内鱼的信息”面板：纯翻页 + 图鉴收集显示。
/// 挂在“容器”上：子节点 1~8 为静态页，每页下 N号鱼 槽位：
///   槽位自身 Image=鱼照片，子物体“印章”=掉宝盖章图，子物体“鱼名字”=名称。
/// 鱼名以编辑器里“鱼名字”的静态文字为准，用 鱼运动 的图鉴集合判定三种状态：
///   没出现 → 鱼照片黑色 + 名字“?”；出现但没掉宝 → 鱼照片正常+真名（无印章）；掉过宝 → 再盖印章。
/// 判定数据随存档持久化（GameStats 读档时会同步给 鱼运动 图鉴集合）。
/// </summary>
public class FishInfoPager : MonoBehaviour
{
    [Tooltip("按顺序填写页面（留空则自动收集子节点中名字为纯数字的页面，按数字排序）")]
    public GameObject[] pages;

    [Tooltip("上一页按钮（留空自动找子节点“上一个”）")]
    public Button prevButton;

    [Tooltip("下一页按钮（留空自动找子节点“下一个”）")]
    public Button nextButton;

    [Header("图鉴显示")]
    [Tooltip("没出现过的鱼的照片颜色（黑色剪影）")]
    public Color lockedColor = Color.black;

    [Tooltip("未出现鱼显示的名字")]
    public string lockedName = "?";

    [Tooltip("面板开着时每隔几秒刷新一次图鉴（鱼可能在面板打开期间首次出现）")]
    public float refreshInterval = 2f;

    int currentIndex;
    float refreshTimer;

    class Slot
    {
        public Image fishImage;    // 槽位自身的鱼照片 Image（未出现时染黑）
        public Image stamp;        // 子物体“印章”（掉宝后显示，本身不染色）
        public TMP_Text nameLabel;
        public string realName;    // 编辑器里摆好的真名（首次刷新前缓存）
        public Color fishColor;    // 鱼照片原色
    }
    readonly List<Slot> slots = new List<Slot>();

    static readonly Regex SlotNamePattern = new Regex(@"^\d+号鱼$", RegexOptions.Compiled);

    void Awake()
    {
        CollectPages();
        FindButtons();
        CollectSlots();
        FixCoralImageAspect();
    }

    void OnEnable()
    {
        CollectPages();
        FindButtons();
        CollectSlots();
        FixCoralImageAspect();
        ShowCurrent();
        RefreshCodex();
    }

    void Start()
    {
        ShowCurrent();
        RefreshCodex();
    }

    void Update()
    {
        if (refreshInterval <= 0f) return;
        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            RefreshCodex();
        }
    }

    void CollectPages()
    {
        if (pages != null && pages.Length > 0) return;

        var found = new List<KeyValuePair<int, GameObject>>();
        foreach (Transform c in transform)
        {
            if (int.TryParse(c.name.Trim(), out int num))
                found.Add(new KeyValuePair<int, GameObject>(num, c.gameObject));
        }
        found.Sort((a, b) => a.Key.CompareTo(b.Key));

        pages = new GameObject[found.Count];
        for (int i = 0; i < found.Count; i++) pages[i] = found[i].Value;
        currentIndex = 0;
    }

    void FindButtons()
    {
        if (prevButton == null) prevButton = FindButton("上一个");
        if (nextButton == null) nextButton = FindButton("下一个");

        if (prevButton != null)
        {
            prevButton.onClick.RemoveListener(PrevPage);
            prevButton.onClick.AddListener(PrevPage);
        }
        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(NextPage);
            nextButton.onClick.AddListener(NextPage);
        }
    }

    Button FindButton(string name)
    {
        Transform t = FindDeep(transform, name);
        return t != null ? t.GetComponent<Button>() : null;
    }

    Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform hit = FindDeep(c, name);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>扫描所有页里的 N号鱼 槽位：
    /// 槽位自身 Image = 鱼照片；子物体“印章”= 掉宝盖章图；子物体“鱼名字” = 名称文本。</summary>
    void CollectSlots()
    {
        if (slots.Count > 0) return;
        if (pages == null) return;

        foreach (GameObject page in pages)
        {
            if (page == null) continue;
            foreach (Transform slot in page.transform)
            {
                if (!SlotNamePattern.IsMatch(slot.name.Trim())) continue;

                Image fishImg = slot.GetComponent<Image>();
                Transform stampTf = FindChild(slot, "印章");
                Transform nameTf = FindChild(slot, "鱼名字");
                if (fishImg == null || stampTf == null || nameTf == null) continue;

                Image stampImg = stampTf.GetComponent<Image>();
                TMP_Text label = nameTf.GetComponent<TMP_Text>();
                if (stampImg == null || label == null) continue;

                // 鱼照片/印章都按原比例显示，不拉伸变形（位置尺寸保持编辑器摆好的样子）
                fishImg.preserveAspect = true;
                stampImg.preserveAspect = true;

                slots.Add(new Slot
                {
                    fishImage = fishImg,
                    fishColor = fishImg.color,
                    stamp = stampImg,
                    nameLabel = label,
                    // 注意：必须在第一次写入“?”之前缓存真名
                    realName = label.text
                });
            }
        }
    }

    /// <summary>每页的“珊瑚图像”在编辑器里被统一拉成同一尺寸的框，各珊瑚精灵原比例不同会变形。
    /// 运行时开启 Preserve Aspect，在框内按原比例完整显示（位置和框大小不动）。</summary>
    void FixCoralImageAspect()
    {
        if (pages == null) return;
        foreach (GameObject page in pages)
        {
            if (page == null) continue;
            Transform coralTf = FindChild(page.transform, "珊瑚图像");
            if (coralTf == null) continue;
            Image coralImg = coralTf.GetComponent<Image>();
            if (coralImg != null) coralImg.preserveAspect = true;
        }
    }

    Transform FindChild(Transform root, string name)
    {
        foreach (Transform c in root)
        {
            if (c.name == name) return c;
            Transform hit = FindChild(c, name);
            if (hit != null) return hit;
        }
        return null;
    }

    public void PrevPage()
    {
        if (pages == null || pages.Length == 0) return;
        currentIndex = currentIndex <= 0 ? pages.Length - 1 : currentIndex - 1;
        ShowCurrent();
        RefreshCodex();
    }

    public void NextPage()
    {
        if (pages == null || pages.Length == 0) return;
        currentIndex = currentIndex >= pages.Length - 1 ? 0 : currentIndex + 1;
        ShowCurrent();
        RefreshCodex();
    }

    void ShowCurrent()
    {
        if (pages == null) return;
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null) pages[i].SetActive(i == currentIndex);
        }
    }

    /// <summary>按图鉴状态刷新所有槽位（三种状态）：
    /// 没出现过 → 鱼照片变黑 + 名字“?” + 不盖印章；
    /// 出现过但没掉宝 → 鱼照片正常 + 真名 + 不盖印章；
    /// 掉过宝 → 鱼照片正常 + 真名 + 显示印章。</summary>
    void RefreshCodex()
    {
        foreach (Slot s in slots)
        {
            if (s == null || s.fishImage == null || s.stamp == null || s.nameLabel == null) continue;

            bool unlocked = 鱼运动.HasFishAppeared(s.realName);
            bool dropped = 鱼运动.HasFishDropped(s.realName);

            if (unlocked)
            {
                // 真名走多语言：立即按当前语言翻译，切语言时由管理器自动重译
                LocalizationManager.ApplyLabel(s.nameLabel, "鱼表格", s.realName);
            }
            else
            {
                s.nameLabel.text = lockedName;
            }

            // 鱼照片：出现过恢复原色，没出现过染成黑色剪影
            s.fishImage.color = unlocked ? s.fishColor : lockedColor;

            // 印章：只表示“掉过宝”，颜色保持编辑器原色，只切换显隐
            s.stamp.enabled = dropped;
        }
    }
}
