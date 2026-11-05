#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一键修复场景文本：
/// 1. 把遗留的 UGUI Text（位图字体非原生字号渲染会糊）原地转成 TextMeshProUGUI，保留文字/颜色/字号/对齐/换行设置
/// 2. 把所有 TMP 文本字体统一切到 VonwaonBitmap SDF（有些新加的文本挂着 LiberationSans SDF，没中文字形会走 fallback 发糊）
/// 运行后检查场景无误，Ctrl+S 保存。
/// </summary>
public static class LegacyTextToTmp
{
    const string VonwaonPath = "Assets/素材/UI/字体/VonwaonBitmap SDF.asset";

    [MenuItem("工具/字体/一键修复场景文本（legacy转TMP + 统一Vonwaon）")]
    public static void FixAll()
    {
        string scenePath = EditorSceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(scenePath)) { Debug.LogError("先打开一个场景"); return; }

        var vonwaon = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(VonwaonPath);
        if (vonwaon == null) { Debug.LogError("找不到字体资产: " + VonwaonPath); return; }

        Undo.SetCurrentGroupName("一键修复场景文本");

        // ---------- 1) legacy Text → TextMeshProUGUI ----------
        var legacies = Object.FindObjectsOfType<Text>(true);
        int converted = 0;
        foreach (var t in legacies)
        {
            if (t == null) continue;
            var go = t.gameObject;

            // 先记录要保留的属性
            string content = t.text;
            Color color = t.color;
            int size = t.fontSize;
            bool raycast = t.raycastTarget;
            TextAnchor align = t.alignment;
            bool wrap = t.horizontalOverflow == HorizontalWrapMode.Wrap;
            bool vOverflow = t.verticalOverflow == VerticalWrapMode.Overflow;
            bool rich = t.supportRichText;

            Undo.DestroyObjectImmediate(t);
            var tmp = Undo.AddComponent<TextMeshProUGUI>(go);
            tmp.text = content;
            tmp.color = color;
            tmp.fontSize = size;
            tmp.raycastTarget = raycast;
            tmp.alignment = MapAlignment(align);
            tmp.enableWordWrapping = wrap;
            tmp.overflowMode = vOverflow ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
            tmp.richText = rich;
            tmp.font = vonwaon;

            EditorUtility.SetDirty(go);
            converted++;
        }

        // ---------- 2) 所有 TMP 统一字体 ----------
        int fontFixed = 0;
        foreach (var tmp in Object.FindObjectsOfType<TextMeshProUGUI>(true))
        {
            if (tmp == null || tmp.font == vonwaon) continue;
            tmp.font = vonwaon;
            EditorUtility.SetDirty(tmp);
            fontFixed++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[字体修复] legacy转TMP {converted} 个，TMP字体统一 {fontFixed} 个。请目视检查后 Ctrl+S 保存场景。");
    }

    static TextAlignmentOptions MapAlignment(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Center;
        }
    }
}
#endif
