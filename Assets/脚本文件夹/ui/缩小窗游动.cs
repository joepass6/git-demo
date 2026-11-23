using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「缩小窗」里的两条鱼：随机换图 + 在圆框内来回游动（撞边反弹、自动转身）。
/// 由 DesktopWindowController 在运行时按名字找到节点后 AddComponent 并注入参数，不改场景 YAML。
/// </summary>
public class 缩小窗游动 : MonoBehaviour
{
    [Header("游动参数")]
    public bool 启用 = true;
    [Tooltip("游动速度（缩小窗本地坐标单位/秒，窗口约 256 宽）")]
    public float 速度 = 32f;
    [Tooltip("鱼的基准高度占缩小窗高度的比例下限/上限；实际取场景里鱼原本的高度，再夹到这个区间")]
    public float 鱼高最小比例 = 0.14f;
    public float 鱼高最大比例 = 0.26f;
    [Tooltip("鱼宽最多占缩小窗宽度的比例")]
    public float 鱼宽最大比例 = 0.62f;
    [Tooltip("圆框内圈半径占框半径的比例（圆框是空心环，约 0.84）")]
    public float 框内圈比例 = 0.84f;

    RectTransform 窗口;
    RectTransform 框;
    RectTransform 鱼1;
    RectTransform 鱼2;
    Image 图1;
    Image 图2;
    Sprite[] 图库;

    Vector2 框中心;
    Vector2 速度1;
    Vector2 速度2;
    // 场景里摆的 localScale.x 符号当“朝右”的基准（鱼2 原本是 -1，翻转时不会被打乱）
    float 基准1 = 1f;
    float 基准2 = 1f;
    // 场景里鱼原本的高度，作为尺寸基准
    float 基准高1 = 48f;
    float 基准高2 = 48f;

    public void 初始化(RectTransform 窗口, RectTransform 框, RectTransform 鱼1, RectTransform 鱼2, Sprite[] 图库)
    {
        this.窗口 = 窗口;
        this.框 = 框;
        this.鱼1 = 鱼1;
        this.鱼2 = 鱼2;
        this.图库 = 图库;

        if (框 != null)
        {
            框中心 = 框.anchoredPosition;
        }
        else
        {
            框中心 = Vector2.zero;
        }

        if (鱼1 != null)
        {
            图1 = 鱼1.GetComponent<Image>();
            基准1 = Mathf.Sign(鱼1.localScale.x == 0f ? 1f : 鱼1.localScale.x);
            基准高1 = 鱼1.rect.height;
        }
        if (鱼2 != null)
        {
            图2 = 鱼2.GetComponent<Image>();
            基准2 = Mathf.Sign(鱼2.localScale.x == 0f ? 1f : 鱼2.localScale.x);
            基准高2 = 鱼2.rect.height;
        }
    }

    /// <summary>每次进入缩小态调用：抽两条不同的鱼，随机位置、速度、朝向。</summary>
    public void 刷新()
    {
        if (窗口 == null || 鱼1 == null || 鱼2 == null) return;

        Sprite a = null, b = null;
        if (图库 != null && 图库.Length > 0)
        {
            int i = Random.Range(0, 图库.Length);
            a = 图库[i];
            if (图库.Length > 1)
            {
                int j = i, guard = 0;
                while (j == i && guard++ < 32) j = Random.Range(0, 图库.Length);
                b = 图库[j];
            }
            else
            {
                b = a;
            }
        }

        if (a != null) 换图(鱼1, 图1, a, 基准高1);
        if (b != null) 换图(鱼2, 图2, b, 基准高2);

        速度1 = 随机速度();
        速度2 = 随机速度();

        Vector2 p1 = 随机位置(鱼1);
        Vector2 p2 = 随机位置(鱼2);
        // 尽量不要一出来就叠在一起
        float 最小间距 = 窗口.rect.height * 0.18f;
        for (int k = 0; k < 8 && Vector2.Distance(p1, p2) < 最小间距; k++) p2 = 随机位置(鱼2);
        鱼1.anchoredPosition = p1;
        鱼2.anchoredPosition = p2;
    }

    void Update()
    {
        if (!启用 || 窗口 == null) return;
        // 用 unscaledDeltaTime：时间缩放不影响这个小窗里的游动
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        游(鱼1, ref 速度1, 基准1, dt);
        游(鱼2, ref 速度2, 基准2, dt);
    }

    void 游(RectTransform rt, ref Vector2 v, float 基准, float dt)
    {
        if (rt == null) return;

        Vector2 p = rt.anchoredPosition + v * dt;
        Vector2 半径 = 游动半径(rt);

        // 以圆框内圈为边界做椭圆夹取，撞边沿法线反弹
        Vector2 d = new Vector2((p.x - 框中心.x) / Mathf.Max(1f, 半径.x),
                                (p.y - 框中心.y) / Mathf.Max(1f, 半径.y));
        float len = d.magnitude;
        if (len > 1f)
        {
            Vector2 n = new Vector2(d.x / Mathf.Max(1f, 半径.x * 半径.x),
                                    d.y / Mathf.Max(1f, 半径.y * 半径.y)).normalized;
            float dot = Vector2.Dot(v, n);
            if (dot > 0f) v -= 2f * dot * n;

            d /= len;
            p = 框中心 + new Vector2(d.x * 半径.x, d.y * 半径.y);
        }

        rt.anchoredPosition = p;

        Vector3 sc = rt.localScale;
        sc.x = 基准 * Mathf.Sign(v.x == 0f ? 1f : v.x);
        rt.localScale = sc;
    }

    /// <summary>鱼中心可以到达的椭圆半径：圆框内圈再扣掉鱼自身的一半尺寸。</summary>
    Vector2 游动半径(RectTransform rt)
    {
        float 框半宽 = (框 != null ? 框.rect.width : 窗口.rect.width) * 0.5f;
        float 框半高 = (框 != null ? 框.rect.height : 窗口.rect.height) * 0.5f;
        float rx = 框半宽 * 框内圈比例 - rt.rect.width * 0.5f;
        float ry = 框半高 * 框内圈比例 - rt.rect.height * 0.5f;
        return new Vector2(Mathf.Max(4f, rx), Mathf.Max(4f, ry));
    }

    Vector2 随机位置(RectTransform rt)
    {
        Vector2 半径 = 游动半径(rt);
        float 角度 = Random.Range(0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(Random.value) * 0.85f;
        return 框中心 + new Vector2(Mathf.Cos(角度) * 半径.x * r, Mathf.Sin(角度) * 半径.y * r);
    }

    Vector2 随机速度()
    {
        float sp = 速度 * Random.Range(0.8f, 1.2f);
        float 角度 = Random.Range(-25f, 25f) * Mathf.Deg2Rad;
        float 方向 = Random.value < 0.5f ? -1f : 1f;
        return new Vector2(Mathf.Cos(角度) * sp * 方向, Mathf.Sin(角度) * sp);
    }

    /// <summary>
    /// 尺寸算法：以场景里摆好的鱼高为基准，夹到窗口高度的 [14%, 26%]；
    /// 宽度按真实图片的宽高比重算，最多占窗口宽度的 62%（超出就反推高度）。
    /// </summary>
    void 换图(RectTransform rt, Image img, Sprite s, float 基准高)
    {
        if (rt == null || img == null) return;

        img.sprite = s;
        img.preserveAspect = true;

        float H = 窗口.rect.height;
        float h = Mathf.Clamp(基准高, H * 鱼高最小比例, H * 鱼高最大比例);
        float aspect = s.rect.width / Mathf.Max(1f, s.rect.height);
        float w = h * aspect;
        float maxW = 窗口.rect.width * 鱼宽最大比例;
        if (w > maxW)
        {
            w = maxW;
            h = w / Mathf.Max(0.01f, aspect);
        }
        rt.sizeDelta = new Vector2(w, h);
    }
}
