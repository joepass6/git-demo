using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 教程分页 + 感谢名单接线（运行时按名字绑定，不生成任何 UI）。
///
/// 关闭规则（全局统一）：
///   1. 面板里有关闭按钮 → 入口按钮只负责打开，关闭只能点面板里的关闭按钮；
///   2. 面板里没有关闭按钮 → 再点一次打开它的那个入口按钮来收起。
///
/// 教程：用每一页里用户自己放的「下一页」按钮翻页（第 1 页的按钮叫 Button 也能按“页内唯一按钮”匹配到）；
///       场景里没有的按钮一律不生成，只在日志里提示缺哪个。最后一页再点「下一页」收起教程。
/// 感谢名单：点「感谢名单」打开「名单」；「名单」里有「关闭」按钮，所以入口只负责打开。
///
/// 另外给现有面板挂 PanelClickBlocker：面板内部的图片/文字被点中时，点击不会再冒泡到
/// 外层“入口按钮”，避免“点到面板里就整块关掉”的误关。
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
    Button 教程入口按钮;
    readonly List<GameObject> 页面列表 = new List<GameObject>();
    readonly List<UnityAction> 翻页动作列表 = new List<UnityAction>();
    int 当前页;
    bool 教程已打开 = false;

    // ===== 感谢名单 =====
    Transform 感谢入口;
    Transform 名单面板;
    Button 名单关闭按钮;
    bool 名单有关闭按钮;

    // 缓存委托实例，重复初始化时 RemoveListener 掉同一份，不会叠加
    UnityAction 教程入口动作;
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
        给现有面板加点击遮挡();

        if (AudioManager.Instance != null) AudioManager.Instance.绑定场景里所有按钮音效();
    }

    // ==================== 教程 ====================

    void 初始化教程()
    {
        教程节点 = 按名找(设置页面, "教程");
        if (教程节点 == null)
        {
            Debug.LogWarning("[教程] 没找到 设置页面/图像/教程");
            return;
        }

        收集页面();

        教程入口按钮 = 教程节点.GetComponent<Button>();
        if (教程入口按钮 == null) 教程入口按钮 = 教程节点.GetComponentInParent<Button>();
        if (教程入口按钮 == null)
        {
            Debug.LogWarning("[教程] 「教程」节点上没有 Button，无法按“再点入口收起”的规则接线");
        }
        else
        {
            教程入口动作 = 点击教程入口;
            绑定(教程入口按钮, 教程入口动作);
        }

        string 场景按钮 = 列出按钮名(教程节点);
        for (int i = 0; i < 页面列表.Count; i++)
        {
            GameObject 页 = 页面列表[i];
            // 页面根挂遮挡：点页面里的图片/文字不会冒泡到外面的「教程」入口按钮
            遮挡(页.transform);

            string 说明;
            Button 下一页 = 找页内下一页按钮(页, out 说明);
            if (下一页 == null)
            {
                Debug.LogWarning($"[教程] 第 {i + 1} 页「{页.name}」{说明}。本脚本不会生成按钮，请把「下一页」按钮放进这一页。教程下现有按钮：{场景按钮}");
                continue;
            }

            int 页下标 = i;
            UnityAction 动作 = () => 点下一页(页下标);
            绑定(下一页, 动作);
            翻页动作列表.Add(动作);
            Debug.Log($"[教程] 第 {i + 1} 页「{页.name}」→ 下一页按钮「{下一页.name}」已绑定（{说明}）");
        }

        刷新教程显示();
        Debug.Log($"[教程] 共 {页面列表.Count} 页；入口=「教程」标签；没有关闭按钮 → 再点一次「教程」收起。场景现有按钮：{场景按钮}");
    }

    void 收集页面()
    {
        页面列表.Clear();
        翻页动作列表.Clear();

        var 其它子物体 = new List<string>();
        foreach (Transform 子 in 教程节点)
        {
            if (子.name.StartsWith("__")) continue;
            if (子.GetComponent<Button>() != null) continue;                                  // 入口/翻页按钮不算页面
            if (子.GetComponent<TMP_Text>() != null || 子.GetComponent<Text>() != null) continue; // 「教程」标签文本
            if (!Regex.IsMatch(子.name, @"\d")) { 其它子物体.Add(子.name); continue; }         // 页容器名带数字（1~5）
            页面列表.Add(子.gameObject);
        }

        // 名字里的数字决定页序
        页面列表.Sort((a, b) => 页号(a.name).CompareTo(页号(b.name)));
        当前页 = 0;

        if (页面列表.Count == 0)
        {
            Debug.LogWarning($"[教程] 没找到页容器（子物体名要带数字）。教程下现有子物体：{string.Join("、", 其它子物体)}");
        }
    }

    static int 页号(string 名)
    {
        Match m = Regex.Match(名, @"\d+");
        return m.Success ? int.Parse(m.Value) : -1;
    }

    /// <summary>
    /// 在「这一页」里找翻页按钮，只认用户自己放的按钮：
    ///   1) 名字含 下一页/下一张/下一个/next；
    ///   2) 否则这一页里只有一个 Button 时，直接用那一个（第 1 页的按钮就叫 Button）；
    ///   3) 都不满足就返回 null，交给调用方打日志，绝不生成新按钮。
    /// </summary>
    static Button 找页内下一页按钮(GameObject 页, out string 说明)
    {
        var 候选 = new List<Button>();
        foreach (Transform t in 全部子孙(页.transform))
        {
            if (t == 页.transform) continue;
            var b = t.GetComponent<Button>();
            if (b != null) 候选.Add(b);
        }

        foreach (var b in 候选)
        {
            if (含任一(b.name, "下一页", "下一张", "下一个", "next"))
            {
                说明 = "按名字匹配";
                return b;
            }
        }

        if (候选.Count == 1)
        {
            说明 = $"页内只有一个按钮「{候选[0].name}」，按唯一按钮匹配";
            return 候选[0];
        }

        string 名单 = 候选.Count == 0 ? "无" : string.Join("、", 候选.ConvertAll(x => x.name));
        说明 = $"没找到「下一页」按钮（规则：名字含 下一页/下一张/下一个/next，或页内唯一按钮）；页内现有按钮：{名单}";
        return null;
    }

    void 刷新教程显示()
    {
        for (int i = 0; i < 页面列表.Count; i++)
        {
            if (页面列表[i] != null) 页面列表[i].SetActive(教程已打开 && i == 当前页);
        }
    }

    void 点下一页(int 页下标)
    {
        if (!教程已打开) return;
        if (页下标 != 当前页) return;

        if (当前页 >= 页面列表.Count - 1)
        {
            关闭教程("最后一页再点下一页");
            return;
        }

        当前页++;
        刷新教程显示();
        Debug.Log($"[教程] 翻到第 {当前页 + 1}/{页面列表.Count} 页");
    }

    void 点击教程入口()
    {
        if (教程已打开)
        {
            关闭教程("再点一次「教程」");
            return;
        }

        教程已打开 = true;
        当前页 = 0;
        关闭名单();
        刷新教程显示();
        Debug.Log("[教程] 打开第 1 页");
    }

    void 关闭教程(string 原因)
    {
        if (!教程已打开) return;
        教程已打开 = false;
        刷新教程显示();
        Debug.Log($"[教程] 收起（{原因}）");
    }

    // ==================== 感谢名单 ====================

    void 初始化感谢名单()
    {
        感谢入口 = 按名找("感谢名单");
        if (感谢入口 == null)
        {
            Debug.LogWarning("[感谢名单] 没找到「感谢名单」按钮");
            return;
        }

        var 入口按钮 = 感谢入口.GetComponent<Button>();
        if (入口按钮 == null) 入口按钮 = 感谢入口.GetComponentInParent<Button>();
        if (入口按钮 == null)
        {
            Debug.LogWarning("[感谢名单] 「感谢名单」上没有 Button 组件");
            return;
        }

        名单面板 = 按名找(感谢入口, "名单");
        if (名单面板 == null) 名单面板 = 按名找(感谢入口, "名单", false);
        if (名单面板 == null)
        {
            Debug.LogWarning("[感谢名单] 「感谢名单」下面没找到「名单」面板");
            return;
        }

        if (名单面板.gameObject.activeSelf) 名单面板.gameObject.SetActive(false);
        遮挡(名单面板);

        // 只认用户自己放的关闭按钮，不生成
        名单关闭按钮 = 找按钮(名单面板, "关闭", "close");
        名单有关闭按钮 = 名单关闭按钮 != null;

        名单入口动作 = 点击感谢名单入口;
        名单关闭动作 = 关闭名单;
        绑定(入口按钮, 名单入口动作);

        if (名单有关闭按钮)
        {
            绑定(名单关闭按钮, 名单关闭动作);
            Debug.Log($"[感谢名单] 有「{名单关闭按钮.name}」按钮 → 入口只负责打开，关闭只点关闭按钮");
        }
        else
        {
            Debug.LogWarning("[感谢名单] 「名单」面板里没有关闭按钮 → 按无关闭按钮规则：再点一次「感谢名单」收起。本脚本不会自动生成关闭按钮");
        }
    }

    void 点击感谢名单入口()
    {
        if (名单面板 == null) return;

        if (名单面板.gameObject.activeSelf)
        {
            if (名单有关闭按钮)
            {
                Debug.Log("[感谢名单] 已打开；有关闭按钮的界面只能点「关闭」关闭，点入口不会关");
                return;
            }

            名单面板.gameObject.SetActive(false);
            Debug.Log("[感谢名单] 再点一次入口，收起");
            return;
        }

        名单面板.gameObject.SetActive(true);
        关闭教程("打开感谢名单");
    }

    void 关闭名单()
    {
        if (名单面板 != null) 名单面板.gameObject.SetActive(false);
    }

    // ==================== 防“点到面板里就误关” ====================

    /// <summary>
    /// 现有面板大多是挂在外层“入口按钮”下面的，面板里的图片/文字被点中时，
    /// 点击会沿父链冒泡到入口 Button，于是整块面板被 toggle 关掉。
    /// 给面板根挂一个空的点击接收器，把面板内部的点击吃掉（内部按钮自己的 onClick 不受影响）。
    /// </summary>
    void 给现有面板加点击遮挡()
    {
        遮挡(按名找("设置页面"));
        遮挡(按名找("语言选择"));
        遮挡(按名找("名单"));
        遮挡(按名找("交换宝"));
        遮挡(按名找("珊瑚容器"));
        遮挡(按名找("珊瑚调整页面容器"));

        Transform 珊瑚界面 = 按名找("珊瑚界面");
        if (珊瑚界面 != null) 遮挡(按名找(珊瑚界面, "珊瑚UI"));

        Transform 鱼图鉴 = 按名找("鱼图鉴");
        if (鱼图鉴 != null) 遮挡(按名找(鱼图鉴, "容器"));
    }

    static void 遮挡(Transform 面板)
    {
        if (面板 == null) return;
        if (面板.GetComponent<PanelClickBlocker>() == null)
            面板.gameObject.AddComponent<PanelClickBlocker>();
    }

    // ==================== 查找 / 绑定 ====================

    static void 绑定(Button 按钮, UnityAction 动作)
    {
        if (按钮 == null || 动作 == null) return;
        按钮.onClick.RemoveListener(动作);
        按钮.onClick.AddListener(动作);
    }

    static string 列出按钮名(Transform root)
    {
        var 名单 = new List<string>();
        foreach (Transform t in 全部子孙(root))
        {
            if (t.GetComponent<Button>() != null) 名单.Add(t.name);
        }
        return 名单.Count == 0 ? "无" : string.Join("、", 名单);
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

    /// <summary>深度优先遍历（包含未激活物体）</summary>
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

/// <summary>
/// 空的点击接收器：挂在面板根上，让面板内部（图片/文字/空白）的点击停在面板这一层，
/// 不再冒泡到外层“入口按钮”，从而避免点面板内部把面板关掉。
/// 面板内部自己的 Button 会先收到点击，不受影响。
/// </summary>
public class PanelClickBlocker : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData) { }
}
