using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// 多语言文本：挂在需要翻译的 Text(TMP) 物体上（TextMeshProUGUI / 世界坐标 TMP 都支持）。
/// tableName = Assets/Resources/Localization 下 CSV 的文件名（不带 .csv），留空则在所有表里按 key 查找；
/// key 对应该表第 1 列的键名。
/// 找不到 key 或该语言文案留空时，保持编辑器里摆好的原文字不动。
/// key 也留空 = 自动把文本当前摆好的中文原文当 key 去所有表里查；查不到就只换字体不改文字。
/// </summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("分类表名 = Resources/Localization 下 CSV 的文件名（不带 .csv），如 ui、coral。留空则在所有表里按 key 查找")]
    public string tableName;

    [Tooltip("翻译键名，对应 CSV 第 1 列的 key。留空 = 自动用文本当前摆好的中文原文当 key 去查")]
    public string key;

    [Tooltip("切换语言时是否同时换成该语言指定的字体（个别文本想保留自己的字体可关掉）")]
    public bool useLanguageFont = true;

    TMP_Text tmp;
    string fallbackText;   // 编辑器里摆好的原文，翻译缺失时回退用
    bool registered;

    // 自动模式下，整句查不到时，识别“中文前缀 + 结尾数字”（如 需要数量：200 / 拥有数量：0），
    // 改用“前缀{0}”模板翻译并把数字填回去，数字不会因切换语言丢失
    static readonly Regex TrailingNumber = new Regex(@"^(.+?\D)(\d+)$", RegexOptions.Compiled);

    TMP_Text Tmp
    {
        get
        {
            if (tmp == null) tmp = GetComponent<TMP_Text>() ?? GetComponentInChildren<TMP_Text>(true);
            return tmp;
        }
    }

    void Awake()
    {
        tmp = Tmp;
        if (tmp != null) fallbackText = tmp.text;
    }

    void OnEnable()
    {
        tmp = Tmp;
        if (tmp != null && string.IsNullOrEmpty(fallbackText)) fallbackText = tmp.text;
        TryRegister();
    }

    void Start()
    {
        // 管理器所在物体比本物体晚激活时的补注册（管理器正常应放在始终激活的物体上）
        if (!registered) TryRegister();
    }

    void OnDisable()
    {
        if (LocalizationManager.Instance != null) LocalizationManager.Instance.Unregister(this);
        registered = false;
    }

    void OnValidate()
    {
        // 编辑器或运行中改了 key，播放状态下立刻刷新一次
        if (Application.isPlaying && LocalizationManager.Instance != null) Refresh();
    }

    void TryRegister()
    {
        if (registered) return;
        var mgr = LocalizationManager.Instance;
        if (mgr == null)
        {
            // 再兜底找一次场景里激活的管理器
            mgr = FindObjectOfType<LocalizationManager>();
        }
        if (mgr != null)
        {
            mgr.Register(this);
            registered = true;
        }
    }

    /// <summary>由 LocalizationManager 在切换语言时调用，也可手动调用刷新</summary>
    public void Refresh()
    {
        if (tmp == null) tmp = Tmp;
        if (tmp == null) return;

        var mgr = LocalizationManager.Instance;
        if (mgr == null) return;

        // key 留空：把当前文字（去首尾空白）当 key 去查，实现“摆好中文即自动翻译”
        string lookupKey = !string.IsNullOrWhiteSpace(key) ? key : (tmp.text ?? string.Empty).Trim();

        bool found = false;
        if (!string.IsNullOrEmpty(lookupKey))
        {
            found = !string.IsNullOrEmpty(tableName)
                ? mgr.Exists(tableName, lookupKey)
                : mgr.Exists(lookupKey);

            if (found)
            {
                string text = mgr.GetText(tableName, lookupKey, fallbackText);
                if (!string.IsNullOrEmpty(text))
                {
                    // 译文回到了 key 本身（当前语言=中文，或该语言没填而回退简中）：
                    // 若是之前翻译过的文本，恢复最初摆好的原文（保留换行等原始排版）
                    if (text == lookupKey)
                    {
                        if (!string.IsNullOrEmpty(fallbackText) && tmp.text != fallbackText) tmp.text = fallbackText;
                    }
                    else
                    {
                        tmp.text = text;
                    }
                }
            }
            else
            {
                // 整句/整 key 查不到：尝试“前缀{0}”模板并把结尾数字填回（数字保留）
                //  · key 留空(自动模式)：前缀和数字都从原文拆，如 “需要数量：200”→查“需要数量：{0}”
                //  · key 非空(如 key=“需要数量：”)：key 就是前缀，数字仍取原文结尾
                TryTrailingNumber(mgr);
            }
            // 都查不到：原文字一字不动
        }

        if (useLanguageFont)
        {
            TMP_FontAsset font = mgr.GetFont(mgr.CurrentLanguage);
            if (font != null) tmp.font = font;
        }

        // 译文比中文长时自动缩字号，保证文字不跑出文本框（放得下时仍是原字号）
        LocalizationManager.FitToBox(tmp);
    }

    /// <summary>
    /// 整 key 查不到时的兜底：原文形如“需要数量：200”，就按“需要数量：{0}”模板翻译并保留结尾数字。
    /// key 留空时前缀也从原文提取；key 非空时（如“需要数量：”）直接用 key 做前缀。
    /// 数字从最初摆好的原文(fallbackText)提取，避免把其它语言译文里的字符误当数字。
    /// </summary>
    void TryTrailingNumber(LocalizationManager mgr)
    {
        string original = !string.IsNullOrEmpty(fallbackText)
            ? fallbackText.Trim()
            : (tmp.text ?? string.Empty).Trim();

        Match m = TrailingNumber.Match(original);
        if (!m.Success) return;

        string tplKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            // 指定了前缀 key：本身带占位符就直接用，否则补 {0}
            tplKey = key.Trim();
            if (tplKey.IndexOf("{0", System.StringComparison.Ordinal) < 0) tplKey += "{0}";
        }
        else
        {
            tplKey = m.Groups[1].Value + "{0}";
        }

        bool has = !string.IsNullOrEmpty(tableName) ? mgr.Exists(tableName, tplKey) : mgr.Exists(tplKey);
        if (!has) return;

        string tpl = mgr.GetText(tableName, tplKey, tplKey);
        if (string.IsNullOrEmpty(tpl)) return;

        if (tpl == tplKey)
        {
            // 回到中文：恢复原始“前缀+数字”
            if (tmp.text != original) tmp.text = original;
        }
        else
        {
            try { tmp.text = string.Format(tpl, m.Groups[2].Value); }
            catch (System.FormatException) { tmp.text = tpl; }
        }
    }
}
