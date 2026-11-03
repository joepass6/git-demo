using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 音频管理器：按时段（白天/夜晚）自动切换 BGM，支持音量滑条实时调节并 PlayerPrefs 持久化。
/// 昼夜分界与 LocalTimeManager 保持一致：isNight = hour >= 18 || hour <= 6。
/// BGM 用的 AudioSource 会自动创建，只需在 Inspector 拖入白天/夜晚两个 AudioClip 即可。
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("BGM - 按时段自动切换")]
    [Tooltip("白天 BGM（7:00 - 17:59 播放）")]
    public AudioClip 白天BGM;

    [Tooltip("夜晚 BGM（18:00 - 6:59 播放）")]
    public AudioClip 夜晚BGM;

    [Tooltip("BGM 用的 AudioSource（不填的话运行时会自动创建一个 Loop=true 的）")]
    public AudioSource BGM播放器;

    [Header("SE - 音效")]
    [Tooltip("按钮点击音效（可以用 粒子音1.wav 或其他短音效）")]
    public AudioClip 按钮音效;

    [Tooltip("鱼产出宝物时的音效")]
    public AudioClip 鱼音效;

    [Tooltip("珊瑚放置成功时的音效")]
    public AudioClip 珊瑚放置音效;

    [Tooltip("拾取掉落物时的音效（可选，不填的话用鱼音效或按钮音效也行）")]
    public AudioClip 拾取音效;

    [Tooltip("放置条件不足/操作被拒绝时的音效（可选，不填自动回退到按钮音效）")]
    public AudioClip 拒绝音效;

    [Tooltip("设置面板里的按钮专用音效（可选，不填则和普通按钮音效一样）")]
    public AudioClip 设置按钮音效;

    [Tooltip("设置面板根节点 —— Inspector 拖一下，AudioManager 会自动识别其子层级里的 Button 用设置按钮音效")]
    public Transform 设置面板根节点;

    [Tooltip("SE 用的 AudioSource（不填的话运行时会自动创建一个，与 BGM 独立，避免打断背景音乐）")]
    public AudioSource SE播放器;

    [Header("SE 独立播放器 - 各音效一个 AudioSource，直接 Play() 零延迟")]
    [Tooltip("按钮音效专属 AudioSource（不填自动创建）")]
    public AudioSource 按钮音效播放器;
    [Tooltip("鱼音效专属 AudioSource（不填自动创建）")]
    public AudioSource 鱼音效播放器;
    [Tooltip("珊瑚放置音效专属 AudioSource（不填自动创建）")]
    public AudioSource 珊瑚放置音效播放器;
    [Tooltip("拾取音效专属 AudioSource（不填自动创建）")]
    public AudioSource 拾取音效播放器;
    [Tooltip("拒绝音效专属 AudioSource（不填自动创建）")]
    public AudioSource 拒绝音效播放器;
    [Tooltip("设置按钮音效专属 AudioSource（不填自动创建）")]
    public AudioSource 设置按钮音效播放器;

    [Header("音量")]
    [Range(0f, 1f)]
    public float Bgm音量 = 0.5f;

    [Range(0f, 1f)]
    public float Se音量 = 0.7f;

    // 缓存事件委托引用，保证 OnDestroy 里能正确取消（lambda 每次 new，不同实例 Equals 为 false）
    private Action<GameObject> _onCoralPlaced;
    private Action<鱼运动> _onDropProduced;
    private Action<DropItem> _onItemCollected;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // 1. 没有 BGM AudioSource 就自动建一个（2D + 默认优先级）
        if (BGM播放器 == null)
        {
            BGM播放器 = gameObject.AddComponent<AudioSource>();
            BGM播放器.playOnAwake = false;
            BGM播放器.loop = true;
            BGM播放器.spatialBlend = 0f;      // 2D，不受位置影响
            BGM播放器.priority = 128;         // 中等，别抢占 SE
        }
        else
        {
            BGM播放器.spatialBlend = 0f;
        }

        // 1.5 SE AudioSource（2D + 最高优先级，零延迟）—— 保留作兜底，下面每个短音效还有独立的
        if (SE播放器 == null)
        {
            SE播放器 = gameObject.AddComponent<AudioSource>();
            SE播放器.playOnAwake = false;
            SE播放器.loop = false;
            SE播放器.spatialBlend = 0f;
            SE播放器.priority = 0;
        }
        else
        {
            SE播放器.spatialBlend = 0f;
            SE播放器.priority = 0;
        }

        // 1.6 为每种短音效创建独立 AudioSource —— 直接 Play() 零延迟，比 PlayOneShot 快得多
        确保SE播放器(ref 按钮音效播放器, 按钮音效);
        确保SE播放器(ref 鱼音效播放器, 鱼音效);
        确保SE播放器(ref 珊瑚放置音效播放器, 珊瑚放置音效);
        确保SE播放器(ref 拾取音效播放器, 拾取音效);
        确保SE播放器(ref 拒绝音效播放器, 拒绝音效);
        确保SE播放器(ref 设置按钮音效播放器, 设置按钮音效);

        // 2. 从 PlayerPrefs 读 BGM 音量（首次运行无记录则用默认 0.5）
        Bgm音量 = PlayerPrefs.GetFloat("BgmVolume", Bgm音量);
        BGM播放器.volume = Bgm音量;

        // 2.5 从 PlayerPrefs 读 SE 音量
        Se音量 = PlayerPrefs.GetFloat("SEVolume", Se音量);
        SE播放器.volume = Se音量;

        // 3. 订阅昼夜切换事件
        LocalTimeManager.OnDayNightChanged += OnDayNightChanged;

        // 3.5 订阅业务事件：珊瑚放置成功、鱼产宝、拾取掉落物
        _onCoralPlaced = _ => 播放珊瑚放置音效();
        _onDropProduced = _ => 播放鱼音效事件();
        _onItemCollected = _ => 播放拾取音效();
        DragAndPlaceSystem.OnCoralPlaced += _onCoralPlaced;
        鱼运动.OnDropProduced += _onDropProduced;
        DropItem.OnItemCollected += _onItemCollected;

        // 4. 立即按当前时段播放对应 BGM
        立即播放当前时段BGM();

        // 5. 自动把场景里所有现有 Button 的 onClick 绑上按钮音效
        绑定场景里所有按钮音效();
    }

    void OnDestroy()
    {
        LocalTimeManager.OnDayNightChanged -= OnDayNightChanged;
        if (_onCoralPlaced != null) DragAndPlaceSystem.OnCoralPlaced -= _onCoralPlaced;
        if (_onDropProduced != null) 鱼运动.OnDropProduced -= _onDropProduced;
        if (_onItemCollected != null) DropItem.OnItemCollected -= _onItemCollected;
    }

    private void OnDayNightChanged(bool isNight)
    {
        播放BGM(isNight ? 夜晚BGM : 白天BGM);
    }

    /// <summary>
    /// 判断当前是否夜晚（与 LocalTimeManager 的 CheckAndSwitchDayNight 完全一致）
    /// </summary>
    private bool 是否夜晚()
    {
        int h = DateTime.Now.Hour;
        return h >= 18 || h <= 6;
    }

    private void 立即播放当前时段BGM()
    {
        AudioClip clip = 是否夜晚() ? 夜晚BGM : 白天BGM;
        播放BGM(clip);
    }

    private void 播放BGM(AudioClip clip)
    {
        if (clip == null) return;
        if (BGM播放器 == null) return;

        // 同一首歌已经在播就跳过，避免中途重切断淡
        if (BGM播放器.clip == clip && BGM播放器.isPlaying) return;

        BGM播放器.volume = Bgm音量;
        BGM播放器.clip = clip;
        BGM播放器.loop = true;
        BGM播放器.Play();
    }

    // ==================== 对外：供 Slider.onValueChanged 绑定 ====================

    /// <summary>
    /// 调节 BGM 音量：实时改 AudioSource，PlayerPrefs 立刻保存。
    /// 在设置菜单里，把 Slider 的 OnValueChanged 事件绑到这个方法即可。
    /// </summary>
    public void 设置BGM音量(float v)
    {
        Bgm音量 = Mathf.Clamp01(v);
        if (BGM播放器 != null) BGM播放器.volume = Bgm音量;
        PlayerPrefs.SetFloat("BgmVolume", Bgm音量);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 当前保存的音量值（供外部初始化 Slider.value）
    /// </summary>
    public float 获取BGM音量() => Bgm音量;

    // ==================== 辅助：创建/确保一个独立 SE AudioSource ====================

    /// <summary>
    /// 如果 ref 里的 AudioSource 为 null 就 AddComponent 创建一个，
    /// 统一配置：2D、最高优先级、不循环、不自动播放、挂载 clip、音量=Se音量。
    /// </summary>
    private void 确保SE播放器(ref AudioSource src, AudioClip clip)
    {
        if (src == null) src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
        src.priority = 0;           // 最高优先级，不被 BGM 抢占
        src.clip = clip;
        src.volume = Se音量;
        src.outputAudioMixerGroup = null;
    }

    // ==================== 类型化播放：直接 Play() 零延迟 ====================

    /// <summary>按钮点击音效 — 独立 AudioSource，Play() 零延迟</summary>
    public void 播放按钮音效()
    {
        播独立(按钮音效播放器, 按钮音效);
    }

    /// <summary>鱼产宝音效（OnDropProduced 事件用这个名字区分）</summary>
    public void 播放鱼音效事件()
    {
        播独立(鱼音效播放器, 鱼音效);
    }

    /// <summary>珊瑚放置成功音效</summary>
    public void 播放珊瑚放置音效()
    {
        播独立(珊瑚放置音效播放器, 珊瑚放置音效);
    }

    /// <summary>拾取掉落物音效</summary>
    public void 播放拾取音效()
    {
        播独立(拾取音效播放器, 拾取音效);
    }

    /// <summary>点击鱼音效 — 给鱼运动.OnMouseDown 调用的入口</summary>
    public void 播放鱼被点击音效()
    {
        播独立(鱼音效播放器, 鱼音效);
    }

    /// <summary>操作被拒绝（数量不足/条件不够/满了等）时的反馈音效</summary>
    public void 播放拒绝音效()
    {
        播独立(拒绝音效播放器, 拒绝音效);
    }

    /// <summary>设置面板里按钮的专用点击音效（不填则自动回退到普通按钮音效）</summary>
    public void 播放设置按钮音效()
    {
        if (设置按钮音效 != null)
            播独立(设置按钮音效播放器, 设置按钮音效);
        else
            播放按钮音效();
    }

    /// <summary>
    /// 直接 Play()：AudioSource 提前创建好、clip 提前挂上，Play() 内部就是提交播放请求，
    /// 不会像 PlayOneShot 那样临时创建实例/动态分配内存/池化查找。
    /// 如果 clip 为 null 自动回退到按钮音效，兜底用 SE播放器 + PlayOneShot。
    /// </summary>
    private void 播独立(AudioSource src, AudioClip fallbackClip)
    {
        if (src != null && src.clip != null)
        {
            src.volume = Se音量;
            src.Play();
        }
        else
        {
            // 兜底：用 SE播放器 + PlayOneShot
            AudioClip clip = fallbackClip != null ? fallbackClip : 按钮音效;
            if (clip == null || SE播放器 == null) return;
            SE播放器.volume = Se音量;
            SE播放器.PlayOneShot(clip);
        }
    }

    // ==================== 通用接口（兜底用） ====================

    /// <summary>
    /// 通用音效播放：优先用对应类型的独立 AudioSource，clip 为 null 时自动回退到按钮音效。
    /// 如果找不到匹配的独立 AudioSource，兜底用 SE播放器 + PlayOneShot。
    /// </summary>
    public void 播放音效(AudioClip clip)
    {
        if (clip == null) { 播放按钮音效(); return; }

        if (clip == 按钮音效) { 播放按钮音效(); return; }
        if (clip == 鱼音效) { 播放鱼被点击音效(); return; }
        if (clip == 珊瑚放置音效) { 播放珊瑚放置音效(); return; }
        if (clip == 拾取音效) { 播放拾取音效(); return; }
        if (clip == 拒绝音效) { 播放拒绝音效(); return; }

        // 未知 clip，用兜底
        if (SE播放器 == null) return;
        SE播放器.volume = Se音量;
        SE播放器.PlayOneShot(clip);
    }

    /// <summary>
    /// 扫描当前场景里所有 Button，把 onClick 都自动绑上音效。
    /// 如果 Inspector 里拖了 设置面板根节点，其子层级里的 Button 会绑 设置按钮音效，
    /// 其他 Button 绑 普通按钮音效。可在动态创建按钮后再次调用。
    /// </summary>
    public void 绑定场景里所有按钮音效()
    {
        var buttons = FindObjectsOfType<Button>(includeInactive: true);
        foreach (var btn in buttons)
        {
            // 先移除避免重复
            btn.onClick.RemoveListener(播放按钮音效);
            btn.onClick.RemoveListener(播放设置按钮音效);

            // 如果拖了设置面板根节点，且按钮是它的子物体 → 用设置按钮音效
            if (设置面板根节点 != null && btn.transform.IsChildOf(设置面板根节点))
                btn.onClick.AddListener(播放设置按钮音效);
            else
                btn.onClick.AddListener(播放按钮音效);
        }
    }

    // ==================== 对外：SE 音量调节 ====================

    /// <summary>
    /// 调节 SE 音量：实时改所有 SE AudioSource，PlayerPrefs 立刻保存。
    /// </summary>
    public void 设置SE音量(float v)
    {
        Se音量 = Mathf.Clamp01(v);
        if (SE播放器 != null) SE播放器.volume = Se音量;
        if (按钮音效播放器 != null) 按钮音效播放器.volume = Se音量;
        if (鱼音效播放器 != null) 鱼音效播放器.volume = Se音量;
        if (珊瑚放置音效播放器 != null) 珊瑚放置音效播放器.volume = Se音量;
        if (拾取音效播放器 != null) 拾取音效播放器.volume = Se音量;
        if (拒绝音效播放器 != null) 拒绝音效播放器.volume = Se音量;
        if (设置按钮音效播放器 != null) 设置按钮音效播放器.volume = Se音量;
        PlayerPrefs.SetFloat("SEVolume", Se音量);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 当前保存的 SE 音量值（供外部初始化 Slider.value）
    /// </summary>
    public float 获取SE音量() => Se音量;
}
