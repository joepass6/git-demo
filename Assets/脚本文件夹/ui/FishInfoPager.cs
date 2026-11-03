using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 纯按钮翻页。挂在容器物体上，子物体名字含数字的就当页面（如 "1页"、"2页"），
/// 按名字里的数字排序；按钮叫"上一个"/"下一个"自动绑定；没数字的子物体（如"放置信息"、按钮自身）会被跳过。
/// 到边界按钮自动禁用，不循环。
/// </summary>
public class FishInfoPager : MonoBehaviour
{
    [Tooltip("按顺序填写页面，留空则自动按子物体名字里的数字排序")]
    public GameObject[] pages;

    [Tooltip("上一页按钮，留空自动找子节点'上一个'")]
    public Button prevButton;

    [Tooltip("下一页按钮，留空自动找子节点'下一个'")]
    public Button nextButton;

    static readonly Regex NumberInName = new Regex(@"\d+", RegexOptions.Compiled);

    int currentIndex;

    void Awake()
    {
        CollectPages();
        FindButtons();
    }

    void OnEnable()
    {
        CollectPages();
        FindButtons();
        ShowCurrent();
    }

    void CollectPages()
    {
        if (pages != null && pages.Length > 0) return;

        var found = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, GameObject>>();
        foreach (Transform c in transform)
        {
            Match m = NumberInName.Match(c.name);
            if (!m.Success) continue;                    // 名字里没数字 → 跳过（比如"上一个"、"下一个"、"放置信息"）
            int num = int.Parse(m.Value);                 // 取第一个出现的数字："1页"→1, "2页"→2, "1页 (3)"→1
            found.Add(new System.Collections.Generic.KeyValuePair<int, GameObject>(num, c.gameObject));
        }
        found.Sort((a, b) => a.Key.CompareTo(b.Key));

        pages = new GameObject[found.Count];
        for (int i = 0; i < found.Count; i++) pages[i] = found[i].Value;
        currentIndex = 0;
    }

    void FindButtons()
    {
        if (prevButton == null) prevButton = FindDeep(transform, "上一个")?.GetComponent<Button>();
        if (nextButton == null) nextButton = FindDeep(transform, "下一个")?.GetComponent<Button>();

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

    /// <summary>
    /// 重新收集页面并翻到第一页。
    /// 供运行时动态增删页面（如 CodexPageBuilder）后刷新使用。
    /// </summary>
    public void RefreshAndFlipToFirst()
    {
        CollectPages();
        FindButtons();
        ShowCurrent();
    }

    public void PrevPage()
    {
        if (pages == null || pages.Length == 0) return;
        if (currentIndex <= 0) return;
        currentIndex--;
        ShowCurrent();
    }

    public void NextPage()
    {
        if (pages == null || pages.Length == 0) return;
        if (currentIndex >= pages.Length - 1) return;
        currentIndex++;
        ShowCurrent();
    }

    void ShowCurrent()
    {
        if (pages == null) return;
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null) pages[i].SetActive(i == currentIndex);
        }
        if (prevButton != null) prevButton.interactable = currentIndex > 0;
        if (nextButton != null) nextButton.interactable = currentIndex < pages.Length - 1;
    }
}
