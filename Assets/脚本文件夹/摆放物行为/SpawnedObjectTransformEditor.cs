using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 珊瑚变换调整器：通过5个指定按钮调整珊瑚（挂了ObjectSpawner的挂载物）的大小、方向、镜像
///   放大/缩小：每按一次整体等比缩放10%（乘法缩放，镜像翻转/旋转后依然整体缩放不变形不翻转）
///   顺时针/逆时针：每按一次旋转 10度，无上限，可转一整圈（两个方向都可以）
///   镜像翻转：按一次左右翻转一次
/// 调整目标 = 当前打开面板的珊瑚（点击哪个珊瑚就调哪个）
/// 用法：把此脚本挂到任意物体上（比如珊瑚面板），把场景里的5个按钮分别拖到对应字段
/// </summary>
public class SpawnedObjectTransformEditor : MonoBehaviour
{
    [Header("5个控制按钮（把场景里的按钮拖到对应字段）")]
    [Tooltip("按一次珊瑚整体放大0.1")]
    public Button scaleUpButton;

    [Tooltip("按一次珊瑚整体缩小0.1（最小0.1，防止缩小没）")]
    public Button scaleDownButton;

    [Tooltip("按一次珊瑚顺时针旋转15度")]
    public Button rotateCwButton;

    [Tooltip("按一次珊瑚逆时针旋转15度")]
    public Button rotateCcwButton;

    [Tooltip("按一次珊瑚左右镜像翻转")]
    public Button mirrorFlipButton;

    [Header("自定义面板")]
    [Tooltip("点击此按钮打开你指定的自定义面板")]
    public Button openPanelButton;

    [Tooltip("可选。不拖的话，点击按钮会自动开关【当前挂载物的子级物体】（点击哪个珊瑚就开关哪个珊瑚的子级）")]
    public GameObject customPanel;

    [Header("步进值")]
    [Tooltip("每次缩放的比例（0.1 = 每次整体放大/缩小10%）")]
    public float scaleStep = 0.1f;

    [Tooltip("每次旋转的角度（度）")]
    public float rotationStep = 10f;

    void Awake()
    {
        DetachCustomPanelFromHover();
        Bind(scaleUpButton, ScaleUp, "放大");
        Bind(scaleDownButton, ScaleDown, "缩小");
        Bind(rotateCwButton, RotateClockwise, "顺时针");
        Bind(rotateCcwButton, RotateCounterClockwise, "逆时针");
        Bind(mirrorFlipButton, MirrorFlip, "镜像翻转");
        Bind(openPanelButton, OpenCustomPanel, "打开面板");
    }

    /// <summary>
    /// 【显隐归属】调整页面容器由“点击”开关，不再交给 HoverTooltip：
    /// HoverTooltip 在鼠标离开调整界面后会把 targets 里的物体 SetActive(false)（hideDelay），
    /// 而鼠标要移到那 5 个按钮上必须离开调整界面 → 容器刚点开就被悬停脚本关掉。
    /// 这里把容器从悬停脚本的 targets 里摘掉，悬停只管别的目标。
    /// </summary>
    void DetachCustomPanelFromHover()
    {
        if (customPanel == null) return;
        var tips = GetComponentsInParent<HoverTooltip>(true);
        for (int i = 0; i < tips.Length; i++)
        {
            var tip = tips[i];
            if (tip == null || tip.targets == null) continue;
            if (tip.targets.Remove(customPanel))
                Debug.Log($"[珊瑚调整] 已把 {customPanel.name} 从 {tip.gameObject.name} 的 HoverTooltip.targets 移除（改由点击开关）");
        }
    }

    void OnEnable()
    {
        // 珊瑚面板每次被打开（珊瑚容器 SetActive(true)）时，把调整页面强制恢复成“收起”：
        // 调整页面的开/关状态会被上一次操作保留下来（它是常驻场景物体，不会被重建）。
        // 如果上次留在“展开”，这次点【调整界面】的第一下反而是“收起”，
        // 玩家看到的就是“点了没反应 / 按钮消失 = 打不开”。
        if (customPanel != null && customPanel.activeSelf)
        {
            customPanel.SetActive(false);
            Debug.Log($"[珊瑚调整] 面板打开，调整页面已复位为收起: {customPanel.name}");
        }
    }

    /// <summary>
    /// 【唯一关闭入口】面板关闭 / 切换珊瑚（珊瑚容器整体被 SetActive(false)）时收起调整页面。
    /// 点击只负责打开，关闭永远走这里，避免“点一下变关、再点一下又开”的不确定状态。
    /// </summary>
    void OnDisable()
    {
        if (customPanel != null && customPanel.activeSelf)
        {
            customPanel.SetActive(false);
            Debug.Log($"[珊瑚调整] 面板关闭，调整页面已收起: {customPanel.name}");
        }
    }

    void Bind(Button btn, UnityEngine.Events.UnityAction action, string desc)
    {
        if (btn == null)
        {
            Debug.LogWarning($"[珊瑚调整] 【{desc}】按钮未拖入！请把场景里的{desc}按钮拖到此脚本对应字段");
            return;
        }
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(action);
        Debug.Log($"[珊瑚调整] 已绑定【{desc}】按钮: {btn.gameObject.name}");
    }

    /// <summary>
    /// 调整目标：当前打开面板的珊瑚（点击哪个珊瑚打开面板就调哪个）
    /// </summary>
    Transform GetTarget()
    {
        ObjectSpawner sp = ObjectSpawner.CurrentSpawner;
        if (sp == null)
        {
            Debug.LogWarning("[珊瑚调整] 当前没有打开任何珊瑚的面板！请先点击一个珊瑚");
            return null;
        }
        return sp.transform;
    }

    void ScaleUp()
    {
        Transform t = GetTarget();
        if (t == null) return;
        // 乘法等比缩放：无论旋转还是镜像翻转后，都是整体同步放大，不变形不意外翻回
        t.localScale *= (1f + scaleStep);
        Debug.Log($"[珊瑚调整] {t.name} 放大 → {Mathf.Abs(t.localScale.x):F2}");
    }

    void ScaleDown()
    {
        Transform t = GetTarget();
        if (t == null) return;
        Vector3 s = t.localScale * (1f - scaleStep);
        // 最小0.1（按绝对值判断，镜像负数也不影响），防止缩小没
        if (Mathf.Abs(s.x) < 0.1f || Mathf.Abs(s.y) < 0.1f || Mathf.Abs(s.z) < 0.1f)
        {
            Debug.Log($"[珊瑚调整] {t.name} 已到最小尺寸，不能再缩小了");
            return;
        }
        t.localScale = s;
        Debug.Log($"[珊瑚调整] {t.name} 缩小 → {Mathf.Abs(s.x):F2}");
    }

    void RotateClockwise()
    {
        Transform t = GetTarget();
        if (t == null) return;
        t.rotation = Quaternion.Euler(0f, 0f, t.eulerAngles.z - rotationStep);
        Debug.Log($"[珊瑚调整] {t.name} 顺时针转{rotationStep}° → Z={t.eulerAngles.z:F0}°");
    }

    void RotateCounterClockwise()
    {
        Transform t = GetTarget();
        if (t == null) return;
        t.rotation = Quaternion.Euler(0f, 0f, t.eulerAngles.z + rotationStep);
        Debug.Log($"[珊瑚调整] {t.name} 逆时针转{rotationStep}° → Z={t.eulerAngles.z:F0}°");
    }

    void MirrorFlip()
    {
        Transform t = GetTarget();
        if (t == null) return;
        Vector3 s = t.localScale;
        s.x = -s.x;
        t.localScale = s;
        Debug.Log($"[珊瑚调整] {t.name} 镜像翻转 → x={s.x:F2}");
    }

    void OpenCustomPanel()
    {
        // 目标：优先用手动拖的customPanel；没拖则自动用当前挂载物的子级（第一个子物体）
        GameObject target = customPanel;
        if (target == null)
        {
            ObjectSpawner sp = ObjectSpawner.CurrentSpawner;
            if (sp == null)
            {
                Debug.LogWarning("[珊瑚调整] 还没点击过任何珊瑚，不知道要开关哪个页面！请先点击一个珊瑚，或把页面拖到customPanel字段");
                return;
            }
            if (sp.transform.childCount == 0)
            {
                Debug.LogWarning($"[珊瑚调整] {sp.name} 下面没有子物体，没有可开关的页面！");
                return;
            }
            target = sp.transform.GetChild(0).gameObject;
        }

        // 防呆1：引用的是Project里的预制体资源而不是场景物体（从Project窗口拖的话SetActive无效）
        if (!target.scene.IsValid())
        {
            Debug.LogError($"[珊瑚调整] 目标引用的是Project里的预制体资源（{target.name}），不是场景中的物体！请在Hierarchy里把场景中的那个页面对象重新拖到 customPanel 字段");
            return;
        }

        // 防呆2：父物体链上有隐藏的，自己激活了也看不见 → 自动激活整条父物体链
        Transform up = target.transform.parent;
        while (up != null)
        {
            if (!up.gameObject.activeSelf)
            {
                up.gameObject.SetActive(true);
                Debug.Log($"[珊瑚调整] 已自动激活隐藏的父物体: {up.name}");
            }
            up = up.parent;
        }

        // （旧的“HoverTooltip 接管显隐”分支已删除：显隐只由点击打开 + OnDisable 关闭决定）

        // 【确定性语义】点击只负责“打开”，从不负责“关闭”：
        // 只要不可见就打开，已打开就保持不动；关闭只由面板关闭（OnDisable）/切换珊瑚触发。
        if (target.activeInHierarchy)
        {
            Debug.Log($"[珊瑚调整] 点击【珊瑚调节】：{target.name} 已打开，保持不动（关闭由面板关闭/切换珊瑚触发）");
            StartCoroutine(DumpLater(target));
            return;
        }

        target.SetActive(true);
        transform.SetAsLastSibling();
        // HoverTooltip 不一定挂在自己身上（可能在祖先按钮上），要沿父链找：
        // 悬停中激活的子级必须同一帧按当前倍数补偿，否则会以放大态出现。
        var tips = GetComponentsInParent<HoverTooltip>(true);
        for (int i = 0; i < tips.Length; i++)
            if (tips[i] != null && tips[i].IsHoveringNow) tips[i].RepinNow();
        string extra = "";
        RectTransform rt = target.transform as RectTransform;
        if (rt != null) extra = $"，anchoredPos={rt.anchoredPosition}，screen={RectTransformUtility.WorldToScreenPoint(null, rt.position)}";
        Debug.Log($"[珊瑚调整] 已打开页面: {target.name}（父级={target.transform.parent.name}{extra}）");

        // 打开后分三个时间点各打一次真实状态，用来看“是谁随后把它关掉的”
        StartCoroutine(DumpLater(target));
    }

    /// <summary>点击后分几个时间点各打印一次，能看出“刚打开→随后被谁关掉”</summary>
    System.Collections.IEnumerator DumpLater(GameObject panel)
    {
        DumpPanelState("点击后 +0.1s", panel);
        yield return new WaitForSecondsRealtime(0.5f);
        DumpPanelState("点击后 +0.6s", panel);
        yield return new WaitForSecondsRealtime(0.9f);
        DumpPanelState("点击后 +1.5s", panel);
    }

    /// <summary>把容器和它每个子按钮的真实运行时状态逐行打出来</summary>
    void DumpPanelState(string tag, GameObject panel)
    {
        if (panel == null) { Debug.Log($"[诊断/{tag}] 容器引用为空"); return; }

        Debug.Log($"===== [诊断/{tag}] 容器={panel.name} activeSelf={panel.activeSelf} activeInHierarchy={panel.activeInHierarchy} 子物体数={panel.transform.childCount} =====");
        DumpNode(panel.transform, "  ");
        for (int i = 0; i < panel.transform.childCount; i++)
            DumpNode(panel.transform.GetChild(i), "    ");

        // 谁在管这个容器的显隐
        var tips = GetComponentsInParent<HoverTooltip>(true);
        if (tips.Length == 0) Debug.Log($"[诊断/{tag}] 调整界面 这条父链上没有 HoverTooltip");
        for (int i = 0; i < tips.Length; i++)
        {
            var tip = tips[i];
            string names = "";
            if (tip.targets != null)
                for (int j = 0; j < tip.targets.Count; j++)
                    names += (tip.targets[j] != null ? tip.targets[j].name + "(act=" + tip.targets[j].activeSelf + ")" : "null") + ", ";
            Debug.Log($"[诊断/{tag}] HoverTooltip@{tip.gameObject.name} enableShow={tip.enableShow} hideDelay={tip.hideDelay} targets=[{names}]");
        }

        // 父级（调整界面）当前缩放：容器是它的子级，父级 1.5 倍就是“容器被放大/位移”的直接来源
        var parentT = panel.transform.parent;
        if (parentT != null)
        {
            var parentTip = parentT.GetComponent<HoverTooltip>();
            Debug.Log($"[诊断/{tag}] 父级={parentT.name} localScale={parentT.localScale} lossyScale={parentT.lossyScale}" +
                      (parentTip != null
                        ? $" HoverTooltip 倍数={parentTip.CurrentMultiplier} 悬停中={parentTip.IsHoveringNow} 明细=[{parentTip.DumpScaleState()}]"
                        : " （父级没有 HoverTooltip）"));
        }

        var parent = panel.transform.parent;
        if (parent != null)
        {
            string sibs = "";
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                var crt = c as RectTransform;
                sibs += $"\n[诊断/{tag}]   #{i} {c.name} act={c.gameObject.activeSelf}";
                if (crt != null) sibs += $" world={crt.position} size={crt.sizeDelta}";
            }
            Debug.Log($"[诊断/{tag}] {parent.name} 的子级顺序(#越大越靠上):{sibs}");
        }
    }

    void DumpNode(Transform node, string indent)
    {
        var go = node.gameObject;
        var rt = node as RectTransform;
        string pos = "无RectTransform";
        if (rt != null)
        {
            Vector3 world = rt.position;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
            pos = $"localScale={rt.localScale} localPos={rt.localPosition} anchored={rt.anchoredPosition} size={rt.sizeDelta} world={world} screen={screen}";
        }

        string img = "";
        foreach (var im in node.GetComponents<Image>())
            img += $" Image[enabled={im.enabled} alpha={im.color.a} raycast={im.raycastTarget} sprite={(im.sprite != null ? im.sprite.name : "null")}]";
        var cg = node.GetComponent<CanvasGroup>();
        if (cg != null) img += $" CanvasGroup[alpha={cg.alpha}]";

        var cv = node.GetComponentInParent<Canvas>();
        string cvs = cv != null ? $" canvas={cv.name}/{cv.renderMode} canvasScale={cv.transform.lossyScale}" : " canvas=无";

        string chain = "";
        for (var p = node; p != null; p = p.parent)
            chain += (chain.Length > 0 ? "<-" : "") + p.name + "(act=" + p.gameObject.activeSelf + ")";

        Debug.Log($"[诊断]{indent}{go.name} activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy} {pos}{img}{cvs} 父链={chain}");
    }

}
