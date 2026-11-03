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

    [Tooltip("循环翻页：到最后一页点下一页回到第一页，第一页点上一页到最后一页")]
    public bool loop = true;

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
            if (!m.Success) continue;
            int num = int.Parse(m.Value);
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

    public void RefreshAndFlipToFirst()
    {
        CollectPages();
        FindButtons();
        ShowCurrent();
    }

    public void PrevPage()
    {
        if (pages == null || pages.Length == 0) return;
        if (loop)
            currentIndex = (currentIndex - 1 + pages.Length) % pages.Length;
        else
        {
            if (currentIndex <= 0) return;
            currentIndex--;
        }
        ShowCurrent();
    }

    public void NextPage()
    {
        if (pages == null || pages.Length == 0) return;
        if (loop)
            currentIndex = (currentIndex + 1) % pages.Length;
        else
        {
            if (currentIndex >= pages.Length - 1) return;
            currentIndex++;
        }
        ShowCurrent();
    }

    void ShowCurrent()
    {
        if (pages == null) return;
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null) pages[i].SetActive(i == currentIndex);
        }
        if (prevButton != null) prevButton.interactable = loop || currentIndex > 0;
        if (nextButton != null) nextButton.interactable = loop || currentIndex < pages.Length - 1;
    }
}
