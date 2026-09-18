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
        Bind(scaleUpButton, ScaleUp, "放大");
        Bind(scaleDownButton, ScaleDown, "缩小");
        Bind(rotateCwButton, RotateClockwise, "顺时针");
        Bind(rotateCcwButton, RotateCounterClockwise, "逆时针");
        Bind(mirrorFlipButton, MirrorFlip, "镜像翻转");
        Bind(openPanelButton, OpenCustomPanel, "打开面板");
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

        // 开关模式：打开状态就关闭，关闭状态就打开
        bool open = !target.activeSelf;
        target.SetActive(open);
        Debug.Log($"[珊瑚调整] {(open ? "已打开" : "已关闭")}页面: {target.name}");
    }
}
