#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 一键修复：给 8 个珊瑚 Prefab 挂 ObjectSpawner + 填好 spawnEntries（鱼 Prefab 按名字加载）
/// 同时挂 LocalizationManager、FishInfoPager 等场景组件。
/// </summary>
public static class SceneAutoFix
{
    // ===== 从 10/19 打包逆向拿到的完整鱼生成配置 =====
    // key = 珊瑚 Prefab 名（不带 .prefab），value = spawnEntries (kindId, 鱼名, weight)
    static readonly Dictionary<string, int[][]> CoralSpawnEntries = new Dictionary<string, int[][]>
    {
        { "大砗磲", new int[][] {
            new int[]{1, 60}, new int[]{2, 20}, new int[]{3, 10}, new int[]{4, 10}
        }},
        { "球珊瑚", new int[][] {
            new int[]{1, 40}, new int[]{2, 40}, new int[]{3, 15}, new int[]{4, 5}
        }},
        { "海带", new int[][] {
            new int[]{1, 50}, new int[]{2, 40}, new int[]{3, 6}, new int[]{4, 4}
        }},
        { "白珊瑚", new int[][] {
            new int[]{1, 40}, new int[]{2, 50}, new int[]{3, 5}, new int[]{4, 5}
        }},
        { "竹笋凤凰螺", new int[][] {
            new int[]{1, 60}, new int[]{2, 30}, new int[]{3, 8}, new int[]{4, 2}
        }},
        { "澳大利亚沙群海葵 ", new int[][] {  // 注意：原 Prefab 名有尾部空格
            new int[]{1, 60}, new int[]{2, 30}, new int[]{3, 9}, new int[]{4, 1}
        }},
        { "葡萄藻", new int[][] {
            new int[]{1, 90}, new int[]{2, 5}, new int[]{3, 3}, new int[]{4, 2}
        }},
        { "澳洲球形海绵", new int[][] {
            new int[]{1, 40}, new int[]{2, 40}, new int[]{3, 19}, new int[]{4, 1}
        }},
    };

    // 珊瑚 Prefab 里每个 entry 对应的鱼名（kindId → 鱼名）
    static readonly Dictionary<string, string[]> CoralFishNames = new Dictionary<string, string[]>
    {
        { "大砗磲", new string[]{ "", "澳洲龙虾", "澳洲帝王蝶鱼", "白边纤齿鲈", "澳洲叶海马" } },
        { "球珊瑚", new string[]{ "", "斑鮟鱇", "班达乌贼", "斑点鹦鹉鱼", "斑点九棘鲈" } },
        { "海带", new string[]{ "", "澳洲珍珠蝴蝶鱼", "八带蝴蝶鱼", "白点宽吻鲀", "白点神仙鱼" } },
        { "白珊瑚", new string[]{ "", "澳大利亚箱形水母", "奥特曼青蛙鱼", "凹纹炮弹鱼", "翱翔飞鱼" } },
        { "竹笋凤凰螺", new string[]{ "", "凹吻篮子鱼", "翱翔真豹鲂鮄", "凹指招潮蟹", "澳洲彩虹蝴蝶鱼" } },
        { "澳大利亚沙群海葵 ", new string[]{ "", "小丑鱼", "蓝丝绒雀鲷", "蠕纹篮子鱼", "三角炮弹鱼" } },
        { "葡萄藻", new string[]{ "", "白尾倒吊", "白线鲈", "白星石斑鱼", "白针狮子鱼" } },
        { "澳洲球形海绵", new string[]{ "", "三棱糙身鲀", "黑炮弹鱼", "圆斑星鲽", "澳大利亚雀鲷" } },
    };

    const string FishPrefabFolder = "Assets/预制体/鱼预制体";
    const string CoralPrefabFolder = "Assets/预制体/珊瑚预制体";

    [MenuItem("Tools/自动修复场景/① 珊瑚Prefab挂ObjectSpawner+填数据（核心！）")]
    public static void FixCoralPrefabs()
    {
        int totalFixed = 0;
        var log = new List<string>();

        string[] coralGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CoralPrefabFolder });
        Debug.Log($"[SceneAutoFix] 找到 {coralGuids.Length} 个珊瑚 Prefab");

        foreach (string guid in coralGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string prefabName = prefab.name;
            // 匹配字典 key（处理尾部空格）
            string dictKey = null;
            foreach (var k in CoralSpawnEntries.Keys)
                if (k.Trim() == prefabName.Trim()) { dictKey = k; break; }
            if (dictKey == null)
            {
                log.Add($"  [跳过] {prefabName} — 不在配置表里");
                continue;
            }

            // === 1. 挂 ObjectSpawner ===
            ObjectSpawner spawner = prefab.GetComponent<ObjectSpawner>();
            if (spawner == null)
            {
                spawner = prefab.AddComponent<ObjectSpawner>();
                log.Add($"  ✔ {prefabName}: 新增 ObjectSpawner");
            }
            else
            {
                log.Add($"  {prefabName}: 已有 ObjectSpawner，更新 spawnEntries");
            }

            // === 2. 填 spawnEntries ===
            var fishNames = CoralFishNames[dictKey];
            var entries = CoralSpawnEntries[dictKey];
            spawner.spawnEntries = new List<ObjectSpawner.SpawnEntry>();

            foreach (var e in entries)
            {
                int kindId = e[0];
                int weight = e[1];
                string fishName = fishNames[kindId];

                // 按名字找鱼 Prefab
                string fishPath = $"{FishPrefabFolder}/{fishName}.prefab";
                GameObject fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fishPath);
                if (fishPrefab == null)
                {
                    // 试试 AssetDatabase.FindAssets
                    string[] found = AssetDatabase.FindAssets(fishName, new[] { FishPrefabFolder });
                    if (found.Length > 0)
                        fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            AssetDatabase.GUIDToAssetPath(found[0]));
                }

                spawner.spawnEntries.Add(new ObjectSpawner.SpawnEntry
                {
                    kindId = kindId,
                    prefab = fishPrefab,
                    weight = weight,
                });

                if (fishPrefab == null)
                    log.Add($"    ⚠ kindId={kindId} 鱼 Prefab 没找到: {fishName}");
                else
                    log.Add($"    [{kindId}] {fishName} weight={weight} ✔");
            }

            // === 3. 填其他字段 ===
            spawner.spawnCount = 1;
            spawner.uiPanelTag = "SnailDetailPanel";
            spawner.closeOtherPanels = true;
            spawner.popupOffset = new Vector2(136.9f, 30.5f);
            spawner.clickDarkenAmount = 0.45f;
            spawner.clickFlashDuration = 0.25f;
            spawner.spawnAreaTag = "SpawnArea";
            spawner.respawnInterval = 0.5f;
            spawner.spawnOffset = 0.5f;

            EditorUtility.SetDirty(prefab);
            totalFixed++;
            log.Add($"  ✅ {prefabName}: {entries.Length} 个 entries 已写入");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = string.Join("\n", log) + $"\n\n=== 共修复 {totalFixed} 个珊瑚 Prefab ===";
        Debug.Log(msg);
        EditorUtility.DisplayDialog("完成", msg, "好");
    }

    [MenuItem("Tools/自动修复场景/② 场景挂 LocalizationManager + FishInfoPager")]
    public static void FixSceneComponents()
    {
        var log = new List<string>();

        for (int si = 0; si < SceneManager.sceneCount; si++)
        {
            Scene scene = SceneManager.GetSceneAt(si);
            if (!scene.isLoaded) continue;

            // === LocalizationManager → 界面ui Canvas 根 ===
            Canvas rootCanvas = FindRootCanvasByName(scene, "界面ui");
            if (rootCanvas != null)
            {
                if (rootCanvas.GetComponent<LocalizationManager>() == null)
                {
                    Undo.AddComponent<LocalizationManager>(rootCanvas.gameObject);
                    log.Add($"  ✔ LocalizationManager → {rootCanvas.name}");
                }
                else log.Add($"  [跳过] {rootCanvas.name} 已有 LocalizationManager");
            }
            else log.Add($"  ⚠ 没找到名为「界面ui」的 Canvas");

            // === FishInfoPager → 场景内鱼的信息 ===
            Transform fishInfo = FindByNameDeep(scene, "场景内鱼的信息");
            if (fishInfo != null)
            {
                if (fishInfo.GetComponent<FishInfoPager>() == null)
                {
                    Undo.AddComponent<FishInfoPager>(fishInfo.gameObject);
                    log.Add($"  ✔ FishInfoPager → {fishInfo.name}");
                }
            }
            else log.Add($"  ⚠ 没找到「场景内鱼的信息」容器");

            // === FishInfoPanelButtons → 珊瑚容器 ===
            Transform coralContainer = FindByNameDeep(scene, "珊瑚容器");
            if (coralContainer != null)
            {
                if (coralContainer.GetComponent<FishInfoPanelButtons>() == null)
                {
                    Undo.AddComponent<FishInfoPanelButtons>(coralContainer.gameObject);
                    log.Add($"  ✔ FishInfoPanelButtons → {coralContainer.name}");
                }
            }

            // === 交换宝 → 名字含"换"/"交换"/"转换"的容器 ===
            Transform exchangeTf = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                exchangeTf = FindByNameContains(root.transform, "交换");
                if (exchangeTf == null) exchangeTf = FindByNameContains(root.transform, "转换");
                if (exchangeTf != null) break;
            }
            if (exchangeTf != null)
            {
                if (exchangeTf.GetComponent<交换宝>() == null)
                {
                    Undo.AddComponent<交换宝>(exchangeTf.gameObject);
                    log.Add($"  ✔ 交换宝 → {exchangeTf.name}");
                }
            }
            else log.Add("  ⚠ 没找到「交换宝/转换宝」容器（名字含 换 或 转换）");

            EditorSceneManager.MarkSceneDirty(scene);
        }

        string msg = string.Join("\n", log) + "\n\n✅ 完成！记得 Ctrl+S 保存场景";
        Debug.Log(msg);
        EditorUtility.DisplayDialog("完成", msg, "好");
    }

    [MenuItem("Tools/自动修复场景/③ 一键全修（珊瑚Prefab + 场景组件）")]
    public static void FullFix()
    {
        if (!EditorUtility.DisplayDialog("一键全修",
            "将执行：\n① 8 个珊瑚 Prefab 挂 ObjectSpawner + 填好 鱼/权重 配置\n② 场景 界面ui Canvas 挂 LocalizationManager\n③ 场景 场景内鱼的信息 挂 FishInfoPager\n\n继续？", "继续", "取消"))
            return;

        FixCoralPrefabs();
        FixSceneComponents();

        string msg = "全部修复完成！\n\n接下来：\n" +
                     "1. 等 Unity 自动编译\n" +
                     "2. Tools → 工具 → 多语言 → 一键处理：当前场景 + 所有预制体\n" +
                     "3. Ctrl+S 保存场景 + 全项目\n" +
                     "4. Play 测试！";
        EditorUtility.DisplayDialog("🎉 全修完成", msg, "好");
    }

    [MenuItem("Tools/自动修复场景/诊断：珊瑚Prefab组件")]
    public static void Diagnose()
    {
        var sb = new System.Text.StringBuilder();
        string[] coralGuids = AssetDatabase.FindAssets("t:Prefab", new[] { CoralPrefabFolder });
        sb.AppendLine($"共 {coralGuids.Length} 个珊瑚 Prefab：");
        foreach (string guid in coralGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var comps = prefab.GetComponents<MonoBehaviour>();
            var compNames = new List<string>();
            foreach (var c in comps) if (c != null) compNames.Add(c.GetType().Name);
            sb.AppendLine($"  {prefab.name}: [{string.Join(", ", compNames)}]");
        }
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("诊断（见 Console）", sb.ToString(), "好");
    }

    // ========== 辅助 ==========

    static Canvas FindRootCanvasByName(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            Canvas c = FindCanvasInChildren(go.transform, name);
            if (c != null && c.isRootCanvas) return c;
        }
        return null;
    }

    static Canvas FindCanvasInChildren(Transform t, string name)
    {
        if (t.name == name)
        {
            Canvas c = t.GetComponent<Canvas>();
            if (c != null) return c;
        }
        foreach (Transform c in t)
        {
            Canvas found = FindCanvasInChildren(c, name);
            if (found != null) return found;
        }
        return null;
    }

    static Transform FindByNameDeep(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindByNameDeep(root.transform, name);
            if (found != null) return found;
        }
        return null;
    }

    static Transform FindByNameDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform found = FindByNameDeep(c, name);
            if (found != null) return found;
        }
        return null;
    }

    static Transform FindByNameContains(Transform t, string keyword)
    {
        if (t.name.Contains(keyword)) return t;
        foreach (Transform c in t)
        {
            Transform found = FindByNameContains(c, keyword);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
