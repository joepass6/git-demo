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

    [Tooltip("SE 用的 AudioSource（不填的话运行时会自动创建一个，与 BGM 独立，避免打断背景音乐）")]
    public AudioSource SE播放器;

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

        // 1.5 SE AudioSource（2D + 最高优先级，零延迟）
        if (SE播放器 == null)
        {
            SE播放器 = gameObject.AddComponent<AudioSource>();
            SE播放器.playOnAwake = false;
            SE播放器.loop = false;
            SE播放器.spatialBlend = 0f;      // 2D，不受位置影响
            SE播放器.priority = 0;           // 最高优先级，不被 BGM 抢占
        }
        else
        {
            SE播放器.spatialBlend = 0f;
            SE播放器.priority = 0;
        }

        // 2. 从 PlayerPrefs 读 BGM 音量（首次运行无记录则用默认 0.5）
        Bgm音量 = PlayerPrefs.GetFloat("BgmVolume", Bgm音量);
        BGM播放器.volume = Bgm音量;

        // 2.5 从 PlayerPrefs 读 SE 音量
        Se音量 = PlayerPrefs.GetFloat("SEVolume", Se音量);
        SE播放器.volume = Se音量;

        // 3. 订阅昼夜切换事件
        LocalTimeManager.OnDayNightChanged += OnDayNightChanged;

        // 3.5 订阅业务事件：珊瑚放置成功、鱼产宝、拾取掉落物
        _onCoralPlaced = _ => 播放音效(珊瑚放置音效);
        _onDropProduced = _ => 播放音效(鱼音效);
        _onItemCollected = _ => 播放音效(拾取音效);
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

    // ==================== 对外：按钮音效 ====================

    /// <summary>
    /// 通用音效播放：用 PlayOneShot，不会打断正在播放的其他音效。
    /// clip 为 null 时自动回退到 按钮音效（方便事件钩子没配置 clip 时也能出声）。
    /// </summary>
    public void 播放音效(AudioClip clip)
    {
        if (SE播放器 == null) return;
        clip = clip != null ? clip : 按钮音效;
        if (clip == null) return;
        SE播放器.volume = Se音量;
        SE播放器.PlayOneShot(clip);
    }

    /// <summary>
    /// 播放按钮点击音效（供动态创建的按钮或其他地方手动调用）。
    /// </summary>
    public void 播放按钮音效()
    {
        播放音效(按钮音效);
    }

    /// <summary>
    /// 扫描当前场景里所有 Button，把 onClick 都自动绑上按钮音效。
    /// 可在动态创建按钮后再次调用，为新按钮补上音效。
    /// 已绑过的按钮 AddListener 会重复添加，所以加了 RemoveListener 先清掉。
    /// </summary>
    public void 绑定场景里所有按钮音效()
    {
        var buttons = FindObjectsOfType<Button>(includeInactive: true);
        foreach (var btn in buttons)
        {
            // 先移除避免重复（同一个按钮可能被多次扫描）
            btn.onClick.RemoveListener(播放按钮音效);
            btn.onClick.AddListener(播放按钮音效);
        }
    }

    // ==================== 对外：SE 音量调节 ====================

    /// <summary>
    /// 调节 SE 音量：实时改 SE AudioSource，PlayerPrefs 立刻保存。
    /// </summary>
    public void 设置SE音量(float v)
    {
        Se音量 = Mathf.Clamp01(v);
        if (SE播放器 != null) SE播放器.volume = Se音量;
        PlayerPrefs.SetFloat("SEVolume", Se音量);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 当前保存的 SE 音量值（供外部初始化 Slider.value）
    /// </summary>
    public float 获取SE音量() => Se音量;
}
