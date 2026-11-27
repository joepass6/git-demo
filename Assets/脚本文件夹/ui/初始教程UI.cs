// 跨重装判定：优先 Steam 云标记（SteamRemoteStorage，跟账号走、Steam Cloud 同步），
// 不可用时退回 PlayerPrefs。Steam 未启动 / 未初始化时所有 Steam 调用安全 no-op。
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

/// <summary>
/// 初始教程（操作驱动，不翻页、不生成 UI）。
///
/// 步骤（按场景里存在的 步骤1~步骤4 依次推进，缺节点只打一条日志并跳过）：
///   1. 点「珊瑚界面」按钮        → 界面ui/显示面板/显示面板/珊瑚界面 的 Button.onClick
///   2. 珊瑚落位成功              → DragAndPlaceSystem.OnCoralPlaced（正常放置 :1121 / 随便放 :1204）
///   3. 点步骤 3 里的「我知道了」 → 步骤3 下按名找 Button
///   4. 点「添加愿望单」          → 步骤4 下按名找 Button；先隐藏整套并写完成标记，再跳 Steam 商店
///
/// 触发条件改为「场上有没有珊瑚」：
///   - 首次读档完成 + 窗口尺寸稳定后只判定一次；场上没有珊瑚 → 从第 1 步开始显示；
///     有珊瑚（读档恢复 / 场景自带）→ 整套隐藏，本次不弹。
///   - 教程开始后不再按帧轮询珊瑚：第 2 步本身就是「放珊瑚」，轮询会让教程在第 2 步自我终结，
///     所以教程一旦开始就继续走完。只有玩家在教程进行中手动读档（Ctrl+L/F9）且读档后场上有珊瑚时，
///     才整套隐藏。
///   - 旧「只出现一次」标记 InitialTutorialDoneV1 / tutorial_done_v1.txt 仍会在完整走完时写入，
///     但只用于统计/日志，不再拦截教程。
/// </summary>
[DisallowMultipleComponent]
public class 初始教程UI : MonoBehaviour
{
    const string 完成键 = "InitialTutorialDoneV1";
    const string 云端标记文件 = "tutorial_done_v1.txt";
    const string 根名 = "初始教程";
    const string 珊瑚标签 = "coral";
    const string 商店AppId = "5385860";
    const string 商店网页 = "https://store.steampowered.com/app/5385860/%E7%8F%8A%E7%91%9A%E7%89%A7%E6%AD%8C/";

    static 初始教程UI 实例;

    /// <summary>教程是否已经完成“显示/隐藏”的最终判定（供开场遮罩判断首屏已定）。</summary>
    public static bool 已判定 { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void 自动创建()
    {
        if (实例 != null) return;
        var go = new GameObject("[初始教程UI]");
        // 驱动对象单独进 DontDestroyOnLoad：任何场景重载/读档都不会把它连带销毁，
        // 避免初始化中途“静默停摆”（日志里只到「收集步骤」就没有下文）。
        DontDestroyOnLoad(go);
        go.AddComponent<初始教程UI>();
        Debug.Log("[初始教程] 已创建驱动对象 [初始教程UI]");
    }

    Transform 根;
    readonly List<GameObject> 步骤对象 = new List<GameObject>();
    readonly List<int> 步骤号 = new List<int>();

    Button 珊瑚界面按钮;
    Button 我知道了按钮;
    Button 愿望单按钮;

    int 当前下标 = -1;
    bool 已结束;
    bool 已订阅放置;
    bool 已订阅读档;

    // 初始化用 Update 状态机（不再依赖协程：协程一旦被静默停掉就完全查不到原因）
    enum 初始化阶段 { 找根, 等读档, 完成 }
    初始化阶段 阶段 = 初始化阶段.找根;
    float 阶段计时;
    bool 首次读档完成;
    float 读档完成时刻 = -1f;
    bool 等待读档中;
    bool 有保存系统;

    UnityAction 珊瑚界面动作;
    UnityAction 我知道了动作;
    UnityAction 愿望单动作;

    void Awake()
    {
        if (实例 != null && 实例 != this) { Destroy(gameObject); return; }
        实例 = this;
        Debug.Log("[初始教程] 驱动 Awake，进入初始化状态机");
    }

    void OnDisable()
    {
        // 正常显示阶段不会走到这里；只有被外部停用（或退出播放）才会触发
        if (阶段 != 初始化阶段.完成)
            Debug.LogWarning("[初始教程] 驱动在初始化未完成时被 OnDisable（后续不会再执行）");
    }

    void OnDestroy()
    {
        Debug.Log("[初始教程] 驱动 OnDestroy");
        退订();
        结束等读档();
        if (实例 == this) 实例 = null;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (阶段 == 初始化阶段.找根)
        {
            阶段计时 += dt;
            根 = 找根();
            if (根 != null)
            {
                Debug.Log("[初始教程] 已找到「初始教程」节点，开始收集步骤");
                收集步骤();

                // 只关步骤节点，不动「初始教程」根：
                // 根上的占位图已由收集步骤隐藏，避免“先把根整套关掉再打开”牵连到别的东西。
                for (int i = 0; i < 步骤对象.Count; i++)
                    if (步骤对象[i] != null && 步骤对象[i].activeSelf) 步骤对象[i].SetActive(false);

                if (步骤对象.Count == 0)
                {
                    Debug.Log("[初始教程] 没有可用的步骤节点（支持「步骤1~步骤4」或「第一步~第四步」），跳过");
                    已结束 = true;
                    已判定 = true;
                    阶段 = 初始化阶段.完成;
                    return;
                }

                开始等读档();
                阶段 = 初始化阶段.等读档;
                阶段计时 = 0f;
            }
            else if (阶段计时 >= 10f)
            {
                Debug.Log("[初始教程] 未找到「初始教程」节点，本次不显示");
                已结束 = true;
                已判定 = true;
                阶段 = 初始化阶段.完成;
            }
            return;
        }

        if (阶段 == 初始化阶段.等读档)
        {
            阶段计时 += dt;
            bool 可以判定;
            if (!有保存系统) 可以判定 = 阶段计时 >= 1f;
            else if (首次读档完成) 可以判定 = Time.realtimeSinceStartup - 读档完成时刻 >= 1f;
            else 可以判定 = 阶段计时 >= 11f;
            if (!可以判定) return;

            if (!有保存系统)
                Debug.Log("[初始教程] 场景没有 SaveSystem，窗口稳定后开始判定");
            else if (!首次读档完成)
                Debug.LogWarning("[初始教程] 等待首次读档超时（11 秒），按当前场景判定");
            else
                Debug.Log("[初始教程] 首次读档完成且窗口已稳定，开始判定");

            结束等读档();

            bool 有历史标记 = 已完成();
            bool 有珊瑚 = 场上有珊瑚();
            Debug.Log($"[初始教程] 判定：历史完成标记={有历史标记}（仅统计/日志，不拦截），场上珊瑚={有珊瑚}");
            已判定 = true;

            if (有珊瑚)
            {
                if (根 != null) 根.gameObject.SetActive(false);
                Debug.Log("[初始教程] 场上已有珊瑚（读档恢复或场景自带），本次不显示初始教程");
                已结束 = true;
                阶段 = 初始化阶段.完成;
                return;
            }

            订阅();
            当前下标 = 0;
            显示当前步();
            阶段 = 初始化阶段.完成;
            Debug.Log($"[初始教程] 开始：第 {步骤号[0]} 步（共 {步骤对象.Count} 个可用步骤）");
        }
    }

    // ==================== 读档等待 ====================

    void 开始等读档()
    {
        if (等待读档中) return;
        有保存系统 = SaveSystem.Instance != null || FindObjectOfType<SaveSystem>() != null;
        if (!有保存系统)
        {
            Debug.Log("[初始教程] 场景里没有 SaveSystem，只等窗口稳定");
            读档完成时刻 = Time.realtimeSinceStartup;
            return;
        }
        首次读档完成 = false;
        读档完成时刻 = -1f;
        SaveSystem.OnLoadComplete += 读档完成回调;
        等待读档中 = true;
        Debug.Log("[初始教程] 等待首次自动读档完成…");
    }

    void 结束等读档()
    {
        if (!等待读档中) return;
        SaveSystem.OnLoadComplete -= 读档完成回调;
        等待读档中 = false;
    }

    void 读档完成回调(int _)
    {
        首次读档完成 = true;
        读档完成时刻 = Time.realtimeSinceStartup;
    }

    // ==================== 珊瑚判定 ====================

    /// <summary>
    /// 场上是否有「真实放置的珊瑚」。
    /// 依据：ObjectSpawner 的静态注册表 AllSpawners（ObjectSpawner.Awake 登记）+ 自身/子物体带 coral 标签。
    /// 过滤掉：
    ///   - 拖拽预览：珊瑚放置.cs 的 DisableSpawnComponents 会把预览体的 ObjectSpawner 置 enabled=false；
    ///   - 已卖出待销毁：SellFishItem.Sell 直接 Destroy，物体在当帧结束后就不存在；
    ///   - 失活/被隐藏的珊瑚：activeInHierarchy 为 false。
    /// 只查组件注册表，不靠对象名，也不会把掉落物/UI 页面（它们没有 ObjectSpawner）算进来。
    /// </summary>
    static bool 场上有珊瑚()
    {
        foreach (ObjectSpawner sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            if (!sp.enabled) continue; // 拖拽预览体
            GameObject go = sp.gameObject;
            if (go == null || !go.activeInHierarchy) continue;
            if (!有珊瑚标签(go)) continue;
            return true;
        }
        return false;
    }

    /// <summary>递归查自身及子物体是否带 coral 标签（不少珊瑚预制体根节点是 Untagged，标签在子物体上）。</summary>
    static bool 有珊瑚标签(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag(珊瑚标签)) return true;
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            if (有珊瑚标签(t.GetChild(i).gameObject)) return true;
        return false;
    }

    /// <summary>
    /// 教程进行中玩家手动读档（Ctrl+L / F9）时，如果读档后场上有珊瑚，就整套隐藏。
    /// 首次自动读档的 OnLoadComplete 在教程开始前已经消费掉，不会走到这里。
    /// </summary>
    void 读档后检查(int _)
    {
        if (已结束) return;
        if (!场上有珊瑚()) return;
        Debug.Log("[初始教程] 教程进行中读档，且读档后场上有珊瑚 → 隐藏整套");
        结束(false, "读档后场上有珊瑚");
    }

    // ==================== 查找 ====================

    static Transform 找根()
    {
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != 根名) continue;
            if (!t.gameObject.scene.IsValid()) continue; // 排除预制体资产
            return t;
        }
        return null;
    }

    void 收集步骤()
    {
        步骤对象.Clear();
        步骤号.Clear();

        // 按名字收集序号：步骤1~步骤9 / 第一步~第九步，两种写法可以混用
        var 候选 = new Dictionary<int, GameObject>();
        foreach (Transform t in 全部子孙(根))
        {
            if (t == null || t == 根) continue;
            int idx = 解析步骤序号(t.name);
            if (idx <= 0) continue;

            if (候选.ContainsKey(idx))
            {
                Debug.LogWarning($"[初始教程] 第 {idx} 步有重复节点：保留「{候选[idx].name}」，忽略「{t.name}」");
                continue;
            }
            候选[idx] = t.gameObject;
        }

        var 缺 = new List<string>();
        for (int n = 1; n <= 4; n++)
            if (!候选.ContainsKey(n)) 缺.Add($"第{n}步");
        if (缺.Count > 0)
            Debug.Log($"[初始教程] 缺少节点：{string.Join("、", 缺)}（支持「步骤N」或「第X步」两种命名）");

        var 序号 = new List<int>(候选.Keys);
        序号.Sort(); // 按序号推进，不按名字字典序

        var 已识别 = new List<string>();
        foreach (int n in 序号)
        {
            GameObject 节点 = 候选[n];

            if (n > 4)
            {
                Debug.Log($"[初始教程] 第 {n} 步（{节点.name}）没有对应的完成规则，已跳过");
                continue;
            }

            if (n == 1)
            {
                珊瑚界面按钮 = 找场景按钮("珊瑚界面");
                if (珊瑚界面按钮 == null)
                {
                    Debug.Log($"[初始教程] 第1步（{节点.name}）需要「珊瑚界面」按钮，但场景里没找到，跳过该步");
                    continue;
                }
            }
            else if (n == 3)
            {
                我知道了按钮 = 找子孙按钮(节点.transform, "我知道了");
                if (我知道了按钮 == null)
                {
                    Debug.Log($"[初始教程] 第3步（{节点.name}）缺少「我知道了」按钮，跳过该步");
                    continue;
                }
            }
            else if (n == 4)
            {
                // 场景里这个按钮现在叫「steam网址」，旧名字「添加愿望单」也兼容
                愿望单按钮 = 找子孙按钮(节点.transform, "添加愿望单")
                          ?? 找子孙按钮(节点.transform, "steam网址")
                          ?? 找子孙按钮(节点.transform, "Steam网址");
                if (愿望单按钮 == null)
                {
                    Debug.Log($"[初始教程] 第4步（{节点.name}）缺少「添加愿望单」/「steam网址」按钮，跳过该步");
                    continue;
                }
            }

            步骤对象.Add(节点);
            步骤号.Add(n);
            已识别.Add($"第{n}步（{节点.name}）");
        }

        if (步骤对象.Count > 0)
            Debug.Log($"[初始教程] 识别到 {步骤对象.Count} 个步骤：{string.Join("、", 已识别)}");

        // 教程层只负责显示，默认不吃射线：否则第1步的高亮图会盖住「珊瑚界面」按钮，点击直接丢失
        关闭教程层射线();

        // 顺手隐藏不属于任何步骤的旧占位图（默认名 Image），避免它一直挂在画面上
        int 隐藏 = 0;
        foreach (Transform t in 全部子孙(根))
        {
            if (t == 根) continue;
            if (t.name != "Image") continue;
            bool 在步骤内 = false;
            foreach (var s in 步骤对象)
                if (s != null && t.IsChildOf(s.transform)) { 在步骤内 = true; break; }
            if (在步骤内) continue;
            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); 隐藏++; }
        }
        if (隐藏 > 0) Debug.Log($"[初始教程] 已隐藏 {隐藏} 个不在步骤内的占位图（名字为 Image）");
    }

    /// <summary>
    /// 教程层默认全部关闭 raycastTarget，避免任何教程图片（尤其第 1 步的高亮圈）
    /// 挡住下层「珊瑚界面」按钮；只给第 3、4 步真正需要点的按钮恢复点击区域。
    /// </summary>
    void 关闭教程层射线()
    {
        if (根 == null) return;

        Graphic[] 图形 = 根.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < 图形.Length; i++)
            if (图形[i] != null) 图形[i].raycastTarget = false;

        打开按钮射线(我知道了按钮);
        打开按钮射线(愿望单按钮);

        Debug.Log($"[初始教程] 已关闭教程层 {图形.Length} 个 Graphic 的射线，仅保留第3/4步按钮可点");
    }

    static void 打开按钮射线(Button 按钮)
    {
        if (按钮 == null) return;
        if (按钮.targetGraphic != null)
        {
            按钮.targetGraphic.raycastTarget = true;
            return;
        }
        Graphic g = 按钮.GetComponent<Graphic>();
        if (g != null) g.raycastTarget = true;
    }

    /// <summary>
    /// 步骤节点名解析：`步骤1`~`步骤9` 或 `第一步`~`第九步`，其它返回 -1。
    /// </summary>
    static int 解析步骤序号(string 名字)
    {
        if (string.IsNullOrEmpty(名字)) return -1;

        // 场景里真实存在「第二步 」这种带尾随空格的节点，先去空白再解析
        名字 = 去空白(名字);
        if (名字.Length == 0) return -1;

        if (名字.StartsWith("步骤", StringComparison.Ordinal))
        {
            int n;
            if (int.TryParse(名字.Substring(2), out n) && n >= 1 && n <= 9) return n;
            return -1;
        }

        if (名字.Length == 3 && 名字[0] == '第' && 名字[2] == '步')
            return 中文数字(名字[1]);

        return -1;
    }

    /// <summary>去掉首尾半角/全角空格、制表、换行、零宽字符。</summary>
    static string 去空白(string 名字)
    {
        if (string.IsNullOrEmpty(名字)) return "";
        return 名字.Trim(' ', '\t', '\r', '\n', '\u3000', '\u200B', '\uFEFF');
    }

    /// <summary>是否为“像步骤的直属子节点或附属引导元素”，用于非当前步兜底关闭。</summary>
    static bool 像步骤或附属(string 名字)
    {
        string n = 去空白(名字);
        if (n.Length == 0) return false;
        if (n.StartsWith("第", StringComparison.Ordinal)) return true;
        if (n.StartsWith("步骤", StringComparison.Ordinal)) return true;
        if (n.IndexOf("引导线", StringComparison.Ordinal) >= 0) return true;
        if (n.IndexOf("圆圈", StringComparison.Ordinal) >= 0) return true;
        if (n.IndexOf("高亮", StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    static int 中文数字(char c)
    {
        switch (c)
        {
            case '一': return 1;
            case '二': return 2;
            case '三': return 3;
            case '四': return 4;
            case '五': return 5;
            case '六': return 6;
            case '七': return 7;
            case '八': return 8;
            case '九': return 9;
            default: return -1;
        }
    }

    static GameObject 找子孙(Transform root, string 名字)
    {
        foreach (Transform t in 全部子孙(root))
            if (t != null && t.name == 名字) return t.gameObject;
        return null;
    }

    static Button 找子孙按钮(Transform root, string 名字)
    {
        foreach (Transform t in 全部子孙(root))
        {
            if (t == null || t.name != 名字) continue;
            Button b = t.GetComponent<Button>();
            if (b != null) return b;
            b = t.GetComponentInParent<Button>();
            if (b != null) return b;
        }
        return null;
    }

    static Button 找场景按钮(string 名字)
    {
        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform hit = null;
            foreach (Transform t in 全部子孙(go.transform))
            {
                if (t.name == 名字) { hit = t; break; }
            }
            if (hit == null) continue;
            Button b = hit.GetComponent<Button>();
            if (b == null) b = hit.GetComponentInParent<Button>();
            if (b != null) return b;
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

    // ==================== 订阅 / 推进 ====================

    void 订阅()
    {
        if (珊瑚界面按钮 != null)
        {
            珊瑚界面动作 = () =>
            {
                if (当前步号() != 1) return;
                Debug.Log("[初始教程] 第1步：点击已到达「珊瑚界面」按钮（onClick 触发）");
                确保珊瑚界面面板打开();
                完成当前步();
            };
            珊瑚界面按钮.onClick.RemoveListener(珊瑚界面动作);
            珊瑚界面按钮.onClick.AddListener(珊瑚界面动作);
        }

        if (我知道了按钮 != null)
        {
            我知道了动作 = () => { if (当前步号() == 3) 完成当前步(); };
            我知道了按钮.onClick.RemoveListener(我知道了动作);
            我知道了按钮.onClick.AddListener(我知道了动作);
        }

        if (愿望单按钮 != null)
        {
            愿望单动作 = 点愿望单;
            愿望单按钮.onClick.RemoveListener(愿望单动作);
            愿望单按钮.onClick.AddListener(愿望单动作);
        }

        DragAndPlaceSystem.OnCoralPlaced += 珊瑚放置成功;
        已订阅放置 = true;

        // 教程进行中手动读档：读档后场上有珊瑚就隐藏（之后的自动读档不会再触发首次判定）
        if (!已订阅读档 && (SaveSystem.Instance != null || FindObjectOfType<SaveSystem>() != null))
        {
            SaveSystem.OnLoadComplete += 读档后检查;
            已订阅读档 = true;
        }
    }

    void 退订()
    {
        if (珊瑚界面按钮 != null && 珊瑚界面动作 != null) 珊瑚界面按钮.onClick.RemoveListener(珊瑚界面动作);
        if (我知道了按钮 != null && 我知道了动作 != null) 我知道了按钮.onClick.RemoveListener(我知道了动作);
        if (愿望单按钮 != null && 愿望单动作 != null) 愿望单按钮.onClick.RemoveListener(愿望单动作);
        if (已订阅放置)
        {
            DragAndPlaceSystem.OnCoralPlaced -= 珊瑚放置成功;
            已订阅放置 = false;
        }
        if (已订阅读档)
        {
            SaveSystem.OnLoadComplete -= 读档后检查;
            已订阅读档 = false;
        }
    }

    int 当前步号()
    {
        if (已结束 || 当前下标 < 0 || 当前下标 >= 步骤号.Count) return -1;
        return 步骤号[当前下标];
    }

    void 珊瑚放置成功(GameObject _)
    {
        if (当前步号() == 2) 完成当前步();
    }

    void 完成当前步()
    {
        if (已结束 || 当前下标 < 0 || 当前下标 >= 步骤对象.Count) return;

        步骤对象[当前下标].SetActive(false);
        当前下标++;

        if (当前下标 >= 步骤对象.Count)
        {
            结束(true, "所有步骤完成");
            return;
        }

        显示当前步();
        Debug.Log($"[初始教程] 进入第 {步骤号[当前下标]} 步");
    }

    void 显示当前步()
    {
        for (int i = 0; i < 步骤对象.Count; i++)
            if (步骤对象[i] != null) 步骤对象[i].SetActive(i == 当前下标);

        // 兜底：根节点下的其他“步骤/附属元素”直属子节点（例如名字没解析成功的「第二步 」，
        // 以及直接挂在根上的引导线/圆圈/高亮），非当前步一律关闭。
        if (根 != null)
        {
            for (int i = 0; i < 根.childCount; i++)
            {
                Transform c = 根.GetChild(i);
                if (c == null) continue;
                GameObject go = c.gameObject;
                bool 是当前步 = 当前下标 >= 0 && 当前下标 < 步骤对象.Count && go == 步骤对象[当前下标];
                if (是当前步) { if (!go.activeSelf) go.SetActive(true); continue; }
                if (go.activeSelf && 像步骤或附属(c.name)) go.SetActive(false);
            }
        }

        根.gameObject.SetActive(true);
    }

    /// <summary>
    /// 第1步兜底：直接打开现有面板对象「珊瑚界面/珊瑚UI」。
    /// 构建版里 LocalTimeManager 的点击验证用的是原始 Input.mousePosition，
    /// 当 Screen(1184x1161) 与客户区(1200x1200) 不一致时，它会把“点到了”算成“点在外面”然后静默 return，
    /// 所以这里不依赖它的 toggle，只在面板未激活时补一次 SetActive(true)。
    /// 若 LocalTimeManager 已经正常打开（编辑器 / 移动过窗口后），这里检测到 activeSelf 就不会重复打开，不会和它抢状态。
    /// </summary>
    void 确保珊瑚界面面板打开()
    {
        if (珊瑚界面按钮 == null) return;

        GameObject 面板 = 找子孙(珊瑚界面按钮.transform, "珊瑚UI");
        if (面板 == null)
        {
            Debug.LogWarning("[初始教程] 第1步兜底：找不到 珊瑚界面/珊瑚UI");
            return;
        }

        if (面板.activeSelf)
        {
            Debug.Log("[初始教程] 第1步：珊瑚UI 已是打开状态，不再重复打开");
            return;
        }

        面板.SetActive(true);
        Debug.Log($"[初始教程] 第1步：已兜底打开珊瑚UI（active={面板.activeSelf}）");
    }

    void 点愿望单()
    {
        if (已结束 || 当前步号() != 4) return;

        // 先收起整套并写标记，再跳转
        结束(true, "点击添加愿望单");

        // 有 Steam 客户端（SteamAPI 初始化成功）直接拉起商店页；否则用 https 网页兜底
        bool 有Steam = SteamManager.Initialized;
        string url = 有Steam ? $"steam://store/{商店AppId}" : 商店网页;
        Debug.Log($"[初始教程] 打开商店：{url}（AppID={商店AppId}，Steam初始化={有Steam}）");

        try { Application.OpenURL(url); }
        catch (Exception e) { Debug.LogWarning("[初始教程] 打开商店失败：" + e.Message); }
    }

    void 结束(bool 写完成标记, string 原因)
    {
        if (已结束) return;
        已结束 = true;
        if (根 != null) 根.gameObject.SetActive(false);
        退订();

        if (写完成标记)
        {
            PlayerPrefs.SetInt(完成键, 1);
            PlayerPrefs.Save();
            写云端标记();
        }
        string 根状态 = 根 == null ? "null" : (根.gameObject.activeInHierarchy ? "仍激活(异常)" : "已隐藏");
        Debug.Log($"[初始教程] 结束（{原因}），完成标记 {完成键}={(写完成标记 ? 1 : 0)}，根节点={根状态}（教程层射线已在启动时全关）");
    }

    // ==================== 跨重装标记 ====================

    /// <summary>
    /// 判定顺序：本地 PlayerPrefs → Steam 云端标记文件。
    /// 任一命中即视为已完成；云端命中时顺手补写本地标记。
    /// </summary>
    static bool 已完成()
    {
        if (PlayerPrefs.GetInt(完成键, 0) == 1) return true;

        if (云端已完成())
        {
            PlayerPrefs.SetInt(完成键, 1);
            PlayerPrefs.Save();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Steam 未初始化 / 云同步关闭 / 调用异常时一律返回 false，不阻塞启动。
    /// </summary>
    static bool 云端已完成()
    {
#if !DISABLESTEAMWORKS
        try
        {
            if (!SteamManager.Initialized) return false;
            if (SteamRemoteStorage.FileExists(云端标记文件))
            {
                Debug.Log($"[初始教程] 云端标记存在：{云端标记文件}（Steam 账号维度，重装后仍然有效）");
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[初始教程] 读取云端标记异常，按未完成处理：" + e.Message);
        }
#endif
        return false;
    }

    /// <summary>
    /// 教程结束时写云端标记（小文件，走 Steam Cloud）。
    /// 失败（未初始化 / 云未开启 / 配额不足）只 warning，本地 PlayerPrefs 仍有效。
    /// </summary>
    static void 写云端标记()
    {
#if !DISABLESTEAMWORKS
        try
        {
            if (!SteamManager.Initialized)
            {
                Debug.Log("[初始教程] Steam 未初始化，跳过云端标记（仅本地标记）");
                return;
            }

            byte[] data = System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("u"));
            if (SteamRemoteStorage.FileWrite(云端标记文件, data, data.Length))
                Debug.Log($"[初始教程] 云端标记已写入：{云端标记文件}");
            else
                Debug.LogWarning("[初始教程] 云端标记写入失败（云同步未开启/配额不足），本地标记仍有效");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[初始教程] 写云端标记异常，已退回本地标记：" + e.Message);
        }
#endif
    }
}
