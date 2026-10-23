#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键恢复 10 月场景功能：挂 LocalizationManager、挂 FishInfoPager。
/// LocalizedText 批量挂载 + CSV key 自动匹配 已由 LocalizedTextBatchTool 实现，
/// 直接去"工具/多语言"菜单点即可。
///
/// 菜单：Tools/恢复10月功能
/// </summary>
public static class SceneRestoreTool
{
    const string RootCanvasName = "界面ui";          // LocalizationManager 挂这里（始终激活）
    const string FishInfoContainerName = "场景内鱼的信息"; // FishInfoPager 挂这里

    [MenuItem("Tools/恢复10月功能/挂 LocalizationManager + FishInfoPager")]
    public static void RestoreAll()
    {
        if (!Confirm("将在当前打开场景中：\n" +
                     "  1) 找到「界面ui」Canvas，挂 LocalizationManager（已有则跳过）\n" +
                     "  2) 找到「场景内鱼的信息」容器，挂 FishInfoPager（已有则跳过）\n\n" +
                     "LocalizedText 批量挂载 + CSV key 自动匹配 请在 Tools → 工具 → 多语言 菜单执行。\n\n继续？"))
            return;

        int addedLoc = 0, addedPager = 0, skippedLoc = 0, skippedPager = 0;
        var log = new List<string>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            // ---- LocalizationManager ----
            // 优先找"界面ui" Canvas → 降级找第一个根 Canvas → 降级找 Main Camera
            GameObject locTarget = null;

            // 1. 指定名字的 Canvas
            Canvas namedCanvas = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Canvas c = FindCanvasByName(root.transform, RootCanvasName);
                if (c != null && c.isRootCanvas) { namedCanvas = c; break; }
            }
            if (namedCanvas != null) locTarget = namedCanvas.gameObject;

            // 2. 第一个根 Canvas
            if (locTarget == null)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Canvas c = FindCanvasByName(root.transform, null); // 任何 Canvas
                    if (c != null && c.isRootCanvas) { locTarget = c.gameObject; break; }
                }
            }

            // 3. Main Camera
            if (locTarget == null)
            {
                var cam = Camera.main;
                if (cam != null) locTarget = cam.gameObject;
            }

            if (locTarget == null)
            {
                log.Add($"  [跳过] 场景「{scene.name}」没找到任何可挂 LocalizationManager 的物体");
            }
            else
            {
                var existing = locTarget.GetComponent<LocalizationManager>();
                if (existing != null)
                {
                    skippedLoc++;
                    log.Add($"  [跳过] 「{locTarget.name}」已有 LocalizationManager");
                }
                else
                {
                    Undo.AddComponent<LocalizationManager>(locTarget);
                    addedLoc++;
                    log.Add($"  ✔ 已在「{locTarget.name}」挂 LocalizationManager");
                }
            }

            // ---- FishInfoPager ----
            Transform fishInfoRoot = FindByName(scene.GetRootGameObjects(), FishInfoContainerName);
            if (fishInfoRoot == null)
            {
                // 降级：场景内可能是子节点
                foreach (GameObject root in scene.GetRootGameObjects())
                    fishInfoRoot = FindDeepByName(root.transform, FishInfoContainerName);
                if (fishInfoRoot == null)
                {
                    log.Add($"  [跳过] 场景「{scene.name}」没找到「{FishInfoContainerName}」容器");
                    continue;
                }
            }

            var existingPager = fishInfoRoot.GetComponent<FishInfoPager>();
            if (existingPager != null)
            {
                skippedPager++;
                log.Add($"  [跳过] 「{FishInfoContainerName}」已有 FishInfoPager");
            }
            else
            {
                Undo.AddComponent<FishInfoPager>(fishInfoRoot.gameObject);
                addedPager++;
                log.Add($"  ✔ 已在「{FishInfoContainerName}」挂 FishInfoPager");
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        string result = string.Join("\n", log);
        string summary = $"LocalizationManager: 新增 {addedLoc}，跳过 {skippedLoc}\n" +
                         $"FishInfoPager: 新增 {addedPager}，跳过 {skippedPager}\n\n" +
                         $"场景已标脏，请 Ctrl+S 保存。\n\n" +
                         $"接下来请执行：\n" +
                         $"  Tools → 工具 → 多语言 → 一键处理：当前场景 + 所有预制体\n" +
                         $"  Tools → 工具 → 多语言 → 按当前文字自动匹配并填充 key";

        Debug.Log("[恢复10月功能]\n" + result + "\n" + summary);
        EditorUtility.DisplayDialog("恢复完成", summary, "好");
    }

    [MenuItem("Tools/恢复10月功能/诊断：打印场景结构")]
    public static void DumpScene()
    {
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            sb.AppendLine($"=== 场景: {scene.name} ===");
            foreach (GameObject root in scene.GetRootGameObjects())
                PrintHierarchy(root.transform, sb, 0, 3);
        }
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("场景结构（见 Console）", "已打印到 Console，按 F12 可展开完整层级。", "好");
    }

    static void PrintHierarchy(Transform t, System.Text.StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        string indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}{t.name}");
        foreach (Transform c in t) PrintHierarchy(c, sb, depth + 1, maxDepth);
    }

    // ========== 小工具 ==========

    static Canvas FindCanvasByName(Transform root, string name)
    {
        var c = root.GetComponent<Canvas>();
        if (c != null && (name == null || root.name == name)) return c;
        foreach (Transform child in root)
        {
            Canvas hit = FindCanvasByName(child, name);
            if (hit != null) return hit;
        }
        return null;
    }

    static Transform FindByName(IEnumerable<GameObject> roots, string name)
    {
        foreach (var r in roots)
            if (r.name == name) return r.transform;
        return null;
    }

    static Transform FindDeepByName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform hit = FindDeepByName(c, name);
            if (hit != null) return hit;
        }
        return null;
    }

    static bool Confirm(string msg) => EditorUtility.DisplayDialog("恢复10月功能", msg, "继续", "取消");
}
#endif
