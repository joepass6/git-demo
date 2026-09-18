using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// 本地时间管理器（最终修复版）
/// 功能：
/// 1. 实时显示时间、日期、太阳/月亮图标。
/// 2. 根据时间自动切换 UI 颜色。
/// 3. 【核心修复】根据时间自动控制 "白天/晚上" GameObject 的显隐开关。
/// </summary>
public class LocalTimeManager : MonoBehaviour
{
    // ==================== 单例 ====================
    public static LocalTimeManager Instance;

    // ==================== 时间显示组件 ====================
    [Header("时间显示 - 文本")]
    [Tooltip("显示小时的 TMP_Text")]
    public TMP_Text hourText;
    
    [Tooltip("显示分钟的 TMP_Text")]
    public TMP_Text minuteText;
    
    [Tooltip("显示日期的 TMP_Text")]
    public TMP_Text timeText;

    [Header("时间显示 - 图标")]
    [Tooltip("太阳图片 (7:00-17:59 显示)")]
    public GameObject sunImage;
    
    [Tooltip("月亮图片 (18:00-6:59 显示)")]
    public GameObject moonImage;

    // ==================== 昼夜控制组件 ====================
    [Header("昼夜控制 - 变色目标")]
    [Tooltip("需要随时间变色的 UI 或物体列表")]
    public ColorTarget[] targets;

    [Header("昼夜控制 - 对象显隐开关")]
    [Tooltip("夜晚时段需要【显示】的游戏对象 (例如：星空背景、路灯)")]
    public GameObject[] nightShowObjects;

    [Tooltip("白天时段需要【显示】的游戏对象 (例如：太阳背景、云朵)")]
    public GameObject[] dayShowObjects;

    // ==================== 按钮打开面板 ====================
    [System.Serializable]
    public class ButtonPanelPair
    {
        [Tooltip("按钮物体（需要有Button组件）")]
        public UnityEngine.UI.Button button;

        [Tooltip("点击按钮后要打开/切换的目标物体")]
        public GameObject targetObject;

        [Tooltip("切换模式：true=点击切换开/关，false=只打开不关闭")]
        public bool toggleMode = true;

        [Tooltip("打开时是否关闭其他已打开的目标（互斥模式）")]
        public bool closeOthers = true;
    }

    [Header("按钮 - 打开指定物体（4个）")]
    public ButtonPanelPair[] buttonPairs = new ButtonPanelPair[4];

    // 内部状态记录：当前是否处于夜晚模式
    private bool isNighttime = false;

    // 缓存时间值，避免每帧分配字符串
    private int cachedHour = -1;
    private int cachedMinute = -1;
    private int cachedDayHour = -1;  // 用于太阳/月亮图标切换

    // ==================== 数据结构 ====================
    [Serializable]
    public class ColorTarget
    {
        [Tooltip("目标物体")]
        public GameObject targetObject;
        
        [Tooltip("夜晚时的目标颜色")]
        public Color targetColor = Color.gray;

        // 以下字段由脚本自动管理，无需手动拖拽
        [HideInInspector] public Image image;
        [HideInInspector] public SpriteRenderer spriteRenderer;
        [HideInInspector] public TMP_Text tmpText;
        [HideInInspector] public Text uiText;
        [HideInInspector] public Color originalColor; // 缓存原始颜色（白天颜色）
        [HideInInspector] public bool initialized = false;
    }

    // ==================== 生命周期 ====================

    void Awake()
    {
        // 单例模式标准写法
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("发现多个 LocalTimeManager，销毁重复实例。");
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // 1. 初始化颜色缓存（保存白天的原始颜色）
        InitializeTargets();

        // 2. 【关键修复】游戏启动时，立即根据当前系统时间设置正确的昼夜状态
        // 这样可以防止因为初始状态不匹配导致的"开关没反应"
        ForceApplyCurrentTimeState();

        // 初始化缓存，避免第一次 Update 重复触发
        cachedHour = DateTime.Now.Hour;
        cachedDayHour = cachedHour;
        cachedMinute = DateTime.Now.Minute;

        // 3. 初始化按钮绑定
        InitializeButtons();
    }

    /// <summary>
    /// 绑定4个按钮的点击事件
    /// </summary>
    void InitializeButtons()
    {
        if (buttonPairs == null) return;

        for (int i = 0; i < buttonPairs.Length; i++)
        {
            int index = i;
            var pair = buttonPairs[i];

            if (pair == null || pair.button == null) continue;

            pair.button.onClick.AddListener(() =>
            {
                OnPanelButtonClicked(index);
            });
        }
    }

    /// <summary>
    /// 按钮点击回调：打开/切换指定物体
    /// </summary>
    void OnPanelButtonClicked(int index)
    {
        if (index < 0 || index >= buttonPairs.Length) return;

        var pair = buttonPairs[index];
        if (pair == null || pair.targetObject == null) return;

        bool shouldOpen = true;

        // 切换模式：如果是打开的就关闭
        if (pair.toggleMode && pair.targetObject.activeSelf)
        {
            shouldOpen = false;
        }

        // 互斥模式：关闭其他已打开的目标
        if (pair.closeOthers)
        {
            for (int i = 0; i < buttonPairs.Length; i++)
            {
                if (i == index) continue;
                var other = buttonPairs[i];
                if (other != null && other.targetObject != null && other.targetObject.activeSelf)
                {
                    other.targetObject.SetActive(false);
                }
            }
        }

        pair.targetObject.SetActive(shouldOpen);
    }

    void Update()
    {
        // 只在分钟改变时更新时间文本（避免每帧字符串分配）
        int currentMinute = DateTime.Now.Minute;
        if (currentMinute != cachedMinute)
        {
            cachedMinute = currentMinute;
            UpdateTimeDisplay();
        }

        // 太阳/月亮图标：每小时检查一次就够了
        int currentHour = DateTime.Now.Hour;
        if (currentHour != cachedDayHour)
        {
            cachedDayHour = currentHour;
            UpdateSunMoonIcons();
        }

        // 昼夜状态切换：只在小时变化时检查（与 cachedDayHour 复用）
        if (currentHour != cachedHour)
        {
            cachedHour = currentHour;
            CheckAndSwitchDayNight();
        }
    }

    // ==================== 核心逻辑方法 ====================

    /// <summary>
    /// 【核心修复方法】
    /// 忽略当前状态，直接根据当前系统时间，强制应用正确的颜色和显隐状态。
    /// 解决“刚开始游戏时开关不动”的问题。
    /// </summary>
    private void ForceApplyCurrentTimeState()
    {
        int currentHour = DateTime.Now.Hour;
        // 定义夜晚时间段：18点以后 或 早上6点以前
        bool isNight = (currentHour >= 18 || currentHour <= 6);

        if (isNight)
        {
            // --- 设置为夜晚状态 ---
            SetColors(true); // 使用夜晚颜色
            SetObjectsActive(nightShowObjects, true);  // 打开夜晚对象
            SetObjectsActive(dayShowObjects, false);   // 关闭白天对象
            isNighttime = true; // 更新内部标记
        }
        else
        {
            // --- 设置为白天状态 ---
            SetColors(false); // 恢复原始颜色（白天颜色）
            SetObjectsActive(nightShowObjects, false); // 关闭夜晚对象
            SetObjectsActive(dayShowObjects, true);    // 打开白天对象
            isNighttime = false; // 更新内部标记
        }
    }

    /// <summary>
    /// 检查当前时间与内部记录的状态是否一致，如果不一致则执行切换。
    /// 主要用于游戏运行过程中跨越时间点（如从 17:59 变成 18:00）时自动切换。
    /// </summary>
    private void CheckAndSwitchDayNight()
    {
        if (targets == null || targets.Length == 0) return;

        int currentHour = DateTime.Now.Hour;
        bool shouldBeNight = (currentHour >= 18 || currentHour <= 6);

        // 情况1：应该是夜晚，但当前标记是白天 -> 切换到夜晚
        if (shouldBeNight && !isNighttime)
        {
            SetColors(true);
            SetObjectsActive(nightShowObjects, true);
            SetObjectsActive(dayShowObjects, false);
            isNighttime = true;
        }
        // 情况2：应该是白天，但当前标记是夜晚 -> 切换到白天
        else if (!shouldBeNight && isNighttime)
        {
            SetColors(false);
            SetObjectsActive(nightShowObjects, false);
            SetObjectsActive(dayShowObjects, true);
            isNighttime = false;
        }
    }

    // ==================== 辅助功能方法 ====================

    /// <summary>
    /// 初始化所有变色目标，缓存组件引用和原始颜色
    /// </summary>
    private void InitializeTargets()
    {
        if (targets == null) return;

        foreach (var t in targets)
        {
            if (t.targetObject == null || t.initialized) continue;

            // 获取组件
            t.image = t.targetObject.GetComponent<Image>();
            t.spriteRenderer = t.targetObject.GetComponent<SpriteRenderer>();
            t.tmpText = t.targetObject.GetComponent<TMP_Text>();
            t.uiText = t.targetObject.GetComponent<Text>();

            // 【Bug修复】读取当前颜色作为原始颜色（白天颜色），而不是赋值默认黑色
            if (t.image != null)
                t.originalColor = t.image.color;
            else if (t.spriteRenderer != null)
                t.originalColor = t.spriteRenderer.color;
            else if (t.tmpText != null)
                t.originalColor = t.tmpText.color;
            else if (t.uiText != null)
                t.originalColor = t.uiText.color; // 修复前这里是 t.uiText.color = t.originalColor (错误)

            t.initialized = true;
        }
    }

    /// <summary>
    /// 统一设置颜色
    /// useNightColor: true=用夜晚色, false=用原始色(白天色)
    /// </summary>
    private void SetColors(bool useNightColor)
    {
        foreach (var t in targets)
        {
            if (t.targetObject == null || !t.initialized) continue;

            Color targetC = useNightColor ? t.targetColor : t.originalColor;

            if (t.image != null) t.image.color = targetC;
            else if (t.spriteRenderer != null) t.spriteRenderer.color = targetC;
            else if (t.tmpText != null) t.tmpText.color = targetC;
            else if (t.uiText != null) t.uiText.color = targetC;
        }
    }

    /// <summary>
    /// 统一设置一组对象的激活状态
    /// </summary>
    private void SetObjectsActive(GameObject[] objs, bool active)
    {
        if (objs == null) return;
        foreach (var obj in objs)
        {
            if (obj != null)
            {
                // 只有当状态不一致时才设置，减少性能开销
                if (obj.activeSelf != active)
                    obj.SetActive(active);
            }
        }
    }

    /// <summary>
    /// 更新时间文本
    /// </summary>
    private void UpdateTimeDisplay()
    {
        if (hourText == null || minuteText == null) return;

        DateTime now = DateTime.Now;
        hourText.text = now.ToString("HH");
        minuteText.text = now.ToString("mm");

        if (timeText != null)
            timeText.text = now.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 更新太阳/月亮图标显隐
    /// </summary>
    private void UpdateSunMoonIcons()
    {
        if (sunImage == null || moonImage == null) return;

        int hour = DateTime.Now.Hour;
        // 7点到17点是白天
        bool isDay = (hour >= 7 && hour <= 17);

        sunImage.gameObject.SetActive(isDay);
        moonImage.gameObject.SetActive(!isDay);
    }

    // ==================== 公共接口 ====================
    public int GetCurrentHour()
    {
        return DateTime.Now.Hour;
    }
}