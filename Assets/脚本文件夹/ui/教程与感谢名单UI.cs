using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 教程分页 + 感谢名单面板的自动接线，运行时按名字找物体，Inspector 不需要拖任何引用。
///   教程：把「教程」节点下叠在一起的 按钮说明 / 拖放珊瑚(1~5) 当成页面，一次只显示一页；
///         自动找「下一页 / 上一页 / 关闭 / 页码」，找不到就用工程现有按钮的图/字体在运行时生成；
///         点「教程」标签可以开关教程内容。
///   感谢名单：点「感谢名单」打开它下面隐藏的「名单」面板；面板里的关闭按钮、点面板背景都能关；
///         每次点击都按面板当前 activeSelf 取反，不会重复绑监听叠出“关了又开”。
/// 思路与 LocalizationManager 自动绑定语言面板一致：运行时按名字找 + 补绑 onClick。
/// </summary>
[DisallowMultipleComponent]
public class 教程与感谢名单UI : MonoBehaviour
{
    static 教程与感谢名单UI 实例;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void 自动创建()
    {
        if (实例 != null) return;
        var go = new GameObject("[教程与感谢名单UI]");
        go.AddComponent<教程与感谢名单UI>();
    }

    // ===== 教程 =====
    Transform 设置页面;
    Transform 教程节点;
    Transform 教程控件容器;
    readonly List<GameObject> 页面列表 = new List<GameObject>();
    readonly List<Transform> 教程控件 = new List<Transform>();
    Button 上一页按钮;
    Button 下一页按钮;
    Button 教程关闭按钮;
    TMP_Text 页码文本;
    int 当前页;
    bool 教程已打开 = true;

    // ===== 感谢名单 =====
    Transform 感谢入口;
    Transform 名单面板;
    Button 名单关闭按钮;

    // ===== 生成按钮用的样式（从教程标签上抄图/字体，保持和现有 UI 一致）=====
    Sprite 按钮图;
    Image.Type 按钮图类型 = Image.Type.Simple;
    Color 按钮底色 = Color.white;
    Color 文字色 = Color.white;
    TMP_FontAsset 字体;

    // 缓存委托实例，重复初始化时能 RemoveListener 掉同一份，不会叠加
    UnityAction 下一页动作;
    UnityAction 上一页动作;
    UnityAction 教程关闭动作;
    UnityAction 教程标签动作;
    UnityAction 名单入口动作;
    UnityAction 名单关闭动作;

    void Awake()
    {
        if (实例 != null && 实例 != this) { Destroy(gameObject); return; }
        实例 = this;
        StartCoroutine(初始化());
    }

    void OnDestroy()
    {
        if (实例 == this) 实例 = null;
    }

    IEnumerator 初始化()
    {
        // 设置面板一开始可能是关闭的（物体未激活），但 Transform 依然存在，可以直接按名字找到
        float 已等 = 0f;
        while (设置页面 == null && 已等 < 15f)
        {
            设置页面 = 按名找("设置页面");
            if (设置页面 == null) 设置页面 = 按名找("设置页面", false);
            if (设置页面 != null) break;
            yield return null;
            已等 += Time.unscaledDeltaTime;
        }

        if (设置页面 == null)
        {
            Debug.LogWarning("[教程/感谢名单] 没找到「设置页面」，本次不接线");
            yield break;
        }

        // 等一帧，让 AudioManager 的 Start 先跑完，避免和待会儿统一补音效重复播
        yield return null;

        初始化教程();
        初始化感谢名单();

        // 运行时生成的按钮不在 AudioManager 启动扫描范围内，用它的公开方法补一次音效
        if (AudioManager.Instance != null) AudioManager.Instance.绑定场景里所有按钮音效();
    }

    // ==================== 教程 ====================

    void 初始化教程()
    {
        教程节点 = 按名找(设置页面, "教程");
        if (教程节点 == null)
        {
            Debug.LogWarning("[教程/感谢名单] 没找到 设置页面/图像/教程");
            return;
        }

        收集样式();
        收集页面();

        上一页按钮 = 找按钮(教程节点, "上一页", "上一张", "上一个", "prev");
        下一页按钮 = 找按钮(教程节点, "下一页", "下一张", "下一个", "next");
        教程关闭按钮 = 找按钮(教程节点, "关闭", "close");
        页码文本 = 找页码(教程节点);

        if (上一页按钮 == null || 下一页按钮 == null || 教程关闭按钮 == null || 页码文本 == null)
            建教程控件();

        // 场景里本来就有的按钮记进“教程控件”，关教程时一起隐藏
        if (上一页按钮 != null) 教程控件.Add(上一页按钮.transform);
        if (下一页按钮 != null) 教程控件.Add(下一页按钮.transform);
        if (教程关闭按钮 != null) 教程控件.Add(教程关闭按钮.transform);
        if (页码文本 != null) 教程控件.Add(页码文本.transform);

        下一页动作 = () => 翻页(1);
        上一页动作 = () => 翻页(-1);
        教程关闭动作 = () => { 教程已打开 = false; 刷新教程显示(); };
        教程标签动作 = 切换教程;

        绑定(下一页按钮, 下一页动作);
        绑定(上一页按钮, 上一页动作);
        绑定(教程关闭按钮, 教程关闭动作);

        // 教程标签本身就是按钮：点它开/关教程
        var 标签按钮 = 教程节点.GetComponent<Button>();
        绑定(标签按钮, 教程标签动作);

        刷新教程显示();
        Debug.Log($"[教程/感谢名单] 教程页数={页面列表.Count}，下一页={(下一页按钮 != null ? 下一页按钮.name : "无")}，页码={(页码文本 != null ? "有" : "无")}");
    }

    void 收集页面()
    {
        页面列表.Clear();
        foreach (Transform 子 in 教程节点)
        {
            if (子.name.StartsWith("__")) continue;          // 运行时生成的控件
            if (子.GetComponent<Button>() != null) continue; // 翻页/关闭按钮不算页面
            if (子.GetComponent<TMP_Text>() != null) continue; // 「教程」标签文本
            if (含任一(子.name, "页码", "页数", "page")) continue;
            页面列表.Add(子.gameObject);
        }

        // 名字里带数字的按数字排（拖放珊瑚 (1)~(5)），不带数字的（按钮说明）排最前
        页面列表.Sort((a, b) => 页号(a.name).CompareTo(页号(b.name)));
        当前页 = 0;
    }

    static int 页号(string 名)
    {
        Match m = Regex.Match(名, @"\d+");
        return m.Success ? int.Parse(m.Value) : -1;
    }

    void 刷新教程显示()
    {
        for (int i = 0; i < 页面列表.Count; i++)
        {
            if (页面列表[i] != null) 页面列表[i].SetActive(教程已打开 && i == 当前页);
        }

        foreach (Transform 控件 in 教程控件)
        {
            if (控件 != null) 控件.gameObject.SetActive(教程已打开);
        }
        if (教程控件容器 != null) 教程控件容器.gameObject.SetActive(教程已打开);

        int 总数 = 页面列表.Count;
        if (页码文本 != null) 页码文本.text = 总数 > 0 ? $"{当前页 + 1} / {总数}" : "0 / 0";
        if (上一页按钮 != null) 上一页按钮.interactable = 教程已打开 && 当前页 > 0;
        if (下一页按钮 != null) 下一页按钮.interactable = 教程已打开 && 当前页 < 总数 - 1;
    }

    void 翻页(int 方向)
    {
        if (!教程已打开 || 页面列表.Count == 0) return;

        int 新页 = Mathf.Clamp(当前页 + 方向, 0, 页面列表.Count - 1);
        if (新页 == 当前页) return;

        当前页 = 新页;
        刷新教程显示();
    }

    void 切换教程()
    {
        教程已打开 = !教程已打开;
        刷新教程显示();
    }

    void 关闭教程()
    {
        if (!教程已打开) return;
        教程已打开 = false;
        刷新教程显示();
    }

    // ==================== 感谢名单 ====================

    void 初始化感谢名单()
    {
        感谢入口 = 按名找("感谢名单");
        if (感谢入口 == null)
        {
            Debug.LogWarning("[教程/感谢名单] 没找到「感谢名单」按钮");
            return;
        }

        var 入口按钮 = 感谢入口.GetComponent<Button>();
        if (入口按钮 == null) 入口按钮 = 感谢入口.GetComponentInParent<Button>();
        if (入口按钮 == null)
        {
            Debug.LogWarning("[教程/感谢名单] 「感谢名单」上没有 Button 组件");
            return;
        }

        名单面板 = 按名找(感谢入口, "名单");
        if (名单面板 == null) 名单面板 = 按名找(感谢入口, "名单", false);
        if (名单面板 == null)
        {
            Debug.LogWarning("[教程/感谢名单] 「感谢名单」下面没找到「名单」面板");
            return;
        }

        名单面板.gameObject.SetActive(false);

        名单入口动作 = 切换名单;
        名单关闭动作 = 关闭名单;
        绑定(入口按钮, 名单入口动作);

        名单关闭按钮 = 找按钮(名单面板, "关闭", "close");
        if (名单关闭按钮 == null) 名单关闭按钮 = 建名单关闭按钮();
        绑定(名单关闭按钮, 名单关闭动作);

        建背景关闭();

        Debug.Log($"[教程/感谢名单] 感谢名单入口已绑定，面板={名单面板.name}，关闭按钮={(名单关闭按钮 != null ? 名单关闭按钮.name : "无")}");
    }

    void 切换名单()
    {
        if (名单面板 == null) return;

        bool 要打开 = !名单面板.gameObject.activeSelf;
        名单面板.gameObject.SetActive(要打开);

        // 名单面板比较大，打开时先把教程大图收起来，避免互相遮挡
        if (要打开) 关闭教程();
    }

    void 关闭名单()
    {
        if (名单面板 != null) 名单面板.gameObject.SetActive(false);
    }

    Button 建名单关闭按钮()
    {
        var btn = 建按钮(名单面板, "__关闭名单", "关闭", new Vector2(-24f, -24f), new Vector2(120f, 48f));
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -24f);
        return btn;
    }

    /// <summary>面板背景点击关闭：透明且铺满面板的按钮，放在最底层，不挡面板里的文字</summary>
    void 建背景关闭()
    {
        var go = new GameObject("__背景关闭", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(名单面板, false);
        go.transform.SetAsFirstSibling();

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var 图 = go.GetComponent<Image>();
        图.color = new Color(0f, 0f, 0f, 0f);
        图.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        绑定(btn, 名单关闭动作);
    }

    // ==================== 生成 UI 的小工具 ====================

    /// <summary>没有现成控件时，用教程标签的图/字体生成按钮，视觉和其它按钮一致</summary>
    void 建教程控件()
    {
        if (教程控件容器 == null)
        {
            var 容器 = new GameObject("__教程控件", typeof(RectTransform));
            教程控件容器 = 容器.transform;
            教程控件容器.SetParent(教程节点, false);
            教程控件容器.SetAsLastSibling();
            var 容器Rect = (RectTransform)教程控件容器;
            容器Rect.anchorMin = 容器Rect.anchorMax = 容器Rect.pivot = new Vector2(0.5f, 0.5f);
            容器Rect.sizeDelta = Vector2.zero;
        }

        // 控件条放在第一张说明图的上方居中（下方会压到「社区页面」那排图标）
        float x = -500f, y = 212f;
        if (页面列表.Count > 0 && 页面列表[0].transform is RectTransform 首图)
        {
            x = 首图.anchoredPosition.x;
            y = 首图.anchoredPosition.y + 首图.rect.height * 0.5f + 44f;
        }

        if (上一页按钮 == null)
            上一页按钮 = 建按钮(教程控件容器, "__上一页", "上一页", new Vector2(x - 165f, y), new Vector2(140f, 48f));

        if (页码文本 == null)
            页码文本 = 建页码(new Vector2(x, y));

        if (下一页按钮 == null)
            下一页按钮 = 建按钮(教程控件容器, "__下一页", "下一页", new Vector2(x + 165f, y), new Vector2(140f, 48f));

        if (教程关闭按钮 == null)
            教程关闭按钮 = 建按钮(教程控件容器, "__关闭教程", "关闭", new Vector2(x + 320f, y), new Vector2(110f, 48f));
    }

    TMP_Text 建页码(Vector2 位置)
    {
        var go = new GameObject("__页码", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(教程控件容器, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(130f, 48f);
        rt.anchoredPosition = 位置;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = "1 / 1";
        设置字体(tmp);
        tmp.fontSize = 24f;
        tmp.color = 文字色;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    Button 建按钮(Transform 父, string 物体名, string 文本, Vector2 位置, Vector2 尺寸)
    {
        var go = new GameObject(物体名, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(父, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = 尺寸;
        rt.anchoredPosition = 位置;

        var 图 = go.GetComponent<Image>();
        图.sprite = 按钮图;
        图.type = 按钮图类型;
        图.color = 按钮底色;
        图.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = 图;

        var 文字物体 = new GameObject("Text (TMP)", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        文字物体.transform.SetParent(go.transform, false);
        var 文字Rect = (RectTransform)文字物体.transform;
        文字Rect.anchorMin = Vector2.zero;
        文字Rect.anchorMax = Vector2.one;
        文字Rect.offsetMin = Vector2.zero;
        文字Rect.offsetMax = Vector2.zero;

        var tmp = 文字物体.GetComponent<TextMeshProUGUI>();
        tmp.text = 文本;
        设置字体(tmp);
        tmp.fontSize = 24f;
        tmp.color = 文字色;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // 生成在 AudioManager 扫描之后，统一由 AudioManager.绑定场景里所有按钮音效 补一次音效
        var 本地化 = 文字物体.AddComponent<LocalizedText>();
        本地化.tableName = "ui";
        本地化.key = 文本;

        return btn;
    }

    void 设置字体(TMP_Text tmp)
    {
        if (tmp != null && 字体 != null) tmp.font = 字体;
    }

    /// <summary>抄「教程」标签的图片样式和字体，保证生成的按钮和现有 UI 一个风格</summary>
    void 收集样式()
    {
        var 图 = 教程节点.GetComponent<Image>();
        if (图 != null)
        {
            按钮图 = 图.sprite;
            按钮图类型 = 图.type;
            按钮底色 = 图.color;
        }

        TMP_Text 参考 = 教程节点.GetComponentInChildren<TMP_Text>(true);
        if (参考 == null && 设置页面 != null) 参考 = 设置页面.GetComponentInChildren<TMP_Text>(true);
        if (参考 != null)
        {
            字体 = 参考.font;
            文字色 = 参考.color;
        }
    }

    // ==================== 查找 / 绑定 ====================

    static void 绑定(Button 按钮, UnityAction 动作)
    {
        if (按钮 == null || 动作 == null) return;
        按钮.onClick.RemoveListener(动作);
        按钮.onClick.AddListener(动作);
    }

    static bool 含任一(string 文本, params string[] 关键词)
    {
        if (string.IsNullOrEmpty(文本)) return false;
        foreach (string 单个 in 关键词)
        {
            if (文本.IndexOf(单个, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    /// <summary>在 root 的子孙里找名字命中关键词、且自身挂 Button 的节点（含未激活）</summary>
    static Button 找按钮(Transform root, params string[] 关键词)
    {
        if (root == null) return null;
        foreach (Transform 子 in 全部子孙(root))
        {
            if (子 == root) continue;
            if (!含任一(子.name, 关键词)) continue;
            var btn = 子.GetComponent<Button>();
            if (btn != null) return btn;
        }
        return null;
    }

    static TMP_Text 找页码(Transform root)
    {
        if (root == null) return null;
        foreach (Transform 子 in 全部子孙(root))
        {
            if (!含任一(子.name, "页码", "页数", "page")) continue;
            var tmp = 子.GetComponent<TMP_Text>();
            if (tmp != null) return tmp;
        }
        return null;
    }

    static IEnumerable<Transform> 全部子孙(Transform root)
    {
        var 栈 = new Stack<Transform>();
        栈.Push(root);
        while (栈.Count > 0)
        {
            Transform 当前 = 栈.Pop();
            yield return 当前;
            for (int i = 当前.childCount - 1; i >= 0; i--) 栈.Push(当前.GetChild(i));
        }
    }

    /// <summary>下面查找 | 按名字找（默认精确匹配；模糊=true 时名字包含即可）</summary>
    static Transform 按名找(string 名字, bool 精确 = true)
    {
        foreach (GameObject 根 in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform 命中 = 按名找(根.transform, 名字, 精确);
            if (命中 != null) return 命中;
        }
        return null;
    }

    static Transform 按名找(Transform root, string 名字, bool 精确 = true)
    {
        if (root == null) return null;
        foreach (Transform t in 全部子孙(root))
        {
            if (t == root) continue;
            if (精确 ? t.name == 名字 : t.name.Contains(名字)) return t;
        }
        return null;
    }
}
