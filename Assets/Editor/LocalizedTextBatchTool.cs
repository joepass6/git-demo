#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 批量给所有 TMP 文本挂 LocalizedText 的编辑器工具。
///
/// 挂上后 key 留空 = 只跟随语言切换字体、不改文字（适合鱼名、数字、计时器等）；
/// 需要翻译的文本再在 Inspector 里填 key，并在 LocalizationManager 的 translations 里加对应文案。
///
/// 菜单：
///   工具/多语言/一键处理：当前场景 + 所有预制体
///   工具/多语言/为当前场景所有文本挂 LocalizedText
///   工具/多语言/为项目所有预制体文本挂 LocalizedText
///   工具/多语言/移除所有 LocalizedText（场景+预制体，用于撤销）
/// </summary>
public static class LocalizedTextBatchTool
{
    [MenuItem("工具/多语言/一键处理：当前场景 + 所有预制体")]
    public static void ProcessAll()
    {
        if (!Confirm("将为【当前打开场景 + Assets 下所有预制体】中的每个 TMP 文本挂 LocalizedText（已有则跳过）。继续？"))
            return;

        int sceneAdded = AddToOpenScenes();
        int prefabAdded = AddToAllPrefabs();
        AssetDatabase.SaveAssets();

        Report("完成",
            $"场景新增 {sceneAdded} 个（场景已标脏，请 Ctrl+S 保存场景）\n预制体新增 {prefabAdded} 个（已直接写盘）");
    }

    [MenuItem("工具/多语言/为当前场景所有文本挂 LocalizedText")]
    public static void ProcessOpenScenes()
    {
        if (!Confirm("将为当前打开的所有场景中每个 TMP 文本挂 LocalizedText（已有则跳过）。继续？"))
            return;

        int added = AddToOpenScenes();
        Report("完成", $"场景新增 {added} 个 LocalizedText。\n场景已标脏，请 Ctrl+S 保存。");
    }

    [MenuItem("工具/多语言/为项目所有预制体文本挂 LocalizedText")]
    public static void ProcessAllPrefabsOnly()
    {
        if (!Confirm("将为 Assets 下所有预制体中的每个 TMP 文本挂 LocalizedText（已有则跳过），直接写入预制体文件。继续？"))
            return;

        int added = AddToAllPrefabs();
        AssetDatabase.SaveAssets();
        Report("完成", $"预制体新增 {added} 个 LocalizedText（已直接写盘）。");
    }

    [MenuItem("工具/多语言/移除所有 LocalizedText（场景+预制体）")]
    public static void RemoveAll()
    {
        if (!Confirm("将移除【当前场景 + Assets 所有预制体】上的全部 LocalizedText，此操作仅影响组件、不删文本。继续？"))
            return;

        int sceneRemoved = RemoveFromOpenScenes();
        int prefabRemoved = RemoveFromAllPrefabs();
        AssetDatabase.SaveAssets();

        Report("完成",
            $"场景移除 {sceneRemoved} 个（场景已标脏，请 Ctrl+S 保存）\n预制体移除 {prefabRemoved} 个（已直接写盘）");
    }

    // ==================== 场景 ====================

    static int AddToOpenScenes()
    {
        int added = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                added += AddToHierarchy(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
        }
        return added;
    }

    static int RemoveFromOpenScenes()
    {
        int removed = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                removed += RemoveFromHierarchy(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
        }
        return removed;
    }

    // ==================== 预制体 ====================

    static int AddToAllPrefabs()
    {
        List<string> paths = CollectPrefabPaths();
        int added = 0;

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar("挂 LocalizedText", path, (float)i / paths.Count))
                    break;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int n = 0;
                try
                {
                    n = AddToHierarchy(root.transform);
                    if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                added += n;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return added;
    }

    static int RemoveFromAllPrefabs()
    {
        List<string> paths = CollectPrefabPaths();
        int removed = 0;

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar("移除 LocalizedText", path, (float)i / paths.Count))
                    break;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int n = 0;
                try
                {
                    n = RemoveFromHierarchy(root.transform);
                    if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                removed += n;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return removed;
    }

    static List<string> CollectPrefabPaths()
    {
        var result = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path)) result.Add(path);
        }
        return result;
    }

    // ==================== 层级处理 ====================

    static int AddToHierarchy(Transform root)
    {
        int added = 0;
        // TMP_Text 是 TextMeshProUGUI 和 3D TextMeshPro 的共同基类，两种都能覆盖
        foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tmp == null || tmp.GetComponent<LocalizedText>() != null) continue;
            tmp.gameObject.AddComponent<LocalizedText>(); // key 留空：只换字体不换文字
            added++;
        }
        return added;
    }

    static int RemoveFromHierarchy(Transform root)
    {
        int removed = 0;
        foreach (LocalizedText lt in root.GetComponentsInChildren<LocalizedText>(true))
        {
            if (lt == null) continue;
            Object.DestroyImmediate(lt, true);
            removed++;
        }
        return removed;
    }

    // ==================== 自动匹配填充 key ====================

    class MatchEntry
    {
        public string table;
        public string key;
    }

    class MatchMap
    {
        // 简体原文（精确） -> 表/key
        public Dictionary<string, MatchEntry> exact = new Dictionary<string, MatchEntry>();
        // 去掉一切空白后 -> 同形候选（只在候选唯一时使用）
        public Dictionary<string, List<MatchEntry>> fuzzy = new Dictionary<string, List<MatchEntry>>();
        // 表名 -> 该表全部 key（判断现有 key 是否有效）
        public Dictionary<string, HashSet<string>> keysByTable = new Dictionary<string, HashSet<string>>();
        public HashSet<string> keysAll = new HashSet<string>();
    }

    class MatchCounters
    {
        public int filled;      // 本次自动填上的
        public int valid;       // 已有有效 key，跳过
        public int unmatched;   // 文字在表里查不到
        public int invalid;     // 已有 key 但表里不存在，且文字也没匹配上
        public int empty;       // 文本本身是空的
    }

    [MenuItem("工具/多语言/按当前文字自动匹配并填充 key（场景+预制体）")]
    public static void AutoMatchKeys()
    {
        if (!Confirm("将读取 Resources/Localization 下全部 CSV，按每个文本【当前摆好的文字】自动匹配并填充 tableName/key。\n\n" +
                     "规则：已有且有效的 key 不动；空 key / 无效 key 才尝试填；只接受唯一精确匹配（含忽略空白），" +
                     "匹配不上的只列清单、绝不乱填。\n继续？"))
            return;

        MatchMap map = BuildMatchMap();
        if (map == null) return;

        var report = new StringBuilder();
        var sceneC = new MatchCounters();
        var prefabC = new MatchCounters();

        // ---- 当前场景 ----
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects())
                AutoMatchHierarchy(root.transform, map, sceneC, report, "场景");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ---- 全部预制体 ----
        foreach (string path in CollectPrefabPaths())
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int before = prefabC.filled;
                AutoMatchHierarchy(root.transform, map, prefabC, report, System.IO.Path.GetFileName(path));
                if (prefabC.filled > before) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();

        if (report.Length > 0)
            Debug.Log("[批量多语言] 未匹配/无效清单：\n" + report);

        string summary =
            $"场景：新填 {sceneC.filled}，已有有效 key {sceneC.valid}，空文本 {sceneC.empty}，未匹配 {sceneC.unmatched}，无效 key {sceneC.invalid}\n" +
            $"预制体：新填 {prefabC.filled}，已有有效 key {prefabC.valid}，空文本 {prefabC.empty}，未匹配 {prefabC.unmatched}，无效 key {prefabC.invalid}\n\n" +
            "场景已标脏请 Ctrl+S 保存；未匹配明细见 Console（多为数字、占位文字或运行时由脚本赋值的鱼名等）。";
        EditorUtility.DisplayDialog("自动匹配完成", summary, "好");
        Debug.Log("[批量多语言] " + summary.Replace("\n", "，"));
    }

    static void AutoMatchHierarchy(Transform root, MatchMap map, MatchCounters c, StringBuilder report, string location)
    {
        foreach (LocalizedText lt in root.GetComponentsInChildren<LocalizedText>(true))
        {
            if (lt == null) continue;
            TMP_Text tmp = lt.GetComponent<TMP_Text>() ?? lt.GetComponentInChildren<TMP_Text>(true);
            if (tmp == null) continue;

            string text = tmp.text ?? string.Empty;
            string path = location + "/" + GetPath(lt.transform);

            // 已填且能在表里查到：不动
            if (!string.IsNullOrEmpty(lt.key))
            {
                if (IsKeyValid(map, lt.tableName, lt.key)) { c.valid++; continue; }
            }

            if (string.IsNullOrWhiteSpace(text)) { c.empty++; continue; }

            MatchEntry hit = null;
            string trimmed = text.Trim();
            if (map.exact.TryGetValue(trimmed, out MatchEntry e))
            {
                hit = e;
            }
            else
            {
                string norm = StripSpace(trimmed);
                if (norm.Length > 0 && map.fuzzy.TryGetValue(norm, out List<MatchEntry> list))
                {
                    var uniq = new List<MatchEntry>();
                    var seen = new HashSet<string>();
                    foreach (var x in list)
                    {
                        string id = x.table + "|" + x.key;
                        if (seen.Add(id)) uniq.Add(x);
                    }
                    if (uniq.Count == 1) hit = uniq[0];
                }
            }

            if (hit != null)
            {
                Undo.RecordObject(lt, "自动填充多语言 key");
                lt.tableName = hit.table;
                lt.key = hit.key;
                EditorUtility.SetDirty(lt);
                c.filled++;
            }
            else
            {
                bool wasInvalid = !string.IsNullOrEmpty(lt.key);
                if (wasInvalid) c.invalid++; else c.unmatched++;
                string preview = trimmed.Replace("\n", " ").Replace("\r", "");
                if (preview.Length > 24) preview = preview.Substring(0, 24) + "…";
                report.AppendLine($"  [{(wasInvalid ? "无效key:" + lt.key : "未匹配")}] {path}  文字=\"{preview}\"");
            }
        }
    }

    static bool IsKeyValid(MatchMap map, string table, string key)
    {
        if (string.IsNullOrEmpty(table)) return map.keysAll.Contains(key);
        return map.keysByTable.TryGetValue(table.Trim(), out var set) && set.Contains(key);
    }

    static string GetPath(Transform t)
    {
        var stack = new List<string>();
        for (Transform x = t; x != null; x = x.parent) stack.Add(x.name);
        stack.Reverse();
        return string.Join("/", stack);
    }

    static string StripSpace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>从 Resources/Localization 读全部 CSV，建立 简体文字 -> 表/key 的索引（key 列本身就是简体原文）</summary>
    static MatchMap BuildMatchMap()
    {
        TextAsset[] assets = Resources.LoadAll<TextAsset>("Localization");
        if (assets == null || assets.Length == 0)
        {
            EditorUtility.DisplayDialog("自动匹配", "Resources/Localization 下没有找到任何 CSV。", "好");
            return null;
        }

        var map = new MatchMap();
        // 同一段文字在多张表出现时（如珊瑚名同时在 ui、珊瑚），按这个顺序取第一张
        string[] tablePriority = { "ui", "珊瑚", "coral", "鱼表格" };

        var ordered = new List<TextAsset>(assets);
        ordered.Sort((a, b) => PriorityIndex(a.name, tablePriority).CompareTo(PriorityIndex(b.name, tablePriority)));

        foreach (TextAsset asset in ordered)
        {
            List<List<string>> rows = MiniCsv.Parse(asset.text);
            if (rows.Count == 0) continue;

            int zhCol = -1;
            for (int col = 1; col < rows[0].Count; col++)
            {
                string h = rows[0][col];
                if (h.Contains("简")) { zhCol = col; break; }
            }

            var keySet = new HashSet<string>();
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0) continue;
                string key = row[0].Trim();
                if (string.IsNullOrEmpty(key)) continue;
                keySet.Add(key);
                map.keysAll.Add(key);

                AddIndex(map, key, asset.name, key);
                if (zhCol > 0 && zhCol < row.Count)
                {
                    string zh = row[zhCol].Trim();
                    if (!string.IsNullOrEmpty(zh)) AddIndex(map, zh, asset.name, key);
                }
            }
            if (keySet.Count > 0) map.keysByTable[asset.name] = keySet;
        }
        return map;
    }

    static void AddIndex(MatchMap map, string zh, string table, string key)
    {
        var entry = new MatchEntry { table = table, key = key };
        // exact：按表优先级，先到的占坑
        if (!map.exact.ContainsKey(zh)) map.exact.Add(zh, entry);

        string norm = StripSpace(zh);
        if (norm.Length == 0) return;
        if (!map.fuzzy.TryGetValue(norm, out var list))
        {
            list = new List<MatchEntry>();
            map.fuzzy[norm] = list;
        }
        list.Add(entry);
    }

    static int PriorityIndex(string tableName, string[] priority)
    {
        for (int i = 0; i < priority.Length; i++)
            if (tableName == priority[i]) return i;
        return priority.Length;
    }

    /// <summary>与运行时 LocalizationManager 同款的极简 CSV 解析（引号转义、字段内逗号/换行）</summary>
    static class MiniCsv
    {
        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            var row = new List<string>();
            var field = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQ)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQ = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"') inQ = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }
    }

    // ==================== 生成韩语/俄语动态字体 ====================

    [MenuItem("工具/多语言/生成韩语·俄语动态字体（Malgun Gothic SDF）")]
    public static void GenerateMalgunDynamicFont()
    {
        const string ttfPath = "Assets/素材/UI/字体/SystemFonts/malgun.ttf";
        const string assetPath = "Assets/素材/UI/字体/SystemFonts/MalgunGothic Dynamic SDF.asset";

        var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null)
        {
            EditorUtility.DisplayDialog("生成动态字体",
                $"找不到源字体：\n{ttfPath}\n\n请确认文件已存在且 Unity 已完成导入（右下角进度条结束）。", "好");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
        {
            EditorUtility.DisplayDialog("生成动态字体", "目标字体资产已存在，未重复生成：\n" + assetPath, "好");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath));
            return;
        }

        // 动态图集：运行时按需把字形渲染进 SDF 图集，无需预先烘焙全部谚文/西里尔字符。
        // 单参重载内部等价于 (font, 90, 9, SDFAA, 1024, 1024, Dynamic, 多图集=true)，且不依赖 TextCore.LowLevel 命名空间
        TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font);
        if (fa == null)
        {
            EditorUtility.DisplayDialog("生成动态字体", "TMP_FontAsset.CreateFontAsset 返回空，生成失败。", "好");
            return;
        }
        fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fa.name = "MalgunGothic Dynamic SDF";

        AssetDatabase.CreateAsset(fa, assetPath);
        if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
        {
            fa.atlasTextures[0].name = "Malgun Atlas";
            if (!AssetDatabase.Contains(fa.atlasTextures[0])) AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        }
        if (fa.material != null)
        {
            fa.material.name = "Malgun SDF Material";
            if (!AssetDatabase.Contains(fa.material)) AssetDatabase.AddObjectToAsset(fa.material, fa);
        }
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("生成动态字体",
            "已生成：\n" + assetPath + "\n\n请把它拖到 LocalizationManager 的 languageFonts 里【韩语】和【俄语】两个槽位。\n" +
            "英/德/法/西可拖自带的 LiberationSans SDF；日语可拖 TerrarumSansBitmap SDF。", "好");
        EditorGUIUtility.PingObject(fa);
        Debug.Log("[批量多语言] 已生成动态字体 " + assetPath);
    }

    // ==================== 小工具 ====================

    static bool Confirm(string message)
    {
        return EditorUtility.DisplayDialog("批量多语言", message, "继续", "取消");
    }

    static void Report(string title, string message)
    {
        Debug.Log($"[批量多语言] {message.Replace("\n", "，")}");
        EditorUtility.DisplayDialog(title, message, "好");
    }
}
#endif
