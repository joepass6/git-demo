using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 按气泡里的『N框』自动填充 DragAndPlaceSystem.需求列表。
/// 规则：每个挂着 DropItemCounterUI 的『现有宝物』数值文本 = 一条需求，
/// 宝物类型取 DropItemCounterUI.itemType，条件文本取同一框里的『宝物条件』。
/// 只改 Inspector 里的引用（序列化数据），不改场景层级、不改 GUID。
/// 菜单：Tools → 珊瑚 → 扫描气泡需求文本（全场景 / 仅选中）
/// </summary>
public static class CoralRequirementScanTool
{
    const string MenuRoot = "Tools/珊瑚/";

    [MenuItem(MenuRoot + "扫描气泡需求文本（全场景）", priority = 120)]
    public static void ScanScene()
    {
        var placers = Object.FindObjectsOfType<DragAndPlaceSystem>(true);
        int changed = 0, noText = 0;

        foreach (var p in placers)
        {
            if (p == null) continue;
            var list = Build(p);
            if (list == null || list.Count == 0) { noText++; continue; }

            Undo.RecordObject(p, "扫描珊瑚需求文本");
            p.需求列表 = list;
            EditorUtility.SetDirty(p);
            EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            changed++;

            var parts = new List<string>();
            foreach (var r in list)
                parts.Add($"{r.itemType} {ParseCost(r.costText)}");
            Debug.Log($"[珊瑚需求扫描] {p.gameObject.name}: {list.Count} 条 → {string.Join(" / ", parts)}");
        }

        Debug.Log($"[珊瑚需求扫描] 完成：填充 {changed} 个气泡，跳过（无可用文本）{noText} 个。记得 Ctrl+S 保存场景。");
    }

    [MenuItem(MenuRoot + "扫描气泡需求文本（仅选中）", priority = 121)]
    public static void ScanSelected()
    {
        int changed = 0;
        foreach (var go in Selection.gameObjects)
        {
            foreach (var p in go.GetComponentsInChildren<DragAndPlaceSystem>(true))
            {
                if (p == null) continue;
                var list = Build(p);
                if (list == null || list.Count == 0)
                {
                    Debug.LogWarning($"[珊瑚需求扫描] {p.gameObject.name}: 没找到『现有宝物』+『宝物条件』配对，跳过");
                    continue;
                }
                Undo.RecordObject(p, "扫描珊瑚需求文本");
                p.需求列表 = list;
                EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                changed++;
                Debug.Log($"[珊瑚需求扫描] {p.gameObject.name}: {list.Count} 条");
            }
        }
        Debug.Log($"[珊瑚需求扫描] 选中对象填充 {changed} 个气泡。记得 Ctrl+S 保存场景。");
    }

    /// <summary>遍历气泡下所有 DropItemCounterUI（=『现有宝物』数值文本），配出需求列表。</summary>
    static List<DragAndPlaceSystem.宝物需求> Build(DragAndPlaceSystem placer)
    {
        var result = new List<DragAndPlaceSystem.宝物需求>();
        var counters = placer.GetComponentsInChildren<DropItemCounterUI>(true);

        foreach (var counter in counters)
        {
            if (counter == null) continue;

            TextMeshProUGUI countText = counter.GetComponent<TextMeshProUGUI>();
            if (countText == null) countText = counter.GetComponentInChildren<TextMeshProUGUI>(true);
            if (countText == null) continue;

            TextMeshProUGUI costText = FindCostText(countText.transform);
            if (costText == null) continue;

            result.Add(new DragAndPlaceSystem.宝物需求
            {
                itemType = counter.itemType,
                countText = countText,
                costText = costText,
                cost = -1
            });
        }
        return result;
    }

    /// <summary>从『现有宝物』值节点往上找同框的『宝物条件』（最多 3 层，避免抓到别的框）。</summary>
    static TextMeshProUGUI FindCostText(Transform valueNode)
    {
        Transform t = valueNode;
        for (int depth = 0; t != null && depth < 3; depth++, t = t.parent)
        {
            var texts = t.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == "宝物条件") return texts[i];
            }
        }
        return null;
    }

    static string ParseCost(TextMeshProUGUI text)
    {
        if (text == null) return "?";
        float v;
        return float.TryParse(text.text, out v) ? v.ToString("0.##") : text.text;
    }
}
