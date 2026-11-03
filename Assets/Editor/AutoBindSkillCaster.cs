using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 自动给珊瑚预制体挂 SkillCaster 组件 + 绑定对应的 SkillDefinition 资产。
/// 菜单: Tools → 绑定技能到预制体
/// 支持 batch mode 执行 (Unity -executeMethod AutoBindSkillCaster.MenuBind)
/// </summary>
public static class AutoBindSkillCaster
{
    [MenuItem("Tools/绑定技能到预制体", priority = 101)]
    public static void MenuBind()
    {
        string skillsFolder = "Assets/ScriptableObjects/技能定义";
        string prefabsFolder = "Assets/预制体/珊瑚预制体";

        if (!Directory.Exists(skillsFolder))
        {
            Debug.LogError("[AutoBind] 技能目录不存在: " + skillsFolder);
            return;
        }

        // 1. 加载所有技能资产
        var skillGuids = AssetDatabase.FindAssets("t:SkillDefinition", new[] { skillsFolder });
        var skillMap = new Dictionary<string, SkillSystem.SkillDefinition>();
        foreach (var guid in skillGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<SkillSystem.SkillDefinition>(path);
            if (asset == null) continue;
            string name = Path.GetFileNameWithoutExtension(path).Replace("技能定义_", "").Trim();
            skillMap[name] = asset;
        }
        Debug.Log($"[AutoBind] 加载了 {skillMap.Count} 个技能资产");

        // 2. 遍历预制体，匹配并绑定
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabsFolder });
        int bound = 0, skipped = 0, noMatch = 0, tagFixed = 0;
        var noMatchList = new List<string>();

        foreach (var guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string prefabName = prefab.name.Replace(" ", "");

            // 找技能资产
            SkillSystem.SkillDefinition skill = null;
            foreach (var kv in skillMap)
            {
                if (kv.Key.Replace(" ", "").Equals(prefabName, System.StringComparison.Ordinal))
                {
                    skill = kv.Value;
                    break;
                }
            }

            if (skill == null) { noMatch++; noMatchList.Add(prefab.name); continue; }

            // 找目标节点（有 ObjectSpawner 的）
            var target = prefab.GetComponent<ObjectSpawner>() != null ? prefab : null;
            if (target == null)
            {
                foreach (var os in prefab.GetComponentsInChildren<ObjectSpawner>(true))
                {
                    target = os.gameObject;
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"[AutoBind] {prefab.name} 无 ObjectSpawner，跳过");
                noMatch++;
                continue;
            }

            // Tag 修正
            if (prefab.tag != "coral") { prefab.tag = "coral"; tagFixed++; }

            // SkillCaster
            var caster = target.GetComponent<SkillSystem.SkillCaster>();
            if (caster == null)
            {
                caster = target.AddComponent<SkillSystem.SkillCaster>();
                bound++;
            }
            else
            {
                skipped++;
            }

            if (caster.skill != skill)
            {
                caster.skill = skill;
                EditorUtility.SetDirty(target);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[AutoBind] 新绑定={bound}, 已存在={skipped}, Tag修正={tagFixed}, 无匹配={noMatch}");
        if (noMatchList.Count > 0)
            Debug.Log("[AutoBind] 无匹配列表: " + string.Join(", ", noMatchList));
    }
}
