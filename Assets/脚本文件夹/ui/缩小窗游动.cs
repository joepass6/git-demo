using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「缩小窗」里的两条鱼：展示<b>最近出现的两条鱼</b>（允许是同一种鱼），并播放鱼预制体自带的 Sprite 动画；
/// 在圆框内来回游动（撞边反弹、自动转身）。
/// 由 DesktopWindowController 在运行时按名字找到节点后 AddComponent 并注入参数，不改场景 YAML。
///
/// 构建版取值方式：鱼预制体不在 Resources/Addressables 里，无法按路径加载；
/// 这里从场景里的 ObjectSpawner.spawnEntries（已序列化的预制体引用）拿到 prefab，
/// 在窗口下实例化一个隐藏"替身"，只保留 SpriteRenderer 用来采样动画帧，再写回 UI Image。
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

    [Header("原画朝向（决定翻面方向）")]
    [Tooltip("鱼1 的原画是否本来就朝右。false = 原画朝左，向右游时要镜像成 -x（三间火箭蝶鱼.png 原画朝左）")]
    public bool 鱼1原画朝右 = false;
    [Tooltip("鱼2 的原画是否本来就朝右。false = 原画朝左，向右游时要镜像成 -x（拟刺尾鱼.png 原画朝左）")]
    public bool 鱼2原画朝右 = false;

    RectTransform 窗口;
    RectTransform 框;
    RectTransform 鱼1;
    RectTransform 鱼2;
    Image 图1;
    Image 图2;
    Sprite[] 图库;
    Sprite 原图1;   // 场景里原本贴的图，最后兜底
    Sprite 原图2;

    Vector2 框中心;
    Vector2 速度1;
    Vector2 速度2;
    // 让画面朝右所需的 localScale.x 符号：原画朝右 = +1，原画朝左 = -1。
    // 再乘场景里的 |localScale.x|，翻面只改符号、不改大小。
    // 注意：不能用场景 localScale.x 的符号当基准（鱼2 摆的是 -1，那只是美术摆位，不是原画朝向）。
    float 缩放1 = 1f;
    float 缩放2 = 1f;
    float 朝右符号1 = -1f;
    float 朝右符号2 = -1f;
    // 场景里鱼原本的高度，作为尺寸基准
    float 基准高1 = 48f;
    float 基准高2 = 48f;

    // ===== 预制体动画：每条鱼一个隐藏替身，只用来采样 SpriteRenderer.sprite =====
    readonly GameObject[] 替身 = new GameObject[2];        // 克隆根（销毁用）
    readonly GameObject[] 采样根 = new GameObject[2];      // 动画绑定根 = Animator 所在节点
    readonly SpriteRenderer[] 替身渲染 = new SpriteRenderer[2];
    readonly AnimationClip[] 替身剪辑 = new AnimationClip[2];
    readonly float[] 采样时间 = new float[2];
    readonly Sprite[] 当前帧 = new Sprite[2];

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
            原图1 = 图1 != null ? 图1.sprite : null;
            缩放1 = Mathf.Max(0.0001f, Mathf.Abs(鱼1.localScale.x));
            朝右符号1 = 鱼1原画朝右 ? 1f : -1f;
            基准高1 = 鱼1.rect.height;
        }
        if (鱼2 != null)
        {
            图2 = 鱼2.GetComponent<Image>();
            原图2 = 图2 != null ? 图2.sprite : null;
            缩放2 = Mathf.Max(0.0001f, Mathf.Abs(鱼2.localScale.x));
            朝右符号2 = 鱼2原画朝右 ? 1f : -1f;
            基准高2 = 鱼2.rect.height;
        }
    }

    /// <summary>
    /// 每次进入缩小态调用：取"当时最近出现的两条鱼"（最近的给鱼1、第二近的给鱼2，允许同种重复），
    /// 两条鱼各自随机位置、速度、朝向和动画相位。关掉缩小窗再打开会重新取一次。
    /// 仅当最近出现不足两条时，空出的槽位才用图鉴/场景原图静态兜底。
    /// </summary>
    public void 刷新()
    {
        if (窗口 == null || 鱼1 == null || 鱼2 == null) return;

        // 最近出现的鱼（最近的排最前），允许重复；不足两条时对应槽位为 null → 走兜底
        List<string> 最近 = 鱼运动.GetRecentAppearances(2);
        string 甲 = 最近.Count > 0 ? 最近[0] : null;
        string 乙 = 最近.Count > 1 ? 最近[1] : null;

        // 兜底：鱼不够两条 → 用图鉴静态图填空位；图鉴也为空 → 保留场景原图
        Sprite 兜底甲 = 图库 != null && 图库.Length > 0 ? 图库[0] : 原图1;
        Sprite 兜底乙 = 图库 != null && 图库.Length > 1 ? 图库[1]
                      : (图库 != null && 图库.Length > 0 ? 图库[0] : 原图2);

        装配(0, 鱼1, 图1, 甲, 兜底甲, 基准高1);
        装配(1, 鱼2, 图2, 乙, 兜底乙, 基准高2);

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
        游(鱼1, ref 速度1, 缩放1 * 朝右符号1, dt);
        游(鱼2, ref 速度2, 缩放2 * 朝右符号2, dt);
        采样(0, dt);
        采样(1, dt);
    }

    /// <summary>给某条鱼装配：能拿到预制体就建替身播动画，否则用兜底静态图。</summary>
    void 装配(int slot, RectTransform rt, Image img, string 鱼种, Sprite 兜底, float 基准高)
    {
        清空替身(slot);

        Sprite 首帧 = null;
        GameObject prefab = 解析鱼种预制体(鱼种);
        if (prefab != null)
        {
            GameObject go = 建替身(prefab, out GameObject 根, out SpriteRenderer sr, out AnimationClip clip);
            替身[slot] = go;
            采样根[slot] = 根;
            替身渲染[slot] = sr;
            替身剪辑[slot] = clip;
            首帧 = sr != null ? sr.sprite : null;
            if (首帧 == null) 清空替身(slot);
        }
        if (首帧 == null) 首帧 = 兜底;
        if (首帧 == null) return;

        换图(rt, img, 首帧, 基准高);
        当前帧[slot] = 首帧;
        // 动画相位随机起步：两条鱼贴图相同也不会同步摆尾，能看出是两条
        采样时间[slot] = 替身剪辑[slot] != null ? Random.Range(0f, Mathf.Max(0.01f, 替身剪辑[slot].length)) : 0f;
        if (替身剪辑[slot] != null) 采样(slot, 0f);
    }

    /// <summary>
    /// 实例化一个隐藏替身：只保留 Transform/SpriteRenderer（采样用），
    /// 脚本、碰撞体、刚体、粒子、所有渲染器全部关掉，避免影响 UI 和场景。
    /// 动画通常在子节点上（结构：根[鱼运动/碰撞] → 鱼体[Animator/SpriteRenderer]），
    /// 所以 Animator 和采样根都从子节点里找。
    /// </summary>
    GameObject 建替身(GameObject prefab, out GameObject 采样根, out SpriteRenderer sr, out AnimationClip clip)
    {
        GameObject go = Instantiate(prefab, 窗口, false);
        go.name = prefab.name + "（缩小窗替身）";
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        foreach (Behaviour b in go.GetComponentsInChildren<Behaviour>(true)) b.enabled = false;
        foreach (Collider2D c in go.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
        foreach (Rigidbody2D rb in go.GetComponentsInChildren<Rigidbody2D>(true)) rb.simulated = false;
        foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        // 动画根：优先 Animator 自己身上的 SpriteRenderer（动画曲线路径相对它）
        采样根 = go;
        sr = null;
        Animator ani = go.GetComponentInChildren<Animator>(true);
        if (ani != null)
        {
            采样根 = ani.gameObject;
            sr = ani.GetComponent<SpriteRenderer>();
            if (sr == null) sr = ani.GetComponentInChildren<SpriteRenderer>(true);
        }
        if (sr == null) sr = go.GetComponentInChildren<SpriteRenderer>(true);

        // 动画剪辑：优先"移动中"（游动帧），没有再取控制器里的第一个
        clip = null;
        if (ani != null && ani.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = ani.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && clips[i].name.Contains("移动中")) { clip = clips[i]; break; }
            if (clip == null)
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i] != null) { clip = clips[i]; break; }
        }

        // 先把第 0 帧采出来，保证有静态首帧可用
        if (clip != null && sr != null) clip.SampleAnimation(采样根, 0f);
        return go;
    }

    /// <summary>逐帧采样动画剪辑，把 SpriteRenderer 当前的 sprite 抄到 UI Image 上。</summary>
    void 采样(int slot, float dt)
    {
        AnimationClip clip = 替身剪辑[slot];
        SpriteRenderer sr = 替身渲染[slot];
        GameObject go = 采样根[slot];
        if (clip == null || sr == null || go == null) return;

        采样时间[slot] += dt;
        if (clip.length > 0.0001f) 采样时间[slot] %= clip.length;
        clip.SampleAnimation(go, 采样时间[slot]);

        Sprite s = sr.sprite;
        if (s == null || s == 当前帧[slot]) return;
        当前帧[slot] = s;
        if (slot == 0) { if (图1 != null) 图1.sprite = s; }
        else { if (图2 != null) 图2.sprite = s; }
    }

    void 清空替身(int slot)
    {
        if (替身[slot] != null) Destroy(替身[slot]);
        替身[slot] = null;
        采样根[slot] = null;
        替身渲染[slot] = null;
        替身剪辑[slot] = null;
        当前帧[slot] = null;
        采样时间[slot] = 0f;
    }

    /// <summary>鱼种名（预制体名）→ 预制体：从场景里所有 ObjectSpawner 的生成表里找，构建版同样可用。</summary>
    static GameObject 解析鱼种预制体(string 鱼种)
    {
        if (string.IsNullOrEmpty(鱼种)) return null;
        foreach (ObjectSpawner sp in ObjectSpawner.AllSpawners)
        {
            if (sp == null) continue;
            List<ObjectSpawner.SpawnEntry> entries = sp.GetSpawnEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].prefab != null && entries[i].prefab.name == 鱼种)
                    return entries[i].prefab;
            }
        }
        return null;
    }

    void 游(RectTransform rt, ref Vector2 v, float 朝右缩放, float dt)
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
        // 画面朝向 = 速度方向：向右游（v.x >= 0）用“朝右”的缩放，向左游取反
        sc.x = v.x >= 0f ? 朝右缩放 : -朝右缩放;
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
