using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 多语言管理器（单例）。
///
/// 翻译数据放在 CSV 表格里（可用 Excel / WPS 编辑）：
///   目录：Assets/Resources/Localization/
///   每一类内容一个 CSV 文件，文件名 = 表名（例如 ui.csv、coral.csv、fish.csv）
///   第 1 行为表头：key,简体中文,繁体中文,English,德语,法语,西班牙语,日语,韩语,俄语
///   列顺序随意，表头写中文名/英文名都行；某语言留空时自动回退简体中文
///   字段里要写逗号或换行时，用英文双引号把该格包起来（Excel 里正常编辑即可，另存 CSV 会自动处理）
///   编码兼容 UTF-8(含/不含BOM) 和 GBK，Excel 默认另存的 CSV 也能读
///
/// 用法：
/// 1. 场景里一个"始终激活"的物体挂本组件（不要挂在默认隐藏的语言面板内部）；
/// 2. 在 languageFonts 里为每种语言指定一个 TMP 字体资源（Reset 时自动补齐 9 条空槽）；
/// 3. "语言选择"容器下的 9 个语言按钮按物体名自动绑定，无需手动拖 OnClick；
/// 4. 需要翻译的 Text(TMP) 挂 LocalizedText，填"表名(可空)+key"；
/// 5. 首次启动识别电脑系统语言；手动切换后存入 PlayerPrefs。
///
/// 其他脚本取文案：LocalizationManager.Tr("ui", "btn_close", "关闭")；表名可省：Tr("btn_close", "关闭")。
/// </summary>
[DefaultExecutionOrder(-100)]
public class LocalizationManager : MonoBehaviour
{
    public enum Language
    {
        English = 0,
        German = 1,
        French = 2,
        Spanish = 3,
        Japanese = 4,
        Korean = 5,
        Russian = 6,
        ChineseTraditional = 7,
        ChineseSimplified = 8
    }

    /// <summary>一种语言对应的字体（某种语言没给字体时，该语言文本保持原字体不变）</summary>
    [Serializable]
    public class LanguageFont
    {
        public Language language;
        public TMP_FontAsset font;
    }

    public static LocalizationManager Instance { get; private set; }

    /// <summary>当前生效语言</summary>
    public Language CurrentLanguage { get; private set; }

    /// <summary>语言切换后触发（参数为新语言），其他系统可订阅以刷新自己生成的文字</summary>
    public event Action<Language> LanguageChanged;

    /// <summary>静态版语言切换事件：不方便拿到 Instance 的静态系统（如 DropItem）订阅这个即可</summary>
    public static event Action<Language> OnLanguageChangedStatic;

    const string PlayerPrefsKey = "GameLanguage";

    /// <summary>Resources 下的表格目录（即 Assets/Resources/Localization）</summary>
    const string CsvFolder = "Localization";

    [Header("每种语言的字体")]
    [Tooltip("为每种语言指定 TMP 字体资源；留空的语言不换字体")]
    public List<LanguageFont> languageFonts = new List<LanguageFont>();

    [Header("语言按钮容器")]
    [Tooltip("语言选择容器（其下直接放 9 个语言节点，每个节点里有 Button）。留空则在场景中按名字自动查找")]
    public Transform languageButtonRoot;

    [Header("字体额外生效范围（可选）")]
    [Tooltip("除挂了 LocalizedText 的文本外，这些节点下的所有 TMP 文本也会在切换语言时一起换字体。不需要可留空")]
    public Transform[] fontRoots;

    [Header("语言面板开关（可选）")]
    [Tooltip("语言选择面板 GameObject。留空则自动搜场景里名字含'语言选择'/'语言面板'/'LanguagePanel'的物体")]
    public GameObject languagePanel;

    [Tooltip("打开语言面板的入口按钮（Button 组件）。留空则自动搜场景里名字含'语言'/'Language'/'切换语言'的外部按钮")]
    public Button languageEntryButton;

    [Tooltip("关闭按钮（Button 组件）。不自动搜名字，你手动拖哪个就是哪个。也可以留空，自己在 Inspector 里把 OnClick 绑到 CloseLanguagePanel/ToggleLanguagePanel")]
    public Button closeButton;

    readonly List<LocalizedText> registeredTexts = new List<LocalizedText>();
    Dictionary<Language, TMP_FontAsset> fontDict;

    // 表名 -> ( key -> ( 语言 -> 文案 ) )
    Dictionary<string, Dictionary<string, Dictionary<Language, string>>> tables;

#if UNITY_EDITOR
    void Reset()
    {
        // 组件刚挂上时自动补齐 9 条语言字体槽，省得手动 List+
        languageFonts = new List<LanguageFont>();
        foreach (Language lang in Enum.GetValues(typeof(Language)))
            languageFonts.Add(new LanguageFont { language = lang });
    }
#endif

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        BuildFontDictionary();
        LoadTables();

        // 先读玩家存档选择；没有存档才识别电脑系统语言
        Language? saved = LoadSavedLanguage();
        CurrentLanguage = saved ?? DetectSystemLanguage();
    }

    void Start()
    {
        BindLanguageButtons();
        BindPanelControls();
        ApplyAll();

        // 语言面板若是运行后才实例化/激活的，Start 时会找不到按钮：延迟多补绑几轮
        StartCoroutine(DelayedRebind());

        int entries = 0;
        if (tables != null)
            foreach (var t in tables.Values) entries += t.Count;

        Debug.Log($"[多语言] 初始化语言={CurrentLanguage}，字体槽 {CountConfiguredFonts()} 个，" +
                  $"CSV 表 {tables?.Count ?? 0} 个共 {entries} 条，已注册文本 {registeredTexts.Count} 个");
    }

    System.Collections.IEnumerator DelayedRebind()
    {
        // 0.5s / 1.5s / 3s 各补一次：覆盖面板延迟生成的情况。BindLanguageButtons 内部有去重，不会重复绑定
        yield return new WaitForSeconds(0.5f);
        BindLanguageButtons();
        BindPanelControls();
        yield return new WaitForSeconds(1f);
        BindLanguageButtons();
        BindPanelControls();
        yield return new WaitForSeconds(1.5f);
        BindLanguageButtons();
        BindPanelControls();
    }

    int CountConfiguredFonts()
    {
        int n = 0;
        if (languageFonts != null)
            foreach (LanguageFont lf in languageFonts)
                if (lf != null && lf.font != null) n++;
        return n;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==================== 切换语言 ====================

    public void SetLanguage(Language lang)
    {
        CurrentLanguage = lang;
        PlayerPrefs.SetInt(PlayerPrefsKey, (int)lang);
        PlayerPrefs.Save();

        ApplyAll();
        LanguageChanged?.Invoke(lang);
        OnLanguageChangedStatic?.Invoke(lang);

        Debug.Log($"[多语言] 已切换为 {lang}，刷新了 {registeredTexts.Count} 个文本，" +
                  $"字体={(GetFont(lang) != null ? GetFont(lang).name : "未配置(保持原字体)")}");

        if (registeredTexts.Count == 0)
        {
            Debug.LogWarning("[多语言] 当前场景没有任何文本挂 LocalizedText：" +
                             "语言已切换并保存，但界面文字不会变化。请给需要翻译的 Text(TMP) 挂 LocalizedText 并填 key");
        }
    }

    /// <summary>供 OnClick 动态 int 参数使用（数值对应 Language 枚举）</summary>
    public void SetLanguage(int index)
    {
        if (Enum.IsDefined(typeof(Language), index)) SetLanguage((Language)index);
    }

    // 也可以把按钮事件直接拖到这 9 个无参方法上（不拖也行，会按按钮节点名自动绑定）
    public void SelectEnglish() { SetLanguage(Language.English); }
    public void SelectGerman() { SetLanguage(Language.German); }
    public void SelectFrench() { SetLanguage(Language.French); }
    public void SelectSpanish() { SetLanguage(Language.Spanish); }
    public void SelectJapanese() { SetLanguage(Language.Japanese); }
    public void SelectKorean() { SetLanguage(Language.Korean); }
    public void SelectRussian() { SetLanguage(Language.Russian); }
    public void SelectChineseTraditional() { SetLanguage(Language.ChineseTraditional); }
    public void SelectChineseSimplified() { SetLanguage(Language.ChineseSimplified); }

    // ==================== 查询 ====================

    /// <summary>不指定表名：在所有 CSV 表里按 key 查找</summary>
    public string GetText(string key, string fallback = null)
    {
        return GetText(null, key, fallback);
    }

    /// <summary>指定表名（CSV 文件名，不带 .csv）+ key 查找；表名为空或查不到时再跨表查</summary>
    public string GetText(string table, string key, string fallback = null)
    {
        if (tables == null || string.IsNullOrEmpty(key)) return fallback;

        Dictionary<Language, string> cells = null;

        if (!string.IsNullOrEmpty(table))
        {
            if (tables.TryGetValue(table.Trim(), out var t) && t.TryGetValue(key, out cells))
                return PickLanguageText(cells, fallback);
        }

        foreach (var t in tables.Values)
        {
            if (t.TryGetValue(key, out cells))
                return PickLanguageText(cells, fallback);
        }
        return fallback;
    }

    string PickLanguageText(Dictionary<Language, string> cells, string fallback)
    {
        if (cells != null)
        {
            if (cells.TryGetValue(CurrentLanguage, out string text) && !string.IsNullOrEmpty(text)) return text;
            // 当前语言没填：回退简体中文，再没有就用 fallback
            if (cells.TryGetValue(Language.ChineseSimplified, out string zh) && !string.IsNullOrEmpty(zh)) return zh;
        }
        return fallback;
    }

    /// <summary>取某语言指定的字体，没配置返回 null（调用方保持原字体）</summary>
    public TMP_FontAsset GetFont(Language lang)
    {
        if (fontDict != null && fontDict.TryGetValue(lang, out TMP_FontAsset font)) return font;
        return null;
    }

    /// <summary>其他脚本取翻译的快捷方式，管理器不存在时直接返回 fallback</summary>
    public static string Tr(string key, string fallback = null)
    {
        return Instance != null ? Instance.GetText(null, key, fallback) : fallback;
    }

    /// <summary>其他脚本取翻译的快捷方式（指定表名），管理器不存在时直接返回 fallback</summary>
    public static string Tr(string table, string key, string fallback = null)
    {
        return Instance != null ? Instance.GetText(table, key, fallback) : fallback;
    }

    /// <summary>表内是否存在该 key</summary>
    public bool Exists(string table, string key)
    {
        if (tables == null || string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key)) return false;
        return tables.TryGetValue(table.Trim(), out var t) && t.ContainsKey(key);
    }

    /// <summary>任意一张表里是否存在该 key</summary>
    public bool Exists(string key)
    {
        if (tables == null || string.IsNullOrEmpty(key)) return false;
        foreach (var t in tables.Values) if (t.ContainsKey(key)) return true;
        return false;
    }

    // ==================== 运行时动态文本（代码直接赋值的鱼名/珊瑚名等） ====================

    readonly Dictionary<TMP_Text, KeyRef> dynamicLabels = new Dictionary<TMP_Text, KeyRef>();

    struct KeyRef
    {
        public string table;
        public string key;
        public object[] args;   // 非 null 时：译文是含 {0} 的模板，用 args 做 string.Format
    }

    /// <summary>
    /// 给"代码直接赋中文文字"的 TMP 文本登记翻译：立即按当前语言翻译，之后每次切语言自动重译并换字体。
    /// 例：LocalizationManager.ApplyLabel(nameLabel, "鱼表格", fishChineseName);
    /// </summary>
    public static void ApplyLabel(TMP_Text tmp, string table, string chineseKey)
    {
        if (tmp == null || string.IsNullOrEmpty(chineseKey)) return;
        if (Instance == null) { tmp.text = chineseKey; return; }
        Instance.dynamicLabels[tmp] = new KeyRef { table = table, key = chineseKey, args = null };
        Instance.RefreshDynamicLabel(tmp, table, chineseKey, null);
    }

    /// <summary>
    /// 给"中文模板 + 动态数字"的 TMP 文本登记翻译。chineseTemplate 形如 "可以放置{0}"，
    /// 立即按当前语言取模板并填入数字，之后每次切语言自动重渲染（数字不丢）并换字体。
    /// 例：LocalizationManager.ApplyFormat(tip, "ui", "可以放置{0}", remaining);
    /// </summary>
    public static void ApplyFormat(TMP_Text tmp, string table, string chineseTemplate, params object[] args)
    {
        if (tmp == null || string.IsNullOrEmpty(chineseTemplate)) return;
        if (Instance == null) { SafeFormat(tmp, chineseTemplate, args); return; }
        Instance.dynamicLabels[tmp] = new KeyRef { table = table, key = chineseTemplate, args = args };
        Instance.RefreshDynamicLabel(tmp, table, chineseTemplate, args);
    }

    static void SafeFormat(TMP_Text tmp, string template, object[] args)
    {
        try { tmp.text = (args == null || args.Length == 0) ? template : string.Format(template, args); }
        catch (FormatException) { tmp.text = template; }
    }

    /// <summary>
    /// 让 TMP 文本在自己的文本框内"放得下就保持原字号、放不下（外文字更长/带数字）自动缩小"，
    /// 避免文字跑出框外。以设计师当前字号为上限，只缩小不放大，不改排版不删文字。
    /// 设计师已手动开启 Auto Size 的文本保持其设置不动。
    /// </summary>
    public static void FitToBox(TMP_Text tmp)
    {
        if (tmp == null) return;

        // 设计师手动开了 Auto Size：尊重原设置
        if (tmp.enableAutoSizing) { tmp.ForceMeshUpdate(); return; }

        // 记住第一次见到的"设计字号"，之后无论译文怎么变都以它为准（避免反复缩小越跑越小）
        if (!fitBaseFontSizes.TryGetValue(tmp, out float baseSize) || baseSize <= 0f)
        {
            baseSize = tmp.fontSize;
            if (baseSize <= 0f) baseSize = 36f;
            fitBaseFontSizes[tmp] = baseSize;
        }

        // 关键：先按设计字号测一次，放得下就绝不开 Auto Size ——
        // 运行后会无条件开启 Auto Size 是"进游戏字体就变小"的直接原因。
        tmp.fontSize = baseSize;
        tmp.ForceMeshUpdate();
        Vector2 preferred = tmp.GetPreferredValues(tmp.text);
        Rect rect = tmp.rectTransform.rect;
        bool rectValid = rect.width > 1f && rect.height > 1f;
        bool fits = rectValid && preferred.x <= rect.width + 0.5f && preferred.y <= rect.height + 0.5f;
        if (!rectValid || fits)
        {
            tmp.enableAutoSizing = false;
            tmp.fontSize = baseSize;
            tmp.ForceMeshUpdate();
            return;
        }

        // 放不下才开启自动缩小，上限=设计字号
        tmp.fontSizeMax = baseSize;
        tmp.fontSizeMin = Mathf.Max(4f, baseSize * 0.45f);
        tmp.enableAutoSizing = true;
        tmp.ForceMeshUpdate();
    }

    // FitToBox 的基准字号（仅运行时缓存，不写回场景）
    static readonly Dictionary<TMP_Text, float> fitBaseFontSizes = new Dictionary<TMP_Text, float>();

    void RefreshDynamicLabel(TMP_Text tmp, string table, string key, object[] args)
    {
        string tpl = GetText(table, key, key);
        if (args == null || args.Length == 0) tmp.text = tpl;
        else
        {
            try { tmp.text = string.Format(tpl, args); }
            catch (FormatException) { tmp.text = tpl; }
        }
        TMP_FontAsset f = GetFont(CurrentLanguage);
        if (f != null && tmp.font != f) tmp.font = f;
        FitToBox(tmp); // 译文可能更长：自动缩字号，保证不跑出框
    }

    void RefreshAllDynamicLabels()
    {
        var dead = new List<TMP_Text>();
        foreach (var kv in dynamicLabels)
        {
            if (kv.Key == null) { dead.Add(kv.Key); continue; }
            RefreshDynamicLabel(kv.Key, kv.Value.table, kv.Value.key, kv.Value.args);
        }
        foreach (TMP_Text d in dead) dynamicLabels.Remove(d);
    }

    // ==================== 注册（LocalizedText 用） ====================

    public void Register(LocalizedText localized)
    {
        if (localized == null || registeredTexts.Contains(localized)) return;
        registeredTexts.Add(localized);
        localized.Refresh();
    }

    public void Unregister(LocalizedText localized)
    {
        if (localized != null) registeredTexts.Remove(localized);
    }

    // ==================== 运行期兜底：没挂 LocalizedText 的文本 ====================
    // 场景里后来手加的文本（如"是否置顶"下的 Label、新加的"神明显示开关"）常常没挂 LocalizedText，
    // 切语言时不会变。这里在不改场景结构的前提下，按"当前文字正好等于 ui 表 key"自动认领并记住 key。

    readonly Dictionary<TMP_Text, string> autoTmpKeys = new Dictionary<TMP_Text, string>();
    readonly Dictionary<Text, string> autoLegacyKeys = new Dictionary<Text, string>();

    /// <summary>
    /// 扫描 root 下所有文本，把"当前文字正好是 ui.csv 里的 key"的控件登记为可翻译，
    /// 并立即按当前语言刷新。可重复调用（已有记录不会重复登记）。
    /// </summary>
    public void LocalizeTree(Transform root)
    {
        if (root == null) return;
        RegisterAutoTexts(root);
        ApplyAutoTexts();
    }

    void RegisterAutoTexts(Transform root)
    {
        foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tmp == null || tmp.GetComponent<LocalizedText>() != null) continue;
            if (autoTmpKeys.ContainsKey(tmp)) continue;
            string key = MatchUiKey(tmp.text);
            if (key != null) autoTmpKeys[tmp] = key;
        }
        foreach (Text legacy in root.GetComponentsInChildren<Text>(true))
        {
            if (legacy == null || autoLegacyKeys.ContainsKey(legacy)) continue;
            string key = MatchUiKey(legacy.text);
            if (key != null) autoLegacyKeys[legacy] = key;
        }
    }

    /// <summary>当前文字（去掉换行/首尾空白后）正好是 ui 表里的 key 才认领，避免误翻译动态内容</summary>
    string MatchUiKey(string text)
    {
        if (tables == null || string.IsNullOrEmpty(text)) return null;
        if (!tables.TryGetValue("ui", out var ui)) return null;
        string key = text.Replace("\\n", "").Trim();
        return !string.IsNullOrEmpty(key) && ui.ContainsKey(key) ? key : null;
    }

    void ApplyAutoTexts()
    {
        TMP_FontAsset font = GetFont(CurrentLanguage);

        var deadTmp = new List<TMP_Text>();
        foreach (var kv in autoTmpKeys)
        {
            TMP_Text tmp = kv.Key;
            if (tmp == null) { deadTmp.Add(tmp); continue; }
            string value = GetText("ui", kv.Value, null);
            if (string.IsNullOrEmpty(value)) continue;
            tmp.text = value;
            if (font != null && tmp.font != font) tmp.font = font;
            FitToBox(tmp);
        }
        foreach (TMP_Text d in deadTmp) autoTmpKeys.Remove(d);

        var deadLegacy = new List<Text>();
        foreach (var kv in autoLegacyKeys)
        {
            Text text = kv.Key;
            if (text == null) { deadLegacy.Add(text); continue; }
            string value = GetText("ui", kv.Value, null);
            if (!string.IsNullOrEmpty(value)) text.text = value;
        }
        foreach (Text d in deadLegacy) autoLegacyKeys.Remove(d);
    }

    // ==================== CSV 表格加载 ====================

    void BuildFontDictionary()
    {
        fontDict = new Dictionary<Language, TMP_FontAsset>();
        if (languageFonts != null)
        {
            foreach (LanguageFont lf in languageFonts)
            {
                if (lf == null) continue;
                if (lf.font != null) fontDict[lf.language] = lf.font; // 手动拖了字体的槽位以此为准
            }
        }

        // 空槽位用内置默认字体补齐：不拖任何字体也能正常显示
        foreach (Language lang in Enum.GetValues(typeof(Language)))
        {
            if (!fontDict.ContainsKey(lang) || fontDict[lang] == null)
            {
                TMP_FontAsset builtin = GetBuiltinFont(lang);
                if (builtin != null) fontDict[lang] = builtin;
            }
        }

        // ★ 诊断日志：打印每种语言最终用的字体
        foreach (Language lang in Enum.GetValues(typeof(Language)))
        {
            fontDict.TryGetValue(lang, out TMP_FontAsset fa);
            string fontName = fa != null ? fa.name : "(null)";
            Debug.Log($"[多语言字体] {lang} -> {fontName}");
        }
    }

    // ---- 内置默认字体（字体槽留空时使用）----
    // 终极统一：TerrarumSansBitmap.otf（48945 字形的像素风超级字体）+ Dynamic Atlas
    // 覆盖 9 种语言：拉丁+重音+西里尔+希腊+平假名+片假名+韩文+常用汉字
    TMP_FontAsset fontUnifiedPixel;   // TerrarumSansBitmap（首选，全语言像素风）
    TMP_FontAsset fontJapaneseFallback;
    TMP_FontAsset fontKoreanFallback;
    TMP_FontAsset fontChineseFallback;

    TMP_FontAsset GetBuiltinFont(Language lang)
    {
        // —— 所有语言统一先走 TerrarumSansBitmap 动态像素字体 ——
        if (fontUnifiedPixel == null)
        {
            // Resources/Fonts/TerrarumSansBitmap.otf 已有一份
            fontUnifiedPixel = LoadDynamicFont("Fonts/TerrarumSansBitmap");
            if (fontUnifiedPixel != null)
                Debug.Log($"[多语言] 统一像素字体加载成功：TerrarumSansBitmap（{lang} 等 9 种语言共享，48945 字形）");
        }
        if (fontUnifiedPixel != null) return fontUnifiedPixel;

        // —— 兜底链：TerrarumSansBitmap 加载失败才各自降级 ——
        switch (lang)
        {
            case Language.English:
            case Language.German:
            case Language.French:
            case Language.Spanish:
            case Language.Russian:
                // TMP 自带：含完整拉丁扩展 + 西里尔
                fontUnifiedPixel = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (fontUnifiedPixel == null)
                    fontUnifiedPixel = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
                return fontUnifiedPixel;

            case Language.Japanese:
                if (fontJapaneseFallback == null)
                    fontJapaneseFallback = LoadOSFontAsset("Yu Gothic", "MS Gothic", "Meiryo");
                if (fontJapaneseFallback == null)
                    fontJapaneseFallback = LoadDynamicFont("Fonts/malgun");
                return fontJapaneseFallback;

            case Language.Korean:
                if (fontKoreanFallback == null)
                    fontKoreanFallback = LoadDynamicFont("Fonts/malgun");
                if (fontKoreanFallback == null)
                    fontKoreanFallback = LoadOSFontAsset("Malgun Gothic", "Gulim");
                return fontKoreanFallback;

            default:
                // 简中/繁中：系统微软雅黑兜底（含完整繁简）
                if (fontChineseFallback == null)
                    fontChineseFallback = LoadOSFontAsset("Microsoft YaHei", "微软雅黑",
                                                           "SimSun", "宋体",
                                                           "SimHei", "黑体");
                return fontChineseFallback;
        }
    }

    /// <summary>从系统字体创建 TMP_FontAsset（runtime 可用）。找不到任一指定字体时返回 null</summary>
    static TMP_FontAsset LoadOSFontAsset(params string[] fontNames)
    {
        foreach (string name in fontNames)
        {
            try
            {
                Font osFont = Font.CreateDynamicFontFromOSFont(name, 90);
                if (osFont == null) continue;
                TMP_FontAsset fa = CreateFontAssetSafe(osFont);
                if (fa != null)
                {
                    fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    // 挂 LiberationSans Fallback 兜底
                    TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
                    if (fallback != null)
                    {
                        if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                        fa.fallbackFontAssetTable.Clear();
                        fa.fallbackFontAssetTable.Add(fallback);
                    }
                    Debug.Log($"[多语言] 系统字体加载成功：{name} -> {fa.name}");
                    return fa;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[多语言] 系统字体 {name} 创建失败：{e.Message}");
            }
        }
        Debug.LogWarning($"[多语言] 所有系统字体都加载失败：{string.Join("/", fontNames)}");
        return null;
    }

    /// <summary>兼容不同 TMP 版本的 CreateFontAsset——优先用完整参数（更高质量 SDF），不支持时降级无参重载</summary>
    static TMP_FontAsset CreateFontAssetSafe(Font sourceFont)
    {
        // 尝试完整参数重载：CreateFontAsset(Font, samplingPointSize, padding, FontAtlasSize)
        // TMP 3.x 枚举名是 FontAtlasSize；旧版也有叫 TMP_AtlasResolution 的
        try
        {
            var atlasSizeType = System.Type.GetType("TMPro.FontAtlasSize, Unity.TextMeshPro");
            if (atlasSizeType == null) atlasSizeType = System.Type.GetType("TMPro.TMP_AtlasResolution, Unity.TextMeshPro");
            if (atlasSizeType != null)
            {
                int atlasValue = 2; // AtlasResolution_2048 / AtlasResolution2048（常见值）
                foreach (System.Reflection.FieldInfo f in atlasSizeType.GetFields())
                {
                    if (f.IsStatic && (f.Name.Contains("2048") || f.Name.Contains("k2048")))
                    {
                        atlasValue = (int)f.GetValue(null);
                        break;
                    }
                }
                var mi = typeof(TMP_FontAsset).GetMethod("CreateFontAsset",
                    new[] { typeof(Font), typeof(int), typeof(int), atlasSizeType });
                if (mi != null)
                {
                    var result = mi.Invoke(null, new object[] { sourceFont, 90, 5, System.Enum.ToObject(atlasSizeType, atlasValue) });
                    if (result != null) return (TMP_FontAsset)result;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"[多语言] 完整参数 CreateFontAsset 不可用：{e.Message}，降级到无参重载");
        }

        // 降级：无参重载，之后手动调 atlas 大小
        return TMP_FontAsset.CreateFontAsset(sourceFont);
    }

    /// <summary>
    /// 把 Resources 里的 ttf/otf 包装成动态 SDF 字体。
    /// 运行时按需生成字形（AtlasPopulationMode.Dynamic），无需预烘焙字符集。
    /// 同时挂 LiberationSans SDF 作为 fallback——当源字体缺某个字（如日文汉字、韩文里没覆盖的拉丁字符）时，
    /// 自动从 fallback 拿，避免出现方块。
    /// </summary>
    static TMP_FontAsset LoadDynamicFont(string resourcesPath)
    {
        Font f = Resources.Load<Font>(resourcesPath);
        if (f == null)
        {
            Debug.LogWarning($"[多语言] Resources 里找不到字体文件：{resourcesPath}（确认字体在 Assets/Resources/ 或其子目录下，文件名去掉扩展名）");
            return null;
        }

        TMP_FontAsset fa;
        try
        {
            fa = CreateFontAssetSafe(f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[多语言] CreateFontAsset 运行时失败：{resourcesPath} -> {e.Message}。尝试无参重载...");
            try { fa = TMP_FontAsset.CreateFontAsset(f); }
            catch { return null; }
        }

        if (fa == null)
        {
            Debug.LogWarning($"[多语言] CreateFontAsset 返回 null：{resourcesPath}。可能是 Runtime 版本不支持，请改用 Editor 预烘焙的 .asset。");
            return null;
        }

        // 关键：设为动态 atlas，新字符自动生成
        fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;

        // 挂 fallback——这个能解决 90% 以上的"缺字变方块"问题
        // 比如 TerrarumSansBitmap（日文）缺拉丁扩展字符、malgun（韩/俄）缺中文汉字，都能自动从 Fallback 里补
        try
        {
            TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
            if (fallback == null) fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (fallback != null)
            {
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                fa.fallbackFontAssetTable.Clear();
                fa.fallbackFontAssetTable.Add(fallback);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[多语言] 挂 fallback 失败：{e.Message}（不影响主字体，只是缺字时无法回退）");
        }

        Debug.Log($"[多语言] 动态字体加载成功：{resourcesPath} -> {fa.name}（atlas={fa.atlasWidth}x{fa.atlasHeight}）");
        return fa;
    }

    /// <summary>读取 Assets/Resources/Localization/ 下的全部 CSV（含子文件夹）</summary>
    void LoadTables()
    {
        tables = new Dictionary<string, Dictionary<string, Dictionary<Language, string>>>(StringComparer.Ordinal);

        TextAsset[] assets = Resources.LoadAll<TextAsset>(CsvFolder);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning($"[多语言] 没有找到任何翻译表格，请在 Assets/Resources/{CsvFolder}/ 下放置 CSV 文件");
            return;
        }

        int total = 0;
        foreach (TextAsset asset in assets)
        {
            if (asset == null) continue;
            int count = LoadOneTable(asset);
            total += count;
            Debug.Log($"[多语言] 读表 '{asset.name}'：{count} 条");
        }
        Debug.Log($"[多语言] 共加载 {tables.Count} 个 CSV 表、{total} 条翻译");
    }

    int LoadOneTable(TextAsset asset)
    {
        string text = DecodeBytes(asset.bytes);
        List<List<string>> rows = CsvParser.Parse(text);
        if (rows.Count == 0) return 0;

        // 表头：第 1 列 key，其余列按列名识别语言（列顺序随意）
        List<string> header = rows[0];
        var colToLang = new Dictionary<int, Language>();
        for (int c = 1; c < header.Count; c++)
        {
            Language? lang = MatchLanguage(header[c]);
            if (lang.HasValue && !colToLang.ContainsValue(lang.Value))
                colToLang[c] = lang.Value;
        }
        if (colToLang.Count == 0)
        {
            Debug.LogWarning($"[多语言] 表 '{asset.name}' 表头没有识别到任何语言列，已跳过");
            return 0;
        }

        var dict = new Dictionary<string, Dictionary<Language, string>>();
        for (int r = 1; r < rows.Count; r++)
        {
            List<string> row = rows[r];
            if (row.Count == 0) continue;

            string key = row[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var cells = new Dictionary<Language, string>();
            foreach (KeyValuePair<int, Language> kv in colToLang)
            {
                if (kv.Key < row.Count)
                {
                    string v = row[kv.Key];
                    if (!string.IsNullOrWhiteSpace(v)) cells[kv.Value] = v;
                }
            }
            dict[key] = cells; // 同 key 后行覆盖前行
        }

        tables[asset.name] = dict;
        return dict.Count;
    }

    /// <summary>自动识别编码：UTF-8 BOM / 严格 UTF-8 / GB18030（Excel 直接另存的简体中文 CSV）</summary>
    static string DecodeBytes(byte[] b)
    {
        if (b == null || b.Length == 0) return string.Empty;

        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);

        try
        {
            return new UTF8Encoding(false, true).GetString(b);
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding("GB18030").GetString(b); }
            catch { return Encoding.Default.GetString(b); }
        }
    }

    /// <summary>
    /// 极简 CSV 解析：支持英文双引号包裹字段、字段内逗号/换行、"" 表示一个引号。
    /// </summary>
    static class CsvParser
    {
        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                    else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); }
                    else field.Append(c);
                }
            }

            // 最后一行（文件末尾无换行时不会在循环里落盘）
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }

    // ==================== 内部实现 ====================

    Language? LoadSavedLanguage()
    {
        if (!PlayerPrefs.HasKey(PlayerPrefsKey)) return null;
        int saved = PlayerPrefs.GetInt(PlayerPrefsKey);
        if (Enum.IsDefined(typeof(Language), saved)) return (Language)saved;
        return null;
    }

    /// <summary>把电脑系统语言映射到游戏支持的 9 种语言；不支持的语言默认英语</summary>
    static Language DetectSystemLanguage()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.Chinese:
            case SystemLanguage.ChineseSimplified:
                return Language.ChineseSimplified;
            case SystemLanguage.ChineseTraditional:
                return Language.ChineseTraditional;
            case SystemLanguage.German:
                return Language.German;
            case SystemLanguage.French:
                return Language.French;
            case SystemLanguage.Spanish:
                return Language.Spanish;
            case SystemLanguage.Japanese:
                return Language.Japanese;
            case SystemLanguage.Korean:
                return Language.Korean;
            case SystemLanguage.Russian:
                return Language.Russian;
            case SystemLanguage.English:
                return Language.English;
            default:
                return Language.English;
        }
    }

    void ApplyAll()
    {
        // 已注册的翻译文本：换文案 + 换字体
        for (int i = registeredTexts.Count - 1; i >= 0; i--)
        {
            if (registeredTexts[i] == null)
            {
                registeredTexts.RemoveAt(i);
                continue;
            }
            registeredTexts[i].Refresh();
        }

        // 代码直接赋值的动态文本（鱼名/珊瑚名等）
        RefreshAllDynamicLabels();

        // 运行期兜底认领的文本（没挂 LocalizedText 的）
        ApplyAutoTexts();

        // 额外范围：只换字体（这些文本不翻译）
        if (fontRoots != null)
        {
            TMP_FontAsset font = GetFont(CurrentLanguage);
            if (font != null)
            {
                foreach (Transform root in fontRoots)
                {
                    if (root == null) continue;
                    foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
                        tmp.font = font;
                }
            }
        }
    }

    /// <summary>
    /// 自动绑定"语言选择"下的 9 个语言按钮：在语言节点自身或其子物体上找 Button。
    /// 与 设置.cs 自动补绑"退出游戏"按钮同一套思路，场景里不用手动拖事件。
    /// </summary>
    readonly HashSet<int> boundButtonIds = new HashSet<int>();

    void BindLanguageButtons()
    {
        Transform root = languageButtonRoot;
        if (root == null) root = FindInSceneByName("语言选择");
        if (root == null)
        {
            // Start 时语言面板可能还没生成，这里降级为普通日志，延迟补绑轮次会再找
            Debug.Log("[多语言] 暂未找到\"语言选择\"容器（面板可能尚未生成，稍后会重试；如已存在请把它拖到 languageButtonRoot）");
            return;
        }

        int newlyBound = 0;
        var details = new StringBuilder();
        foreach (Transform node in root)
        {
            Language? lang = MatchLanguage(node.name);
            if (lang == null)
            {
                details.Append($"  跳过(名字不匹配): {node.name}\n");
                continue;
            }

            // GetComponentInChildren 包含节点自身；再兜底取子层 Button（语言页结构：英语/Button/Text）
            Button btn = node.GetComponentInChildren<Button>(true);
            if (btn == null)
            {
                details.Append($"  {node.name}->{lang}：没找到 Button 组件！\n");
                continue;
            }

            if (boundButtonIds.Add(btn.GetInstanceID()))
            {
                Language target = lang.Value; // 闭包捕获固定值
                btn.onClick.AddListener(() => SetLanguage(target));
                newlyBound++;
                details.Append($"  已绑定: {node.name} -> {target}（按钮物体:{btn.name}）\n");
            }

            // ★ 关键：给按钮上的 TMP 文字自动挂 LocalizedText，让按钮名字能随语言切换
            // 按钮节点名字就是 CSV key（"英语"/"日语"/"韩语"/"德语"/"法语"/"俄语"/"繁体"/"简中"/"西班牙语"）
            EnsureButtonLabelLocalized(btn, node.name);
        }

        Debug.Log($"[多语言] 语言按钮容器='{root.name}'，本次新绑 {newlyBound} 个，累计 {boundButtonIds.Count}\n{details}");
    }

    /// <summary>
    /// 给按钮上的 TMP 文字挂 LocalizedText（运行时 AddComponent 避免污染预制体）。
    /// 挂好后 Awake 会自动注册到 LocalizationManager 并 Refresh，保证当前语言下按钮文字正确。
    /// </summary>
    void EnsureButtonLabelLocalized(Button btn, string csvKey)
    {
        TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;

        var loc = label.GetComponent<LocalizedText>();
        if (loc == null)
        {
            loc = label.gameObject.AddComponent<LocalizedText>();
        }

        // ★ 关键：即使已经有 LocalizedText（比如批量工具之前加过），
        // 只要 tableName 或 key 是空的，就补上（运行时 AddComponent 不会把空组件脏写回场景）
        bool needRefresh = false;
        if (string.IsNullOrEmpty(loc.tableName)) { loc.tableName = "ui"; needRefresh = true; }
        if (string.IsNullOrEmpty(loc.key)) { loc.key = csvKey; needRefresh = true; }

        // 清理一下 TMP 上可能残留的换行/空白（西班牙语按钮之前被人加了个 \n 在末尾）
        if (!string.IsNullOrEmpty(label.text) && (label.text.StartsWith(" ") || label.text.EndsWith(" ") || label.text.Contains("\n")))
        {
            label.text = label.text.Trim();
            needRefresh = true;
        }

        if (needRefresh) loc.Refresh();
    }

    // ==================== 面板开关 ====================

    readonly HashSet<int> boundPanelBtnIds = new HashSet<int>();

    /// <summary>
    /// 自动查找 + 自动绑定入口按钮和关闭按钮。
    /// languagePanel 留空 → 搜名字含"语言选择"/"语言面板"/"LanguagePanel"的 GameObject
    /// languageEntryButton 留空 → 搜名字含"语言"/"Language"/"切换语言"的外部 Button
    /// closeButton 只绑手动拖的（不自动搜，避免误绑面板里的子按钮）
    /// </summary>
    void BindPanelControls()
    {
        // ===== 1. 自动找 languagePanel（如果没手动拖）=====
        if (languagePanel == null)
        {
            string[] panelKeywords = { "语言选择", "语言面板", "LanguagePanel", "Language Select", "LanguageSelect" };
            foreach (var keyword in panelKeywords)
            {
                Transform found = FindInSceneByName(keyword);
                if (found != null)
                {
                    languagePanel = found.gameObject;
                    Debug.Log($"[多语言] ✅ 自动找到 languagePanel: {languagePanel.name}");
                    break;
                }
            }
            if (languagePanel == null)
            {
                // 更宽松的兜底：名字含"语言"的 GameObject
                foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    Transform hit = FindDeep(root.transform, t => t.name.Contains("语言") || t.name.Contains("anguage"));
                    if (hit != null)
                    {
                        languagePanel = hit.gameObject;
                        Debug.Log($"[多语言] ✅ 宽松匹配找到 languagePanel: {languagePanel.name}");
                        break;
                    }
                }
            }
        }

        // ===== 2. 强制默认隐藏语言面板（如果找到了）=====
        if (languagePanel != null && languagePanel.activeSelf)
        {
            languagePanel.SetActive(false);
            Debug.Log($"[多语言] 语言面板默认隐藏: {languagePanel.name}");
        }

        // ===== 3. 自动找入口按钮（如果没手动拖）=====
        if (languageEntryButton == null)
        {
            string[] btnKeywords = { "切换语言", "语言按钮", "Language Button", "LanguageBtn" };
            foreach (var keyword in btnKeywords)
            {
                Transform found = FindInSceneByName(keyword);
                if (found != null)
                {
                    languageEntryButton = found.GetComponentInParent<Button>() ?? found.GetComponent<Button>() ?? found.GetComponentInChildren<Button>(true);
                    if (languageEntryButton != null)
                    {
                        Debug.Log($"[多语言] ✅ 自动找到入口按钮(精确): {languageEntryButton.name}");
                        break;
                    }
                }
            }
            // 兜底：找名字含"语言"的外部按钮
            if (languageEntryButton == null)
            {
                foreach (var btn in FindObjectsOfType<Button>(true))
                {
                    if (btn == null) continue;
                    string n = btn.name.ToLowerInvariant();
                    if (n.Contains("语言") || n.Contains("language") || n.Contains("切换语言"))
                    {
                        languageEntryButton = btn;
                        Debug.Log($"[多语言] ✅ 宽松匹配找到入口按钮: {btn.name}");
                        break;
                    }
                }
            }
        }

        // ===== 4. 绑定入口按钮 → Toggle 语言面板 =====
        if (languageEntryButton != null && boundPanelBtnIds.Add(languageEntryButton.GetInstanceID()))
        {
            languageEntryButton.onClick.AddListener(() =>
            {
                if (languagePanel != null) ToggleLanguagePanel();
                else Debug.LogWarning("[多语言] 入口按钮已绑，但 languagePanel 没找到，不知道开什么面板");
            });
            Debug.Log($"[多语言] ✅ 入口按钮已绑定: {languageEntryButton.name} -> Toggle {languagePanel?.name ?? "(未找到面板)"}");
        }

        // ===== 5. 绑定关闭按钮（只绑手动拖的，不自动搜）=====
        if (closeButton != null && boundPanelBtnIds.Add(closeButton.GetInstanceID()))
        {
            closeButton.onClick.AddListener(() =>
            {
                if (languagePanel != null) CloseLanguagePanel();
                else Debug.LogWarning("[多语言] 关闭按钮已绑，但 languagePanel 没找到，不知道关什么");
            });
            Debug.Log($"[多语言] ✅ 关闭按钮已绑定: {closeButton.name} -> 关 {languagePanel?.name ?? "(未找到面板)"}");
        }
    }

    /// <summary>关闭 languagePanel（SetActive(false)）。Inspector 没拖 languagePanel 时啥也不做</summary>
    public void CloseLanguagePanel()
    {
        if (languagePanel != null) languagePanel.SetActive(false);
    }

    /// <summary>打开 languagePanel（SetActive(true)）</summary>
    public void OpenLanguagePanel()
    {
        if (languagePanel != null) languagePanel.SetActive(true);
    }

    /// <summary>切换 languagePanel 开关</summary>
    public void ToggleLanguagePanel()
    {
        if (languagePanel != null) languagePanel.SetActive(!languagePanel.activeSelf);
    }

    /// <summary>按名字匹配语言（语言按钮节点名、CSV 表头列名都用它，兼容中文名与各语言本名/代码）</summary>
    static Language? MatchLanguage(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string n = name.Trim().ToLowerInvariant();

        switch (n)
        {
            // 精确匹配常见语言代码
            case "en": case "en-us": case "eng": return Language.English;
            case "de": case "ger": return Language.German;
            case "fr": case "fra": return Language.French;
            case "es": case "spa": return Language.Spanish;
            case "ja": case "jpn": return Language.Japanese;
            case "ko": case "kor": return Language.Korean;
            case "ru": case "rus": return Language.Russian;
            case "zh": case "zh-cn": case "zh-hans": case "cn": return Language.ChineseSimplified;
            case "zh-tw": case "zh-hk": case "zh-hant": case "tw": return Language.ChineseTraditional;
        }

        // 繁/简优先，避免被"中文"之类的宽泛词抢先匹配
        if (n.Contains("繁") || n.Contains("traditional")) return Language.ChineseTraditional;
        if (n.Contains("简") || n.Contains("simplified")) return Language.ChineseSimplified;
        if (n.Contains("英") || n.Contains("english")) return Language.English;
        if (n.Contains("德") || n.Contains("german") || n.Contains("deutsch")) return Language.German;
        if (n.Contains("法") || n.Contains("french") || n.Contains("fran")) return Language.French;
        if (n.Contains("西班牙") || n.Contains("spanish") || n.Contains("espa")) return Language.Spanish;
        if (n.Contains("日") || n.Contains("japanese") || n.Contains("japon")) return Language.Japanese;
        if (n.Contains("韩") || n.Contains("korean")) return Language.Korean;
        if (n.Contains("俄") || n.Contains("russ")) return Language.Russian;
        return null;
    }

    static Transform FindInSceneByName(string targetName)
    {
        // 不用 GameObject.Find：语言面板默认隐藏时它找不到未激活物体
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform hit = FindDeep(root.transform, targetName);
            if (hit != null) return hit;
        }
        return null;
    }

    static Transform FindDeep(Transform root, string targetName)
    {
        if (root.name == targetName) return root;
        foreach (Transform child in root)
        {
            Transform hit = FindDeep(child, targetName);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>按条件查找（支持 lambda 谓词）</summary>
    static Transform FindDeep(Transform root, System.Func<Transform, bool> predicate)
    {
        if (predicate(root)) return root;
        foreach (Transform child in root)
        {
            Transform hit = FindDeep(child, predicate);
            if (hit != null) return hit;
        }
        return null;
    }
}
